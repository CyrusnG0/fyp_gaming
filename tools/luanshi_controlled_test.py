#!/usr/bin/env python3
"""Unit tests for the LuanShi controlled-scenario Python tools (design section 6).

    python3 tools/luanshi_controlled_test.py

Every fake test builds a tiny synthetic run folder in a temp dir, except LiveAuditTests, which
copies a saved fake run from runs/controlled-scenarios/ and relabels it in a temp dir. Nothing
here needs the C# project to build, touches Tests/ControlledScenarios/, reads a credential or
makes a network call.
"""
import contextlib
import copy
import io
import json
import os
import shutil
import sys
import tempfile
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import luanshi_controlled_audit as audit  # noqa: E402
import luanshi_controlled_audit_schema as schema  # noqa: E402
import luanshi_controlled_scenarios as scenarios  # noqa: E402

RUN_ID = "controlled-test"
SCENARIO_ID = "cs-test-v1"
SEED = 4242
HEAD = "abc1234"
SETUP_HASH = "a" * 64


def hex64(char):
    return char * 64


def write_json(path, value):
    with open(path, "w", encoding="utf-8") as handle:
        json.dump(value, handle, indent=2)
        handle.write("\n")


def write_lines(path, rows):
    with open(path, "w", encoding="utf-8") as handle:
        for row in rows:
            handle.write(json.dumps(row) + "\n")


def make_prompt(index=1):
    system, user = "SYS %d" % index, "USER %d" % index
    return {
        "system": system, "user": user,
        "system_sha256": schema.sha256_text(system), "user_sha256": schema.sha256_text(user),
        "prompt_version": "controlled-test/1", "schema_hash": "schema-test",
        "visible_options": {"catalogue": [], "catalogue_text": None,
                            "movement_json": None, "attacks_json": None},
        "truncation": {"prompt_chars": len(system) + len(user), "prompt_limit": 40000,
                       "movement_truncated": False, "attack_truncated": None,
                       "spatial": None, "sections_present": [], "unknown_fields": []},
    }


def make_reply(raw="end_turn", input_tokens=None, output_tokens=None, finish_reason="stop"):
    reply = {
        "raw_text": raw, "source": "fake_scripted", "finish_reason": finish_reason,
        "input_tokens": input_tokens, "output_tokens": output_tokens,
        "reasoning_tokens": None, "total_tokens": None, "http_status": None,
        "provider_request_id": None, "latency_ms": 0, "elapsed_ms": 0, "unknown_fields": [],
    }
    for field in schema.REPLY_NULLABLE:
        if reply[field] is None:
            reply["unknown_fields"].append(field)
    return reply


def snapshot(tag):
    return {"observation_sha256": schema.sha256_text(tag),
            "observation_json": json.dumps({"tag": tag}),
            "canonical_world_digest": schema.sha256_text(tag + "-world")}


def make_dispatch(index, prompt, reply, turn_token):
    return {
        "schema": schema.DISPATCH_SCHEMA, "run_id": RUN_ID, "scenario_id": SCENARIO_ID,
        "dispatch_id": "%s:%d" % (RUN_ID, index),
        "commit": HEAD, "base_commit": HEAD, "dirty": False, "seed": SEED,
        "seat_id": "seat-0", "turn_token": turn_token, "season": 1,
        "request_index": index, "seat_request_index": index,
        "provider": {"kind": "fake", "model": "fake-model", "live": False,
                     "usage_source": "fake_deterministic"},
        "settings": {"settings_hash": hex64("c"),
                     "config_redacted": "provider=fake; api_key=<missing>",
                     "flags": {"attack": False, "research": False,
                               "manual_lookup": False, "deliberation": False},
                     "ceilings": {"max_request_seconds": 30, "max_scenario_seconds": 60}},
        "prompt": prompt, "reply": reply,
        "explanation": {"text": "", "source": "model_written_summary",
                        "reasoning_text_captured": False},
        "action": {"kind": "action", "command_json": json.dumps({"type": "end_turn"}),
                   "accepted": True, "result_error": None, "inquiry": None, "note": None},
        "state": {"before": snapshot("before"), "after": snapshot("after")},
        "links": {}, "status": "complete",
    }


def make_manifest(*, usage=None, ceilings=None, provider=None, status="completed",
                  scenario_status="completed", script_sha256=None):
    return {
        "schema": schema.MANIFEST_SCHEMA, "run_id": RUN_ID,
        "branch": "codex/controlled-scenarios", "base_commit": HEAD, "head_commit": HEAD,
        "dirty_files": [], "untracked_build_inputs": [],
        "limitations": ["fake replies are not model behaviour"],
        "provider": provider or {"kind": "fake", "model": "fake-model", "live": False,
                                 "status_note": "no network; fake scripts are plumbing evidence"},
        "scenarios": [{"scenario_id": SCENARIO_ID, "seed": SEED, "study_seat": "seat-0",
                       "seats": ["seat-0", "seat-1"], "seasons": 1,
                       "setup_hash": SETUP_HASH, "script_sha256": script_sha256 or hex64("b"),
                       "status": scenario_status}],
        "run_ceilings": ceilings or {"max_scenarios": 6, "max_run_requests": 200,
                                     "max_run_wall_seconds": 120,
                                     "unknown_usage_policy": "report"},
        "usage": usage or {"input_tokens": 0, "output_tokens": 0,
                           "unknown_requests": 0, "source": "fake_deterministic"},
        "status": status,
    }


def build_run(root, *, usage="known", dispatch_count=1, tokens=None,
              scenario_status="completed", dispatch_status="complete", manifest_status="completed"):
    """Create a schema-valid run folder whose links join and whose setup hashes recompute."""
    run_dir = os.path.join(root, "run")
    scenario_dir = os.path.join(run_dir, SCENARIO_ID)
    os.makedirs(scenario_dir)
    prompts = [make_prompt(i + 1) for i in range(dispatch_count)]
    replies = []
    for i in range(dispatch_count):
        if usage == "unknown":
            input_tokens = output_tokens = None
        else:
            input_tokens, output_tokens = tokens[i] if tokens else (10 * (i + 1), 5 * (i + 1))
        replies.append(make_reply(input_tokens=input_tokens, output_tokens=output_tokens))

    script_path = os.path.join(scenario_dir, "script.json")
    write_json(script_path, [{"text": "end_turn"}])
    settings = {"attack": False, "research": False, "manual_lookup": False, "deliberation": False}
    write_json(os.path.join(scenario_dir, "settings.json"),
               {"settings": settings, "settings_hash": schema.canonical_hash(settings)})
    write_json(os.path.join(scenario_dir, "scenario.json"),
               {"scenario_id": SCENARIO_ID, "setup_hash": SETUP_HASH, "seed": SEED,
                "study_seat": "seat-0", "seats": ["seat-0", "seat-1"], "seasons": 1})
    write_lines(os.path.join(scenario_dir, "prompts.ndjson"), [
        {"system": prompt["system"], "user": prompt["user"],
         "system_sha256": prompt["system_sha256"], "user_sha256": prompt["user_sha256"]}
        for prompt in prompts])
    write_lines(os.path.join(scenario_dir, "run-%s.jsonl" % RUN_ID), [
        {"raw_model_text": reply["raw_text"], "seat_id": "seat-0", "turn_token": index + 1}
        for index, reply in enumerate(replies)])
    exchange_rows = []
    for prompt, reply in zip(prompts, replies):
        exchange_rows.append({"exchange_event": "request",
                              "request": {"system": prompt["system"], "user": prompt["user"]}})
        exchange_rows.append({"exchange_event": "result", "response": {"text": reply["raw_text"]}})
    write_lines(os.path.join(scenario_dir, "run-%s-exchanges.ndjson" % RUN_ID), exchange_rows)

    dispatches = []
    for index, (prompt, reply) in enumerate(zip(prompts, replies), start=1):
        dispatch = make_dispatch(index, prompt, reply, turn_token=index)
        dispatch["status"] = dispatch_status
        dispatch["links"] = {
            "run_record": "run-%s.jsonl:%d" % (RUN_ID, index),
            "exchange_request": "run-%s-exchanges.ndjson:%d" % (RUN_ID, 2 * index - 1),
            "exchange_result": "run-%s-exchanges.ndjson:%d" % (RUN_ID, 2 * index),
            "prompt_capture": "prompts.ndjson:%d" % index,
        }
        dispatches.append(dispatch)

    if usage == "unknown":
        usage_record = {"input_tokens": None, "output_tokens": None,
                        "unknown_requests": dispatch_count, "source": "fake_deterministic"}
    else:
        usage_record = {"input_tokens": sum(r["input_tokens"] for r in replies),
                        "output_tokens": sum(r["output_tokens"] for r in replies),
                        "unknown_requests": 0, "source": "fake_deterministic"}
    manifest = make_manifest(usage=usage_record, status=manifest_status,
                             scenario_status=scenario_status,
                             script_sha256=schema.sha256_file(script_path))
    write_json(os.path.join(run_dir, "run-manifest.json"), manifest)
    write_lines(os.path.join(run_dir, "dispatches.ndjson"), dispatches)
    return run_dir, manifest, dispatches


WORKTREE = os.path.dirname(HERE)
RUNS_ROOT = os.path.join(WORKTREE, "runs", "controlled-scenarios")
LIVE_ENV_NAMES = ["LUANSHI_LLM_API_KEY", "LUANSHI_LLM_BASE_URL", "LUANSHI_LLM_MODEL"]


def find_fake_run():
    """The most recent saved fake run, or LUANSHI_CONTROLLED_RUN when set."""
    override = os.environ.get("LUANSHI_CONTROLLED_RUN")
    if override and os.path.isfile(os.path.join(override, "run-manifest.json")):
        return override
    try:
        names = sorted(os.listdir(RUNS_ROOT), reverse=True)
    except OSError:
        return None
    for name in names:
        candidate = os.path.join(RUNS_ROOT, name)
        try:
            manifest = schema.read_json(os.path.join(candidate, "run-manifest.json"))
        except (OSError, ValueError):
            continue
        provider = manifest.get("provider") if isinstance(manifest, dict) else None
        if isinstance(provider, dict) and provider.get("live") is False:
            return candidate
    return None


class Fixture(unittest.TestCase):
    def tempdir(self):
        directory = tempfile.mkdtemp(prefix="luanshi-controlled-test-")
        self.addCleanup(shutil.rmtree, directory, True)
        return directory

    def write_manifest(self, mutate=lambda manifest: None):
        manifest = make_manifest()
        mutate(manifest)
        path = os.path.join(self.tempdir(), "manifest.json")
        write_json(path, manifest)
        return path

    def audit(self, run_dir):
        return audit.audit_run(run_dir)


class ManifestTests(Fixture):
    def test_manifest_ceilings(self):
        manifest, problems = scenarios.check_manifest(self.write_manifest())
        self.assertIsNotNone(manifest)
        self.assertEqual(problems, [])

        def drop_wall(value):
            del value["run_ceilings"]["max_run_wall_seconds"]

        _, problems = scenarios.check_manifest(self.write_manifest(drop_wall))
        self.assertTrue(any("max_run_wall_seconds" in p for p in problems))

        _, problems = scenarios.check_manifest(
            self.write_manifest(lambda m: m["run_ceilings"].update({"max_scenarios": 0})))
        self.assertTrue(any("max_scenarios" in p for p in problems))

        _, problems = scenarios.check_manifest(
            self.write_manifest(lambda m: m["run_ceilings"].update({"unknown_usage_policy": "ignore"})))
        self.assertTrue(any("unknown_usage_policy" in p for p in problems))

    def test_credential_refusal(self):
        path = self.write_manifest(
            lambda m: m["provider"].update({"api_key": "sk-ABCDEFGHIJKLMNOPQRST"}))
        manifest, problems = scenarios.check_manifest(path)
        self.assertIsNone(manifest)
        self.assertTrue(any("credential-like key" in p for p in problems))
        self.assertTrue(all("ABCDEFGHIJKLMNOPQRST" not in p for p in problems))

        path = self.write_manifest(
            lambda m: m["provider"].update({"status_note": "Bearer abcdefghijklmnop1234"}))
        manifest, problems = scenarios.check_manifest(path)
        self.assertIsNone(manifest)
        self.assertTrue(any("credential-like value" in p for p in problems))
        self.assertTrue(all("abcdefghijklmnop1234" not in p for p in problems))


class AuditTests(Fixture):
    def test_audit_recomputes_totals(self):
        run_dir, manifest, _ = build_run(self.tempdir(), dispatch_count=2,
                                         tokens=[(10, 5), (20, 7)])
        result = self.audit(run_dir)
        self.assertEqual(result["problems"], [])
        self.assertEqual(result["incomplete"], [])
        self.assertEqual(manifest["usage"]["input_tokens"], 30)
        self.assertEqual(manifest["usage"]["output_tokens"], 12)
        self.assertEqual(audit.exit_code_of(result), 0)

        path = os.path.join(run_dir, "run-manifest.json")
        tampered = copy.deepcopy(manifest)
        tampered["usage"]["input_tokens"] = 999
        write_json(path, tampered)
        self.assertTrue(any("input_tokens" in p for p in self.audit(run_dir)["problems"]))

    def test_unknown_usage_not_zero(self):
        run_dir, manifest, dispatches = build_run(self.tempdir(), usage="unknown")
        result = self.audit(run_dir)
        self.assertEqual(result["problems"], [])
        self.assertIsNone(manifest["usage"]["input_tokens"])
        self.assertIsNone(manifest["usage"]["output_tokens"])
        self.assertEqual(manifest["usage"]["unknown_requests"], 1)
        reply = dispatches[0]["reply"]
        self.assertIsNone(reply["input_tokens"])
        self.assertIn("input_tokens", reply["unknown_fields"])
        self.assertTrue(any("not zero" in note for note in result["notes"]))

        path = os.path.join(run_dir, "run-manifest.json")
        tampered = copy.deepcopy(manifest)
        tampered["usage"]["input_tokens"] = 0
        write_json(path, tampered)
        problems = self.audit(run_dir)["problems"]
        self.assertTrue(any("never a partial or zero total" in p for p in problems))

    def test_dispatch_links(self):
        run_dir, _, dispatches = build_run(self.tempdir(), dispatch_count=2)
        result = self.audit(run_dir)
        self.assertEqual(result["problems"], [])
        self.assertEqual(result["incomplete"], [])

        path = os.path.join(run_dir, "dispatches.ndjson")
        broken = copy.deepcopy(dispatches)
        broken[0]["links"]["prompt_capture"] = "prompts.ndjson:99"
        write_lines(path, broken)
        self.assertTrue(any("outside" in p for p in self.audit(run_dir)["problems"]))

        duplicate = copy.deepcopy(dispatches)
        duplicate[1]["links"]["run_record"] = duplicate[0]["links"]["run_record"]
        write_lines(path, duplicate)
        self.assertTrue(any("reuses" in p for p in self.audit(run_dir)["problems"]))

    def test_setup_reproduction(self):
        run_dir, manifest, _ = build_run(self.tempdir())
        scenario_dir = os.path.join(run_dir, SCENARIO_ID)
        self.assertEqual(self.audit(run_dir)["problems"], [])

        saved = schema.read_json(os.path.join(scenario_dir, "scenario.json"))
        self.assertEqual(saved["setup_hash"], manifest["scenarios"][0]["setup_hash"])
        script_path = os.path.join(scenario_dir, "script.json")
        self.assertEqual(schema.sha256_file(script_path),
                         manifest["scenarios"][0]["script_sha256"])
        settings = schema.read_json(os.path.join(scenario_dir, "settings.json"))
        self.assertEqual(schema.canonical_hash(settings["settings"]),
                         settings["settings_hash"])
        self.assertEqual(audit.setup_signature(scenario_dir)["scenario.json"],
                         schema.sha256_file(os.path.join(scenario_dir, "scenario.json")))

        settings["settings"]["attack"] = True
        write_json(os.path.join(scenario_dir, "settings.json"), settings)
        self.assertTrue(any("settings.json hash" in p
                            for p in self.audit(run_dir)["problems"]))

        run_dir, _, _ = build_run(self.tempdir())
        scenario_dir = os.path.join(run_dir, SCENARIO_ID)
        write_json(os.path.join(scenario_dir, "script.json"), [{"text": "changed"}])
        self.assertTrue(any("script.json does not match" in p
                            for p in self.audit(run_dir)["problems"]))


class LiveAuditTests(Fixture):
    """Copy one real fake run, relabel it live, and assert each audit outcome.

    Nothing here runs the provider or reads a credential: the copied fake evidence is only a
    schema-valid skeleton whose labels and provider blocks are rewritten in a temp directory.
    """

    def setUp(self):
        self.source = find_fake_run()
        if self.source is None:
            self.skipTest("no fake run under %s; run the fake runner first" % RUNS_ROOT)

    def clone(self):
        run_dir = os.path.join(self.tempdir(), "run")
        shutil.copytree(self.source, run_dir)
        return run_dir

    def paths(self, run_dir):
        return (os.path.join(run_dir, "run-manifest.json"),
                os.path.join(run_dir, "dispatches.ndjson"))

    def rewrite_live(self, run_dir):
        manifest_path, dispatch_path = self.paths(run_dir)
        manifest = schema.read_json(manifest_path)
        manifest["provider"].update({
            "kind": "openai-compatible", "model": "live-model", "live": True,
            "base_url_display": "https://api.example.com/v1",
            "env_var_names": list(LIVE_ENV_NAMES), "max_output_tokens": 512,
            "request_timeout_ms": 25000, "effective_max_requests": 200,
            "effective_max_wall_seconds": 120, "commit": "abc1234", "dirty": False,
            "seed": None, "scenario_id": None, "settings_hash": None,
        })
        manifest["usage"]["source"] = "provider_reported"
        for scenario in manifest.get("scenarios", []):
            block = scenario.get("provider") if isinstance(scenario, dict) else None
            if isinstance(block, dict):
                block.update({
                    "kind": "openai-compatible", "model": "live-model", "live": True,
                    "base_url_display": "https://api.example.com/v1",
                    "env_var_names": list(LIVE_ENV_NAMES), "max_output_tokens": 512,
                    "request_timeout_ms": 25000, "effective_max_requests": 40,
                    "effective_max_wall_seconds": 30, "commit": "abc1234", "dirty": False,
                    "seed": scenario.get("seed"), "scenario_id": scenario.get("scenario_id"),
                    "settings_hash": scenario.get("settings_hash"),
                })
        write_json(manifest_path, manifest)
        rows = schema.read_lines(dispatch_path)
        for row in rows:
            block = row.setdefault("provider", {})
            block.update({
                "kind": "openai-compatible", "model": "live-model", "live": True,
                "usage_source": "provider_reported",
                "base_url_display": "https://api.example.com/v1",
                "env_var_names": list(LIVE_ENV_NAMES), "max_output_tokens": 512,
                "request_timeout_ms": 25000, "effective_max_requests": 40,
                "effective_max_wall_seconds": 30, "commit": "abc1234", "dirty": False,
                "seed": row.get("seed"), "scenario_id": row.get("scenario_id"),
                "settings_hash": block.get("settings_hash"),
            })
            row["reply"]["source"] = "provider"
        write_lines(dispatch_path, rows)
        return manifest, rows

    def test_live_labelled_run_is_consistent(self):
        run_dir = self.clone()
        self.rewrite_live(run_dir)
        result = self.audit(run_dir)
        self.assertEqual(result["problems"], [])
        self.assertEqual(result["incomplete"], [])
        self.assertEqual(audit.exit_code_of(result), 0)
        self.assertTrue(any("cannot prove a live run was valid" in note
                            for note in result["notes"]))

    def test_live_fake_source_is_rejected(self):
        run_dir = self.clone()
        _manifest, rows = self.rewrite_live(run_dir)
        rows[0]["reply"]["source"] = "fake_scripted"
        write_lines(self.paths(run_dir)[1], rows)
        problems = self.audit(run_dir)["problems"]
        self.assertTrue(any("source" in problem and "fake_scripted" in problem
                            for problem in problems))

    def test_fake_run_provider_source_is_rejected(self):
        run_dir = self.clone()
        path = self.paths(run_dir)[1]
        rows = schema.read_lines(path)
        rows[0]["reply"]["source"] = "provider"
        write_lines(path, rows)
        problems = self.audit(run_dir)["problems"]
        self.assertTrue(any("provider" in problem and "fake run" in problem
                            for problem in problems))

    def test_secret_value_is_rejected(self):
        run_dir = self.clone()
        self.rewrite_live(run_dir)
        with open(os.path.join(run_dir, "credential-leak.txt"), "w", encoding="utf-8") as handle:
            handle.write("token sk-ABCDEFGHIJKLMNOPQRST\n")
        problems = self.audit(run_dir)["problems"]
        self.assertTrue(any("credential-like value" in problem for problem in problems))
        self.assertTrue(all("ABCDEFGHIJKLMNOPQRST" not in problem for problem in problems))

    def test_api_key_with_value_is_rejected(self):
        run_dir = self.clone()
        _manifest, rows = self.rewrite_live(run_dir)
        rows[0]["settings"]["config_redacted"] = "provider=fake; api_key=sk-ABCDEFGHIJKLMNOPQRST"
        write_lines(self.paths(run_dir)[1], rows)
        problems = self.audit(run_dir)["problems"]
        self.assertTrue(any("credential-like value" in problem for problem in problems))
        self.assertTrue(any("must show api_key" in problem for problem in problems))

    def test_config_redacted_requires_allowed_form(self):
        run_dir = self.clone()
        path = self.paths(run_dir)[1]
        rows = schema.read_lines(path)
        rows[0]["settings"]["config_redacted"] = "{}"
        write_lines(path, rows)
        problems = self.audit(run_dir)["problems"]
        self.assertTrue(any("must show api_key=<set> or api_key=<missing>" in problem
                            for problem in problems))

    def test_live_cost_claim_is_rejected(self):
        run_dir = self.clone()
        self.rewrite_live(run_dir)
        with open(os.path.join(run_dir, "cost-report.txt"), "w", encoding="utf-8") as handle:
            handle.write("Estimated cost: USD 12.34\n")
        problems = self.audit(run_dir)["problems"]
        self.assertTrue(any("USD figure" in problem or "dollar-cap claim" in problem
                            for problem in problems))

    def test_live_requires_positive_output_token_limit(self):
        run_dir = self.clone()
        _manifest, rows = self.rewrite_live(run_dir)
        manifest_path, dispatch_path = self.paths(run_dir)
        manifest = schema.read_json(manifest_path)
        manifest["provider"]["max_output_tokens"] = 0
        write_json(manifest_path, manifest)
        for row in rows:
            row["provider"]["max_output_tokens"] = 0
        write_lines(dispatch_path, rows)
        problems = self.audit(run_dir)["problems"]
        self.assertTrue(any("max_output_tokens" in problem and "positive" in problem
                            for problem in problems))

    def test_live_unknown_usage_is_counted_not_zero(self):
        run_dir = self.clone()
        _manifest, rows = self.rewrite_live(run_dir)
        manifest_path, dispatch_path = self.paths(run_dir)
        rows[0]["reply"]["input_tokens"] = None
        rows[0]["reply"]["output_tokens"] = None
        unknown = rows[0]["reply"].setdefault("unknown_fields", [])
        for field in ("input_tokens", "output_tokens"):
            if field not in unknown:
                unknown.append(field)
        write_lines(dispatch_path, rows)
        manifest = schema.read_json(manifest_path)
        manifest["usage"] = {"input_tokens": None, "output_tokens": None,
                             "unknown_requests": 1, "source": "provider_reported"}
        write_json(manifest_path, manifest)
        result = self.audit(run_dir)
        self.assertEqual(result["problems"], [])
        self.assertTrue(any("counted as unknown, not zero" in note for note in result["notes"]))

        manifest["usage"]["input_tokens"] = 0
        write_json(manifest_path, manifest)
        problems = self.audit(run_dir)["problems"]
        self.assertTrue(any("null, never zero" in problem or "zero total" in problem
                            for problem in problems))


class BaseUrlDisplayAuditTests(unittest.TestCase):
    """The audit flags a non-sk- secret-like path segment without echoing the value."""

    def test_secret_like_display_path_is_flagged(self):
        sentinel = "SENTINELKEY-ab12cd34ef56gh78"
        block = {"kind": "openai-compatible", "model": "m", "live": True,
                 "base_url_display": "https://example.invalid/v1/%s" % sentinel,
                 "env_var_names": list(LIVE_ENV_NAMES), "max_output_tokens": 512,
                 "request_timeout_ms": 25000, "effective_max_requests": 200,
                 "effective_max_wall_seconds": 120, "commit": "abc1234", "dirty": False}
        problems = []
        audit.check_live_block(block, "provider", problems, run_level=True)
        self.assertTrue(any("secret-like path segment" in problem for problem in problems))
        self.assertTrue(all(sentinel not in problem for problem in problems))

        block["base_url_display"] = "https://example.invalid/v1"
        problems = []
        audit.check_live_block(block, "provider", problems, run_level=True)
        self.assertFalse(any("secret-like path segment" in problem for problem in problems))

    def test_percent_encoded_display_path_is_flagged(self):
        block = {"kind": "openai-compatible", "model": "m", "live": True,
                 "base_url_display": "https://example.invalid/v1/Ab%2BCd%2FEf%3DGh",
                 "env_var_names": list(LIVE_ENV_NAMES), "max_output_tokens": 512,
                 "request_timeout_ms": 25000, "effective_max_requests": 200,
                 "effective_max_wall_seconds": 120, "commit": "abc1234", "dirty": False}
        problems = []
        audit.check_live_block(block, "provider", problems, run_level=True)
        self.assertTrue(any("percent-encoded" in problem for problem in problems))
        self.assertTrue(all("Ab%2B" not in problem for problem in problems))


class ProviderStatusTests(unittest.TestCase):
    def test_provider_status(self):
        output = io.StringIO()
        with contextlib.redirect_stdout(output):
            code = scenarios.main(["provider-status"])
        text = output.getvalue()
        self.assertEqual(code, 0)
        self.assertIn("fake", text)
        self.assertIn("available", text)
        self.assertIn("openai-compatible", text)
        self.assertIn("NOT exercised and NOT validated", text)
        self.assertIn("no network call is made", text)
        self.assertIn("no credential is read or printed", text)
        self.assertNotIn("sk-", text)


if __name__ == "__main__":
    unittest.main(verbosity=2)
