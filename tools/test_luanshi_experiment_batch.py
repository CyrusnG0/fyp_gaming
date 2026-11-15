#!/usr/bin/env python3
"""Self-test for tools/luanshi_experiment_batch.py. Offline; runs the fake-provider batch once.

    python tools/test_luanshi_experiment_batch.py
"""
import contextlib
import io
import json
import os
import shutil
import sys
import tempfile
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import luanshi_experiment_batch as tool  # noqa: E402

EXAMPLE = os.path.join(tool.REPO, "fyp_gaming", "Tests", "ExperimentBatch", "examples", "example-manifest.json")


def quiet(function, *args):
    with contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()):
        return function(*args)


class ManifestChecks(unittest.TestCase):
    def write(self, mutate):
        with open(EXAMPLE, encoding="utf-8") as handle:
            manifest = json.load(handle)
        mutate(manifest)
        directory = tempfile.mkdtemp()
        self.addCleanup(shutil.rmtree, directory, True)
        path = os.path.join(directory, "m.json")
        with open(path, "w", encoding="utf-8") as handle:
            json.dump(manifest, handle)
        return path

    def test_example_is_valid(self):
        manifest, problems = tool.check_manifest(EXAMPLE)
        self.assertEqual(problems, [])
        self.assertEqual(manifest["pinned_commit"], "e93f7f2")

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


class AuditChecks(unittest.TestCase):
    def test_audit_passes_then_detects_doctored_evidence(self):
        out = tempfile.mkdtemp()
        self.addCleanup(shutil.rmtree, out, True)
        code = quiet(tool.subprocess.call, ["dotnet", "run", "--project", tool.PROJECT, "-c", "Release", "--", "run", EXAMPLE, "--out", out])
        self.assertEqual(code, 0)
        batch = os.path.join(out, "spatial-pair-offline-demo")
        problems, _ = tool.audit_batch(batch)
        self.assertEqual(problems, [])

        match = os.path.join(batch, "seed4242-control")
        summary = [f for f in os.listdir(match) if f.startswith("pilot-")][0]
        path = os.path.join(match, summary)
        with open(path, "rb") as handle:
            original = handle.read()
        with open(path, "wb") as handle:
            handle.write(original.replace(b'"accepted":4', b'"accepted":5', 1))
        problems, _ = tool.audit_batch(batch)
        self.assertTrue(any("sha256" in p for p in problems) and any("accepted" in p for p in problems), problems)
        with open(path, "wb") as handle:
            handle.write(original)

        settings = os.path.join(match.replace("control", "treatment"), "settings.json")
        with open(settings, encoding="utf-8") as handle:
            data = json.load(handle)
        data["settings"]["seasons"] = "3"
        with open(settings, "w", encoding="utf-8") as handle:
            json.dump(data, handle)
        problems, _ = tool.audit_batch(batch)
        self.assertTrue(any("settings hash" in p for p in problems), problems)


if __name__ == "__main__":
    unittest.main(verbosity=2)
