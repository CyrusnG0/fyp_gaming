"""Check the adapter's calls into the engine against the engine's current signatures.
Complements arity_audit.py (which covers locally-declared methods): the P0-P4 work changed or added
method signatures on both sides, so both directions get audited.

Table: method name -> (min args, max args), taken from the engine sources at time of writing. Update
the table when an engine signature changes; this audit is a guard, not a source of truth.

Usage: python external_audit.py <file.cs> [<file.cs> ...]
"""
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from arity_audit import code_only, match_paren, count_args, NL

SIGS = {
    # GameEngine
    "Observe": (1, 1), "InSight": (3, 3), "SeesEvent": (2, 2), "Submit": (1, 1),
    "BeginSeason": (0, 0), "EndSeason": (0, 0),
    # GameState
    "FindFaction": (1, 1), "FindCity": (1, 1), "FindArmy": (1, 1),
    "CityAt": (2, 2), "ArmyAt": (2, 2), "CitiesOf": (1, 1), "ArmiesOf": (1, 1),
    # MapData
    "AreAdjacent": (4, 4), "PathCost": (5, 5), "IsWalkable": (2, 2), "HexDistance": (5, 5),
    # EventLog
    "ToJsonLines": (0, 0),
    # controllers / config
    "PlanTurn": (2, 2), "CommandPointCost": (1, 1),
    # diplomacy
    "GetTrust": (2, 2), "IsAtWar": (2, 2), "HasTreaty": (3, 3), "FindTreaty": (3, 3),
    "FindProposal": (2, 2), "DeclareWar": (2, 2), "MakePeace": (2, 2), "BreakTreaty": (1, 1),
    # Observation
    "Relation": (1, 1), "Faction": (1, 1),
    # SeatRotation
    "PlayOrder": (1, 1), "HumanSeats": (1, 1), "FirstSeat": (1, 1),
    "IsHuman": (2, 2), "NextAfter": (2, 2),
}

def main(argv):
    if not argv:
        sys.exit("usage: python external_audit.py <file.cs> [<file.cs> ...]")
    exit_code = 0
    for path in argv:
        src = code_only(open(path, encoding="utf-8").read())

        problems = []
        counts = {}
        for name, (lo, hi) in sorted(SIGS.items()):
            for m in re.finditer(r'\.' + re.escape(name) + r'\s*\(', src):
                open_idx = src.index('(', m.start())
                close_idx = match_paren(src, open_idx)
                if close_idx < 0:
                    continue
                given = count_args(src[open_idx + 1:close_idx])
                counts[name] = counts.get(name, 0) + 1
                if not (lo <= given <= hi):
                    line = src[:m.start()].count(NL) + 1
                    problems.append("%s:%d: .%s called with %d args, engine expects %d..%d"
                                    % (path, line, name, given, lo, hi))

        print("--- engine calls found in %s ---" % path)
        print("  " + ", ".join("%s×%d" % (k, counts[k]) for k in sorted(counts)))
        for p in problems:
            print("  ENGINE-ARITY MISMATCH " + p)
        if not problems:
            print("  no engine-call arity mismatches")
        else:
            exit_code = 1
    return exit_code


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
