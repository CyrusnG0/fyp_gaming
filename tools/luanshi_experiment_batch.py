#!/usr/bin/env python3
"""LuanShi experiment-batch tool (offline).

An independent second implementation of the checks the C# batch facility makes, so a batch's evidence
can be verified from its files alone, without trusting the program that wrote them.

  check-manifest <manifest>            schema, credential scan, explicit ceilings, pinned commit exists
  run <manifest> --out <dir>           check, run the offline C# batch (fake provider only), then audit
  audit <batch-dir>                    re-derive every total, the pairs' settings and a spatial pair's prompts from the files

No network call is made and no credential is read. Needs Python 3.8+ and, for `run`, the .NET SDK.
Exit codes: 0 records consistent and matched batch complete; 1 audit mismatch; 2 invalid input;
3 records consistent but the matched batch is INCOMPLETE (a skipped, stopped, failed or errored arm).
"""
import argparse
import hashlib
import json
import os
import re
import subprocess
import sys

from luanshi_batch_spatial_audit import audit_spatial_pair

SCHEMA = "luanshi-experiment-batch/1"
SECRET_KEY = re.compile(r"^(api_?key|apikey|secret|password|passwd|authorization|bearer|credentials?|access_?token|auth_?token)$", re.I)
SECRET_VALUE = re.compile(r"(sk-[A-Za-z0-9_\-]{12,}|Bearer\s+[A-Za-z0-9._\-]{12,})")
MATCH_CEILINGS = ["max_requests_per_turn", "max_total_requests", "max_actions_per_turn", "max_accepted_actions_per_session",
                  "max_inquiries_per_turn", "max_inquiries_per_session", "max_turn_seconds", "max_session_seconds",
                  "request_timeout_ms", "max_output_tokens", "max_session_input_tokens", "max_session_output_tokens",
                  "max_prompt_chars"]
BATCH_CEILINGS = ["max_matches", "max_total_requests", "max_total_input_tokens", "max_total_output_tokens",
                  "max_batch_wall_seconds", "max_match_wall_seconds"]
REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PROJECT = os.path.join(REPO, "fyp_gaming", "Tests", "ExperimentBatch", "ExperimentBatch.csproj")


def sha256_bytes(data):
    return hashlib.sha256(data).hexdigest()


def sha256_file(path):
    with open(path, "rb") as handle:
        return sha256_bytes(handle.read())


def scan(node, path, problems):
    if isinstance(node, dict):
        for key, value in node.items():
            if SECRET_KEY.match(key):
                problems.append("credential-like key at %s.%s" % (path, key))
            scan(value, path + "." + key, problems)
    elif isinstance(node, list):
        for index, item in enumerate(node):
            scan(item, "%s[%d]" % (path, index), problems)
    elif isinstance(node, str) and SECRET_VALUE.search(node):
        problems.append("credential-like value at %s (not shown)" % path)


BATCH_DIR = "fyp_gaming/Tests/ExperimentBatch"
BUILD_CONFIG = ("Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props", "global.json", "nuget.config")


def is_build_input(path):
    """True for a repo-relative path the csproj would compile or load: batch-project *.cs, Assets/GameEngine/**/*.cs,
    the project file, the demo map, and MSBuild/NuGet config in ancestor folders."""
    parts = path.split("/")
    engine = path.startswith("fyp_gaming/Assets/GameEngine/") and path.endswith(".cs")
    project = path.startswith(BATCH_DIR + "/") and len(parts) == 4 and path.endswith((".cs", ".csproj"))
    return engine or project or path == "fyp_gaming/Tests/Harness/demo-map.json" or (parts[-1] in BUILD_CONFIG and len(parts) <= 4)


def git_state(repo):
    """(HEAD, tracked files with uncommitted changes, untracked files that can change the build). Gitignored files are
    listed too (an ignored .cs in a compiled path is still compiled); runs, logs and notes are not."""
    def git(*args):
        return subprocess.run(["git", "-C", repo, *args], capture_output=True).stdout

    def nul_paths(output):
        # NUL-delimited output is never quoted or escaped, so non-ASCII and unusual names arrive as raw UTF-8.
        return [item.decode("utf-8", "surrogateescape") for item in output.split(b"\0") if item]
    dirty, entries = [], nul_paths(git("status", "--porcelain", "-z", "--untracked-files=no"))
    skip = False
    for entry in entries:
        if skip:                                   # the original path that follows a rename or copy entry
            skip = False
            continue
        dirty.append(entry[3:])
        skip = entry[0] in "RC" or entry[1] in "RC"
    scope = ["fyp_gaming/Assets/GameEngine", BATCH_DIR, "fyp_gaming/Tests/Harness", ".", "fyp_gaming", "fyp_gaming/Tests"]
    others = nul_paths(git("ls-files", "--others", "-z", "--", *scope))
    return (git("rev-parse", "HEAD").decode("ascii", "replace").strip(), dirty, sorted(set(f for f in others if is_build_input(f))))


def check_manifest(path, require_head=False, allow_dirty=False):
    problems = []
    try:
        with open(path, encoding="utf-8") as handle:
            manifest = json.load(handle)
    except (OSError, ValueError) as error:
        return None, ["cannot read manifest: %s" % error]
    scan(manifest, "$", problems)
    if problems:
        return None, problems          # a manifest holding a secret is never inspected further
    if manifest.get("schema") != SCHEMA:
        problems.append("schema must be " + SCHEMA)
    for name in ("batch_id", "pinned_commit", "scenario", "balance_profile", "first_mover"):
        if not manifest.get(name):
            problems.append(name + " is required")
    if manifest.get("provider", {}).get("kind") != "fake":
        problems.append("provider.kind must be 'fake' (offline tool)")
    for name in MATCH_CEILINGS:
        value = manifest.get("ceilings", {}).get(name)
        if not isinstance(value, int) or isinstance(value, bool) or value <= 0:
            problems.append("ceilings.%s must be a positive integer" % name)
    for name in BATCH_CEILINGS:
        value = manifest.get("batch_ceilings", {}).get(name)
        if not isinstance(value, int) or isinstance(value, bool) or value <= 0:
            problems.append("batch_ceilings.%s must be a positive integer" % name)
    if manifest.get("batch_ceilings", {}).get("unknown_usage_policy") not in ("stop_batch", "continue_on_unknown"):
        problems.append("batch_ceilings.unknown_usage_policy must be stop_batch or continue_on_unknown")
    experiment = manifest.get("experiment", {})
    if experiment.get("variable") not in manifest.get("features", {}):
        problems.append("experiment.variable must name a features.* flag present in the manifest")
    if experiment.get("control") == experiment.get("treatment"):
        problems.append("experiment.control and experiment.treatment must differ")
    if not manifest.get("seeds"):
        problems.append("seeds must list at least one seed")
    commit = manifest.get("pinned_commit", "")
    if re.fullmatch(r"[0-9a-f]{7,40}", commit or ""):
        found = subprocess.run(["git", "-C", REPO, "cat-file", "-e", commit + "^{commit}"], capture_output=True)
        if found.returncode != 0:
            problems.append("pinned_commit %s does not exist in this repository" % commit)
        elif require_head:
            head, dirty, untracked = git_state(REPO)
            if not head.startswith(commit):
                problems.append("HEAD (%s) is not the pinned commit %s" % (head[:10], commit))
            if dirty and not allow_dirty:
                problems.append("tracked working-tree changes (%d file(s), e.g. %s): the pinned commit is not what would run; "
                                "commit them or pass --allow-dirty to record the dirty tree explicitly" % (len(dirty), dirty[0]))
            if untracked and not allow_dirty:
                problems.append("untracked file(s) the build would compile or load (%d, e.g. %s): the pinned commit is not what would run; "
                                "remove or commit them, or pass --allow-dirty to record them explicitly" % (len(untracked), untracked[0]))
    else:
        problems.append("pinned_commit must be 7-40 lowercase hex characters")
    return (manifest if not problems else None), problems


def read_lines(path):
    with open(path, encoding="utf-8") as handle:
        return [json.loads(line) for line in handle if line.strip()]


def audit_match(match_dir, problems, notes):
    with open(os.path.join(match_dir, "match.json"), encoding="utf-8") as handle:
        match = json.load(handle)
    if match.get("status") == "skipped":
        notes.append("%s: skipped (%s)" % (match["match_id"], match.get("detail")))
        return match
    files = match["files"]
    label = match["match_id"]
    if "prompts" not in files or "summary" not in files:
        # An errored match kept no run evidence: nothing to reconcile, and its usage is unknown.
        notes.append("%s: %s, no run evidence retained (%s)" % (label, match.get("status"), match.get("detail")))
        match["_usage_unknown"] = True
        return match
    for key, ref in files.items():
        if key == "match":
            continue                    # match.json cannot contain its own hash
        path = os.path.join(match_dir, ref["path"])
        if not os.path.exists(path):
            problems.append("%s: missing file %s" % (label, ref["path"]))
        elif sha256_file(path) != ref["sha256"] or os.path.getsize(path) != ref["bytes"]:
            problems.append("%s: %s does not match its recorded sha256/size" % (label, ref["path"]))
    records = read_lines(os.path.join(match_dir, files["run_jsonl"]["path"])) if "run_jsonl" in files else []
    exchanges = read_lines(os.path.join(match_dir, files["exchanges"]["path"])) if "exchanges" in files else []
    prompts = read_lines(os.path.join(match_dir, files["prompts"]["path"]))
    # A wall-clock stop between ticks can leave one request sent but unrecorded; that gap, and only that, is tolerated.
    in_flight = max(0, min(1, len(prompts) - len(records))) if match.get("wall_limit") else 0
    with open(os.path.join(match_dir, files["summary"]["path"]), encoding="utf-8") as handle:
        summary = json.load(handle)
    totals = summary["totals"]

    n = len(records)
    exchange_requests = [x for x in exchanges if x.get("exchange_event") == "request"]
    counts = {"prompts": len(prompts), "exchange requests": len(exchange_requests),
              "summary requests": totals["requests"], "summary top-level requests": summary["requests"]}
    for name, value in counts.items():
        if value != n + in_flight:
            problems.append("%s: %s=%s but %d expected from %d per-request records" % (label, name, value, n + in_flight, n))
    if in_flight:
        notes.append("%s: stopped by %s mid-turn; 1 request was sent without a recorded reply" % (label, match["wall_limit"]))
    for index, (line, exchange) in enumerate(zip(prompts, exchange_requests), 1):
        sent = exchange["request"]
        if (sha256_bytes(line["system"].encode("utf-8")) != line["system_sha256"]
                or sha256_bytes(line["user"].encode("utf-8")) != line["user_sha256"]):
            problems.append("%s: prompt %d does not match its own recorded hash" % (label, index))
        if line["system"] != sent["system"] or line["user"] != sent["user"]:
            problems.append("%s: prompt %d differs between the batch capture and the recorder's exchange" % (label, index))

    unknown = sum(1 for r in records if r.get("input_tokens") is None or r.get("output_tokens") is None)
    want_in = None if unknown else sum(r["input_tokens"] for r in records)
    want_out = None if unknown else sum(r["output_tokens"] for r in records)
    for name, want in (("input_tokens", want_in), ("output_tokens", want_out)):
        if totals[name] != want or summary[name] != want:
            problems.append("%s: summary %s is %s/%s but the records give %s" % (label, name, summary[name], totals[name], want))
    match["_usage_unknown"] = bool(unknown)
    if unknown:
        notes.append("%s: usage unknown for %d of %d requests (totals unknown, not zero)" % (label, unknown, n))

    accepted = rejected = 0
    for r in records:
        command = r.get("command_json")
        if not command or r.get("result_ok") is None:
            continue
        if json.loads(command).get("type") == "end_turn":
            continue
        accepted += 1 if r["result_ok"] else 0
        rejected += 0 if r["result_ok"] else 1
    if totals["accepted"] != accepted or totals["rejected"] != rejected:
        problems.append("%s: summary accepted/rejected %s/%s but the records give %d/%d"
                        % (label, totals["accepted"], totals["rejected"], accepted, rejected))
    for r, line in zip(records, prompts):
        if len(line["reply"] or "") < 4000 and (r.get("raw_model_text") or "") != (line["reply"] or ""):
            problems.append("%s: request %s reply differs between run record and prompt capture" % (label, r.get("request_index")))
            break
    length = sum(1 for r in records if r.get("finish_reason") == "length")
    if length:
        notes.append("%s: %d reply(ies) truncated by the output limit (finish_reason=length)" % (label, length))
    return match


def canonical_hash(flat):
    text = "".join("%s=%s\n" % (key, flat[key]) for key in sorted(flat))
    return sha256_bytes(text.encode("utf-8"))


def prompt_alignment(match_dir, match):
    """{(seat, nth request of that seat): system_sha256} from the saved prompt capture, or None if the arm has none."""
    ref = match.get("files", {}).get("prompts")
    path = os.path.join(match_dir, ref["path"]) if ref else None
    if not path or not os.path.exists(path):
        return None
    return {(p["seat"], p["seat_request"]): p["system_sha256"] for p in read_lines(path)}


def compare_prompts(left, right):
    """(identical, aligned, differing, control_only, treatment_only). Identical needs the same requests, not just the same prompts."""
    aligned = [k for k in right if k in left]
    differing = sum(1 for k in aligned if left[k] != right[k])
    only_left = len([k for k in left if k not in right])
    only_right = len([k for k in right if k not in left])
    return (differing == 0 and only_left == 0 and only_right == 0, len(aligned), differing, only_left, only_right)


def audit_batch(batch_dir):
    """(problems, notes, incomplete). Problems are inconsistent evidence; incomplete lists arms/pairs that did not finish."""
    problems, notes, incomplete = [], [], []
    with open(os.path.join(batch_dir, "manifest.json"), "rb") as handle:
        manifest_bytes = handle.read()          # hash the bytes on disk, not newline-translated text
    manifest = json.loads(manifest_bytes.decode("utf-8"))
    with open(os.path.join(batch_dir, "batch-result.json"), encoding="utf-8") as handle:
        result = json.load(handle)
    if sha256_bytes(manifest_bytes) != result["manifest_sha256"]:
        problems.append("manifest.json does not match the manifest hash recorded in batch-result.json")
    variable = "features." + manifest["experiment"]["variable"]
    matches, all_pairs_complete = {}, True
    for seed in manifest["seeds"]:
        flats, ran = {}, {}
        for arm in ("control", "treatment"):
            name = "seed%d-%s" % (seed, arm)
            match_dir = os.path.join(batch_dir, name)
            if not os.path.isdir(match_dir) or not os.path.exists(os.path.join(match_dir, "match.json")):
                problems.append("missing match directory or match.json: " + name)
                incomplete.append("%s: no retained outcome" % name)
                all_pairs_complete = False
                continue
            match = audit_match(match_dir, problems, notes)
            matches[name] = match
            ran[arm] = match.get("status") in ("completed", "completed_with_stops")
            settings_path = os.path.join(match_dir, "settings.json")
            if not os.path.exists(settings_path):
                # A skipped arm may have no settings file: that is an incomplete pair, never a crash.
                incomplete.append("%s: %s, no settings.json retained" % (name, match.get("status")))
                continue
            with open(settings_path, encoding="utf-8") as handle:
                settings = json.load(handle)
            if canonical_hash(settings["settings"]) != match["settings_hash"] or settings["settings_hash"] != match["settings_hash"]:
                problems.append("%s: settings hash does not match the saved settings" % match["match_id"])
            flats[arm] = settings["settings"]
        if len(flats) == 2:
            differing = sorted(k for k in set(flats["control"]) | set(flats["treatment"])
                               if flats["control"].get(k) != flats["treatment"].get(k))
            if differing != [variable]:
                problems.append("seed %d: arms differ in %s, expected only %s" % (seed, differing, variable))
        pair_id = "seed%d" % seed
        if not (ran.get("control") and ran.get("treatment") and len(flats) == 2):
            all_pairs_complete = False
            incomplete.append("%s: control %s, treatment %s" % (pair_id, matches.get("seed%d-control" % seed, {}).get("status", "missing"),
                                                                matches.get("seed%d-treatment" % seed, {}).get("status", "missing")))
        elif len(ran) == 2:
            left = prompt_alignment(os.path.join(batch_dir, "seed%d-control" % seed), matches["seed%d-control" % seed])
            right = prompt_alignment(os.path.join(batch_dir, "seed%d-treatment" % seed), matches["seed%d-treatment" % seed])
            if left is not None and right is not None:
                identical, aligned, differing_n, only_l, only_r = compare_prompts(left, right)
                notes.append("%s: system prompts aligned by seat and request number: %d aligned, %d differ, %d control-only, %d treatment-only -> %s"
                             % (pair_id, aligned, differing_n, only_l, only_r, "identical" if identical else "NOT identical"))
                recorded = [p for p in result.get("pairs", []) if p.get("pair_id") == pair_id]
                if manifest["experiment"]["variable"] == "spatial_context":
                    audit_spatial_pair(batch_dir, seed, recorded, problems, notes)
                if recorded and recorded[0].get("system_prompts_identical") != identical:
                    problems.append("%s: batch-result says system_prompts_identical=%s but the saved prompts give %s"
                                    % (pair_id, recorded[0].get("system_prompts_identical"), identical))
    expected_unknown = sum(1 for m in matches.values() if m.get("_usage_unknown"))
    if result.get("matches_with_unknown_usage") != expected_unknown:
        problems.append("matches_with_unknown_usage is %s but the match files say %d" % (result.get("matches_with_unknown_usage"), expected_unknown))
    complete = all_pairs_complete and not incomplete and len(matches) == 2 * len(manifest["seeds"])
    if bool(result.get("matched_batch_complete")) != complete:
        problems.append("batch-result says matched_batch_complete=%s but the saved matches give %s" % (result.get("matched_batch_complete"), complete))
    for ceiling in result.get("batch_ceilings_hit", []):
        notes.append("batch ceiling hit: " + ceiling)
    provenance = os.path.join(batch_dir, "provenance.json")
    if os.path.exists(provenance):
        with open(provenance, encoding="utf-8") as handle:
            info = json.load(handle)
        notes.append("provenance: HEAD %s, tracked working tree %s, untracked build inputs %s" % (
            info.get("head", "?")[:10], "DIRTY (%s)" % ", ".join(info["dirty_files"][:3]) if info.get("dirty_files") else "clean",
            "PRESENT (%s)" % ", ".join(info["untracked_build_inputs"][:3]) if info.get("untracked_build_inputs") else "none"))
    else:
        notes.append("provenance: not recorded (batch was not started through this tool)")
    return problems, notes, incomplete


def command_check(args):
    manifest, problems = check_manifest(args.manifest, args.require_head, args.allow_dirty)
    for problem in problems:
        print("invalid: " + problem, file=sys.stderr)
    if manifest:
        print("manifest ok: %s @ %s" % (manifest["batch_id"], manifest["pinned_commit"]))
    return 0 if manifest else 2


def command_audit(args):
    problems, notes, incomplete = audit_batch(args.batch_dir)
    for note in notes:
        print("note: " + note)
    for problem in problems:
        print("MISMATCH: " + problem)
    print("records %s (%d problem(s))" % ("INCONSISTENT" if problems else "internally consistent", len(problems)))
    if incomplete:
        print("matched batch INCOMPLETE - not a completed experiment:")
        for item in incomplete:
            print("  " + item)
    else:
        print("matched batch complete")
    print("audit %s" % ("FAILED" if problems else "ok" if not incomplete else "ok (records consistent, batch incomplete)"))
    return 1 if problems else 3 if incomplete else 0


def command_run(args):
    manifest, problems = check_manifest(args.manifest, args.require_head, args.allow_dirty)
    for problem in problems:
        print("invalid: " + problem, file=sys.stderr)
    if not manifest:
        return 2
    head, dirty, untracked = git_state(REPO)
    code = subprocess.call(["dotnet", "run", "--project", PROJECT, "-c", "Release", "--", "run", args.manifest, "--out", args.out])
    batch_dir = os.path.join(args.out, manifest["batch_id"])
    if not os.path.exists(os.path.join(batch_dir, "batch-result.json")):
        return code or 2
    with open(os.path.join(batch_dir, "provenance.json"), "w", encoding="utf-8") as handle:
        json.dump({"head": head, "pinned_commit": manifest["pinned_commit"], "dirty_files": dirty, "untracked_build_inputs": untracked, "allow_dirty": bool(args.allow_dirty),
                   "require_head": bool(args.require_head)}, handle, indent=2)
    audit_code = command_audit(argparse.Namespace(batch_dir=batch_dir))
    return code or audit_code


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest="command", required=True)
    p = sub.add_parser("check-manifest")
    p.add_argument("manifest")
    p.add_argument("--require-head", action="store_true", help="fail unless HEAD is the pinned commit, no tracked file is modified and no untracked file would be built")
    p.add_argument("--allow-dirty", action="store_true", help="with --require-head, accept tracked changes and untracked build inputs (recorded in provenance.json)")
    p.set_defaults(func=command_check)
    p = sub.add_parser("run")
    p.add_argument("manifest")
    p.add_argument("--out", required=True)
    p.add_argument("--require-head", action="store_true")
    p.add_argument("--allow-dirty", action="store_true")
    p.set_defaults(func=command_run)
    p = sub.add_parser("audit")
    p.add_argument("batch_dir")
    p.set_defaults(func=command_audit)
    args = parser.parse_args(argv)
    return args.func(args)


if __name__ == "__main__":
    sys.exit(main())
