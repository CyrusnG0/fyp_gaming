"""Parse the exact dispatched prompt for the structural facts an evidence report needs.

Only the sections the report cites are read: the prompt-version line, the candidate options, the
MOVEMENT OPTIONS blocks and the embedded seat observation. Missing sections stay ``None`` so a
caller can report "unknown" rather than "not offered". No prompt text is ever emitted wholesale.
"""
import json
import re
from collections import OrderedDict

_CANDIDATE_RE = re.compile(r"^- type=([a-z_]+)(.*)$")
_TARGET_RE = re.compile(r"\btarget_id=(\S+)")
_ARGS_RE = re.compile(r"\bargs=(\{[^}]*\})")
_COST_RE = re.compile(r"\bcost=(\d+)")
_AFFORDABLE_RE = re.compile(r"\blikely_affordable=(yes|no)")
_LABEL_RE = re.compile(r'\blabel="([^"]*)"')
_NOTE_RE = re.compile(r'\bnote="([^"]*)"')

_ARMY_RE = re.compile(r"^- army_id=(\S+) unit_type=(\S*) position=(\d+),(\d+) "
                      r"marches_left=(\d+) budget=(\d+)(.*)$")
_OPTION_RE = re.compile(r"^(\d+),(\d+)\s+cost=(\d+)(?:\s+([a-z_]+))?")
_OPTIONS_LINE_RE = re.compile(r"^  options: (.*)$")
_TRUNCATED_RE = re.compile(r"^  \[truncated: (\d+) more\]$")
_ARMIES_TRUNCATED_RE = re.compile(r"^\[truncated: (\d+) more armies\]$")

_OBS_BEGIN = "<<<BEGIN_UNTRUSTED_OBSERVATION_JSON>>>"
_OBS_END = "<<<END_UNTRUSTED_OBSERVATION_JSON>>>"

_RP_PRICE_RE = re.compile(r"for (\d+) research points")


def research_points_price(label):
    """The research-points price embedded in a research candidate's label, or None.

    ``ActionCatalogue`` renders the command-point cost in the ``cost=`` field and the research
    points price only inside the human label, so both must be read separately.
    """
    if not isinstance(label, str):
        return None
    match = _RP_PRICE_RE.search(label)
    return int(match.group(1)) if match else None


def _parse_candidate(line):
    match = _CANDIDATE_RE.match(line)
    if not match:
        return None
    option = {"type": match.group(1), "target_id": None, "args": None, "cost": None,
              "likely_affordable": None, "label": None, "note": None}
    rest = match.group(2)
    target = _TARGET_RE.search(rest)
    if target:
        option["target_id"] = target.group(1)
    args = _ARGS_RE.search(rest)
    if args:
        try:
            parsed = json.loads(args.group(1))
            option["args"] = parsed if isinstance(parsed, dict) else None
        except ValueError:
            option["args"] = None
    cost = _COST_RE.search(rest)
    if cost:
        option["cost"] = int(cost.group(1))
    affordable = _AFFORDABLE_RE.search(rest)
    if affordable:
        option["likely_affordable"] = affordable.group(1) == "yes"
    label = _LABEL_RE.search(rest)
    if label:
        option["label"] = label.group(1)
    note = _NOTE_RE.search(rest)
    if note:
        option["note"] = note.group(1)
    return option


def _parse_option_token(token):
    match = _OPTION_RE.match(token.strip())
    if not match:
        return None
    return {"x": int(match.group(1)), "z": int(match.group(2)),
            "cost": int(match.group(3)), "kind": match.group(4)}


def _parse_movement(lines, text):
    if "MOVEMENT OPTIONS" not in text:
        return None, None
    army_lines = text.splitlines()
    start = next((index + 1 for index, line in enumerate(army_lines)
                  if line.startswith("MOVEMENT OPTIONS")), None)
    if start is None:
        return None, None
    armies = OrderedDict()
    current = None
    armies_truncated = None
    for line in army_lines[start:]:
        if line.startswith("CURRENT SEAT-FILTERED OBSERVATION"):
            break
        army = _ARMY_RE.match(line)
        if army:
            current = OrderedDict([
                ("army_id", army.group(1)), ("unit_type", army.group(2)),
                ("x", int(army.group(3))), ("z", int(army.group(4))),
                ("marches_left", int(army.group(5))), ("budget", int(army.group(6))),
                ("options", []), ("unlisted_options", None), ("blocked", None),
            ])
            blocked = re.search(r'blocked="([^"]*)"', army.group(7))
            if blocked:
                current["blocked"] = blocked.group(1)
            armies[current["army_id"]] = current
            continue
        if current is None:
            continue
        option_line = _OPTIONS_LINE_RE.match(line)
        if option_line:
            body = option_line.group(1).strip()
            if body == "(none)":
                current["options"] = []
            else:
                parsed = [_parse_option_token(token) for token in body.split("; ")]
                current["options"] = [item for item in parsed if item is not None]
            continue
        truncated = _TRUNCATED_RE.match(line)
        if truncated:
            current["unlisted_options"] = int(truncated.group(1))
            continue
        armies_dropped = _ARMIES_TRUNCATED_RE.match(line)
        if armies_dropped:
            armies_truncated = int(armies_dropped.group(1))
    return armies, armies_truncated


def parse_prompt(text):
    """Extract only the structural facts this tool reports from the exact dispatched prompt."""
    result = {
        "status": "missing",
        "missing_markers": [],
        "options": None,
        "movement": None,
        "armies_truncated": None,
        "observation": None,
        "prompt_version": None,
        "requests_left": None,
        "actions_left": None,
    }
    if not isinstance(text, str) or not text.strip():
        return result
    result["status"] = "ok"
    if _OBS_BEGIN not in text:
        result["missing_markers"].append("BEGIN_UNTRUSTED_OBSERVATION_JSON")
    if _OBS_END not in text:
        result["missing_markers"].append("END_UNTRUSTED_OBSERVATION_JSON")
    if "Respond with exactly one JSON object" not in text:
        result["missing_markers"].append("output_contract_footer")
    if result["missing_markers"]:
        result["status"] = "truncated"

    version = re.search(r"^Prompt version: (\S+)$", text, re.MULTILINE)
    if version:
        result["prompt_version"] = version.group(1)
    requests_left = re.search(r"^Requests left this turn: (\d+)$", text, re.MULTILINE)
    if requests_left:
        result["requests_left"] = int(requests_left.group(1))
    actions_left = re.search(r"^Actions left this turn: (\d+)$", text, re.MULTILINE)
    if actions_left:
        result["actions_left"] = int(actions_left.group(1))

    options = []
    for line in text.splitlines():
        if line.startswith("- type="):
            parsed = _parse_candidate(line)
            if parsed is not None:
                options.append(parsed)
    if options:
        result["options"] = options

    if _OBS_BEGIN in text and _OBS_END in text:
        start = text.index(_OBS_BEGIN) + len(_OBS_BEGIN)
        end = text.index(_OBS_END)
        try:
            observation = json.loads(text[start:end].strip())
        except ValueError:
            observation = None
        if isinstance(observation, dict):
            result["observation"] = observation

    result["movement"], result["armies_truncated"] = _parse_movement(text.splitlines(), text)
    return result


def option_text(options):
    """Stable, compact rendering of displayed movement options for evidence rows."""
    if options is None:
        return None
    return ["%d,%d cost=%d %s" % (item["x"], item["z"], item["cost"], item.get("kind") or "?")
            for item in options]


def research_lines_in(options):
    """Sorted research line ids offered by a parsed candidate-option list, or None."""
    if options is None:
        return None
    return sorted({(option.get("args") or {}).get("line")
                   for option in options
                   if option["type"] == "research" and (option.get("args") or {}).get("line")})
