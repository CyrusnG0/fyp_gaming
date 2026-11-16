#!/usr/bin/env python3
"""LuanShi controlled-scenario launcher and reporter (offline by default).

  validate <manifest> [--require-head] [--allow-dirty] [--with-csharp]
  run <manifest> --out <dir> [--require-head] [--allow-dirty]
  replay <run-dir> --out <dir>
  report <run-dir> [--json]
  provider-status [--with-csharp]

`validate` checks the input manifest in Python (no build needed). `run`/`replay`/`--with-csharp`
delegate to the C# CLI at fyp_gaming/Tests/ControlledScenarios/ControlledScenarios.csproj, stamp
git provenance (HEAD, tracked changes, untracked build inputs) into the produced run folder, then
audit it with luanshi_controlled_audit.py. Output is refused unless it is under
<root>/runs/controlled-scenarios/. No network call is made and no credential is read.

Exit codes: 0 ok; 1 audit mismatch; 2 invalid input or launcher failure; 3 incomplete (not pass).
"""
import argparse
import json
import os
import subprocess
import sys
from datetime import datetime, timezone

from luanshi_controlled_audit import AuditError, audit_run, exit_code_of, render_report, setup_signature
from luanshi_controlled_audit_schema import MANIFEST_SCHEMA, is_int, read_json, scan

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PROJECT = os.path.join(ROOT, "fyp_gaming", "Tests", "ControlledScenarios", "ControlledScenarios.csproj")
RUNS_ROOT = os.path.join(ROOT, "runs", "controlled-scenarios")
CEILINGS = ("max_scenarios", "max_run_requests", "max_run_wall_seconds")
POLICIES = ("report", "stop_run")
UNTRACKED_SCOPES = ("fyp_gaming/Tests/ControlledScenarios", "fyp_gaming/Assets/GameEngine",
                    "fyp_gaming/Tests/Harness", "tools")


def write_json(path, value):
    with open(path, "w", encoding="utf-8") as handle:
        json.dump(value, handle, indent=2)
        handle.write("\n")


def _git(*args):
    return subprocess.run(["git", "-C", ROOT, *args], capture_output=True).stdout


def nul_paths(output):
    return [item.decode("utf-8", "surrogateescape") for item in output.split(b"\0") if item]


def git_state():
    """(HEAD, tracked files with uncommitted changes, untracked build/tool inputs)."""
    dirty, skip = [], False
    for entry in nul_paths(_git("status", "--porcelain", "-z", "--untracked-files=no")):
        if skip:                                   # the original path after a rename/copy entry
            skip = False
            continue
        dirty.append(entry[3:])
        skip = entry[0] in "RC" or entry[1] in "RC"
    untracked = nul_paths(_git("ls-files", "--others", "-z", "--", *UNTRACKED_SCOPES))
    return (_git("rev-parse", "HEAD").decode("ascii", "replace").strip(), dirty, sorted(set(untracked)))


def resolve_out(out):
    """Absolute output path, refused unless it is the controlled-scenarios runs root or below it."""
    base = os.path.realpath(RUNS_ROOT)
    target = os.path.realpath(out if os.path.isabs(out) else os.path.join(ROOT, out))
    folded_base, folded_target = os.path.normcase(base), os.path.normcase(target)
    if folded_target != folded_base and not folded_target.startswith(folded_base + os.sep):
        raise ValueError("--out must be inside %s (got %s)" % (base, target))
    return target


def check_manifest(path, require_head=False, allow_dirty=False):
    """(manifest or None, problems). Malformed or credential-bearing manifests are refused whole."""
    try:
        manifest = read_json(path)
    except (OSError, ValueError) as error:
        return None, ["cannot read manifest: %s" % error]
    problems = []
    scan(manifest, "$", problems)
    if problems:
        return None, problems                    # never inspect further a manifest holding a secret
    if not isinstance(manifest, dict):
        return None, ["manifest must be a JSON object"]
    if manifest.get("schema") != MANIFEST_SCHEMA:
        problems.append("schema must be " + MANIFEST_SCHEMA)
    provider = manifest.get("provider")
    if not isinstance(provider, dict):
        problems.append("provider is required")
    else:
        if provider.get("kind") != "fake":
            problems.append("provider.kind must be 'fake': this offline tool does not run a live provider")
        if not isinstance(provider.get("model"), str) or not provider["model"]:
            problems.append("provider.model is required")
    ceilings = manifest.get("run_ceilings")
    if not isinstance(ceilings, dict):
        problems.append("run_ceilings is required")
    else:
        for key in CEILINGS:
            value = ceilings.get(key)
            if not is_int(value) or value <= 0:
                problems.append("run_ceilings.%s must be a positive integer" % key)
        if ceilings.get("unknown_usage_policy") not in POLICIES:
            problems.append("run_ceilings.unknown_usage_policy must be report or stop_run")
    scenarios = manifest.get("scenarios")
    limit = ceilings.get("max_scenarios") if isinstance(ceilings, dict) else None
    if not isinstance(scenarios, list) or not scenarios:
        problems.append("scenarios must list at least one scenario")
    else:
        if is_int(limit) and len(scenarios) > limit:
            problems.append("scenarios has %d entries but run_ceilings.max_scenarios is %d" % (len(scenarios), limit))
        seen = set()
        for index, scenario in enumerate(scenarios):
            path_name = "scenarios[%d]" % index
            if not isinstance(scenario, dict):
                problems.append(path_name + " is not an object")
                continue
            scenario_id = scenario.get("scenario_id")
            if not isinstance(scenario_id, str) or not scenario_id:
                problems.append(path_name + ".scenario_id is required")
            elif scenario_id in seen:
                problems.append(path_name + ".scenario_id is duplicated")
            else:
                seen.add(scenario_id)
            if not is_int(scenario.get("seed")):
                problems.append(path_name + ".seed must be an integer")
            seats = scenario.get("seats")
            if not isinstance(seats, list) or not seats or any(not isinstance(seat, str) for seat in seats):
                problems.append(path_name + ".seats must list seat ids")
            elif scenario.get("study_seat") not in seats:
                problems.append(path_name + ".study_seat is not one of the seats")
            if not is_int(scenario.get("seasons")) or scenario.get("seasons") <= 0:
                problems.append(path_name + ".seasons must be a positive integer")
            if scenario.get("script") is not None and not isinstance(scenario.get("script"), list):
                problems.append(path_name + ".script must be a list")
    if require_head:
        head, dirty, untracked = git_state()
        pinned = manifest.get("base_commit") or manifest.get("pinned_commit") or manifest.get("head_commit")
        if pinned and not head.startswith(str(pinned)):
            problems.append("HEAD (%s) is not the manifest commit %s" % (head[:10], pinned))
        if dirty and not allow_dirty:
            problems.append("tracked working-tree changes (%d file(s), e.g. %s): commit them or pass --allow-dirty"
                            % (len(dirty), dirty[0]))
        if untracked:
            print("note: %d untracked build/tool input(s) will be recorded in provenance (e.g. %s)"
                  % (len(untracked), untracked[0]), file=sys.stderr)
    return (manifest if not problems else None), problems


def invoke(arguments):
    if not os.path.isfile(PROJECT):
        print("invalid: C# project not found at %s (the parallel C# worker builds it)" % PROJECT, file=sys.stderr)
        return 2
    command = ["dotnet", "run", "--project", PROJECT, "-c", "Release", "--"] + arguments
    print("running: " + " ".join(command), file=sys.stderr)
    try:
        return subprocess.call(command, cwd=ROOT)
    except FileNotFoundError:
        print("invalid: dotnet SDK not found on PATH", file=sys.stderr)
        return 2


def new_run_dir(out, before):
    if not os.path.isdir(out):
        return None
    candidates = [os.path.join(out, name) for name in os.listdir(out)
                  if os.path.isdir(os.path.join(out, name)) and os.path.abspath(os.path.join(out, name)) not in before]
    return max(candidates, key=os.path.getmtime) if candidates else None


def stamp_provenance(run_dir, manifest, require_head, allow_dirty, command):
    head, dirty, untracked = git_state()
    write_json(os.path.join(run_dir, "python-provenance.json"), {
        "tool": "luanshi_controlled_scenarios.py",
        "recorded_at": datetime.now(timezone.utc).isoformat(),
        "head": head,
        "declared_commit": manifest.get("base_commit") or manifest.get("pinned_commit") or manifest.get("head_commit"),
        "dirty_files": dirty,
        "untracked_build_inputs": untracked,
        "require_head": bool(require_head),
        "allow_dirty": bool(allow_dirty),
        "command": command,
    })


def audit_and_report(run_dir):
    try:
        result = audit_run(run_dir)
    except AuditError as error:
        print("invalid: " + str(error), file=sys.stderr)
        return 2
    print(render_report(result))
    return exit_code_of(result)


def command_validate(args):
    manifest, problems = check_manifest(args.manifest, args.require_head, args.allow_dirty)
    for problem in problems:
        print("invalid: " + problem, file=sys.stderr)
    if not manifest:
        return 2
    print("manifest ok: %d scenario(s), provider=%s/%s" % (
        len(manifest["scenarios"]), manifest["provider"]["kind"], manifest["provider"]["model"]))
    if args.with_csharp:
        return invoke(["validate", "--manifest", os.path.abspath(args.manifest)])
    return 0


def command_run(args):
    manifest, problems = check_manifest(args.manifest, args.require_head, args.allow_dirty)
    for problem in problems:
        print("invalid: " + problem, file=sys.stderr)
    if not manifest:
        return 2
    try:
        out = resolve_out(args.out)
    except ValueError as error:
        print("invalid: " + str(error), file=sys.stderr)
        return 2
    os.makedirs(out, exist_ok=True)
    before = {os.path.abspath(os.path.join(out, name)) for name in os.listdir(out)}
    code = invoke(["run", "--manifest", os.path.abspath(args.manifest), "--out", out])
    run_dir = new_run_dir(out, before)
    if run_dir is None:
        print("invalid: the C# run produced no timestamped run folder under " + out, file=sys.stderr)
        return code or 2
    stamp_provenance(run_dir, manifest, args.require_head, args.allow_dirty,
                     ["run", "--manifest", os.path.abspath(args.manifest), "--out", out])
    print("run folder: " + run_dir)
    return code or audit_and_report(run_dir)


def command_replay(args):
    source = os.path.abspath(args.run_dir)
    if not os.path.isdir(source):
        print("invalid: run folder not found: " + source, file=sys.stderr)
        return 2
    try:
        out = resolve_out(args.out)
    except ValueError as error:
        print("invalid: " + str(error), file=sys.stderr)
        return 2
    os.makedirs(out, exist_ok=True)
    before = {os.path.abspath(os.path.join(out, name)) for name in os.listdir(out)}
    code = invoke(["replay", "--run", source, "--out", out])
    run_dir = new_run_dir(out, before)
    if run_dir is None:
        print("invalid: the C# replay produced no timestamped run folder under " + out, file=sys.stderr)
        return code or 2
    manifest = read_json(os.path.join(source, "run-manifest.json")) if os.path.isfile(
        os.path.join(source, "run-manifest.json")) else {}
    stamp_provenance(run_dir, manifest, False, False, ["replay", "--run", source, "--out", out])
    print("run folder: " + run_dir)
    return code or audit_and_report(run_dir)


def command_report(args):
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


def command_provider_status(args):
    print("provider status (LuanShi controlled-scenario Python tool)")
    print("  fake               available: offline deterministic scripts; fixtures, not model behaviour")
    print("  openai-compatible  NOT exercised and NOT validated by this tool")
    print("  network            no network call is made")
    print("  credentials        no credential is read or printed")
    print("  csharp project     %s (%s)" % (PROJECT, "present" if os.path.isfile(PROJECT) else "missing"))
    if args.with_csharp:
        return invoke(["provider-status"])
    print("  live next step     add CsLiveScenarioProvider plus an explicit --allow-live in the C# adapter;")
    print("                     the transport interface is ready, but no live call or readiness is claimed here")
    return 0


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest="command", required=True)
    validate_parser = sub.add_parser("validate", help="check an input manifest without building the C# project")
    validate_parser.add_argument("manifest")
    validate_parser.add_argument("--require-head", action="store_true")
    validate_parser.add_argument("--allow-dirty", action="store_true")
    validate_parser.add_argument("--with-csharp", action="store_true", help="also run the C# manifest validator")
    validate_parser.set_defaults(func=command_validate)
    run_parser = sub.add_parser("run", help="run the C# controlled scenarios through the fake provider")
    run_parser.add_argument("manifest")
    run_parser.add_argument("--out", required=True)
    run_parser.add_argument("--require-head", action="store_true")
    run_parser.add_argument("--allow-dirty", action="store_true")
    run_parser.set_defaults(func=command_run)
    replay_parser = sub.add_parser("replay", help="replay a saved run from its recorded setup")
    replay_parser.add_argument("run_dir")
    replay_parser.add_argument("--out", required=True)
    replay_parser.set_defaults(func=command_replay)
    report_parser = sub.add_parser("report", help="audit and report on a saved run folder")
    report_parser.add_argument("run_dir")
    report_parser.add_argument("--json", action="store_true")
    report_parser.set_defaults(func=command_report)
    status_parser = sub.add_parser("provider-status", help="state honestly what provider is available")
    status_parser.add_argument("--with-csharp", action="store_true", help="also ask the C# CLI")
    status_parser.set_defaults(func=command_provider_status)
    args = parser.parse_args(argv)
    return args.func(args)


if __name__ == "__main__":
    sys.exit(main())
