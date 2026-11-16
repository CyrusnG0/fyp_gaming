#!/usr/bin/env python3
"""Independent audit of a saved LuanShi controlled-scenario run folder.

Checks design section 5 evidence (run-manifest.json, dispatches.ndjson), link joins, prompt
sha256, recomputed token totals, null-not-zero unknown usage, setup/script reproduction, and
credential patterns. Incomplete/stopped/failed scenarios are reported as NOT pass. No network
call is made and no environment credential is read. Schema details live in
luanshi_controlled_audit_schema.py.

  audit <run-dir> [--json]
Exit codes: 0 consistent and complete; 1 schema/evidence mismatch; 2 unreadable run folder;
3 records consistent but incomplete.
"""
import argparse
import json
import os
import sys

from luanshi_controlled_audit_schema import (
    MANIFEST_SCHEMA, REPLY_NULLABLE, SECRET_VALUE, TEXT_EXTENSIONS, canonical_hash, need,
    read_json, read_lines, scan, sha256_file, sha256_text, validate_dispatch, validate_manifest)


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
