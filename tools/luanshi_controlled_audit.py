#!/usr/bin/env python3
"""Independent audit of a saved LuanShi controlled-scenario run folder.

Checks design section 5 evidence (run-manifest.json, dispatches.ndjson), link joins, prompt
sha256, recomputed token totals, null-not-zero unknown usage, setup/script reproduction, and
credential patterns. Live-labelled runs are additionally checked for provider source labels, the
provider identity block, the exact limits note, redacted api_key forms and USD/dollar-cap claims;
the audit still cannot prove a live run was valid or that the model reasoned. Incomplete/stopped/
failed scenarios are reported as NOT pass. No network call is made and no environment credential
is read. Schema details live in luanshi_controlled_audit_schema.py.

  audit <run-dir> [--json]
Exit codes: 0 consistent and complete; 1 schema/evidence mismatch; 2 unreadable run folder;
3 records consistent but incomplete.
"""
import argparse
import json
import os
import re
import sys

from luanshi_controlled_audit_schema import (
    LIMITS_NOTE, MANIFEST_SCHEMA, REPLY_NULLABLE, SECRET_VALUE, TEXT_EXTENSIONS, USD_CLAIM,
    canonical_hash, is_int, need, read_json, read_lines, scan, sha256_file, sha256_text,
    validate_dispatch, validate_manifest)


class AuditError(Exception):
    pass


def parse_ref(ref):
    if not isinstance(ref, str) or ":" not in ref:
        return None
    name, number = ref.rsplit(":", 1)
    return (name, int(number)) if name and number.isdigit() and int(number) > 0 else None


def find_scenario_dir(run_dir, scenario_id):
    for base, directories, files in os.walk(run_dir):
        if os.path.relpath(base, run_dir).count(os.sep) >= 3:
            directories[:] = []
            continue
        if "scenario.json" not in files:
            continue
        try:
            data = read_json(os.path.join(base, "scenario.json"))
        except (OSError, ValueError):
            continue
        if isinstance(data, dict) and data.get("scenario_id") == scenario_id:
            return base
    return None


def locate(run_dir, scenario_dir, name):
    for candidate in ([os.path.join(scenario_dir, name)] if scenario_dir else []) + [os.path.join(run_dir, name)]:
        if os.path.isfile(candidate):
            return candidate
    return None


def check_links(dispatch, index, run_dir, scenario_dir, cache, seen, problems):
    path = "dispatch[%d]" % index
    prompt = dispatch.get("prompt") or {}
    for key in ("run_record", "exchange_request", "exchange_result", "prompt_capture"):
        parsed = parse_ref((dispatch.get("links") or {}).get(key))
        if parsed is None:
            continue                          # reported by schema validation
        name, number = parsed
        file_path = locate(run_dir, scenario_dir, name)
        if file_path is None:
            problems.append("%s.links.%s refers to missing file %s" % (path, key, name))
            continue
        absolute = os.path.abspath(file_path)
        if absolute not in cache:
            try:
                cache[absolute] = read_lines(file_path)
            except (OSError, ValueError) as error:
                problems.append("%s.links.%s cannot be read: %s" % (path, key, error))
                cache[absolute] = None
        lines = cache[absolute]
        if lines is None:
            continue
        if number > len(lines):
            problems.append("%s.links.%s line %d is outside %s (%d line(s))" % (path, key, number, name, len(lines)))
            continue
        marker = (absolute, number)
        if marker in seen[key]:
            problems.append("%s.links.%s reuses %s:%d already used by another dispatch" % (path, key, name, number))
        seen[key].add(marker)
        line = lines[number - 1]
        if key == "prompt_capture" and isinstance(line, dict):
            for field in ("system", "user", "system_sha256", "user_sha256"):
                if line.get(field) is not None and line.get(field) != prompt.get(field):
                    problems.append("%s: prompt capture %s differs from the dispatch prompt" % (path, field))
        elif key in ("exchange_request", "exchange_result") and isinstance(line, dict):
            expected = "request" if key == "exchange_request" else "result"
            event = line.get("exchange_event")
            if event is not None and event != expected:
                problems.append("%s.links.%s points to exchange_event %s, expected %s" % (path, key, event, expected))
            request = line.get("request")
            if key == "exchange_request" and isinstance(request, dict):
                for field in ("system", "user"):
                    if request.get(field) is not None and request.get(field) != prompt.get(field):
                        problems.append("%s: exchange request %s differs from the dispatch prompt" % (path, field))
        elif key == "run_record" and isinstance(line, dict):
            raw = (dispatch.get("reply") or {}).get("raw_text")
            recorded = line.get("raw_model_text")
            # The production recorder truncates long replies; compare only short, untruncated pairs.
            if isinstance(raw, str) and isinstance(recorded, str) and len(raw) < 4000 and len(recorded) < 4000 and raw != recorded:
                problems.append(path + ": run record raw_model_text differs from the dispatch reply")
            for field in ("seat_id", "turn_token"):
                if line.get(field) is not None and line.get(field) != dispatch.get(field):
                    problems.append("%s: run record %s differs from the dispatch" % (path, field))


def recompute_usage(manifest, dispatches, problems, notes):
    usage = manifest.get("usage") if isinstance(manifest, dict) else None
    if not isinstance(usage, dict) or not dispatches:
        return
    unknown, total_input, total_output = [], 0, 0
    for dispatch in dispatches:
        reply = dispatch.get("reply") or {}
        input_tokens, output_tokens = reply.get("input_tokens"), reply.get("output_tokens")
        if input_tokens is None or output_tokens is None:
            unknown.append(dispatch.get("dispatch_id"))
            continue
        total_input += input_tokens
        total_output += output_tokens
    if unknown:
        if usage.get("input_tokens") is not None or usage.get("output_tokens") is not None:
            problems.append("manifest.usage input/output totals must be null when %d request(s) have unknown usage, never a partial or zero total" % len(unknown))
        if usage.get("unknown_requests") != len(unknown):
            problems.append("manifest.usage.unknown_requests is %s but %d dispatch(es) have null usage" % (usage.get("unknown_requests"), len(unknown)))
        notes.append("usage unknown for %d of %d request(s); totals are null, not zero" % (len(unknown), len(dispatches)))
    else:
        if usage.get("input_tokens") != total_input:
            problems.append("manifest.usage.input_tokens is %s but the dispatches sum to %d" % (usage.get("input_tokens"), total_input))
        if usage.get("output_tokens") != total_output:
            problems.append("manifest.usage.output_tokens is %s but the dispatches sum to %d" % (usage.get("output_tokens"), total_output))
        if usage.get("unknown_requests") != 0:
            problems.append("manifest.usage.unknown_requests is %s but every dispatch has usage" % usage.get("unknown_requests"))


def setup_signature(scenario_dir):
    """Hashes of the saved setup inputs, used to prove a replay starts from the recorded scenario."""
    return {name: sha256_file(os.path.join(scenario_dir, name)) if os.path.isfile(os.path.join(scenario_dir, name)) else None
            for name in ("scenario.json", "settings.json", "script.json")}


def check_setup(run_dir, manifest, problems, notes):
    for scenario in manifest.get("scenarios", []):
        if not isinstance(scenario, dict):
            continue
        scenario_id = scenario.get("scenario_id")
        if scenario.get("status") == "skipped":
            # A skipped scenario was never executed, so there is no saved per-scenario setup to
            # verify; the manifest still carries the staged hashes and the run stays incomplete.
            notes.append("%s: skipped, no saved setup to verify" % scenario_id)
            continue
        scenario_dir = find_scenario_dir(run_dir, scenario_id)
        if scenario_dir is None:
            problems.append("%s: missing scenario directory containing scenario.json" % scenario_id)
            continue
        try:
            saved = read_json(os.path.join(scenario_dir, "scenario.json"))
        except (OSError, ValueError) as error:
            problems.append("%s: cannot read scenario.json: %s" % (scenario_id, error))
            saved = None
        if isinstance(saved, dict) and saved.get("setup_hash") is not None:
            if saved["setup_hash"] != scenario.get("setup_hash"):
                problems.append("%s: scenario.json setup_hash does not match the manifest" % scenario_id)
        elif scenario.get("setup_hash"):
            notes.append("%s: setup_hash cannot be re-derived from scenario.json; relying on the saved setup files" % scenario_id)
        script_path = os.path.join(scenario_dir, "script.json")
        if not os.path.isfile(script_path):
            problems.append("%s: missing script.json" % scenario_id)
        else:
            canonical = None
            try:
                canonical = sha256_text(json.dumps(read_json(script_path), sort_keys=True, separators=(",", ":")))
            except (OSError, ValueError):
                pass
            if scenario.get("script_sha256") not in (sha256_file(script_path), canonical):
                problems.append("%s: script.json does not match the manifest script_sha256" % scenario_id)
        settings_path = os.path.join(scenario_dir, "settings.json")
        if os.path.isfile(settings_path):
            try:
                settings = read_json(settings_path)
            except (OSError, ValueError) as error:
                problems.append("%s: cannot read settings.json: %s" % (scenario_id, error))
                settings = None
            if isinstance(settings, dict) and "settings" in settings and "settings_hash" in settings:
                if canonical_hash(settings["settings"]) != settings["settings_hash"]:
                    problems.append("%s: settings.json hash does not match its saved settings" % scenario_id)


def collect_incomplete(manifest, dispatches):
    incomplete = []
    if manifest.get("status") != "completed":
        incomplete.append("run status is %r, not 'completed'" % manifest.get("status"))
    by_scenario = {}
    for dispatch in dispatches:
        by_scenario.setdefault(dispatch.get("scenario_id"), []).append(dispatch.get("status"))
    for scenario in manifest.get("scenarios", []):
        if not isinstance(scenario, dict):
            continue
        scenario_id = scenario.get("scenario_id")
        if scenario.get("status") != "completed":
            incomplete.append("%s: scenario status is %r, NOT pass" % (scenario_id, scenario.get("status")))
        statuses = by_scenario.get(scenario_id, [])
        if not statuses:
            incomplete.append("%s: no dispatch evidence" % scenario_id)
        bad = [status for status in statuses if status != "complete"]
        if bad:
            incomplete.append("%s: %d dispatch(es) not complete (%s)" % (scenario_id, len(bad), ", ".join(sorted(set(str(s) for s in bad)))))
    return incomplete


def scan_tree(run_dir, skip, problems):
    for base, _directories, files in os.walk(run_dir):
        for name in files:
            path = os.path.join(base, name)
            if not name.lower().endswith(TEXT_EXTENSIONS) or os.path.abspath(path) in skip:
                continue
            try:
                if os.path.getsize(path) > 8 * 1024 * 1024:
                    continue
                with open(path, encoding="utf-8", errors="replace") as handle:
                    text = handle.read()
            except OSError:
                continue
            if SECRET_VALUE.search(text):
                problems.append("credential-like value in %s (not shown)" % os.path.relpath(path, run_dir))


# Provider identity fields the C# runner writes for every run. A live run must carry all of them;
# seed/scenario_id/settings_hash are scenario-specific and stay null in the run-level block.
LIVE_PROVIDER_FIELDS = (
    "kind", "model", "live", "base_url_display", "env_var_names", "max_output_tokens",
    "request_timeout_ms", "effective_max_requests", "effective_max_wall_seconds",
    "commit", "dirty", "seed", "scenario_id", "settings_hash")
LIVE_ENV_NAMES = ("LUANSHI_LLM_API_KEY", "LUANSHI_LLM_BASE_URL", "LUANSHI_LLM_MODEL")
API_KEY_REDACTED = re.compile(r"(?i:api_?key)\s*=\s*([^;\s]*)")
# A display URL must not retain a token-like path segment (>=24 alnum/-/_ chars).
_SECRET_PATH_SEGMENT = re.compile(r"^[A-Za-z0-9_-]{24,}$")


def secret_like_display_path(base):
    """True when base_url_display keeps a long plain path segment that looks like a secret."""
    if not isinstance(base, str) or "://" not in base:
        return False
    rest = base.split("://", 1)[1].split("?", 1)[0].split("#", 1)[0]
    if "/" not in rest:
        return False
    path = rest.split("/", 1)[1]
    return any(_SECRET_PATH_SEGMENT.match(segment) for segment in path.split("/"))
LIVE_HONESTY = ("audit limitation: this checks internal consistency only; it cannot prove a live "
                "run was valid or that the model reasoned")


def iter_text_files(run_dir):
    """Yield (relative path, text) for the small text artefacts, without recursing forever."""
    for base, _directories, files in os.walk(run_dir):
        for name in files:
            path = os.path.join(base, name)
            if not name.lower().endswith(TEXT_EXTENSIONS):
                continue
            try:
                if os.path.getsize(path) > 8 * 1024 * 1024:
                    continue
                with open(path, encoding="utf-8", errors="replace") as handle:
                    yield os.path.relpath(path, run_dir), handle.read()
            except OSError:
                continue


def carries_string(node, target):
    if isinstance(node, str):
        return target in node
    if isinstance(node, dict):
        return any(carries_string(value, target) for value in node.values())
    if isinstance(node, list):
        return any(carries_string(item, target) for item in node)
    return False


def check_sources(manifest, dispatches, problems):
    """Live replies with text must be source=provider; a fake run never uses source=provider."""
    provider = manifest.get("provider") if isinstance(manifest, dict) else None
    live = isinstance(provider, dict) and provider.get("live") is True
    for index, dispatch in enumerate(dispatches):
        if not isinstance(dispatch, dict):
            continue
        reply = dispatch.get("reply") if isinstance(dispatch.get("reply"), dict) else {}
        source = reply.get("source")
        has_text = isinstance(reply.get("raw_text"), str) and reply.get("raw_text") != ""
        if live:
            if has_text and source != "provider":
                problems.append("dispatch[%d].reply.source is %r for a live run; every reply with "
                                "text must be 'provider' (never fake_scripted)" % (index, source))
            elif source == "fake_scripted":
                problems.append("dispatch[%d].reply.source is 'fake_scripted' in a live run" % index)
        elif source == "provider":
            problems.append("dispatch[%d].reply.source is 'provider' in a fake run" % index)
        block = dispatch.get("provider") if isinstance(dispatch.get("provider"), dict) else {}
        if not live and block.get("usage_source") == "provider_reported":
            problems.append("dispatch[%d].provider.usage_source is 'provider_reported' in a fake "
                            "run" % index)


def check_config_redacted(dispatches, run_dir, problems):
    """config_redacted may show only api_key=<set> or api_key=<missing>."""
    values = []
    for index, dispatch in enumerate(dispatches):
        settings = dispatch.get("settings") if isinstance(dispatch, dict) else None
        if isinstance(settings, dict) and "config_redacted" in settings:
            values.append(("dispatch[%d].settings.config_redacted" % index,
                           settings.get("config_redacted")))
    for base, _directories, files in os.walk(run_dir):
        for name in files:
            if not name.lower().endswith(".json"):
                continue
            path = os.path.join(base, name)
            try:
                saved = read_json(path)
            except (OSError, ValueError):
                continue
            if isinstance(saved, dict) and "config_redacted" in saved:
                values.append((os.path.relpath(path, run_dir) + ".config_redacted",
                               saved.get("config_redacted")))
    for where, text in values:
        if not isinstance(text, str):
            continue
        match = API_KEY_REDACTED.search(text)
        if match is None:
            problems.append("%s must show api_key=<set> or api_key=<missing>" % where)
        elif match.group(1) not in ("<set>", "<missing>"):
            problems.append("%s must show api_key=<set> or <missing> only (a value was present)" % where)


def check_live_usage(live, manifest, dispatches, problems, notes):
    """Unknown live usage stays null and is named/counted, never turned into zero."""
    if not live:
        return
    unknown = 0
    for index, dispatch in enumerate(dispatches):
        if not isinstance(dispatch, dict):
            continue
        provider = dispatch.get("provider") if isinstance(dispatch.get("provider"), dict) else {}
        if provider.get("live") is not True:
            continue
        if provider.get("usage_source") != "provider_reported":
            problems.append("dispatch[%d].provider.usage_source must be 'provider_reported' for a "
                            "live dispatch" % index)
        reply = dispatch.get("reply") if isinstance(dispatch.get("reply"), dict) else {}
        named = reply.get("unknown_fields") if isinstance(reply.get("unknown_fields"), list) else []
        missing = [field for field in ("input_tokens", "output_tokens") if reply.get(field) is None]
        for field in missing:
            if field not in named:
                problems.append("dispatch[%d].reply.%s is unknown on a live dispatch but is not "
                                "named in unknown_fields" % (index, field))
        if missing:
            unknown += 1
    if unknown:
        usage = manifest.get("usage") if isinstance(manifest, dict) else None
        if isinstance(usage, dict):
            if usage.get("unknown_requests") != unknown:
                problems.append("live usage is unknown for %d dispatch(es) but "
                                "manifest.usage.unknown_requests is %s (unknown is never zero)"
                                % (unknown, usage.get("unknown_requests")))
            if usage.get("input_tokens") is not None or usage.get("output_tokens") is not None:
                problems.append("live usage is unknown for %d dispatch(es); manifest totals must "
                                "be null, never zero or partial" % unknown)
        notes.append("live usage unknown for %d of %d request(s); counted as unknown, not zero"
                     % (unknown, len(dispatches)))


def check_live_block(block, path, problems, run_level=False):
    """Every live provider block carries the identity/ceiling fields with sane values."""
    if not isinstance(block, dict):
        problems.append(path + " is required for a live run")
        return
    for key in LIVE_PROVIDER_FIELDS:
        if key not in block:
            problems.append("%s.%s is required for a live run" % (path, key))
    if "kind" in block and block.get("kind") != "openai-compatible":
        problems.append(path + ".kind must be 'openai-compatible' for a live run")
    if "model" in block and (not isinstance(block.get("model"), str) or not block.get("model")):
        problems.append(path + ".model must be a non-empty string")
    if "live" in block and block.get("live") is not True:
        problems.append(path + ".live must be true")
    base = block.get("base_url_display")
    if "base_url_display" in block and (
            not isinstance(base, str) or not base.lower().startswith("https://")):
        problems.append(path + ".base_url_display must be an https scheme://host[:port]/path value")
    elif secret_like_display_path(base):
        problems.append(path + ".base_url_display must not contain a secret-like path segment")
    names = block.get("env_var_names")
    if "env_var_names" in block and (
            not isinstance(names, list) or not all(isinstance(name, str) for name in names)
            or not set(LIVE_ENV_NAMES).issubset(names)):
        problems.append(path + ".env_var_names must name " + ", ".join(LIVE_ENV_NAMES))
    tokens = block.get("max_output_tokens")
    if "max_output_tokens" in block and (not is_int(tokens) or tokens <= 0):
        problems.append(path + ".max_output_tokens must be a finite positive integer")
    for key in ("request_timeout_ms", "effective_max_requests", "effective_max_wall_seconds"):
        value = block.get(key)
        if key in block and (not is_int(value) or value <= 0):
            problems.append("%s.%s must be a positive integer" % (path, key))
    if "commit" in block and (not isinstance(block.get("commit"), str) or not block.get("commit")):
        problems.append(path + ".commit must be a non-empty string")
    if "dirty" in block and not isinstance(block.get("dirty"), bool):
        problems.append(path + ".dirty must be a boolean")
    if run_level:
        return                              # seed/scenario_id/settings_hash are per scenario
    if "seed" in block and not is_int(block.get("seed")):
        problems.append(path + ".seed must be an integer")
    for key in ("scenario_id", "settings_hash"):
        if key in block and (not isinstance(block.get(key), str) or not block.get(key)):
            problems.append(path + ".%s must be a non-empty string" % key)


def check_limits_note(run_dir, manifest, problems):
    if carries_string(manifest, LIMITS_NOTE):
        return
    if any(LIMITS_NOTE in text for _name, text in iter_text_files(run_dir)):
        return
    problems.append("live run must carry the exact limits note in its manifest or report: "
                    + LIMITS_NOTE)


def check_usd_claims(run_dir, manifest, dispatches, problems):
    blobs = [json.dumps(manifest), json.dumps(dispatches)]
    blobs.extend(text for _name, text in iter_text_files(run_dir))
    for text in blobs:
        if USD_CLAIM.search(text):
            problems.append("live evidence contains a USD figure or a dollar-cap claim "
                            "(value not shown); no dollar cap is claimed")
            return


def check_live_rules(run_dir, manifest, dispatches, problems, notes):
    provider = manifest.get("provider") if isinstance(manifest, dict) else None
    live = isinstance(provider, dict) and provider.get("live") is True
    check_sources(manifest, dispatches, problems)
    check_config_redacted(dispatches, run_dir, problems)
    check_live_usage(live, manifest, dispatches, problems, notes)
    if not live:
        return
    check_live_block(provider, "manifest.provider", problems, run_level=True)
    for index, dispatch in enumerate(dispatches):
        if isinstance(dispatch, dict):
            check_live_block(dispatch.get("provider"), "dispatch[%d].provider" % index, problems)
    check_limits_note(run_dir, manifest, problems)
    check_usd_claims(run_dir, manifest, dispatches, problems)
    notes.append(LIVE_HONESTY)


def audit_run(run_dir):
    run_dir = os.path.abspath(run_dir)
    if not os.path.isdir(run_dir):
        raise AuditError("run folder not found: " + run_dir)
    manifest_path = os.path.join(run_dir, "run-manifest.json")
    dispatch_path = os.path.join(run_dir, "dispatches.ndjson")
    if not os.path.isfile(manifest_path):
        raise AuditError("missing run-manifest.json in " + run_dir)
    problems, notes = [], []
    try:
        manifest = read_json(manifest_path)
    except (OSError, ValueError) as error:
        raise AuditError("cannot read run-manifest.json: %s" % error)
    validate_manifest(manifest, problems)
    scan(manifest, "manifest", problems)
    dispatches = []
    if not os.path.isfile(dispatch_path):
        problems.append("missing dispatches.ndjson")
    else:
        try:
            dispatches = read_lines(dispatch_path)
        except (OSError, ValueError) as error:
            problems.append("cannot read dispatches.ndjson: %s" % error)
    for index, dispatch in enumerate(dispatches):
        validate_dispatch(dispatch, index, manifest if isinstance(manifest, dict) else {}, problems)
        if isinstance(dispatch, dict):
            scan(dispatch, "dispatch[%d]" % index, problems)
    scenario_dirs = {}
    if isinstance(manifest, dict):
        for scenario in manifest.get("scenarios", []):
            if isinstance(scenario, dict):
                scenario_dirs[scenario.get("scenario_id")] = find_scenario_dir(run_dir, scenario.get("scenario_id"))
    cache = {}
    seen = {key: set() for key in ("run_record", "exchange_request", "exchange_result", "prompt_capture")}
    for index, dispatch in enumerate(dispatches):
        if isinstance(dispatch, dict):
            check_links(dispatch, index, run_dir, scenario_dirs.get(dispatch.get("scenario_id")), cache, seen, problems)
    if isinstance(manifest, dict):
        recompute_usage(manifest, dispatches, problems, notes)
        check_setup(run_dir, manifest, problems, notes)
        check_live_rules(run_dir, manifest, dispatches, problems, notes)
        incomplete = collect_incomplete(manifest, dispatches)
    else:
        incomplete = ["run manifest is not a JSON object"]
    scan_tree(run_dir, {os.path.abspath(manifest_path), os.path.abspath(dispatch_path)}, problems)
    return {"manifest": manifest, "dispatches": dispatches,
            "problems": list(dict.fromkeys(problems)), "notes": notes, "incomplete": incomplete}


def exit_code_of(result):
    if result.get("problems"):
        return 1
    if result.get("incomplete"):
        return 3
    return 0


def render_report(result):
    manifest = result.get("manifest") if isinstance(result.get("manifest"), dict) else {}
    provider = manifest.get("provider") if isinstance(manifest.get("provider"), dict) else {}
    usage = manifest.get("usage") if isinstance(manifest.get("usage"), dict) else {}
    counts = {}
    for dispatch in result.get("dispatches", []):
        if isinstance(dispatch, dict):
            counts[dispatch.get("scenario_id")] = counts.get(dispatch.get("scenario_id"), 0) + 1
    lines = ["run: %s" % manifest.get("run_id"),
             "commit: %s (base %s) dirty=%s" % (manifest.get("head_commit"), manifest.get("base_commit"), bool(manifest.get("dirty_files"))),
             "provider: kind=%s model=%s live=%s" % (provider.get("kind"), provider.get("model"), provider.get("live")),
             "status: %s" % manifest.get("status"), "scenarios:"]
    for scenario in manifest.get("scenarios", []) if isinstance(manifest.get("scenarios"), list) else []:
        if isinstance(scenario, dict):
            lines.append("  %s status=%s dispatches=%d %s" % (
                scenario.get("scenario_id"), scenario.get("status"), counts.get(scenario.get("scenario_id"), 0),
                "PASS" if scenario.get("status") == "completed" else "NOT pass"))
    lines.append("usage: input_tokens=%s output_tokens=%s unknown_requests=%s" % (
        usage.get("input_tokens"), usage.get("output_tokens"), usage.get("unknown_requests")))
    lines.extend("limitation: " + str(item) for item in manifest.get("limitations", [])
                 if isinstance(manifest.get("limitations"), list))
    lines.extend("note: " + note for note in result.get("notes", []))
    lines.extend("MISMATCH: " + problem for problem in result.get("problems", []))
    if result.get("incomplete"):
        lines.append("INCOMPLETE - NOT pass:")
        lines.extend("  " + item for item in result["incomplete"])
    else:
        lines.append("all scenarios completed")
    lines.append("audit " + ("FAILED" if result.get("problems") else "ok" if not result.get("incomplete")
                             else "ok (records consistent, run incomplete)"))
    return "\n".join(lines)


def command_audit(args):
    try:
        result = audit_run(args.run_dir)
    except AuditError as error:
        print("invalid: " + str(error), file=sys.stderr)
        return 2
    if args.json:
        print(json.dumps({"run_id": (result.get("manifest") or {}).get("run_id"),
                          "problems": result["problems"], "notes": result["notes"],
                          "incomplete": result["incomplete"]}, indent=2))
    else:
        print(render_report(result))
    return exit_code_of(result)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest="command", required=True)
    audit_parser = sub.add_parser("audit", help="audit one saved run folder")
    audit_parser.add_argument("run_dir")
    audit_parser.add_argument("--json", action="store_true", help="machine-readable problems/notes/status")
    audit_parser.set_defaults(func=command_audit)
    args = parser.parse_args(argv)
    return args.func(args)


if __name__ == "__main__":
    sys.exit(main())
