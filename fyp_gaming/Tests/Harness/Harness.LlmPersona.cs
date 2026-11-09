using System;
using System.Collections.Generic;
using System.IO;
using LuanShi.Engine;

// Headless unit and integration tests for opt-in per-country LLM personas.
// Covers configuration, disabled preservation, architect text fidelity,
// manual_lookup inquiry compatibility, unknown faction fallback, and fog/validation invariance.

internal static partial class Harness
{
    private static void RunLlmPersona()
    {
        Section("[PERSONA] opt-in per-country LLM personas");
        TestPersonaOptInAndConfig();
        TestPersonaDisabledBehaviorPreserved();
        TestPersonaArchitectPersonasAndText();
        TestPersonaManualLookupCompatibility();
        TestPersonaUnknownFactionsAndMultiSeat();
        TestPersonaInvarianceAndFogIntegrity();
    }

    private static void PCheck(bool ok, string name) => Check(ok, "[PERSONA] " + name);

    private sealed class PersonaTestHost : ITurnHost, IPersonaHost
    {
        public string FactionKey { get; set; }
        public Observation Observe() => null;
        public ValidationResult Submit(ActionCommand cmd) => ValidationResult.Success;
        public void EndTurn() { }
    }

    // ---- 1. Opt-in & configuration -----------------------------------------

    private static void TestPersonaOptInAndConfig()
    {
        var cfg = new LlmConfig();
        PCheck(!cfg.EnablePersonas, "EnablePersonas is false by default");
        string redactedDefault = cfg.Redacted();
        PCheck(!redactedDefault.Contains("personas="), "Redacted() does not mention personas when disabled");

        string dir = LlmTempDir("persona-cfg");
        try
        {
            string path1 = Path.Combine(dir, "optin.json");
            File.WriteAllText(path1, "{\"enable_personas\":true}");
            LlmConfig loaded1 = LlmConfig.LoadFromFile(path1);
            PCheck(loaded1.EnablePersonas, "loading config sets EnablePersonas = true");
            PCheck(loaded1.Redacted().Contains("personas=true"), "Redacted() contains personas=true when enabled");

            string path2 = Path.Combine(dir, "overrides.json");
            File.WriteAllText(path2,
                "{\"enable_personas\":true,\"personas\":{\"custom-seat\":\"shu-aggressive\",\"wei\":\"wei-mild\"}}");
            LlmConfig loaded2 = LlmConfig.LoadFromFile(path2);
            PCheck(loaded2.PersonaOverrides.Count == 2, "overrides parsed into dictionary");
            PCheck(LlmPersona.Resolve("custom-seat", loaded2).Id == LlmPersona.ShuPersonaId,
                "custom-seat resolves to shu-aggressive override");
            PCheck(LlmPersona.Resolve("wei", loaded2).Id == LlmPersona.WeiPersonaId,
                "wei resolves to wei-mild override");
            PCheck(LlmPersona.ResolveIfEnabled(loaded2, "custom-seat").Id == LlmPersona.ShuPersonaId,
                "ResolveIfEnabled returns override persona when enabled");

            loaded2.EnablePersonas = false;
            PCheck(LlmPersona.ResolveIfEnabled(loaded2, "custom-seat") == null,
                "ResolveIfEnabled returns null when EnablePersonas is false");
            PCheck(LlmPersona.ResolveForTurn(loaded2, null, null, "custom-seat") == null,
                "ResolveForTurn returns null when EnablePersonas is false");
        }
        finally { LlmDelete(dir); }
    }

    // ---- 2. Disabled behavior preservation ----------------------------------

    private static void TestPersonaDisabledBehaviorPreserved()
    {
        PCheck(PromptBuilder.VersionFor(true, false, false) == PromptBuilder.PromptVersion,
            "VersionFor(true, false, false) is demo-slice-4");
        PCheck(PromptBuilder.VersionFor(false, false, false) == PromptBuilder.DomesticPromptVersion,
            "VersionFor(false, false, false) is demo-slice-3");
        PCheck(PromptBuilder.VersionFor(true, true, false) == PromptBuilder.InquiryPromptVersion,
            "VersionFor(true, true, false) is demo-slice-4-inquiry-1");
        PCheck(PromptBuilder.VersionFor(false, true, false) == PromptBuilder.InquiryPromptVersion,
            "VersionFor(false, true, false) is demo-slice-4-inquiry-1");

        LlmPersona nil = null;
        PCheck(PromptBuilder.VersionFor(true, false, nil) == PromptBuilder.PromptVersion,
            "VersionFor with null persona matches planning version");
        PCheck(PromptBuilder.VersionFor(false, false, nil) == PromptBuilder.DomesticPromptVersion,
            "VersionFor with null persona matches domestic version");
        PCheck(PromptBuilder.VersionFor(true, true, nil) == PromptBuilder.InquiryPromptVersion,
            "VersionFor with inquiry and null persona matches inquiry version");

        PCheck(PromptBuilder.SystemPromptFor(true, false, nil) == PromptBuilder.SystemPromptFor(true, false),
            "SystemPromptFor(true, false, null) matches baseline");
        PCheck(PromptBuilder.SystemPromptFor(false, false, nil) == PromptBuilder.SystemPromptFor(false, false),
            "SystemPromptFor(false, false, null) matches baseline");
        PCheck(PromptBuilder.SystemPromptFor(true, true, nil) == PromptBuilder.SystemPromptFor(true, true),
            "SystemPromptFor(true, true, null) matches baseline");

        PCheck(PromptBuilder.SchemaHashFor(true, false, nil) == PromptBuilder.SchemaHashFor(true, false),
            "SchemaHashFor(true, false, null) matches baseline");
        PCheck(PromptBuilder.SchemaHashFor(true, true, nil) == PromptBuilder.SchemaHashFor(true, true),
            "SchemaHashFor(true, true, null) matches baseline");

        GameEngine engine = LlmEngine();
        Observation obs = engine.Observe("seat-a");
        List<ActionOption> options = ActionCatalogue.Build(obs);
        MovementView move = MovementPlanner.Build(engine, "seat-a");
        var history = new List<string> { "history item 1" };

        string domWithout = PromptBuilder.UserPrompt(obs, options, history, 4, 3);
        string domWithNull = PromptBuilder.UserPrompt(obs, options, history, 4, 3, nil);
        PCheck(domWithout == domWithNull, "domestic UserPrompt with null persona is byte-for-byte identical");

        string planWithout = PromptBuilder.UserPrompt(obs, options, history, 4, 3, move);
        string planWithNull = PromptBuilder.UserPrompt(obs, options, history, 4, 3, move, nil);
        PCheck(planWithout == planWithNull, "planning UserPrompt with null persona is byte-for-byte identical");
    }

    // ---- 3. Both personas & exact architect text + persona IDs --------------

    private static void TestPersonaArchitectPersonasAndText()
    {
        const string expectedShu =
            "You are an ambitious, aggressive ruler. Prioritise expanding territory, " +
            "marching armies toward neutral and enemy cities, and seizing opportunities even " +
            "at some risk. Do not sit idle when a legal march can improve your position.";
        const string expectedWei =
            "You are a cautious, defensive ruler. Prioritise food security, fortification, " +
            "troop training and steady growth. Avoid risky marches unless your food and " +
            "defences are secure.";

        PCheck(LlmPersona.ShuPromptText == expectedShu, "ShuPromptText matches architect exact text");
        PCheck(LlmPersona.WeiPromptText == expectedWei, "WeiPromptText matches architect exact text");

        LlmPersona shu = LlmPersona.ForKey("Shu");
        PCheck(shu.Id == LlmPersona.ShuPersonaId && !shu.IsDefault, "Shu resolves to ShuAggressive");
        PCheck(LlmPersona.ForKey("蜀") == LlmPersona.ShuAggressive, "蜀 resolves to ShuAggressive");
        PCheck(LlmPersona.ForKey("aggressive") == LlmPersona.ShuAggressive, "aggressive resolves to ShuAggressive");
        PCheck(LlmPersona.ForKey("seat-shu") == LlmPersona.ShuAggressive, "seat-shu resolves to ShuAggressive");

        string shuSys = PromptBuilder.SystemPromptFor(true, false, shu);
        PCheck(shuSys.Contains("Persona ID: " + LlmPersona.ShuPersonaId), "Shu system prompt contains Persona ID");
        PCheck(shuSys.EndsWith(expectedShu), "Shu system prompt ends with exact architect Shu prompt text");

        LlmPersona wei = LlmPersona.ForKey("Wei");
        PCheck(wei.Id == LlmPersona.WeiPersonaId && !wei.IsDefault, "Wei resolves to WeiMild");
        PCheck(LlmPersona.ForKey("魏") == LlmPersona.WeiMild, "魏 resolves to WeiMild");
        PCheck(LlmPersona.ForKey("mild") == LlmPersona.WeiMild, "mild resolves to WeiMild");
        PCheck(LlmPersona.ForKey("faction-wei") == LlmPersona.WeiMild, "faction-wei resolves to WeiMild");

        string weiSys = PromptBuilder.SystemPromptFor(true, false, wei);
        PCheck(weiSys.Contains("Persona ID: " + LlmPersona.WeiPersonaId), "Wei system prompt contains Persona ID");
        PCheck(weiSys.EndsWith(expectedWei), "Wei system prompt ends with exact architect Wei prompt text");

        GameEngine engine = LlmEngine();
        Observation obs = engine.Observe("seat-a");
        List<ActionOption> options = ActionCatalogue.Build(obs);
        MovementView move = MovementPlanner.Build(engine, "seat-a");

        string shuUser = PromptBuilder.UserPrompt(obs, options, null, 4, 3, move, shu);
        PCheck(shuUser.Contains("Prompt version: " + PromptBuilder.PersonaPromptVersion),
            "Shu UserPrompt identifies demo-slice-5-persona");
        PCheck(shuUser.Contains("Persona ID: " + LlmPersona.ShuPersonaId),
            "Shu UserPrompt identifies persona ID");

        string weiUser = PromptBuilder.UserPrompt(obs, options, null, 4, 3, move, wei);
        PCheck(weiUser.Contains("Prompt version: " + PromptBuilder.PersonaPromptVersion),
            "Wei UserPrompt identifies demo-slice-5-persona");
        PCheck(weiUser.Contains("Persona ID: " + LlmPersona.WeiPersonaId),
            "Wei UserPrompt identifies persona ID");

        string hashBase = PromptBuilder.SchemaHashFor(true, false);
        string hashShu = PromptBuilder.SchemaHashFor(true, false, shu);
        string hashWei = PromptBuilder.SchemaHashFor(true, false, wei);
        PCheck(hashBase != hashShu && hashBase != hashWei && hashShu != hashWei,
            "SchemaHashFor produces unique hashes for disabled, Shu, and Wei");
    }

    // ---- 4. Manual-lookup (inquiry) compatibility ---------------------------

    private static void TestPersonaManualLookupCompatibility()
    {
        LlmPersona shu = LlmPersona.ShuAggressive;
        PCheck(PromptBuilder.VersionFor(true, true, shu) == PromptBuilder.PersonaPromptVersion,
            "VersionFor with both inquiry and persona returns demo-slice-5-persona");

        string sys = PromptBuilder.SystemPromptFor(true, true, shu);
        PCheck(sys.Contains("OPTIONAL READ-ONLY INQUIRY (manual_lookup only)"),
            "combined system prompt contains manual_lookup addendum");
        PCheck(sys.Contains("Persona ID: " + LlmPersona.ShuPersonaId) && sys.EndsWith(LlmPersona.ShuPromptText),
            "combined system prompt contains persona addendum and ends with prompt text");

        int inqIndex = sys.IndexOf("OPTIONAL READ-ONLY INQUIRY", StringComparison.Ordinal);
        int perIndex = sys.IndexOf("FACTION PERSONA", StringComparison.Ordinal);
        PCheck(inqIndex > 0 && perIndex > inqIndex,
            "system prompt appends InquiryAddendum before PersonaAddendum deterministically");

        GameEngine engine = LlmEngine();
        Observation obs = engine.Observe("seat-a");
        List<ActionOption> options = ActionCatalogue.Build(obs);
        MovementView move = MovementPlanner.Build(engine, "seat-a");

        string user = PromptBuilder.UserPrompt(obs, options, null, 4, 3, move, null, "{\"topic\":\"recruit\"}", true, shu);
        PCheck(user.Contains("PREVIOUS INQUIRY RESULT"), "UserPrompt includes PREVIOUS INQUIRY RESULT");
        PCheck(user.Contains("Persona ID: " + LlmPersona.ShuPersonaId), "UserPrompt includes persona ID");
        PCheck(user.Contains("Prompt version: " + PromptBuilder.PersonaPromptVersion), "UserPrompt includes persona prompt version");

        string hashCombined = PromptBuilder.SchemaHashFor(true, true, shu);
        string hashInqOnly = PromptBuilder.SchemaHashFor(true, true, null);
        string hashPersonaOnly = PromptBuilder.SchemaHashFor(true, false, shu);
        PCheck(hashCombined != hashInqOnly && hashCombined != hashPersonaOnly,
            "combined schema hash uniquely fingerprints inquiry + persona combination");
    }

    // ---- 5. Unknown / undefined factions & multi-seat fallback --------------

    private static void TestPersonaUnknownFactionsAndMultiSeat()
    {
        const string expectedDefault =
            "You are a balanced, pragmatic ruler. Weigh food security, city defence, troop " +
            "readiness and legal expansion according to your current observation and command budget.";
        PCheck(LlmPersona.DefaultPromptText == expectedDefault, "DefaultPromptText matches safe documented text");

        string[] unknowns = { "Wu", "吳", "DongZhuo", "dong_zhuo", "seat-7", "", null, "   ", "custom_faction" };
        foreach (string u in unknowns)
        {
            LlmPersona p = LlmPersona.ForKey(u);
            PCheck(p == LlmPersona.DefaultBalanced && p.Id == LlmPersona.DefaultPersonaId && p.IsDefault,
                $"unknown faction '{u}' safely resolves to DefaultBalanced");
        }

        string defSys = PromptBuilder.SystemPromptFor(true, false, LlmPersona.DefaultBalanced);
        PCheck(defSys.Contains("Persona ID: " + LlmPersona.DefaultPersonaId) && defSys.EndsWith(expectedDefault),
            "DefaultBalanced system prompt contains persona ID and default prompt text");

        var engine = new GameEngine(42);
        engine.State.Map = Corridor(12);
        engine.State.Factions.Add(new FactionState { SeatId = "seat-a", Name = "Wei", Controller = ControllerType.Human });
        engine.State.Factions.Add(new FactionState { SeatId = "seat-b", Name = "Shu", Controller = ControllerType.Llm });
        engine.State.Factions.Add(new FactionState { SeatId = "seat-c", Name = "Wu", Controller = ControllerType.ScriptedAi });
        engine.State.Factions.Add(new FactionState { SeatId = "seat-d", Name = "DongZhuo", Controller = ControllerType.ScriptedAi });
        engine.BeginSeason();

        var cfg = new LlmConfig { EnablePersonas = true };

        var personaHost = new PersonaTestHost { FactionKey = "Shu" };
        PCheck(LlmPersona.ResolveForTurn(cfg, personaHost, null, "seat-c") == LlmPersona.ShuAggressive,
            "ResolveForTurn binds via IPersonaHost authoritative FactionKey first");

        Observation obsB = engine.Observe("seat-b");
        PCheck(LlmPersona.ResolveForTurn(cfg, null, obsB, "seat-b") == LlmPersona.ShuAggressive,
            "ResolveForTurn binds via observation Faction(seat).Name when host has no FactionKey");

        Observation obsC = engine.Observe("seat-c");
        PCheck(LlmPersona.ResolveForTurn(cfg, null, obsC, "seat-c") == LlmPersona.DefaultBalanced,
            "ResolveForTurn falls back to DefaultBalanced for Wu in 4-faction match");

        PCheck(LlmPersona.ResolveForTurn(cfg, null, obsB, "seat-a") == LlmPersona.DefaultBalanced,
            "ResolveForTurn ignores mismatched observation seat");

        cfg.SetPersonaOverride("seat-b", "wei-mild");
        PCheck(LlmPersona.ResolveForTurn(cfg, personaHost, obsB, "seat-b") == LlmPersona.WeiMild,
            "explicit seat override in config takes priority over host and observation");
        cfg.SetPersonaOverride("seat-b", "typo");
        PCheck(LlmPersona.ResolveForTurn(cfg, personaHost, obsB, "seat-b") == LlmPersona.ShuAggressive,
            "invalid seat override falls back to the authoritative faction");
    }

    // ---- 6. Invariance of observation, candidates, movement & fog validation -

    private static void TestPersonaInvarianceAndFogIntegrity()
    {
        GameEngine engine1 = Build(17, 3, morale: 100, food: 5000, sight: false);
        engine1.State.Cities.Add(new CityState
        {
            Id = "city-enemy-hidden", Name = "Hidden City", X = 8, Z = 0, OwnerSeatId = "seat-b",
            Population = 5000, Food = 1000, Gold = 500, Garrison = 200, Morale = 80
        });
        AddArmy(engine1, "seat-b", 8, 800);

        GameEngine engine2 = Build(17, 3, morale: 100, food: 5000, sight: false);
        engine2.State.Cities.Add(new CityState
        {
            Id = "city-enemy-other", Name = "Other City", X = 8, Z = 0, OwnerSeatId = "seat-b",
            Population = 9999, Food = 9999, Gold = 9999, Garrison = 999, Morale = 90
        });
        AddArmy(engine2, "seat-b", 8, 3000);

        Observation obs1 = engine1.Observe("seat-a");
        Observation obs2 = engine2.Observe("seat-a");
        List<ActionOption> opts1 = ActionCatalogue.Build(obs1);
        List<ActionOption> opts2 = ActionCatalogue.Build(obs2);
        MovementView move1 = MovementPlanner.Build(engine1, "seat-a");
        MovementView move2 = MovementPlanner.Build(engine2, "seat-a");

        string obsJsonOff = PromptBuilder.BoundedObservationJson(obs1);
        string optsTextOff = ActionCatalogue.ToPromptText(opts1);
        string moveTextOff = PromptBuilder.MovementOptionsText(move1);

        PCheck(obsJsonOff == PromptBuilder.BoundedObservationJson(obs1),
            "BoundedObservationJson is identical across calls");
        PCheck(optsTextOff == ActionCatalogue.ToPromptText(ActionCatalogue.Build(obs1)),
            "ActionCatalogue.ToPromptText is identical across calls");
        PCheck(moveTextOff == PromptBuilder.MovementOptionsText(MovementPlanner.Build(engine1, "seat-a")),
            "MovementOptionsText is identical across calls");

        LlmPersona[] personas = { LlmPersona.ShuAggressive, LlmPersona.WeiMild, LlmPersona.DefaultBalanced };
        foreach (LlmPersona p in personas)
        {
            string user1 = PromptBuilder.UserPrompt(obs1, opts1, null, 4, 3, move1, p);
            string user2 = PromptBuilder.UserPrompt(obs2, opts2, null, 4, 3, move2, p);
            PCheck(user1 == user2, $"UserPrompt with {p.Id} does not leak hidden enemy entities across fog worlds");
            PCheck(!user1.Contains("city-enemy") && !user1.Contains("Hidden City") && !user1.Contains("Other City"),
                $"fog-masked entities never appear in UserPrompt for {p.Id}");
        }

        int token = engine1.BeginSeatTurn("seat-a");
        var illegalCmd = new ActionCommand
        {
            Type = ActionType.March,
            ActorSeatId = "seat-a",
            TargetId = "army-seat-a-1",
            ParamA = 8,
            ParamB = 0
        };
        ValidationResult res1 = engine1.SubmitAs("seat-a", token, illegalCmd);
        PCheck(!res1.Ok, "engine validation rejects out-of-sight march identically");
    }
}
