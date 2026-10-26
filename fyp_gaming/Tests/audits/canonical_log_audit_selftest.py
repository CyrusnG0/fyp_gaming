"""Negative self-test for canonical_log_audit.py.

Each case below is a formatting/alias bypass that the audit must reject with exit code 1. A clean
`Debug.Log` and a comment-only mention must stay clean, proving the engine pattern is word-bounded
and comments are stripped. Uses only the standard library and a temporary directory.
"""
import os
import subprocess
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
AUDIT = os.path.join(HERE, "canonical_log_audit.py")

BYPASSES = (
    ("spaced dot", 'class X { void M() { engine . Log("x"); } }'),
    ("null-conditional", 'class X { void M() { engine?.Log("x"); } }'),
    ("underscore alias", 'class X { void M() { _engine.Log("x"); } }'),
    ("null-forgiving", 'class X { void M() { engine!.Log("x"); } }'),
    ("call result", 'class X { void M() { GetEngine().Log("x"); } }'),
    ("short alias", 'class X { void M() { var e = engine; var l = e.Log; } }'),
    ("aliased engine", 'class X { void M() { var e = engine; e.Log.Records(); } }'),
    ("ToJsonLines", 'class X { void M() { engine.ToJsonLines(); } }'),
    ("EventLog declaration", "class X { void M() { EventLog x; } }"),
    ("EventRecord declaration", "class X { void M() { EventRecord x; } }"),
    ("legacy log filename", 'class X { string P = "luanshi_log.jsonl"; }'),
)


def run(source):
    with tempfile.TemporaryDirectory() as directory:
        path = os.path.join(directory, "Temp.cs")
        with open(path, "w", encoding="utf-8") as handle:
            handle.write(source)
        return subprocess.run(
            [sys.executable, AUDIT, path],
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            text=True,
        )


def main():
    passed = failed = 0
    print("canonical_log_audit self-test")
    for label, source in BYPASSES:
        result = run(source)
        if result.returncode == 1:
            passed += 1
            print("  PASS  rejected bypass: " + label)
        else:
            failed += 1
            print("  FAIL  missed bypass: %s (exit %d)" % (label, result.returncode))
            print(result.stdout)

    clean_cases = (
        ("Debug.Log is not the canonical log", 'class X { void M() { Debug.Log("ok"); } }'),
        ("Debug.LogWarning is not the canonical log",
         'class X { void M() { Debug.LogWarning("ok"); } }'),
        ("comment-only mention is stripped", "class X { // engine.Log\n void M() {} }"),
    )
    for label, source in clean_cases:
        result = run(source)
        if result.returncode == 0:
            passed += 1
            print("  PASS  accepts: " + label)
        else:
            failed += 1
            print("  FAIL  false positive: %s (exit %d)" % (label, result.returncode))
            print(result.stdout)

    print("canonical_log_audit self-test: %d passed, %d failed" % (passed, failed))
    return 0 if failed == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
