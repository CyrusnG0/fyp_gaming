using System;
using LuanShi.Engine;

// [LS015-W2] Contract B: deny-by-default feedback allowlist.
//
// The table enumerates every rejection string reachable from the supported actions (domestic +
// march + the Submit-level guards), asserts the mapped category is safe, and proves that hidden
// worlds produce byte-identical model-visible feedback.

internal static partial class Harness
{
    private static void RunFeedbackPolicy()
    {
        Section("[LS015-W2] feedback policy: typed allowlist over every reachable rejection");
        FeedbackPolicyTable();
        FeedbackPolicyHiddenWorlds();
    }

    private static void FeedbackPolicyTable()
    {
        var table = new[]
        {
            // Submit-level guards.
            ("not this seat's turn", FeedbackPolicy.Generic),
            ("game over", FeedbackPolicy.GameOver),
            ("unknown seat 'seat-zzz'", FeedbackPolicy.InvalidTarget),
            ("not in orders phase (resolution)", FeedbackPolicy.Generic),
            ("no command points left", FeedbackPolicy.NoOrdersLeft),
            ("unknown action type 'bogus'", FeedbackPolicy.UnsupportedAction),
            // Domestic orders (levy_tax, lighten_labor, reclaim, recruit, train, fortify, relief).
            ("unknown city 'city-zzz'", FeedbackPolicy.InvalidTarget),
            ("not your city", FeedbackPolicy.InvalidTarget),
            ("not enough gold (needs 40)", FeedbackPolicy.NotEnoughGold),
            ("city already reclaimed this season", FeedbackPolicy.CapReached),
            ("land fully reclaimed (cap 60%)", FeedbackPolicy.CapReached),
            ("unknown unit_type 'dragon'", FeedbackPolicy.UnsupportedAction),
            ("archer needs Military 2 research", FeedbackPolicy.Generic),
            ("garrison is archer; recruit the same type or empty the garrison first",
                FeedbackPolicy.Generic),
            ("not enough gold (needs 200)", FeedbackPolicy.NotEnoughGold),
            ("not enough population (needs 1000)", FeedbackPolicy.NotEnoughPopulation),
            ("training already at max (3)", FeedbackPolicy.CapReached),
            ("defense already at max (5)", FeedbackPolicy.CapReached),
            ("not enough food (needs 300)", FeedbackPolicy.NotEnoughFood),
            // March.
            ("unknown army 'army-zzz'", FeedbackPolicy.InvalidTarget),
            ("not your army", FeedbackPolicy.InvalidTarget),
            ("army has no troops", FeedbackPolicy.Generic),
            ("army already marched this season", FeedbackPolicy.AlreadyMarched),
            ("army already marched 2 times this season", FeedbackPolicy.AlreadyMarched),
            ("destination not walkable", FeedbackPolicy.DestinationBlocked),
            ("already there", FeedbackPolicy.Generic),
            ("destination is beyond your sight", FeedbackPolicy.DestinationNotVisible),
            ("destination occupied by an army", FeedbackPolicy.DestinationBlocked),
            ("enemy city — battle is beyond this demo", FeedbackPolicy.UnsupportedAction),
            ("destination out of reach this season (max cost 6)",
                FeedbackPolicy.DestinationNotReachable),
            // Parser/host messages that could reach the same display path.
            ("target is required", FeedbackPolicy.Generic),
            ("invalid field type", FeedbackPolicy.UnsupportedAction),
            ("forbidden field", FeedbackPolicy.UnsupportedAction),
            ("unsupported argument", FeedbackPolicy.UnsupportedAction),
            ("string too long", FeedbackPolicy.Generic),
            ("host submit failed", FeedbackPolicy.Generic),
            ("something nobody anticipated", FeedbackPolicy.Generic),
        };

        bool allMapped = true, allSafe = true, noHiddenFacts = true;
        foreach (var row in table)
        {
            string mapped = FeedbackPolicy.ForModel(row.Item1);
            allMapped &= mapped == row.Item2;
            allSafe &= FeedbackPolicy.IsSafeCategory(mapped);
            noHiddenFacts &= !mapped.Contains("'") && !mapped.Contains("\"")
                && !mapped.Contains("city-") && !mapped.Contains("army-")
                && !mapped.Contains("seat-") && !mapped.Contains("0")
                && !mapped.Contains("1") && !mapped.Contains("2") && !mapped.Contains("3")
                && !mapped.Contains("4") && !mapped.Contains("5") && !mapped.Contains("6")
                && !mapped.Contains("7") && !mapped.Contains("8") && !mapped.Contains("9");
        }
        W2Check(table.Length >= 35, $"the table enumerates every reachable rejection ({table.Length})");
        W2Check(allMapped, "every reachable rejection maps to its typed safe category");
        W2Check(allSafe, "every mapped value is in the published allowlist");
        W2Check(noHiddenFacts, "no mapped message echoes an id, quote or engine number");
        W2Check(FeedbackPolicy.ForModel(null) == FeedbackPolicy.Generic
           && FeedbackPolicy.ForModel("") == FeedbackPolicy.Generic,
            "null/empty feedback collapses to the generic category");
    }

    private static void FeedbackPolicyHiddenWorlds()
    {
        var baseline = HiddenWorld(9301, "baseline");
        var withArmy = HiddenWorld(9301, "army");

        // Same model-visible answer whether a hidden-but-existing army id exists or not.
        var missing = baseline.Submit(MarchOrder(baseline, "seat-a", "army-hidden", 5, 0));
        var foreign = withArmy.Submit(MarchOrder(withArmy, "seat-a", "army-hidden", 5, 0));
        W2Check(!missing.Ok && !foreign.Ok
           && FeedbackPolicy.ForModel(missing.Error) == FeedbackPolicy.InvalidTarget
           && FeedbackPolicy.ForModel(missing.Error) == FeedbackPolicy.ForModel(foreign.Error),
            "a hidden-but-existing army id and a nonexistent one give identical 'invalid target' feedback");

        // Same destination, same out-of-sight answer in both worlds.
        var baseDest = baseline.Submit(MarchOrder(baseline, "seat-a", "army-a-1", 5, 0));
        var hiddenDest = withArmy.Submit(MarchOrder(withArmy, "seat-a", "army-a-1", 5, 0));
        W2Check(!baseDest.Ok && !hiddenDest.Ok
           && FeedbackPolicy.ForModel(baseDest.Error) == FeedbackPolicy.DestinationNotVisible
           && FeedbackPolicy.ForModel(baseDest.Error) == FeedbackPolicy.ForModel(hiddenDest.Error),
            "an out-of-sight destination gives identical 'destination not visible' feedback");
    }

}
