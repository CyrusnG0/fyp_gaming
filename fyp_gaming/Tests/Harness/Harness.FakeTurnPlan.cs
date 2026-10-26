using System.Collections.Generic;
using LuanShi.Engine;

// [LS015-W3] The offline rehearsal plan (FAKE, no provider) is pure C# so its choices are testable:
// captures first, coordinates copied from the advertised options, every line labelled FAKE.

internal static partial class Harness
{
    private static void RunFakeTurnPlan()
    {
        Section("[LS015-W3] FAKE rehearsal plan: capture first, no invented coordinates");
        FakePlanPrefersCaptures();
        FakePlanFallsBackToFirstOption();
        FakePlanWithoutMovement();
        FakePlanMatchesRealMovementView();
    }

    private static ArmyMovePlan W3FakeArmy(string id, params MoveOption[] options)
        => new ArmyMovePlan
        {
            ArmyId = id, ArmyName = id, UnitType = UnitTypes.Infantry, X = 0, Z = 0,
            MarchesLeft = 1, Budget = 6, CanMarch = true,
            TotalReachable = options.Length,
            Options = new List<MoveOption>(options),
        };

    private static MoveOption W3FakeOption(string armyId, int x, int z, string kind)
        => new MoveOption
        {
            ArmyId = armyId, X = x, Z = z, PathCost = 1, Kind = kind,
            Note = kind == "move" ? null : "safe note",
        };

    private static LlmAction W3ParsePlan(string json, string label)
    {
        ParseResult parsed = LlmActionParser.Parse(json);
        W3Check(parsed.Ok, label + " is a parseable action");
        return parsed.Ok ? parsed.Action : null;
    }

    private static bool W3AllLabelled(IReadOnlyList<string> plan)
    {
        if (plan == null || plan.Count == 0) return false;
        foreach (string line in plan)
            if (line == null || !line.Contains("FAKE rehearsal")) return false;
        return true;
    }

    private static void FakePlanPrefersCaptures()
    {
        var engine = MovementFixture(9601, 2, UnitTypes.Infantry);
        var obs = engine.Observe("seat-a");
        var view = new MovementView
        {
            SeatId = "seat-a", Season = 2, CommandPoints = 5, MarchOrderCost = 1,
            Armies = new List<ArmyMovePlan>
            {
                W3FakeArmy("army-z", W3FakeOption("army-z", 0, 0, "move")),
                W3FakeArmy("army-a",
                    W3FakeOption("army-a", 1, 0, "move"),
                    W3FakeOption("army-a", 3, 1, "capture_neutral_city")),
            },
        };
        var plan = FakeTurnPlan.Build(view, obs, "seat-a");
        LlmAction march = W3ParsePlan(plan[0], "the fake march");
        W3Check(plan.Count == 3 && march != null && march.Type == ActionType.March
           && march.TargetId == "army-a" && march.ParamA == 3 && march.ParamB == 1,
            "the fake plan marches to a capture option even when another army is listed first");
        W3Check(plan[1].Contains("\"type\":\"levy_tax\"") && plan[1].Contains("city-a")
           && plan[2].Contains("\"type\":\"end_turn\"") && W3AllLabelled(plan),
            "the fake plan then levies tax and ends the turn, every line labelled FAKE");

        view.Armies = new List<ArmyMovePlan>
        {
            W3FakeArmy("army-a",
                W3FakeOption("army-a", 2, 2, "move"),
                W3FakeOption("army-a", 4, 0, "capture_tower")),
        };
        LlmAction tower = W3ParsePlan(FakeTurnPlan.Build(view, obs, "seat-a")[0], "the fake tower march");
        W3Check(tower != null && tower.TargetId == "army-a"
           && tower.ParamA == 4 && tower.ParamB == 0,
            "a tower capture is preferred over an earlier plain move");
    }

    private static void FakePlanFallsBackToFirstOption()
    {
        var engine = MovementFixture(9602, 2, UnitTypes.Infantry);
        var obs = engine.Observe("seat-a");
        var view = new MovementView
        {
            SeatId = "seat-a", Season = 2, CommandPoints = 5, MarchOrderCost = 1,
            Armies = new List<ArmyMovePlan>
            {
                W3FakeArmy("army-a",
                    W3FakeOption("army-a", 7, 3, "move"),
                    W3FakeOption("army-a", 1, 5, "move")),
            },
        };
        var plan = FakeTurnPlan.Build(view, obs, "seat-a");
        LlmAction march = W3ParsePlan(plan[0], "the fake fallback march");
        W3Check(march != null && march.TargetId == "army-a"
           && march.ParamA == 7 && march.ParamB == 3,
            "with no capture option the fake plan uses the first advertised option");
        W3Check(plan.Count == 3 && W3AllLabelled(plan),
            "the fallback plan keeps levy + end_turn and FAKE labels");
    }

    private static void FakePlanWithoutMovement()
    {
        var engine = MovementFixture(9603, 2, UnitTypes.Infantry);
        var obs = engine.Observe("seat-a");
        var plan = FakeTurnPlan.Build(null, obs, "seat-a");
        W3Check(plan.Count == 2 && !plan[0].Contains("march")
           && plan[0].Contains("\"type\":\"levy_tax\"") && plan[1].Contains("\"type\":\"end_turn\"")
           && W3AllLabelled(plan),
            "without a movement view the fake plan is a labelled levy + end_turn");

        var blocked = new MovementView
        {
            SeatId = "seat-a", Season = 2, CommandPoints = 5, MarchOrderCost = 1,
            Armies = new List<ArmyMovePlan>
            {
                new ArmyMovePlan
                {
                    ArmyId = "army-a", UnitType = UnitTypes.Infantry, CanMarch = false,
                    BlockedReason = "no orders left",
                    Options = new List<MoveOption>
                    {
                        W3FakeOption("army-a", 2, 2, "capture_neutral_city"),
                    },
                },
            },
        };
        var blockedPlan = FakeTurnPlan.Build(blocked, obs, "seat-a");
        W3Check(blockedPlan.Count == 2 && !blockedPlan[0].Contains("march"),
            "a march-blocked army is never scripted to march");
    }

    private static void FakePlanMatchesRealMovementView()
    {
        var engine = W2CaptureWorld(9604);
        var obs = engine.Observe("seat-a");
        var view = MovementPlanner.Build(engine, "seat-a");
        var plan = FakeTurnPlan.Build(view, obs, "seat-a");
        LlmAction march = W3ParsePlan(plan[0], "the real-view fake march");

        bool listed = false;
        foreach (ArmyMovePlan army in view.Armies)
            foreach (MoveOption option in army.Options)
                if (march != null && option.ArmyId == march.TargetId
                    && option.X == march.ParamA && option.Z == march.ParamB)
                    listed = option.Kind == "capture_neutral_city" || option.Kind == "capture_tower";

        W3Check(march != null && march.Type == ActionType.March && listed,
            "the fake march coordinates are copied from a real advertised capture option");
        W3Check(march != null && march.ParamA == 2 && march.ParamB == 0,
            "the fixture's neutral-city capture at (2,0) is selected");
    }
}
