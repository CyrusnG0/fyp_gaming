#!/usr/bin/env python3
"""Self-test for tools/luanshi_experiment_batch.py. Offline; runs the fake-provider batch a few times.

    python tools/test_luanshi_experiment_batch.py
"""
import contextlib
import io
import json
import os
import shutil
import subprocess
import sys
import tempfile
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import luanshi_experiment_batch as tool  # noqa: E402
import luanshi_pin_manifest as pin  # noqa: E402

EXAMPLE = os.path.join(tool.REPO, "fyp_gaming", "Tests", "ExperimentBatch", "examples", "example-manifest.json")


def quiet(function, *args):
    with contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()):
        return function(*args)


def write_manifest(mutate):
    with open(EXAMPLE, encoding="utf-8") as handle:
        manifest = json.load(handle)
    mutate(manifest)
    directory = tempfile.mkdtemp()
    path = os.path.join(directory, "m.json")
    with open(path, "w", encoding="utf-8") as handle:
        json.dump(manifest, handle)
    return path, directory


class ManifestChecks(unittest.TestCase):
    def write(self, mutate):
        path, directory = write_manifest(mutate)
        self.addCleanup(shutil.rmtree, directory, True)
        return path

    def test_example_is_valid(self):
        manifest, problems = tool.check_manifest(EXAMPLE)
        self.assertEqual(problems, [])
        self.assertRegex(manifest["pinned_commit"], "^[0-9a-f]{7,40}$")   # exists in this repository (checked above); which commit it names is a release decision

    def test_credentials_are_refused_without_echo(self):
        path = self.write(lambda m: m["provider"].update({"api_key": "sk-ABCDEFGHIJKLMNOPQRSTUV"}))
        manifest, problems = tool.check_manifest(path)
        self.assertIsNone(manifest)
        self.assertTrue(problems and all("ABCDEFGH" not in p for p in problems))
        manifest, problems = tool.check_manifest(self.write(lambda m: m["provider"].update({"model": "Bearer abcdefghijklmnop1234"})))
        self.assertIsNone(manifest)

    def test_unset_ceiling_and_real_provider_are_refused(self):
        _, problems = tool.check_manifest(self.write(lambda m: m["ceilings"].pop("max_total_requests")))
        self.assertTrue(any("max_total_requests" in p for p in problems))
        _, problems = tool.check_manifest(self.write(lambda m: m["provider"].update({"kind": "deepseek"})))
        self.assertTrue(any("fake" in p for p in problems))
        _, problems = tool.check_manifest(self.write(lambda m: m["experiment"].update({"treatment": False})))
        self.assertTrue(any("must differ" in p for p in problems))

    def test_missing_pinned_commit_is_refused(self):
        _, problems = tool.check_manifest(self.write(lambda m: m.update({"pinned_commit": "0000000"})))
        self.assertTrue(any("does not exist" in p for p in problems))


class DirtyTreeChecks(unittest.TestCase):
    """--require-head must refuse tracked changes unless --allow-dirty is explicit. Uses a throwaway repository."""

    def git(self, repo, *args):
        subprocess.run(["git", "-C", repo, "-c", "user.name=t", "-c", "user.email=t@t", *args], check=True, capture_output=True)

    def test_tracked_changes_are_refused_unless_allowed(self):
        repo = tempfile.mkdtemp()
        self.addCleanup(shutil.rmtree, repo, True)
        self.git(repo, "init", "-q")
        with open(os.path.join(repo, "a.txt"), "w") as handle:
            handle.write("one")
        self.git(repo, "add", "a.txt")
        self.git(repo, "commit", "-q", "-m", "c")
        head = subprocess.run(["git", "-C", repo, "rev-parse", "HEAD"], capture_output=True, text=True).stdout.strip()
        path, directory = write_manifest(lambda m: m.update({"pinned_commit": head[:10]}))
        self.addCleanup(shutil.rmtree, directory, True)
        saved, tool.REPO = tool.REPO, repo
        try:
            _, problems = tool.check_manifest(path, require_head=True)
            self.assertEqual(problems, [], "a clean tree at the pinned commit passes")
            with open(os.path.join(repo, "untracked.txt"), "w") as handle:
                handle.write("x")
            _, problems = tool.check_manifest(path, require_head=True)
            self.assertEqual(problems, [], "an untracked file cannot change what is built, so it is not a tracked change")
            with open(os.path.join(repo, "a.txt"), "w") as handle:
                handle.write("two")
            _, problems = tool.check_manifest(path, require_head=True)
            self.assertTrue(any("tracked working-tree changes" in p for p in problems), problems)
            _, problems = tool.check_manifest(path, require_head=True, allow_dirty=True)
            self.assertEqual(problems, [], "--allow-dirty is the explicit, recorded override")
            _, problems = tool.check_manifest(path, require_head=False)
            self.assertEqual(problems, [], "without --require-head the tree is not inspected")
            self.assertEqual(tool.git_state(repo)[1], ["a.txt"])
        finally:
            tool.REPO = saved

    def test_untracked_build_inputs_are_refused_but_unrelated_files_are_not(self):
        repo = tempfile.mkdtemp()
        self.addCleanup(shutil.rmtree, repo, True)
        self.git(repo, "init", "-q")
        self.put(repo, "fyp_gaming/Assets/GameEngine/Engine.cs", "class E {}")
        self.put(repo, "fyp_gaming/Tests/ExperimentBatch/Program.cs", "class P {}")
        self.put(repo, ".gitignore", "ignored/\n")
        self.git(repo, "add", "-A")
        self.git(repo, "commit", "-q", "-m", "c")
        head = subprocess.run(["git", "-C", repo, "rev-parse", "HEAD"], capture_output=True, text=True).stdout.strip()
        path, directory = write_manifest(lambda m: m.update({"pinned_commit": head[:10]}))
        self.addCleanup(shutil.rmtree, directory, True)
        saved, tool.REPO = tool.REPO, repo

        def check(**flags):
            return tool.check_manifest(path, require_head=True, **flags)[1]
        try:
            for unrelated in ("runs/out/batch-result.json", "logs/run.log", "fyp_gaming/Tests/ExperimentBatch/bin/Release/Gen.cs",
                              "fyp_gaming/Tests/ExperimentBatch/notes.md", "fyp_gaming/Assets/GameEngine/notes.txt", "fyp_gaming/Other/Extra.cs"):
                self.put(repo, unrelated, "x")
            self.assertEqual(check(), [], "runs, logs, notes and .cs files outside the compiled paths do not change the build")
            self.assertEqual(tool.git_state(repo)[2], [])

            for compiled in ("fyp_gaming/Assets/GameEngine/Sub/New.cs", "fyp_gaming/Tests/ExperimentBatch/Extra.cs"):
                self.put(repo, compiled, "class N {}")
                problems = check()
                self.assertTrue(any("untracked file(s) the build would compile" in x and compiled in x for x in problems), problems)
                self.assertEqual(check(allow_dirty=True), [], "--allow-dirty is the explicit override")
                os.remove(os.path.join(repo, compiled))
                self.assertEqual(check(), [])

            self.put(repo, "fyp_gaming/Assets/GameEngine/Hidden.cs", "class H {}")
            self.put(repo, "ignored/x.txt", "x")
            with open(os.path.join(repo, ".gitignore"), "w") as handle:
                handle.write("ignored/\nHidden.cs\n")
            self.assertTrue(any("Hidden.cs" in x for x in check()), "an ignored .cs in a compiled path is still compiled, so still refused")
            self.assertEqual(tool.git_state(repo)[2], ["fyp_gaming/Assets/GameEngine/Hidden.cs"])
        finally:
            tool.REPO = saved

    def test_non_ascii_paths_are_not_lost_to_git_quoting(self):
        repo = tempfile.mkdtemp()
        self.addCleanup(shutil.rmtree, repo, True)
        self.git(repo, "init", "-q")
        base = "fyp_gaming/Assets/GameEngine/"
        ignored_name, untracked_name, staged_name = "\u5ffd\u7565.cs", "\u6e2c\u8a66.cs", "\u6e2c.cs"
        self.put(repo, base + "Engine.cs", "class E {}")
        self.put(repo, ".gitignore", "ignored/\n" + ignored_name + "\n")
        self.git(repo, "add", "-A")
        self.git(repo, "commit", "-q", "-m", "c")
        head = subprocess.run(["git", "-C", repo, "rev-parse", "HEAD"], capture_output=True, text=True).stdout.strip()
        path, directory = write_manifest(lambda m: m.update({"pinned_commit": head[:10]}))
        self.addCleanup(shutil.rmtree, directory, True)
        saved, tool.REPO = tool.REPO, repo

        def check(**flags):
            return tool.check_manifest(path, require_head=True, **flags)[1]
        try:
            self.put(repo, "ignored/" + untracked_name, "x")
            self.put(repo, "runs/" + untracked_name, "x")
            self.put(repo, base + "\u8aaa\u660e.md", "x")
            self.assertEqual(check(), [], "non-ASCII files outside the compiled paths, or not .cs, are unrelated")

            self.put(repo, base + untracked_name, "class T {}")
            self.assertEqual(tool.git_state(repo)[2], [base + untracked_name])
            self.assertTrue(any(base + untracked_name in x for x in check()), "an untracked non-ASCII .cs is refused, not hidden by Git's quoted display form")
            self.assertEqual(check(allow_dirty=True), [])
            os.remove(os.path.join(repo, *(base + untracked_name).split("/")))

            self.put(repo, base + ignored_name, "class I {}")
            self.assertEqual(tool.git_state(repo)[2], [base + ignored_name])
            self.assertTrue(any(base + ignored_name in x for x in check()), "an ignored non-ASCII .cs in a compiled path is refused")
            os.remove(os.path.join(repo, *(base + ignored_name).split("/")))

            self.put(repo, base + "Engine.cs", "class E2 {}")
            self.put(repo, base + staged_name, "class T {}")
            self.git(repo, "add", base + staged_name)
            self.assertEqual(sorted(tool.git_state(repo)[1]), sorted([base + "Engine.cs", base + staged_name]),
                             "tracked changes keep their real non-ASCII names")
        finally:
            tool.REPO = saved

    @staticmethod
    def put(repo, relative, text):
        full = os.path.join(repo, *relative.split("/"))
        os.makedirs(os.path.dirname(full), exist_ok=True)
        with open(full, "w", encoding="utf-8") as handle:
            handle.write(text)


class AuditChecks(unittest.TestCase):
    def run_batch(self, mutate=None, delay_ms=None):
        path, directory = write_manifest(mutate or (lambda m: None))
        out = tempfile.mkdtemp()
        self.addCleanup(shutil.rmtree, directory, True)
        self.addCleanup(shutil.rmtree, out, True)
        env = dict(os.environ)
        if delay_ms:
            env["LUANSHI_BATCH_TEST_DELAY_MS"] = str(delay_ms)
        code = subprocess.call(["dotnet", "run", "--project", tool.PROJECT, "-c", "Release", "--", "run", path, "--out", out],
                               env=env, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        self.assertEqual(code, 0)
        return os.path.join(out, "spatial-pair-offline-demo")

    @staticmethod
    def edit_json(path, mutate):
        with open(path, encoding="utf-8") as handle:
            data = json.load(handle)
        mutate(data)
        with open(path, "w", encoding="utf-8") as handle:
            json.dump(data, handle)

    def test_audit_passes_then_detects_doctored_evidence(self):
        batch = self.run_batch()
        problems, _, incomplete = tool.audit_batch(batch)
        self.assertEqual((problems, incomplete), ([], []))
        self.assertEqual(quiet(tool.command_audit, __import__("argparse").Namespace(batch_dir=batch)), 0)

        match = os.path.join(batch, "seed4242-control")
        summary = [f for f in os.listdir(match) if f.startswith("pilot-")][0]
        path = os.path.join(match, summary)
        with open(path, "rb") as handle:
            original = handle.read()
        with open(path, "wb") as handle:
            handle.write(original.replace(b'"accepted":4', b'"accepted":5', 1))
        problems, _, _ = tool.audit_batch(batch)
        self.assertTrue(any("sha256" in p for p in problems) and any("accepted" in p for p in problems), problems)
        with open(path, "wb") as handle:
            handle.write(original)

        self.edit_json(os.path.join(batch, "seed4242-treatment", "settings.json"), lambda d: d["settings"].update({"seasons": "3"}))
        problems, _, _ = tool.audit_batch(batch)
        self.assertTrue(any("settings hash" in p for p in problems), problems)

    def test_result_claims_are_rechecked_against_the_files(self):
        batch = self.run_batch()
        result = os.path.join(batch, "batch-result.json")
        self.edit_json(result, lambda d: d.update({"matches_with_unknown_usage": 2}))
        problems, _, _ = tool.audit_batch(batch)
        self.assertTrue(any("matches_with_unknown_usage" in p for p in problems), problems)
        self.edit_json(result, lambda d: d.update({"matches_with_unknown_usage": 0, "matched_batch_complete": False}))
        problems, _, _ = tool.audit_batch(batch)
        self.assertTrue(any("matched_batch_complete" in p for p in problems), problems)
        self.edit_json(result, lambda d: (d.update({"matched_batch_complete": True}), d["pairs"][0].update({"system_prompts_identical": True})))
        problems, notes, _ = tool.audit_batch(batch)
        self.assertTrue(any("system_prompts_identical" in p for p in problems), problems)
        self.assertTrue(any("aligned by seat and request number" in n and "14 aligned" in n for n in notes), notes)

    def test_prompt_comparison_needs_the_same_requests(self):
        same = {("seat-0", 1): "X", ("seat-0", 2): "Y"}
        self.assertEqual(tool.compare_prompts(same, dict(same))[0], True)
        self.assertEqual(tool.compare_prompts(same, {("seat-0", 1): "Y", ("seat-0", 2): "X"})[:3], (False, 2, 2))
        identical, aligned, differing, only_left, only_right = tool.compare_prompts(same, {("seat-0", 1): "X"})
        self.assertEqual((identical, aligned, only_left, only_right), (False, 1, 1, 0))

    def test_spatial_pair_is_audited_from_the_saved_files(self):
        batch = self.run_batch()
        problems, notes, incomplete = tool.audit_batch(batch)
        self.assertEqual((problems, incomplete), ([], []))
        self.assertTrue(any("spatial pair ok from saved files" in n and "14 treatment request(s)" in n and "move_option_cap" in n for n in notes), notes)
        with open(os.path.join(batch, "batch-result.json"), encoding="utf-8") as handle:
            pair = json.load(handle)["pairs"][0]
        self.assertEqual(pair["spatial_problems"], [])
        self.assertTrue(pair["final_world_identical"])
        self.assertFalse(pair["system_prompts_identical"], "the treatment system prompt carries the production spatial addendum")

        match = os.path.join(batch, "seed4242-treatment")
        self.edit_json(os.path.join(match, "match.json"), lambda d: d["spatial_evidence"]["privacy"]["leaks"].append("seat-0 request 1 shows 'city-shu', hidden at dispatch and never shown to this seat"))
        problems, _, _ = tool.audit_batch(batch)
        self.assertTrue(any("spatial pair check" in p and "leak hidden ids" in p for p in problems), problems)

    def test_spatial_audit_catches_a_prompt_that_lost_its_spatial_material(self):
        batch = self.run_batch()
        path = os.path.join(batch, "seed4242-treatment", "prompts.ndjson")
        with open(path, encoding="utf-8") as handle:
            lines = [json.loads(line) for line in handle if line.strip()]
        lines[0]["system"] = lines[0]["system"].replace("SPATIAL CONTEXT AND SIGHT BOUNDARIES", "SIGHT")
        with open(path, "w", encoding="utf-8", newline="\n") as handle:
            handle.write("".join(json.dumps(line) + "\n" for line in lines))
        problems, _, _ = tool.audit_batch(batch)
        self.assertTrue(any("spatial pair check" in p and "lacks the spatial addendum" in p for p in problems), problems)

        self.edit_json(os.path.join(batch, "batch-result.json"), lambda d: d["pairs"][0].update({"spatial_problems": []}))
        problems, _, _ = tool.audit_batch(batch)
        self.assertTrue(any("disagrees with the saved prompts" in p for p in problems), problems)

    def test_skipped_arm_without_settings_is_an_incomplete_pair_not_a_crash(self):
        batch = self.run_batch(lambda m: m["batch_ceilings"].update({"max_total_requests": 90}))   # treatment cannot start
        skipped = os.path.join(batch, "seed4242-treatment")
        os.remove(os.path.join(skipped, "settings.json"))
        problems, notes, incomplete = tool.audit_batch(batch)          # must not raise
        self.assertEqual(problems, [], problems)
        self.assertTrue(any("seed4242" in i and "skipped" in i for i in incomplete), incomplete)
        namespace = __import__("argparse").Namespace(batch_dir=batch)
        out = io.StringIO()
        with contextlib.redirect_stdout(out):
            code = tool.command_audit(namespace)
        self.assertEqual(code, 3)
        self.assertIn("records internally consistent", out.getvalue())
        self.assertIn("matched batch INCOMPLETE - not a completed experiment", out.getvalue())
        self.assertNotIn("audit ok\n", out.getvalue())

    def test_a_wall_stopped_match_reconciles_and_leaves_the_batch_incomplete(self):
        batch = self.run_batch(lambda m: m["batch_ceilings"].update({"max_match_wall_seconds": 1}), delay_ms=150)
        problems, notes, incomplete = tool.audit_batch(batch)
        self.assertEqual(problems, [], problems)
        self.assertTrue(any("mid-turn" in n for n in notes), notes)
        self.assertTrue(any("stopped" in i for i in incomplete), incomplete)

    def test_input_token_ceiling_and_provider_error_audit_and_leave_batch_incomplete(self):
        token_batch = self.run_batch(lambda m: m["ceilings"].update({"max_session_input_tokens": 6000}))
        problems, _, incomplete = tool.audit_batch(token_batch)
        self.assertEqual(problems, [], problems)
        self.assertEqual(len(incomplete), 1, incomplete)
        self.assertIn("control stopped, treatment stopped", incomplete[0])
        self.assertEqual(quiet(tool.command_audit, __import__("argparse").Namespace(batch_dir=token_batch)), 3)

        failed_batch = self.run_batch(lambda m: (
            m["provider"]["faults"].append({"seat": "seat-0", "seat_request": 3, "kind": "provider_error"}),
            m["batch_ceilings"].update({"unknown_usage_policy": "continue_on_unknown"}),
        ))
        problems, notes, incomplete = tool.audit_batch(failed_batch)
        self.assertEqual(problems, [], problems)
        self.assertEqual(len(incomplete), 1, incomplete)
        self.assertIn("control failed, treatment failed", incomplete[0])
        self.assertTrue(any("usage unknown" in n for n in notes), notes)
        self.assertEqual(quiet(tool.command_audit, __import__("argparse").Namespace(batch_dir=failed_batch)), 3)

        turn_stop_batch = self.run_batch(lambda m: m["ceilings"].update({"max_requests_per_turn": 3}))
        problems, _, incomplete = tool.audit_batch(turn_stop_batch)
        self.assertEqual((problems, incomplete), ([], []))
        self.assertEqual(quiet(tool.command_audit, __import__("argparse").Namespace(batch_dir=turn_stop_batch)), 0)


class PinManifest(unittest.TestCase):
    def test_pin_writes_a_copy_and_leaves_the_source_alone(self):
        directory = tempfile.mkdtemp()
        self.addCleanup(shutil.rmtree, directory, True)
        out = os.path.join(directory, "pinned.json")
        with open(EXAMPLE, "rb") as handle:
            before = handle.read()
        self.assertEqual(quiet(pin.main, [EXAMPLE, "--out", out]), 0)
        head = subprocess.run(["git", "-C", tool.REPO, "rev-parse", "HEAD"], capture_output=True, text=True).stdout.strip()
        with open(out, encoding="utf-8") as handle:
            pinned = json.load(handle)
        with open(EXAMPLE, encoding="utf-8") as handle:
            original = json.load(handle)
        self.assertEqual(pinned["pinned_commit"], head)
        self.assertEqual({k: v for k, v in pinned.items() if k != "pinned_commit"}, {k: v for k, v in original.items() if k != "pinned_commit"})
        with open(EXAMPLE, "rb") as handle:
            self.assertEqual(handle.read(), before)
        self.assertEqual(tool.check_manifest(out)[1], [], "the pinned copy is a valid manifest")
        self.assertEqual(quiet(pin.main, [EXAMPLE, "--out", out]), 2, "an existing output is never overwritten")

    def test_pin_refuses_an_unknown_commit_and_an_unreadable_manifest(self):
        directory = tempfile.mkdtemp()
        self.addCleanup(shutil.rmtree, directory, True)
        self.assertEqual(quiet(pin.main, [EXAMPLE, "--out", os.path.join(directory, "a.json"), "--commit", "0000000"]), 2)
        self.assertEqual(quiet(pin.main, [os.path.join(directory, "missing.json"), "--out", os.path.join(directory, "b.json")]), 2)
        self.assertFalse(os.path.exists(os.path.join(directory, "a.json")) or os.path.exists(os.path.join(directory, "b.json")))


if __name__ == "__main__":
    unittest.main(verbosity=2)
