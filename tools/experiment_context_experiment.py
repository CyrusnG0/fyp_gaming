"""Join every input file into one experiment and index the optional spectator summary.

Summary turns and scoreboards are keyed only by seat and season. That is unambiguous for a
single-run summary, but a summary covering several runs (or a run file holding several run ids)
cannot be split by those keys alone, so it is diagnosed as ambiguous instead of guessed.
"""
import json
from collections import OrderedDict

from experiment_context_loader import (EXCHANGE_REQUEST, EXCHANGE_RESULT, EXPERIMENT_CONFIG,
                                       RUN_RECORD, TURN_END, Duplicate, JoinedRequest, classify,
                                       composite_key, key_text, read_jsonl, turn_key)
from experiment_context_paths import absolute_path, display_path, sha256_of


def _clean_key_part(value, integer=False):
    if integer:
        if isinstance(value, bool) or not isinstance(value, int):
            return None
        return value
    if value is None:
        return None
    text = str(value).strip()
    return text or None


class Experiment:
    """All parsed evidence for one experiment, already joined and diagnosed."""

    def __init__(self, run_entries, exchange_entries, summary, sources):
        self.sources = sources
        self.summary = summary if isinstance(summary, dict) else None
        self.summary_ambiguous = False
        self.diagnostics = []
        self.joined = OrderedDict()
        self.turn_ends = OrderedDict()
        self.experiment_configs = []
        self.summary_turns = OrderedDict()
        self.summary_seasons = OrderedDict()
        self.duplicate_summary_seasons = set()
        self._load(run_entries, exchange_entries)

    # -- loading ------------------------------------------------------------
    def _load(self, run_entries, exchange_entries):
        groups = {RUN_RECORD: OrderedDict(), EXCHANGE_REQUEST: OrderedDict(),
                  EXCHANGE_RESULT: OrderedDict()}
        turn_groups = OrderedDict()
        seen_exchange_ids = OrderedDict()

        for entry in run_entries + exchange_entries:
            kind = classify(entry)
            if entry.obj is None:
                self.diagnostics.append(entry.diagnostic())
                continue
            if kind in (RUN_RECORD, EXCHANGE_REQUEST, EXCHANGE_RESULT):
                key = composite_key(entry.obj)
                if key is None:
                    self.diagnostics.append({
                        "kind": "record_without_composite_key", "record_kind": kind,
                        "source": entry.source, "line": entry.line})
                    continue
                groups[kind].setdefault(key, []).append(entry)
                exchange_id = entry.get("exchange_id")
                if exchange_id is not None:
                    seen_exchange_ids.setdefault(str(exchange_id), []).append(
                        (key, entry.source, entry.line))
            elif kind == TURN_END:
                key = turn_key(entry.obj)
                if key is None:
                    self.diagnostics.append({
                        "kind": "turn_end_without_turn_key", "source": entry.source,
                        "line": entry.line})
                    continue
                turn_groups.setdefault(key, []).append(entry)
            elif kind == EXPERIMENT_CONFIG:
                self.experiment_configs.append(entry)
            else:
                self.diagnostics.append({
                    "kind": "unrecognised_exchange_event", "event": entry.get("exchange_event"),
                    "source": entry.source, "line": entry.line})

        self._record_duplicates(groups)
        self._record_legacy_exchange_ids(seen_exchange_ids)

        for key in sorted(set(groups[RUN_RECORD]) | set(groups[EXCHANGE_REQUEST])
                          | set(groups[EXCHANGE_RESULT])):
            joins = {kind: groups[kind].get(key, []) for kind in groups}
            request = JoinedRequest(key)
            request.run = joins[RUN_RECORD][0] if len(joins[RUN_RECORD]) == 1 else None
            request.request = joins[EXCHANGE_REQUEST][0] if len(joins[EXCHANGE_REQUEST]) == 1 else None
            request.result = joins[EXCHANGE_RESULT][0] if len(joins[EXCHANGE_RESULT]) == 1 else None
            request.ambiguous = any(len(joins[kind]) > 1 for kind in groups)
            if request.ambiguous:
                self.diagnostics.append({
                    "kind": "ambiguous_composite_key", "key": key_text(key),
                    "counts": {kind: len(values) for kind, values in joins.items()}})
            else:
                request.validate()
                for problem in request.problems:
                    self.diagnostics.append({
                        "kind": "join_problem", "problem": problem, "key": key_text(key),
                        "sources": request.cites()})
                for conflict in request.conflicts:
                    self.diagnostics.append({
                        "kind": "representation_conflict", "field": conflict["field"],
                        "key": key_text(key), "sources": request.cites(),
                        "representations": conflict.get("sources")})
            self.joined[key] = request

        for key, entries in turn_groups.items():
            if len(entries) > 1:
                self.diagnostics.append({
                    "kind": "duplicate_turn_end", "key": key_text(key),
                    "lines": [entry.line for entry in entries],
                    "sources": sorted({entry.source for entry in entries})})
                continue
            self.turn_ends[key] = entries[0]

        self._load_summary()

    def _record_duplicates(self, groups):
        for kind, mapping in groups.items():
            for key, entries in mapping.items():
                if len(entries) > 1:
                    duplicate = Duplicate(key)
                    for entry in entries:
                        duplicate.add(entry)
                    record = duplicate.to_json()
                    record["record_kind"] = kind
                    self.diagnostics.append(record)

    def _record_legacy_exchange_ids(self, seen):
        """exchange_id is run:index only; flag any id that repeats across composite keys."""
        for exchange_id, occurrences in seen.items():
            keys = sorted({item[0] for item in occurrences})
            if len(keys) > 1:
                self.diagnostics.append({
                    "kind": "legacy_exchange_id_collision",
                    "exchange_id": exchange_id,
                    "distinct_composite_keys": len(keys),
                    "example_keys": [key_text(key) for key in keys[:3]],
                    "note": "exchange_id is never used as a join key"})

    def _load_summary(self):
        if not self.summary:
            return
        run_ids = {request.run_id for request in self.joined.values() if request.run_id}
        summary_run_ids = self.summary.get("run_ids")
        if not isinstance(summary_run_ids, list):
            summary_run_ids = [self.summary.get("run_id")]
        summary_run_ids = {str(value) for value in summary_run_ids if value}
        if run_ids and summary_run_ids and not (run_ids & summary_run_ids):
            self.diagnostics.append({
                "kind": "summary_run_id_mismatch",
                "summary_run_ids": sorted(summary_run_ids),
                "run_ids": sorted(run_ids),
                "note": "summary turns are not joined to this run"})
            self.summary = None
            return
        if len(summary_run_ids) > 1 or len(run_ids) > 1:
            # seat/season alone cannot identify a turn or scoreboard across several runs.
            self.summary_ambiguous = True
            self.diagnostics.append({
                "kind": "summary_scope_ambiguous",
                "summary_run_ids": sorted(summary_run_ids),
                "run_ids": sorted(run_ids),
                "note": "summary turns and scoreboards are keyed by seat and season only, so "
                        "they are not used for a multi-run summary"})
            return
        for turn in self.summary.get("turns") or []:
            if not isinstance(turn, dict):
                continue
            key = (_clean_key_part(turn.get("seat_id")),
                   _clean_key_part(turn.get("season"), integer=True))
            if None in key:
                continue
            if key in self.summary_turns:
                self.diagnostics.append({"kind": "duplicate_summary_turn", "key": list(key)})
                self.summary_turns[key] = None
                continue
            self.summary_turns[key] = turn
        for season in self.summary.get("seasons") or []:
            if not isinstance(season, dict):
                continue
            number = _clean_key_part(season.get("season"), integer=True)
            if number is None:
                continue
            if number in self.summary_seasons:
                self.diagnostics.append({"kind": "duplicate_summary_season",
                                         "season": number})
                self.summary_seasons[number] = None
                self.duplicate_summary_seasons.add(number)
                continue
            self.summary_seasons[number] = season

    # -- queries ------------------------------------------------------------
    def clean_requests(self):
        """Usable requests, including degraded ones whose missing parts are reported as unknown."""
        return [request for request in self.joined.values() if request.usable]

    def requests_for(self, seat_id, season):
        return sorted((request for request in self.clean_requests()
                       if request.seat_id == seat_id and request.season == season),
                      key=lambda request: request.request_index)

    def excluded_for(self, seat_id, season):
        """Joined keys with duplicates or identity mismatches: counted, never silently dropped."""
        return sorted((request for request in self.joined.values()
                       if request.seat_id == seat_id and request.season == season
                       and not request.usable),
                      key=lambda request: request.request_index)

    def degraded_for(self, seat_id, season):
        """Usable requests with a missing part: analysed, with the missing facts left unknown."""
        return sorted((request for request in self.clean_requests()
                       if request.seat_id == seat_id and request.season == season
                       and request.degraded),
                      key=lambda request: request.request_index)

    def seats(self):
        seats = OrderedDict()
        for request in self.joined.values():
            seats.setdefault(request.seat_id, set()).add(request.season)
        for key in self.summary_turns:
            if key[0]:
                seats.setdefault(key[0], set()).add(key[1])
        return OrderedDict((seat, sorted(seasons)) for seat, seasons in sorted(seats.items()))

    def summary_season(self, season):
        """The summary's season entry, or None (including the duplicate-entry case)."""
        if self.summary_ambiguous:
            return None
        return self.summary_seasons.get(season)

    def turn_end_for(self, seat_id, season):
        matching = [request for request in self.joined.values()
                    if request.seat_id == seat_id and request.season == season]
        tokens = sorted({request.turn_token for request in matching
                         if request.turn_token is not None})
        run_ids = sorted({request.run_id for request in matching if request.run_id})
        if len(tokens) > 1:
            return {"reason": "unknown", "source": "ambiguous_turn_tokens",
                    "turn_tokens": tokens}
        if len(tokens) != 1 or len(run_ids) != 1:
            return None
        entry = self.turn_ends.get((run_ids[0], seat_id, season, tokens[0]))
        if entry is None:
            return None
        return {"reason": entry.get("end_reason") or "unknown",
                "stopped_by_ceiling": entry.get("stopped_by_ceiling"),
                "stop_ceiling": entry.get("stop_ceiling"),
                "source": "turn_end sidecar", "sources": [entry.cite]}

    def diagnostic_counts(self):
        counts = OrderedDict()
        for item in self.diagnostics:
            kind = item.get("kind") or "unknown"
            counts[kind] = counts.get(kind, 0) + 1
        return counts


def load_experiment(run_path, exchange_path=None, summary_path=None, source_label=None,
                    repo_root=None):
    """Read and join every supplied file. Returns (Experiment, sources metadata)."""
    def source_name(path, fallback):
        if source_label:
            return "%s:%s" % (source_label, path.name)
        return path.name if path is not None else fallback

    absolute_run = absolute_path(run_path)
    run_source = source_name(run_path, "run")
    run_entries = read_jsonl(absolute_run, run_source)
    exchange_entries = []
    exchange_source = None
    absolute_exchange = absolute_path(exchange_path)
    if absolute_exchange is not None:
        exchange_source = source_name(exchange_path, "exchanges")
        exchange_entries = read_jsonl(absolute_exchange, exchange_source)

    summary = None
    summary_meta = {"status": "missing"}
    absolute_summary = absolute_path(summary_path)
    if absolute_summary is not None:
        try:
            with open(absolute_summary, "r", encoding="utf-8-sig") as stream:
                summary = json.load(stream)
            summary_meta = {"status": "ok" if isinstance(summary, dict) else "not_an_object"}
            if not isinstance(summary, dict):
                summary = None
        except ValueError:
            summary_meta = {"status": "invalid_json"}
        except OSError:
            summary_meta = {"status": "unreadable"}

    sources = {
        "run": {"path": display_path(absolute_run, repo_root), "absolute_path": absolute_run,
                "source": run_source, "lines": len(run_entries)},
        "exchanges": ({"path": display_path(absolute_exchange, repo_root),
                       "absolute_path": absolute_exchange, "source": exchange_source,
                       "lines": len(exchange_entries)}
                      if absolute_exchange is not None else {"status": "missing"}),
        "summary": dict(summary_meta, path=display_path(absolute_summary, repo_root),
                        absolute_path=absolute_summary),
    }
    experiment = Experiment(run_entries, exchange_entries, summary, sources)
    return experiment, sources


def input_metadata(sources):
    """Portable input table: display path plus a hash of the absolute file (CWD independent)."""
    data = {}
    for key, info in sources.items():
        item = {"path": info.get("path"), "source": info.get("source"),
                "lines": info.get("lines"), "status": info.get("status") or "loaded"}
        absolute = info.get("absolute_path")
        if absolute:
            try:
                item["sha256"] = sha256_of(absolute)
            except OSError:
                item["sha256"] = None
        data[key] = item
    return data
