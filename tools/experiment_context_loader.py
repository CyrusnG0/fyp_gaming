"""Read run/exchange lines and reconstruct one request per composite key.

The composite key is ``run_id + seat_id + season + request_index``. The legacy
``exchange_id`` (``run:N``) is recorded for diagnostics only and is never used to join, because it
repeats across seats and seasons. Malformed lines become diagnostics that carry a line number and a
category, never file content.
"""
import json
from collections import OrderedDict

from experiment_context_constants import (EXCHANGE_REQUEST, EXCHANGE_RESULT, EXPERIMENT_CONFIG,
                                          IDENTITY_FIELDS, OPTIONAL_IDENTITY_FIELDS,
                                          RESULT_RESPONSE_FIELDS, RUN_RECORD, TURN_END,
                                          UNKNOWN_EXCHANGE)
from experiment_context_prompt import parse_prompt

REQUEST_KIND_COMMAND = "command"
REQUEST_KIND_INQUIRY = "inquiry"
REQUEST_KIND_NOTE = "note"
REQUEST_KIND_NONE = "no_command"


def _as_int(value):
    if isinstance(value, bool):
        return None
    if isinstance(value, int):
        return value
    if isinstance(value, float) and value.is_integer():
        return int(value)
    return None


def _clean_key_part(value, integer=False):
    if integer:
        number = _as_int(value)
        return None if number is None else number
    if value is None:
        return None
    text = str(value).strip()
    return text or None


class Entry:
    """One physical JSONL line, with its source and line number for citations."""

    __slots__ = ("source", "line", "obj", "error", "kind")

    def __init__(self, source, line, obj=None, error=None):
        self.source = source
        self.line = line
        self.obj = obj
        self.error = error
        self.kind = None

    def get(self, key, default=None):
        return self.obj.get(key, default) if isinstance(self.obj, dict) else default

    @property
    def cite(self):
        return "%s:%d" % (self.source, self.line)

    def diagnostic(self):
        return {"source": self.source, "line": self.line, "kind": self.error}


def read_jsonl(path, source):
    """Read one JSON/JSONL file. A malformed line yields a diagnostic, never a content echo."""
    entries = []
    with open(path, "r", encoding="utf-8-sig", errors="replace") as stream:
        for line_no, raw in enumerate(stream, 1):
            if not raw.strip():
                continue
            try:
                obj = json.loads(raw)
            except ValueError as exc:
                message = getattr(exc, "msg", "invalid json")
                entries.append(Entry(source, line_no, None, "invalid_json: " + str(message)))
                continue
            if not isinstance(obj, dict):
                entries.append(Entry(source, line_no, None, "not_a_json_object"))
                continue
            entries.append(Entry(source, line_no, obj))
    return entries


def classify(entry):
    """Label a well-formed line by the role the runner gave it."""
    if entry.obj is None:
        return None
    event = entry.get("exchange_event")
    if event == EXPERIMENT_CONFIG:
        entry.kind = EXPERIMENT_CONFIG
    elif event == TURN_END:
        entry.kind = TURN_END
    elif event == "request":
        entry.kind = EXCHANGE_REQUEST
    elif event == "result":
        entry.kind = EXCHANGE_RESULT
    elif event is not None:
        entry.kind = UNKNOWN_EXCHANGE
    else:
        entry.kind = RUN_RECORD
    return entry.kind


def composite_key(obj):
    """(run_id, seat_id, season, request_index) or None when any part is missing/ill-typed."""
    run_id = _clean_key_part(obj.get("run_id"))
    seat_id = _clean_key_part(obj.get("seat_id"))
    season = _clean_key_part(obj.get("season"), integer=True)
    index = _clean_key_part(obj.get("request_index"), integer=True)
    if run_id is None or seat_id is None or season is None or index is None:
        return None
    return (run_id, seat_id, season, index)


def turn_key(obj):
    """(run_id, seat_id, season, turn_token) used by the turn_end sidecar."""
    run_id = _clean_key_part(obj.get("run_id"))
    seat_id = _clean_key_part(obj.get("seat_id"))
    season = _clean_key_part(obj.get("season"), integer=True)
    token = _clean_key_part(obj.get("turn_token"), integer=True)
    if run_id is None or seat_id is None or season is None or token is None:
        return None
    return (run_id, seat_id, season, token)


def key_text(key):
    return None if key is None else list(key)


class Duplicate:
    __slots__ = ("key", "sources")

    def __init__(self, key):
        self.key = key
        self.sources = OrderedDict()

    def add(self, entry):
        self.sources.setdefault(entry.source, []).append(entry.line)

    def to_json(self):
        return {"kind": "duplicate_composite_key", "key": key_text(self.key),
                "lines": {source: lines for source, lines in self.sources.items()}}


class JoinedRequest:
    """One request reconstructed from a run record plus its request/result exchange lines.

    A missing part degrades the record (its facts become unknown); a duplicated key or a
    mismatched identity makes it unusable, because then no single request exists to describe.
    """

    def __init__(self, key):
        self.key = key
        self.run = None
        self.request = None
        self.result = None
        self.problems = []
        self.ambiguous = False

    # -- identity -----------------------------------------------------------
    def identity(self):
        values = {}
        for field in IDENTITY_FIELDS + OPTIONAL_IDENTITY_FIELDS:
            seen = []
            for entry in (self.run, self.request, self.result):
                if entry is None:
                    continue
                value = entry.get(field)
                if value is not None and value != "":
                    seen.append(value)
            values[field] = seen
        return values

    def validate(self):
        """Record explicit problems instead of picking a winner."""
        if self.run is None:
            self.problems.append("missing_run_record")
        if self.request is None:
            self.problems.append("missing_request_exchange")
        if self.result is None:
            self.problems.append("missing_result_exchange")
        for field, seen in self.identity().items():
            if len({str(value) for value in seen}) > 1:
                self.problems.append("identity_mismatch:" + field)
        return self

    @property
    def identity_mismatched(self):
        return any(problem.startswith("identity_mismatch:") for problem in self.problems)

    @property
    def usable(self):
        """False when two records claim the same key or their identity fields disagree."""
        return not self.ambiguous and not self.identity_mismatched

    @property
    def degraded(self):
        return bool(self.problems)

    # -- convenience --------------------------------------------------------
    @property
    def seat_id(self):
        return self.key[1]

    @property
    def season(self):
        return self.key[2]

    @property
    def request_index(self):
        return self.key[3]

    @property
    def run_id(self):
        return self.key[0]

    @property
    def turn_token(self):
        for entry in (self.run, self.request, self.result):
            token = _as_int(entry.get("turn_token")) if entry is not None else None
            if token is not None:
                return token
        return None

    def cites(self):
        data = {}
        for name in ("run", "request", "result"):
            entry = getattr(self, name)
            if entry is not None:
                data[name] = entry.cite
        return data

    # -- records ------------------------------------------------------------
    def observation(self):
        """(observation dict, status, source). Never returns partial JSON as if it were whole."""
        for name, entry in (("run", self.run), ("request", self.request)):
            if entry is None:
                continue
            raw = entry.get("observation_json")
            if raw is None:
                continue
            if isinstance(raw, dict):
                return raw, "ok", name
            try:
                parsed = json.loads(raw)
            except (ValueError, TypeError):
                return None, "invalid_observation_json", name
            return (parsed, "ok", name) if isinstance(parsed, dict) else (None, "observation_not_object", name)
        embedded = parse_prompt(self.prompt_text()).get("observation")
        if embedded is not None:
            return embedded, "from_prompt", "request"
        return None, "unknown", None

    def observation_variants(self):
        """(run-record observation, prompt-embedded observation) as parsed dicts or None."""
        run_observation = None
        if self.run is not None:
            raw = self.run.get("observation_json")
            if isinstance(raw, dict):
                run_observation = raw
            elif isinstance(raw, str):
                try:
                    parsed = json.loads(raw)
                    run_observation = parsed if isinstance(parsed, dict) else None
                except ValueError:
                    run_observation = None
        prompt_observation = parse_prompt(self.prompt_text()).get("observation")
        return run_observation, prompt_observation

    def prompt_text(self):
        if self.request is None:
            return None
        request = self.request.get("request")
        if not isinstance(request, dict):
            return None
        user = request.get("user")
        return user if isinstance(user, str) else None

    def system_text(self):
        if self.request is None:
            return None
        request = self.request.get("request")
        if not isinstance(request, dict):
            return None
        system = request.get("system")
        return system if isinstance(system, str) else None

    def response_fields(self):
        """Whitelisted provider fields (never raw_body/content_text/reasoning/detail)."""
        if self.result is None:
            return {}
        response = self.result.get("response")
        if not isinstance(response, dict):
            return {}
        return {key: response[key] for key in RESULT_RESPONSE_FIELDS if key in response}

    def outcome(self):
        if self.result is None:
            return {}
        outcome = self.result.get("outcome")
        return outcome if isinstance(outcome, dict) else {}

    def command(self):
        """(command dict or None, source) from the engine-side outcome first."""
        for name, raw in (("result", self.outcome().get("command_json")),
                          ("run", self.run.get("command_json") if self.run is not None else None)):
            if raw is None:
                continue
            if isinstance(raw, dict):
                return raw, name
            try:
                parsed = json.loads(raw)
            except (ValueError, TypeError):
                return None, name + ":invalid_command_json"
            if isinstance(parsed, dict):
                return parsed, name
            return None, name + ":command_not_object"
        return None, "unknown"

    def model_reply(self):
        """(reply dict or None, status) from the run record's own reply text.

        Parsed in memory only; the raw text is never emitted, so provider `raw_body`,
        `content_text` and reasoning fields cannot reach a report.
        """
        raw = self.run.get("raw_model_text") if self.run is not None else None
        if raw is None:
            return None, "unknown"
        if isinstance(raw, dict):
            return raw, "ok"
        try:
            parsed = json.loads(raw)
        except (ValueError, TypeError):
            return None, "invalid_model_text"
        if isinstance(parsed, dict):
            return parsed, "ok"
        return None, "model_text_not_object"

    def deliberation_event(self):
        value = self.run.get("deliberation_event") if self.run is not None else None
        return value if isinstance(value, dict) else None

    def inquiry_tool(self):
        value = self.run.get("inquiry_tool") if self.run is not None else None
        return value if isinstance(value, str) and value.strip() else None

    def request_kind(self):
        """command | inquiry | note | no_command, from the recorded reply and runner fields."""
        command, _ = self.command()
        if isinstance(command, dict) and command.get("type"):
            return REQUEST_KIND_COMMAND
        reply, _ = self.model_reply()
        if isinstance(reply, dict):
            if "tool" in reply:
                return REQUEST_KIND_INQUIRY
            if "note" in reply:
                return REQUEST_KIND_NOTE
        event = self.deliberation_event()
        if event is not None:
            if event.get("kind") == "inquiry":
                return REQUEST_KIND_INQUIRY
            if event.get("kind") == "note":
                return REQUEST_KIND_NOTE
        if self.inquiry_tool() is not None:
            return REQUEST_KIND_INQUIRY
        return REQUEST_KIND_NONE

    def acceptance(self):
        """Acceptance comes from result_ok only; the prompt is never evidence of legality."""
        value = self.outcome().get("result_ok")
        if isinstance(value, bool):
            return value
        if self.run is not None and isinstance(self.run.get("result_ok"), bool):
            return self.run.get("result_ok")
        return None

    def parse_ok(self):
        value = self.outcome().get("parse_ok")
        if isinstance(value, bool):
            return value
        if self.run is not None and isinstance(self.run.get("parse_ok"), bool):
            return self.run.get("parse_ok")
        return None

    def result_error(self):
        value = self.outcome().get("result_error")
        if value is None and self.run is not None:
            value = self.run.get("result_error")
        return value if isinstance(value, str) and value.strip() else None

    def to_json(self):
        return {
            "key": {"run_id": self.run_id, "seat_id": self.seat_id, "season": self.season,
                    "request_index": self.request_index},
            "turn_token": self.turn_token,
            "sources": self.cites(),
            "problems": list(self.problems),
        }
