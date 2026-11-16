#!/usr/bin/env python3
"""Schema checks for the LuanShi controlled-scenario audit (design section 5).

This module only inspects data handed to it; it performs no I/O beyond the helpers it exposes and
makes no network call. `luanshi_controlled_audit.py` combines it with link, usage, setup and
completeness checks so the audit stays independent of the program that wrote the evidence.
"""
import hashlib
import json
import re

MANIFEST_SCHEMA = "luanshi-controlled-scenarios/1"
DISPATCH_SCHEMA = "luanshi-controlled-dispatch/1"
SECRET_KEY = re.compile(r"^(api_?key|apikey|secret|password|passwd|authorization|bearer|credentials?|access_?token|auth_?token)$", re.I)
# The two permitted redacted forms, including JSON-escaped angle brackets as written by
# System.Text.Json. Anything else after api_key= is a credential-like value.
_REDACTED_API_KEY = r"<set>|<missing>|\\u003[cC](?:set|missing)\\u003[eE]"
SECRET_VALUE = re.compile(
    r"(sk-[A-Za-z0-9_\-]{12,}"
    r"|Bearer\s+[A-Za-z0-9._\-]{12,}"
    r"|(?i:api_?key)[\"']?\s*[:=]\s*[\"']?(?!(?:" + _REDACTED_API_KEY + r"))[^\s\"',;{]+)")

# Fixed text every live run must carry (design decision 5). It explicitly denies a dollar cap.
LIMITS_NOTE = ("Ceilings are request-count, wall-time and output-token limits only. They are NOT a "
               "dollar or token-cost cap; usage and cost may be unknown or incomplete.")

# A USD figure, or a dollar-cap assertion with a numeric value. The fixed LIMITS_NOTE contains the
# words "dollar" and "cap" but no digits, so it never matches.
USD_CLAIM = re.compile(
    r"(\$\s?\d"
    r"|\b\d+(?:\.\d+)?\s*usd\b"
    r"|\busd\s*\d+(?:\.\d+)?"
    r"|\busd[a-z_]*\s*[=:]\s*\$?\d"
    r"|\b(?:max_usd|usd_cap|dollar_cap|cost_cap|usd_budget)\s*[=:]\s*\$?\d"
    r"|\b(?:dollar|usd)\s+cap\s+(?:of\s+)?\$?\d)",
    re.I)
HEX64 = re.compile(r"[0-9a-f]{64}")
REPLY_NULLABLE = ("finish_reason", "input_tokens", "output_tokens", "reasoning_tokens",
                  "total_tokens", "http_status", "provider_request_id")
TEXT_EXTENSIONS = (".json", ".jsonl", ".ndjson", ".txt", ".log")


def sha256_bytes(data):
    return hashlib.sha256(data).hexdigest()


def sha256_text(text):
    return sha256_bytes(text.encode("utf-8"))


def sha256_file(path):
    with open(path, "rb") as handle:
        return sha256_bytes(handle.read())


def canonical_hash(flat):
    return sha256_text("".join("%s=%s\n" % (key, flat[key]) for key in sorted(flat)))


def read_json(path):
    with open(path, encoding="utf-8") as handle:
        return json.load(handle)


def read_lines(path):
    with open(path, encoding="utf-8") as handle:
        return [json.loads(line) for line in handle if line.strip()]


def is_int(value):
    return isinstance(value, int) and not isinstance(value, bool)


def need(obj, key, kind, path, problems, nullable=False):
    if not isinstance(obj, dict) or key not in obj:
        problems.append("%s.%s is required" % (path, key))
        return None
    value = obj[key]
    if value is None:
        if not nullable:
            problems.append("%s.%s must not be null" % (path, key))
        return None
    if kind == "int":
        ok = is_int(value)
    elif kind == "bool":
        ok = isinstance(value, bool)
    else:
        ok = isinstance(value, {"str": str, "list": list, "dict": dict}[kind])
    if not ok:
        problems.append("%s.%s has the wrong type" % (path, key))
        return None
    return value


def check_keys(obj, path, spec, problems):
    for key, kind in spec:
        need(obj, key, kind, path, problems)


def scan(node, path, problems):
    """Flag credential-like keys and values. Secrets are never echoed."""
    if isinstance(node, dict):
        for key, value in node.items():
            if SECRET_KEY.match(str(key)):
                problems.append("credential-like key at %s.%s" % (path, key))
            scan(value, path + "." + str(key), problems)
    elif isinstance(node, list):
        for index, item in enumerate(node):
            scan(item, "%s[%d]" % (path, index), problems)
    elif isinstance(node, str) and SECRET_VALUE.search(node):
        problems.append("credential-like value at %s (not shown)" % path)


def validate_manifest(manifest, problems):
    if not isinstance(manifest, dict):
        problems.append("manifest is not a JSON object")
        return
    if manifest.get("schema") != MANIFEST_SCHEMA:
        problems.append("manifest.schema must be " + MANIFEST_SCHEMA)
    check_keys(manifest, "manifest", (("run_id", "str"), ("branch", "str"), ("base_commit", "str"),
                                      ("head_commit", "str"), ("status", "str"), ("dirty_files", "list"),
                                      ("untracked_build_inputs", "list"), ("limitations", "list")), problems)
    provider = need(manifest, "provider", "dict", "manifest", problems)
    if provider is not None:
        check_keys(provider, "manifest.provider", (("kind", "str"), ("model", "str"),
                                                   ("status_note", "str"), ("live", "bool")), problems)
    ceilings = need(manifest, "run_ceilings", "dict", "manifest", problems)
    limit = ceilings.get("max_scenarios") if isinstance(ceilings, dict) else None
    if ceilings is not None:
        for key in ("max_scenarios", "max_run_requests", "max_run_wall_seconds"):
            if not is_int(ceilings.get(key)) or ceilings.get(key) <= 0:
                problems.append("manifest.run_ceilings.%s must be a positive integer" % key)
        if ceilings.get("unknown_usage_policy") not in ("report", "stop_run"):
            problems.append("manifest.run_ceilings.unknown_usage_policy must be report or stop_run")
    scenarios = need(manifest, "scenarios", "list", "manifest", problems)
    if isinstance(scenarios, list):
        if not scenarios:
            problems.append("manifest.scenarios must not be empty")
        if is_int(limit) and len(scenarios) > limit:
            problems.append("manifest lists %d scenarios but max_scenarios is %d" % (len(scenarios), limit))
        seen = set()
        for index, scenario in enumerate(scenarios):
            path = "manifest.scenarios[%d]" % index
            if not isinstance(scenario, dict):
                problems.append(path + " is not an object")
                continue
            check_keys(scenario, path, (("scenario_id", "str"), ("setup_hash", "str"), ("status", "str")), problems)
            if scenario.get("scenario_id") in seen:
                problems.append(path + ".scenario_id is duplicated")
            seen.add(scenario.get("scenario_id"))
            if not is_int(scenario.get("seed")):
                problems.append(path + ".seed must be an integer")
            seats = scenario.get("seats")
            if not isinstance(seats, list) or not seats or any(not isinstance(seat, str) for seat in seats):
                problems.append(path + ".seats must list seat ids")
            elif scenario.get("study_seat") not in seats:
                problems.append(path + ".study_seat is not one of the seats")
            if not is_int(scenario.get("seasons")) or scenario.get("seasons") <= 0:
                problems.append(path + ".seasons must be a positive integer")
            if not isinstance(scenario.get("script_sha256"), str) or not HEX64.fullmatch(scenario["script_sha256"]):
                problems.append(path + ".script_sha256 must be 64 lowercase hex characters")
    usage = need(manifest, "usage", "dict", "manifest", problems)
    if usage is not None:
        for key in ("input_tokens", "output_tokens"):
            value = usage.get(key)
            if value is not None and (not is_int(value) or value < 0):
                problems.append("manifest.usage.%s must be a non-negative integer or null" % key)
        if not is_int(usage.get("unknown_requests")) or usage.get("unknown_requests") < 0:
            problems.append("manifest.usage.unknown_requests must be a non-negative integer")
        need(usage, "source", "str", "manifest.usage", problems)
        if is_int(usage.get("unknown_requests")) and usage["unknown_requests"] > 0:
            if usage.get("input_tokens") is not None or usage.get("output_tokens") is not None:
                problems.append("manifest.usage totals must be null when usage is unknown; never a partial or zero total")


def validate_prompt(prompt, path, problems):
    system = need(prompt, "system", "str", path, problems)
    user = need(prompt, "user", "str", path, problems)
    check_keys(prompt, path, (("system_sha256", "str"), ("user_sha256", "str"), ("prompt_version", "str"),
                              ("schema_hash", "str"), ("visible_options", "dict"), ("truncation", "dict")), problems)
    for key, text in (("system_sha256", system), ("user_sha256", user)):
        recorded = prompt.get(key)
        if isinstance(recorded, str) and not HEX64.fullmatch(recorded):
            problems.append("%s.%s must be 64 lowercase hex characters" % (path, key))
        elif isinstance(recorded, str) and isinstance(text, str) and sha256_text(text) != recorded:
            problems.append("%s.%s does not match the saved prompt" % (path, key))
    visible = prompt.get("visible_options")
    truncation_unknown = (prompt.get("truncation") or {}).get("unknown_fields") \
        if isinstance(prompt.get("truncation"), dict) else []
    if isinstance(visible, dict):
        catalogue = visible.get("catalogue")
        if catalogue is None:
            if not isinstance(truncation_unknown, list) or "prompt.visible_options" not in truncation_unknown:
                problems.append(path + ".visible_options.catalogue must be a list")
        elif not isinstance(catalogue, list):
            problems.append(path + ".visible_options.catalogue must be a list")
        for key in ("catalogue_text", "movement_json", "attacks_json"):
            if visible.get(key) is not None and not isinstance(visible[key], str):
                problems.append("%s.visible_options.%s must be a string or null" % (path, key))
    truncation = prompt.get("truncation")
    if isinstance(truncation, dict):
        for key in ("sections_present", "unknown_fields"):
            if not isinstance(truncation.get(key), list):
                problems.append("%s.truncation.%s must be a list" % (path, key))
        for key in ("prompt_chars", "prompt_limit"):
            value = truncation.get(key)
            if value is not None and (not is_int(value) or value < 0):
                problems.append("%s.truncation.%s must be a non-negative integer or null" % (path, key))


def validate_reply(reply, path, problems):
    need(reply, "source", "str", path, problems)
    unknown = reply.get("unknown_fields")
    if not isinstance(unknown, list):
        problems.append(path + ".unknown_fields must be a list")
        unknown = []
    # A refused, abandoned or still-in-flight dispatch has no response: raw_text and the timing
    # fields are null (never an empty string or zero) and the names say why.
    raw = reply.get("raw_text")
    if raw is None:
        if "response_text" not in unknown:
            problems.append(path + ".raw_text is null but is not named in unknown_fields (response_text)")
    elif not isinstance(raw, str):
        problems.append(path + ".raw_text has the wrong type")
    for key in ("latency_ms", "elapsed_ms"):
        value = reply.get(key)
        if value is None:
            if key not in unknown:
                problems.append("%s.%s is null but is not named in unknown_fields" % (path, key))
        elif not is_int(value) or value < 0:
            problems.append("%s.%s must be a non-negative integer" % (path, key))
    for field in unknown:
        if not isinstance(field, str):
            problems.append(path + ".unknown_fields entries must be strings")
            break
        if field in reply and reply[field] is not None:
            problems.append("%s.%s is named unknown but is not null" % (path, field))
    for field in REPLY_NULLABLE:
        if field not in reply:
            problems.append("%s.%s is required (null plus unknown_fields when unknown)" % (path, field))
        elif reply[field] is None and field not in unknown:
            problems.append("%s.%s is null but is not named in unknown_fields" % (path, field))
    for key in ("input_tokens", "output_tokens", "reasoning_tokens", "total_tokens"):
        value = reply.get(key)
        if value is not None and (not is_int(value) or value < 0):
            problems.append("%s.%s must be a non-negative integer or null (unknown is never zero)" % (path, key))


def validate_action(action, path, problems):
    kind = need(action, "kind", "str", path, problems)
    if kind not in (None, "action", "inquiry", "note"):
        problems.append(path + ".kind must be action, inquiry or note")
    need(action, "command_json", "str", path, problems, nullable=True)
    if action.get("accepted") is not None and not isinstance(action["accepted"], bool):
        problems.append(path + ".accepted must be a boolean or null")
    if kind == "inquiry":
        inquiry = action.get("inquiry")
        if not isinstance(inquiry, dict):
            problems.append(path + ".inquiry must be an object for an inquiry")
        elif not is_int(inquiry.get("result_chars")) or not isinstance(inquiry.get("ok"), bool):
            problems.append(path + ".inquiry needs integer result_chars and boolean ok")
    if kind == "note" and not isinstance(action.get("note"), dict):
        problems.append(path + ".note must be an object for a note")


def validate_dispatch(dispatch, index, manifest, problems):
    path = "dispatch[%d]" % index
    if not isinstance(dispatch, dict):
        problems.append(path + " is not an object")
        return
    if dispatch.get("schema") != DISPATCH_SCHEMA:
        problems.append(path + ".schema must be " + DISPATCH_SCHEMA)
    check_keys(dispatch, path, (("run_id", "str"), ("scenario_id", "str"), ("dispatch_id", "str"),
                                ("commit", "str"), ("base_commit", "str"), ("seat_id", "str"),
                                ("status", "str"), ("dirty", "bool"), ("provider", "dict"),
                                ("settings", "dict"), ("prompt", "dict"), ("reply", "dict"),
                                ("explanation", "dict"), ("action", "dict"), ("state", "dict"),
                                ("links", "dict")), problems)
    for key in ("seed", "turn_token", "season", "request_index", "seat_request_index"):
        if not is_int(dispatch.get(key)):
            problems.append("%s.%s must be an integer" % (path, key))
    if isinstance(manifest, dict):
        if dispatch.get("run_id") != manifest.get("run_id"):
            problems.append(path + ".run_id does not match the manifest")
        scenario = next((s for s in manifest.get("scenarios", [])
                         if isinstance(s, dict) and s.get("scenario_id") == dispatch.get("scenario_id")), None)
        if scenario is None:
            problems.append(path + ".scenario_id is not in the manifest")
        else:
            if dispatch.get("seed") != scenario.get("seed"):
                problems.append(path + ".seed does not match the scenario seed")
            if dispatch.get("seat_id") not in (scenario.get("seats") or []):
                problems.append(path + ".seat_id is not one of the scenario seats")
        for key, manifest_key in (("base_commit", "base_commit"), ("commit", "head_commit")):
            left, right = dispatch.get(key), manifest.get(manifest_key)
            if isinstance(left, str) and isinstance(right, str) and not (left.startswith(right) or right.startswith(left)):
                problems.append("%s.%s does not match manifest.%s" % (path, key, manifest_key))
    provider, settings = dispatch.get("provider"), dispatch.get("settings")
    if isinstance(provider, dict):
        check_keys(provider, path + ".provider", (("kind", "str"), ("model", "str"),
                                                  ("usage_source", "str"), ("live", "bool")), problems)
    if isinstance(settings, dict):
        check_keys(settings, path + ".settings", (("settings_hash", "str"), ("config_redacted", "str"),
                                                  ("flags", "dict"), ("ceilings", "dict")), problems)
        flags = settings.get("flags")
        if isinstance(flags, dict):
            for key in ("attack", "research", "manual_lookup", "deliberation"):
                if not isinstance(flags.get(key), bool):
                    problems.append("%s.settings.flags.%s must be a boolean" % (path, key))
    prompt, reply = dispatch.get("prompt"), dispatch.get("reply")
    if isinstance(prompt, dict):
        validate_prompt(prompt, path + ".prompt", problems)
    if isinstance(reply, dict):
        validate_reply(reply, path + ".reply", problems)
    explanation = dispatch.get("explanation")
    if isinstance(explanation, dict):
        check_keys(explanation, path + ".explanation", (("text", "str"), ("source", "str")), problems)
        if explanation.get("reasoning_text_captured") is not False:
            problems.append(path + ".explanation.reasoning_text_captured must be false; telemetry is never an explanation")
    action = dispatch.get("action")
    if isinstance(action, dict):
        validate_action(action, path + ".action", problems)
    state = dispatch.get("state")
    if isinstance(state, dict):
        state_unknown = dispatch.get("unknown_fields")
        state_unknown = state_unknown if isinstance(state_unknown, list) else []
        for side in ("before", "after"):
            snapshot = state.get(side)
            if not isinstance(snapshot, dict):
                problems.append("%s.state.%s must be an object" % (path, side))
                continue
            # A dispatch with no response may have no after-state; the name says so.
            allow_missing = ("after_state" in state_unknown or "state.after" in state_unknown) \
                if side == "after" else "state.before" in state_unknown
            for key in ("observation_sha256", "observation_json", "canonical_world_digest"):
                value = snapshot.get(key)
                if value is None and allow_missing:
                    continue
                if not isinstance(value, str):
                    problems.append("%s.state.%s.%s must be a string" % (path, side, key))
    links = dispatch.get("links")
    if isinstance(links, dict):
        for key in ("run_record", "exchange_request", "exchange_result", "prompt_capture"):
            ref = links.get(key)
            if not isinstance(ref, str) or ":" not in ref or not ref.rsplit(":", 1)[1].isdigit():
                problems.append("%s.links.%s must be '<file>:<line>'" % (path, key))
