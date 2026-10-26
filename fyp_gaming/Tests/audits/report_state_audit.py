"""Court-report state audit: the report screen must be built from the seat's observation.

Why this exists: the LS-003 review found the court report resolving a captured city through canonical
state, which leaked a hidden city's name and named the city's later owner instead of the event's
captor. `KingdomDemo.Report.cs` may therefore use `engine.State.FindFaction` (faction names are public)
but must never read cities or armies from canonical state; those facts come from the Observation.

Usage: python report_state_audit.py [file.cs ...]
With no arguments it audits Assets/GameUI/Scripts/KingdomDemo.Report.cs in the same checkout.
Exit code: 0 when clean, 1 on a forbidden access, 2 on a setup problem.
"""
import os
import sys

FORBIDDEN = (
    "engine.State.FindCity",
    "engine.State.FindArmy",
    "engine.State.Cities",
    "engine.State.Armies",
)

DEFAULT = os.path.join(
    os.path.dirname(os.path.abspath(__file__)),
    "..", "..", "Assets", "GameUI", "Scripts", "KingdomDemo.Report.cs",
)


def audit(path):
    src = open(path, encoding="utf-8").read()
    problems = []
    for token in FORBIDDEN:
        start = 0
        while True:
            idx = src.find(token, start)
            if idx < 0:
                break
            line = src[:idx].count("\n") + 1
            problems.append((line, "%s:%d: forbidden canonical-state access `%s`"
                                   % (path, line, token)))
            start = idx + len(token)
    return sorted(problems)


def main(argv):
    paths = argv or [DEFAULT]
    exit_code = 0
    for path in paths:
        if not os.path.isfile(path):
            print("error: file not found: %s" % path, file=sys.stderr)
            return 2
        problems = audit(path)
        print("--- court report state access in %s ---" % path)
        if not problems:
            print("  no canonical city/army access from the court report")
        for _, problem in problems:
            print("  REPORT-STATE PROBLEM " + problem)
        if problems:
            exit_code = 1
    return exit_code


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
