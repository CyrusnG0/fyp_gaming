#!/usr/bin/env python3
"""LuanShi experiment-batch tool (offline).

An independent second implementation of the checks the C# batch facility makes, so a batch's evidence
can be verified from its files alone, without trusting the program that wrote them.

  check-manifest <manifest>            schema, credential scan, explicit ceilings, pinned commit exists
  run <manifest> --out <dir>           check, run the offline C# batch (fake provider only), then audit
  audit <batch-dir>                    re-derive every total from the per-request records and the pairs'
                                       settings from the saved settings files

No network call is made and no credential is read. Exit code: 0 ok, 1 audit/mismatch, 2 invalid input.
Needs Python 3.8+ and, for `run`, the .NET SDK. Run it from anywhere inside the repository.
"""
import argparse
import hashlib
import json
import os
import re
import subprocess
import sys

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


def check_manifest(path, require_head=False):
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
            head = subprocess.run(["git", "-C", REPO, "rev-parse", "HEAD"], capture_output=True, text=True).stdout.strip()
            if not head.startswith(commit):
                problems.append("HEAD (%s) is not the pinned commit %s" % (head[:10], commit))
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
    with open(os.path.join(match_dir, files["summary"]["path"]), encoding="utf-8") as handle:
        summary = json.load(handle)
    totals = summary["totals"]

    n = len(records)
    exchange_requests = [x for x in exchanges if x.get("exchange_event") == "request"]
    counts = {"prompts": len(prompts), "exchange requests": len(exchange_requests),
              "summary requests": totals["requests"], "summary top-level requests": summary["requests"]}
    for name, value in counts.items():
        if value != n:
            problems.append("%s: %s=%s but %d per-request records" % (label, name, value, n))
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


def audit_batch(batch_dir):
    problems, notes = [], []
    with open(os.path.join(batch_dir, "manifest.json"), "rb") as handle:
        manifest_bytes = handle.read()          # hash the bytes on disk, not newline-translated text
    manifest = json.loads(manifest_bytes.decode("utf-8"))
    with open(os.path.join(batch_dir, "batch-result.json"), encoding="utf-8") as handle:
        result = json.load(handle)
    if sha256_bytes(manifest_bytes) != result["manifest_sha256"]:
        problems.append("manifest.json does not match the manifest hash recorded in batch-result.json")
    variable = "features." + manifest["experiment"]["variable"]
    for seed in manifest["seeds"]:
        flats = {}
        for arm in ("control", "treatment"):
            match_dir = os.path.join(batch_dir, "seed%d-%s" % (seed, arm))
            if not os.path.isdir(match_dir):
                problems.append("missing match directory " + os.path.basename(match_dir))
                continue
            match = audit_match(match_dir, problems, notes)
            with open(os.path.join(match_dir, "settings.json"), encoding="utf-8") as handle:
                settings = json.load(handle)
            if canonical_hash(settings["settings"]) != match["settings_hash"] or settings["settings_hash"] != match["settings_hash"]:
                problems.append("%s: settings hash does not match the saved settings" % match["match_id"])
            flats[arm] = settings["settings"]
        if len(flats) == 2:
            differing = sorted(k for k in set(flats["control"]) | set(flats["treatment"])
                               if flats["control"].get(k) != flats["treatment"].get(k))
            if differing != [variable]:
                problems.append("seed %d: arms differ in %s, expected only %s" % (seed, differing, variable))
    for ceiling in result.get("batch_ceilings_hit", []):
        notes.append("batch ceiling hit: " + ceiling)
    return problems, notes


def command_check(args):
    manifest, problems = check_manifest(args.manifest, args.require_head)
    for problem in problems:
        print("invalid: " + problem, file=sys.stderr)
    if manifest:
        print("manifest ok: %s @ %s" % (manifest["batch_id"], manifest["pinned_commit"]))
    return 0 if manifest else 2


def command_audit(args):
    problems, notes = audit_batch(args.batch_dir)
    for note in notes:
        print("note: " + note)
    for problem in problems:
        print("MISMATCH: " + problem)
    print("audit %s (%d problem(s))" % ("FAILED" if problems else "ok", len(problems)))
    return 1 if problems else 0


def command_run(args):
    manifest, problems = check_manifest(args.manifest, args.require_head)
    for problem in problems:
        print("invalid: " + problem, file=sys.stderr)
    if not manifest:
        return 2
    code = subprocess.call(["dotnet", "run", "--project", PROJECT, "-c", "Release", "--", "run", args.manifest, "--out", args.out])
    batch_dir = os.path.join(args.out, manifest["batch_id"])
    if not os.path.exists(os.path.join(batch_dir, "batch-result.json")):
        return code or 2
    audit_code = command_audit(argparse.Namespace(batch_dir=batch_dir))
    return code or audit_code


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest="command", required=True)
    p = sub.add_parser("check-manifest")
    p.add_argument("manifest")
    p.add_argument("--require-head", action="store_true", help="fail unless HEAD is the pinned commit")
    p.set_defaults(func=command_check)
    p = sub.add_parser("run")
    p.add_argument("manifest")
    p.add_argument("--out", required=True)
    p.add_argument("--require-head", action="store_true")
    p.set_defaults(func=command_run)
    p = sub.add_parser("audit")
    p.add_argument("batch_dir")
    p.set_defaults(func=command_audit)
    args = parser.parse_args(argv)
    return args.func(args)


if __name__ == "__main__":
    sys.exit(main())
