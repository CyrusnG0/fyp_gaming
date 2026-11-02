using System;
using System.Collections.Generic;
using LuanShi.Engine;

// [LS015-W2] Contract B: bounded, deterministic MOVEMENT OPTIONS prompt text.
//
// The prompt carries only seat-observable movement facts, stays under the shared cap, drops
// options deterministically (captures first) and is byte-identical across hidden worlds.

internal static partial class Harness
{
    private static void RunLlmMovementPrompts()
    {
        Section("[LS015-W2] movement prompt: bounded, deterministic, hidden-world neutral");
        PromptBuilder.ResetPromptVersion();
        MovementPromptSection();
        MovementPromptDomesticUnchanged();
        MovementPromptTruncation();
        MovementPromptHiddenWorlds();
        PromptBuilder.ResetPromptVersion();

        Section("[LS015-W3] movement prompt: rendered option notes");
        MovementPromptNotesRendered();
        MovementPromptNotesBounded();
        MovementPromptNotesDomesticAbsent();
        MovementPromptNotesNeverLeakHiddenIds();
        PromptBuilder.ResetPromptVersion();
    }

    private static void W3Check(bool ok, string name) => Check(ok, "[LS015-W3] " + name);

    private static void MovementPromptSection()
    {
        var engine = W2CaptureWorld(9201);
        var obs = engine.Observe("seat-a");
        var view = MovementPlanner.Build(engine, "seat-a");
        var history = new List<string> { "levy_tax city-a -> ok" };
        string prompt = PromptBuilder.UserPrompt(obs, ActionCatalogue.Build(obs), history, 4, 3, view);

        W2Check(PromptBuilder.VersionFor(true) == "demo-slice-4"
           && PromptBuilder.SystemPromptFor(true).Contains("MOVEMENT OPTIONS"),
            "planning prompts use demo-slice-4 and advertise the movement section");
        W2Check(prompt.Contains("MOVEMENT OPTIONS (candidates, revalidated by the engine)")
           && prompt.Contains("army_id=army-a-1 unit_type=infantry position=1,1")
           && prompt.Contains("marches_left=1") && prompt.Contains("budget=")
           && prompt.Contains("2,0 cost=2 capture_neutral_city"),
            "the section names each own army and its listed candidate destinations");
        W2Check(!prompt.Contains("city-hidden")
           && prompt == PromptBuilder.UserPrompt(obs, ActionCatalogue.Build(obs),
                history, 4, 3, MovementPlanner.Build(engine, "seat-a")),
            "the movement prompt contains no hidden id and is deterministic");
    }

    private static void MovementPromptDomesticUnchanged()
    {
        var engine = MovementFixture(9202, 2, UnitTypes.Infantry);
        var obs = engine.Observe("seat-a");
        var history = new List<string>();
        string domestic = PromptBuilder.UserPrompt(obs, ActionCatalogue.Build(obs), history, 4, 3);

        W2Check(PromptBuilder.VersionFor(false) == "demo-slice-3"
           && PromptBuilder.SystemPromptFor(false) == PromptBuilder.DomesticSystemPrompt()
           && !PromptBuilder.SystemPromptFor(false).Contains("MOVEMENT OPTIONS"),
            "a non-planning host keeps the exact demo-slice-3 system text");
        W2Check(domestic.Contains("Prompt version: demo-slice-3")
           && !domestic.Contains("MOVEMENT OPTIONS")
           && domestic == PromptBuilder.UserPrompt(obs, ActionCatalogue.Build(obs), history, 4, 3),
            "the domestic user prompt is unchanged (no movement section, stable text)");
    }

    private static void MovementPromptTruncation()
    {
        var engine = MovementFixture(9203, 2, UnitTypes.Infantry);
        var obs = engine.Observe("seat-a");
        var options = ActionCatalogue.Build(obs);
        var armies = new List<ArmyMovePlan>();
        var view = new MovementView
        {
            SeatId = "seat-a", Season = 2, CommandPoints = 5, MarchOrderCost = 1,
            Armies = armies,
        };
        // 300 armies x 12 options is far over the prompt cap. The first option of every army is a
        // capture, so a deterministic captures-first prefix must survive truncation.
        for (int army = 0; army < 300; army++)
        {
            var planOptions = new List<MoveOption>();
            var plan = new ArmyMovePlan
            {
                ArmyId = "army-" + army, ArmyName = "A" + army, UnitType = UnitTypes.Infantry,
                X = army % 9, Z = army % 7, MarchesLeft = 1, Budget = 6,
                CanMarch = true, TotalReachable = 12, Truncated = true,
                Options = planOptions,
            };
            for (int i = 0; i < 12; i++)
                planOptions.Add(new MoveOption
                {
                    ArmyId = plan.ArmyId, X = i, Z = army % 7, PathCost = i,
                    Kind = i == 0 ? "capture_neutral_city" : "move",
                });
            armies.Add(plan);
        }

        string first = PromptBuilder.UserPrompt(obs, options, new List<string>(), 4, 3, view);
        string second = PromptBuilder.UserPrompt(obs, options, new List<string>(), 4, 3, view);
        int total = PromptBuilder.SystemPromptFor(true).Length + first.Length;
        W2Check(total <= PromptBuilder.MaxPromptChars,
            $"an oversized movement list is truncated under the prompt cap ({total})");
        W2Check(first == second && first.Contains("[truncated:")
           && first.Contains("0,0 cost=0 capture_neutral_city"),
            "truncation is deterministic and keeps each army's captures-first prefix");
    }

    private static void MovementPromptHiddenWorlds()
    {
        var baseline = HiddenWorld(9204, "baseline");
        string basePrompt = MovementPromptFor(baseline);
        W2Check(basePrompt.Contains("army_id=army-a-1"),
            "the hidden-world baseline prompt advertises the own army");

        string[] variants = { "army", "adjacent", "city", "tower" };
        foreach (string variant in variants)
        {
            var world = HiddenWorld(9204, variant);
            W2Check(MovementPromptFor(world) == basePrompt,
                $"hidden {variant}: the movement prompt is byte-identical");
        }
    }

    private static string MovementPromptFor(GameEngine engine)
    {
        Observation obs = engine.Observe("seat-a");
        return PromptBuilder.UserPrompt(obs, ActionCatalogue.Build(obs), new List<string>(),
            4, 3, MovementPlanner.Build(engine, "seat-a"));
    }

    // ---- [LS015-W3] rendered option notes ----------------------------------

    private static void MovementPromptNotesRendered()
    {
        var cityEngine = W2CaptureWorld(9401);
        var cityObs = cityEngine.Observe("seat-a");
        var cityView = MovementPlanner.Build(cityEngine, "seat-a");
        string cityPrompt = PromptBuilder.UserPrompt(cityObs, ActionCatalogue.Build(cityObs),
            new List<string>(), 4, 3, cityView);
        W3Check(cityPrompt.Contains(
                "2,0 cost=2 capture_neutral_city - neutral city in sight, march captures on arrival"),
            "the neutral-city option renders its note after the kind");

        var terrainEngine = MovementFixture(9402, 2, UnitTypes.Infantry);
        ApplyMovementTerrain(terrainEngine);
        var terrainObs = terrainEngine.Observe("seat-a");
        var terrainView = MovementPlanner.Build(terrainEngine, "seat-a");
        string first = PromptBuilder.UserPrompt(terrainObs, ActionCatalogue.Build(terrainObs),
            new List<string>(), 4, 3, terrainView);
        string second = PromptBuilder.UserPrompt(terrainObs, ActionCatalogue.Build(terrainObs),
            new List<string>(), 4, 3, terrainView);
        W3Check(first == second && first.Contains(" - terrain cost counted"),
            "the terrain-cost note renders and the prompt is deterministic");

        // MovementOptions.cs emits only these three note strings (plus null). Anything else in a
        // test-built view was not produced by the engine.
        bool onlyEngineNotes = true;
        foreach (ArmyMovePlan plan in cityView.Armies)
        {
            if (plan.Options == null) continue;
            foreach (MoveOption option in plan.Options)
                if (option.Note != null
                    && option.Note != "terrain cost counted"
                    && option.Note != "neutral city in sight; march captures on arrival"
                    && option.Note != "watch tower in sight; march captures on arrival")
                    onlyEngineNotes = false;
        }
        W3Check(onlyEngineNotes,
            "the rendered notes are exactly the three engine-produced safe strings");
    }

    private static void MovementPromptNotesBounded()
    {
        var engine = MovementFixture(9403, 2, UnitTypes.Infantry);
        var obs = engine.Observe("seat-a");
        var options = ActionCatalogue.Build(obs);

        var oneArmies = new List<ArmyMovePlan>();
        var oneView = new MovementView
        {
            SeatId = "seat-a", Season = 2, CommandPoints = 5, MarchOrderCost = 1,
            Armies = oneArmies,
        };
        var oneOptions = new List<MoveOption>
        {
            new MoveOption
            {
                ArmyId = "army-a-1", X = 0, Z = 1, PathCost = 1, Kind = "move",
                Note = new string('N', 500),
            },
        };
        oneArmies.Add(new ArmyMovePlan
        {
            ArmyId = "army-a-1", UnitType = UnitTypes.Infantry, X = 1, Z = 1,
            MarchesLeft = 1, Budget = 6, CanMarch = true, TotalReachable = 1, Options = oneOptions,
        });
        string notePrompt = PromptBuilder.UserPrompt(obs, options, new List<string>(), 4, 3, oneView);
        string bounded = new string('N', PromptBuilder.MaxOptionNoteChars);
        W3Check(notePrompt.Contains(" - " + bounded) && !notePrompt.Contains(bounded + "N"),
            "a long note is cut deterministically at MaxOptionNoteChars");

        // Notes still obey the shared prompt cap: the fallback drops options, deterministically.
        var bigArmies = new List<ArmyMovePlan>();
        var bigView = new MovementView
        {
            SeatId = "seat-a", Season = 2, CommandPoints = 5, MarchOrderCost = 1,
            Armies = bigArmies,
        };
        for (int army = 0; army < 40; army++)
        {
            var planOptions = new List<MoveOption>();
            for (int i = 0; i < 12; i++)
                planOptions.Add(new MoveOption
                {
                    ArmyId = "army-" + army, X = i, Z = army % 7, PathCost = i,
                    Kind = i == 0 ? "capture_neutral_city" : "move",
                    Note = new string('N', 200),
                });
            bigArmies.Add(new ArmyMovePlan
            {
                ArmyId = "army-" + army, UnitType = UnitTypes.Infantry, X = army % 9, Z = army % 7,
                MarchesLeft = 1, Budget = 6, CanMarch = true, TotalReachable = 12, Truncated = true,
                Options = planOptions,
            });
        }
        string bigFirst = PromptBuilder.UserPrompt(obs, options, new List<string>(), 4, 3, bigView);
        string bigSecond = PromptBuilder.UserPrompt(obs, options, new List<string>(), 4, 3, bigView);
        int total = PromptBuilder.SystemPromptFor(true).Length + bigFirst.Length;
        W3Check(total <= PromptBuilder.MaxPromptChars && bigFirst == bigSecond
           && bigFirst.Contains("[truncated:"),
            $"notes stay inside the prompt cap and options drop deterministically ({total})");
    }

    private static void MovementPromptNotesDomesticAbsent()
    {
        var engine = W2CaptureWorld(9404);
        var obs = engine.Observe("seat-a");
        string domestic = PromptBuilder.UserPrompt(obs, ActionCatalogue.Build(obs),
            new List<string>(), 4, 3);
        W3Check(!domestic.Contains("MOVEMENT OPTIONS")
           && !domestic.Contains("neutral city in sight")
           && !domestic.Contains("terrain cost counted")
           && !domestic.Contains("march captures on arrival"),
            "a non-planning host's demo-slice-3 prompt renders no option notes");
        W3Check(PromptBuilder.VersionFor(true) == "demo-slice-4"
           && PromptBuilder.SystemPromptFor(false) == PromptBuilder.DomesticSystemPrompt(),
            "the system text is unchanged, so the prompt version stays demo-slice-4");
    }

    private static void MovementPromptNotesNeverLeakHiddenIds()
    {
        string basePrompt = MovementPromptFor(HiddenWorld(9405, "baseline"));
        W3Check(basePrompt.Contains("MOVEMENT OPTIONS")
           && !basePrompt.Contains("city-hidden")
           && !basePrompt.Contains("army-hidden")
           && !basePrompt.Contains("tower-hidden"),
            "notes never name city/army/tower ids outside the seat observation");

        string[] variants = { "army", "adjacent", "city", "tower" };
        foreach (string variant in variants)
            W3Check(MovementPromptFor(HiddenWorld(9405, variant)) == basePrompt,
                $"hidden {variant}: rendered notes keep the planning prompt byte-identical");
    }
}
