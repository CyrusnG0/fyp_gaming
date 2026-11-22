using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        FakePlanGarrisonNotDefaultWhenFlagEnabled();
        FakeGarrisonRehearsalRunsThroughSingleLlmTurnHost();
        FakeGarrisonRehearsalHandlesExistingArmyId();
        FakeGarrisonRehearsalFallsBackWhenUnaffordable();
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

    private static void FakePlanGarrisonNotDefaultWhenFlagEnabled()
    {
        var engine = CombinedWorld(9605);
        var config = new LlmConfig { Provider = "fake", EnableGarrisonConversion = true };
        var obs = engine.Observe("seat-b");
        var view = MovementPlanner.Build(engine, "seat-b");
        var plan = FakeTurnPlan.Build(view, obs, "seat-b");
        W3Check(config.EnableGarrisonConversion
                && plan.Count == 3
                && !plan.Any(line => line.Contains("\"type\":\"recruit\"")
                                  || line.Contains("\"type\":\"convert_garrison\"")),
            "enabling EnableGarrisonConversion does not make recruit->convert->march the default FakeTurnPlan");
    }

    private static void FakeGarrisonRehearsalRunsThroughSingleLlmTurnHost()
    {
        var engine = CombinedWorld(9606);
        // Demo-like opening for seat-b: city-b at (7,5) starts with 0 garrison; army-b-1 stands away at (6,4).
        var baseConfig = new LlmConfig
        {
            Provider = "deepseek",
            BaseUrl = "https://api.deepseek.com",
            Model = "deepseek-chat",
            EnableGarrisonConversion = false,
            MaxUsd = 1.0,
        };
        LlmConfig rehearsalConfig = FakeTurnPlan.GarrisonRehearsalConfig(baseConfig);
        W3Check(!baseConfig.EnableGarrisonConversion
                && baseConfig.Provider == "deepseek"
                && rehearsalConfig.Provider == "fake"
                && !rehearsalConfig.HasApiKey
                && rehearsalConfig.EnableGarrisonConversion
                && !rehearsalConfig.MaxUsd.HasValue,
            "garrison rehearsal config isolates provider='fake', strips keys/USD caps, and leaves baseConfig untouched");

        string runsDir = Path.Combine(Path.GetTempPath(), "luanshi-fake-garrison-" + Guid.NewGuid().ToString("N"));
        try
        {
            var recorder = new LlmRunRecorder(runsDir);
            var fake = new FakeLlmTransport();
            var runner = new LlmTurnRunner(fake, rehearsalConfig, recorder);
            bool endTurnRequested = false;
            int token = engine.BeginSeatTurn("seat-b");
            var host = new SingleLlmTurnHost(
                engine,
                rehearsalConfig,
                "seat-b",
                () => engine.Observe("seat-b"),
                cmd => engine.SubmitAs("seat-b", token, cmd),
                () => endTurnRequested = true);

            FakeTurnPlan.EnqueueGarrisonRehearsal(fake, host, "seat-b", rehearsalConfig.SpatialContext);
            runner.Begin(host, token, "seat-b", 9606);
            for (int i = 0; i < 64 && (runner.State == RunnerState.Requesting || runner.State == RunnerState.Executing); i++)
            {
                runner.Tick();
                System.Threading.Thread.Sleep(1);
            }
            engine.EndSeatTurn(token);

            ArmyState formed = engine.State.FindArmy("army-seat-b-1");
            CityState city = engine.State.FindCity("city-b");
            var evt = Find(engine.Log, "garrison_converted");
            string[] logLines = File.Exists(recorder.FilePath)
                ? File.ReadAllLines(recorder.FilePath) : Array.Empty<string>();

            W3Check(runner.State == RunnerState.Finished
                    && runner.EndReason == EndReason.ModelEndedTurn
                    && endTurnRequested,
                "garrison rehearsal completes cleanly through SingleLlmTurnHost (" + runner.EndReason + ")");
            W3Check(fake.CallCount == 4 && runner.RequestsThisTurn == 4 && runner.ActionsThisTurn == 3,
                "garrison rehearsal sends 4 fake requests and executes 3 accepted actions");
            W3Check(city.Garrison == 0 && city.Gold == 900 && city.Population == 9000,
                "recruit + convert_garrison update city-b resources and empty its garrison");
            W3Check(formed != null && formed.Troops == 1000
                    && (formed.X != city.X || formed.Z != city.Z)
                    && evt != null && evt.Payload["army_id"] == "army-seat-b-1",
                "converted army-seat-b-1 forms with 1000 troops and marches off city-b");
            W3Check(logLines.Length == 4
                    && logLines.All(line => line.Contains("\"provider\":\"fake\"")
                                         && line.Contains(PromptBuilder.GarrisonConversionVersionSuffix))
                    && logLines[0].Contains("\\\"type\\\":\\\"recruit\\\"")
                    && logLines[1].Contains("\\\"type\\\":\\\"convert_garrison\\\"")
                    && logLines[2].Contains("\\\"type\\\":\\\"march\\\"")
                    && logLines[3].Contains("\\\"end_turn\\\""),
                "run log records provider=fake, +garrison-conversion-2, and recruit -> convert_garrison -> march -> end_turn");
        }
        finally
        {
            if (Directory.Exists(runsDir)) Directory.Delete(runsDir, true);
        }
    }

    private static void FakeGarrisonRehearsalHandlesExistingArmyId()
    {
        var engine = CombinedWorld(9607);
        // Simulate an already-existing army-seat-b-1 from an earlier turn away from city-b.
        engine.State.Armies.Add(new ArmyState
        {
            Id = "army-seat-b-1", Name = "Prior Army", OwnerSeatId = "seat-b",
            Troops = 1000, UnitType = UnitTypes.Infantry, X = 6, Z = 5,
        });

        LlmConfig config = FakeTurnPlan.GarrisonRehearsalConfig();
        var fake = new FakeLlmTransport();
        var runner = new LlmTurnRunner(fake, config, null);
        int token = engine.BeginSeatTurn("seat-b");
        var host = new SingleLlmTurnHost(
            engine, config, "seat-b",
            () => engine.Observe("seat-b"),
            cmd => engine.SubmitAs("seat-b", token, cmd),
            () => { });

        FakeTurnPlan.EnqueueGarrisonRehearsal(fake, host, "seat-b", config.SpatialContext);
        runner.Begin(host, token, "seat-b", 9607);
        for (int i = 0; i < 64 && (runner.State == RunnerState.Requesting || runner.State == RunnerState.Executing); i++)
        {
            runner.Tick();
            System.Threading.Thread.Sleep(1);
        }
        engine.EndSeatTurn(token);

        ArmyState second = engine.State.FindArmy("army-seat-b-2");
        CityState city = engine.State.FindCity("city-b");
        W3Check(runner.ActionsThisTurn == 3 && second != null
                && (second.X != city.X || second.Z != city.Z),
            "when army-seat-b-1 already exists, dynamic rehearsal resolves and marches army-seat-b-2");
    }

    private static void FakeGarrisonRehearsalFallsBackWhenUnaffordable()
    {
        var engine = CombinedWorld(9608);
        engine.State.FindCity("city-b").Gold = 0;
        LlmConfig config = FakeTurnPlan.GarrisonRehearsalConfig();
        var fake = new FakeLlmTransport();
        var runner = new LlmTurnRunner(fake, config, null);
        int token = engine.BeginSeatTurn("seat-b");
        var host = new SingleLlmTurnHost(
            engine, config, "seat-b",
            () => engine.Observe("seat-b"),
            cmd => engine.SubmitAs("seat-b", token, cmd),
            () => { });

        FakeTurnPlan.EnqueueGarrisonRehearsal(fake, host, "seat-b", config.SpatialContext);
        runner.Begin(host, token, "seat-b", 9608);
        for (int i = 0; i < 32 && (runner.State == RunnerState.Requesting || runner.State == RunnerState.Executing); i++)
        {
            runner.Tick();
            System.Threading.Thread.Sleep(1);
        }
        engine.EndSeatTurn(token);

        W3Check(runner.State == RunnerState.Finished
                && runner.EndReason == EndReason.ModelEndedTurn
                && fake.CallCount == 1
                && runner.ActionsThisTurn == 0
                && runner.InvalidThisTurn == 0,
            "unaffordable city falls back immediately to end_turn without rejected orders");
    }
}
