using System;
using System.Collections.Generic;
using System.Globalization;
using LuanShi.Engine;

internal static partial class Harness
{
    private static void RunSpatialProductionTests()
    {
        SpatialProductionHiddenWorldAndTerrainInvariance();
        SpatialProductionContextAccuracyAndBearings();
        SpatialProductionNormalRunnerDispatchAndFeatureOff();
        SpatialProductionConfigurableLevelsAndTruncation();
        SpatialProductionWithPersonasAttackAndDeliberation();
    }

    private static GameEngine BuildCompactTwinWorld(bool variant, int seed = 515)
    {
        MapData map = DemoMap();
        if (variant)
        {
            map.Walkable[map.Index(10, 8)] = false;
            map.Cost[map.Index(12, 8)] = 4;
            map.Cost[map.Index(9, 7)] = 3;
        }

        var engine = new GameEngine(seed, "compact-prod-twin", BalanceProfile.Standard);
        engine.State.Map = map;
        CompactScenario.CompactContact.Apply(engine);
        if (variant)
        {
            ArmyState weiVanguard = engine.State.FindArmy("army-wei-1");
            weiVanguard.X = 7; weiVanguard.Z = 7;
            CityState weiSecond = engine.State.FindCity("city-wei-2");
            weiSecond.X = 3; weiSecond.Z = 10;
            CityState neutral = engine.State.FindCity("city-neutral");
            neutral.X = 11; neutral.Z = 9;
            engine.State.Armies.Add(new ArmyState
            {
                Id = "army-wei-hidden", Name = "Wei Hidden Flank", OwnerSeatId = "seat-0",
                X = 4, Z = 6, Troops = 1400, UnitType = UnitTypes.Cavalry,
            });
            engine.State.WatchTowers.Add(new WatchTowerState
            {
                Id = "tower-hidden", Name = "Western Tower", X = 2, Z = 2, OwnerSeatId = null,
            });
        }
        engine.BeginSeason();

        if (variant)
        {
            int[][] unseenCoords =
            {
                new[] { 10, 8 }, new[] { 12, 8 }, new[] { 9, 7 }, new[] { 7, 7 },
                new[] { 3, 10 }, new[] { 11, 9 }, new[] { 4, 6 }, new[] { 2, 2 },
            };
            foreach (int[] c in unseenCoords)
                Check(!engine.InSight("seat-1", c[0], c[1]),
                    $"compact twin variant mutation at ({c[0]},{c[1]}) stays outside Shu sight");
        }
        return engine;
    }

    private static FakeLlmTransport RunOneCompactSeasonAsEndTurn(GameEngine engine, bool requireVisible,
        int seed = 515)
    {
        var fake = new FakeLlmTransport();
        fake.EnqueueSuccess("{\"type\":\"end_turn\"}");
        fake.EnqueueSuccess("{\"type\":\"end_turn\"}");
        var runner = new LlmTurnRunner(fake, new LlmConfig { RequireVisibleMarchRoutes = requireVisible }, null);
        var session = new SpectatorSession(engine, runner, 1, seed);
        session.Start();
        SpectatorDrain(session);
        Check(session.State == SpectatorState.Completed && fake.Requests.Count == 2,
            "compact spectator single season produced one Wei and one Shu prompt");
        return fake;
    }

    private static void AssertNoHiddenCompactLeaks(string prompt, string label)
    {
        string[] forbidden =
        {
            "Luoyang", "Xuchang", "Wancheng", "Central Watchtower", "Wei Hidden Flank",
            "Western Tower", "army-wei", "city-wei", "city-neutral", "tower-mid",
            "unexplored", "\"cost\"",
        };
        foreach (string token in forbidden)
            Check(!prompt.Contains(token, StringComparison.Ordinal),
                label + " omits unseen token: " + token);
    }

    private static void SpatialProductionHiddenWorldAndTerrainInvariance()
    {
        try
        {
            foreach (SpatialDetailLevel level in new[] { SpatialDetailLevel.Standard, SpatialDetailLevel.Rich })
            {
                PromptBuilder.ConfigureSpatialContext(new SpatialContextOptions { Level = level });
                FakeLlmTransport twinA = RunOneCompactSeasonAsEndTurn(BuildCompactTwinWorld(false), true);
                FakeLlmTransport twinB = RunOneCompactSeasonAsEndTurn(BuildCompactTwinWorld(true), true);
                Check(string.Equals(twinA.Requests[1].System, twinB.Requests[1].System, StringComparison.Ordinal)
                      && string.Equals(twinA.Requests[1].User, twinB.Requests[1].User, StringComparison.Ordinal),
                    $"prod runner ({level}): Shu prompt is byte-identical across hidden enemy and unseen terrain worlds");
                AssertNoHiddenCompactLeaks(twinA.Requests[1].User, $"prod runner ({level})");
            }
        }
        finally
        {
            PromptBuilder.ResetSpatialContextOptions();
        }
    }

    private static void SpatialProductionContextAccuracyAndBearings()
    {
        try
        {
            PromptBuilder.ResetSpatialContextOptions();
            GameEngine engine = ScenarioEngine(CompactScenario.CompactContact, BalanceProfile.Standard, 11);
            FakeLlmTransport fake = RunOneCompactSeasonAsEndTurn(engine, true, 11);
            string shuUser = fake.Requests[1].User;

            Check(shuUser.Contains(PromptBuilder.SpatialHeader, StringComparison.Ordinal)
                  && shuUser.Contains("context_config: level=standard provider_char_limit=40000", StringComparison.Ordinal)
                  && shuUser.Contains("map: width=26 height=17 axes=\"x:0..25 West->East, z:0..16 South->North\" center=13,8", StringComparison.Ordinal)
                  && shuUser.Contains("name=\"Chengdu\" pos=20,8", StringComparison.Ordinal)
                  && shuUser.Contains("edge_dist(W=20,E=5,S=8,N=8)", StringComparison.Ordinal)
                  && shuUser.Contains("name=\"Jiangzhou\" pos=21,10", StringComparison.Ordinal)
                  && shuUser.Contains("id=army-shu-1 name=\"Shu Vanguard\" pos=18,8", StringComparison.Ordinal)
                  && shuUser.Contains("from_primary=city-shu:2@W", StringComparison.Ordinal)
                  && shuUser.Contains("to_center=5@W(dx=-5,dz=0)", StringComparison.Ordinal)
                  && shuUser.Contains("id=army-shu-2 name=\"Shu Main Host\" pos=20,9", StringComparison.Ordinal)
                  && shuUser.Contains("unseen_beyond_sight(", StringComparison.Ordinal)
                  && shuUser.Contains("spatial_truncation: truncated=true reason=move_option_cap", StringComparison.Ordinal)
                  && shuUser.Contains("16,8 cost=2 move dir=W dist=2 [frontier] (goal: frontier W (toward map_center))", StringComparison.Ordinal),
                "prod Shu prompt shows map bounds, center, own bearings, edge distances, unseen bounds and center-bound frontier move");
            AssertNoHiddenCompactLeaks(shuUser, "compact Shu opening");

            GameEngine contactEngine = new GameEngine(19, "contact-check", BalanceProfile.Standard);
            contactEngine.State.Map = DemoMap();
            CompactScenario.CompactContact.Apply(contactEngine);
            ArmyState vang = contactEngine.State.FindArmy("army-shu-1");
            vang.X = 14;
            vang.Z = 8;
            contactEngine.BeginSeason();
            Check(contactEngine.State.Map.IsWalkable(14, 8)
                  && contactEngine.InSight("seat-1", 13, 8)
                  && contactEngine.InSight("seat-1", 12, 8)
                  && !contactEngine.InSight("seat-1", 8, 8),
                "Shu vanguard at (14,8) sees Wancheng (13,8) and Central Watchtower (12,8) while Wei stays unseen");

            FakeLlmTransport contactFake = RunOneCompactSeasonAsEndTurn(contactEngine, true, 19);
            string contactUser = contactFake.Requests[1].User;
            Check(contactUser.Contains("[visible] city id=city-neutral name=\"Wancheng\" owner=neutral allegiance=neutral pos=13,8", StringComparison.Ordinal)
                  && contactUser.Contains("from_own=army-shu-1:1@W to_center=0@here edge_dist(W=13,E=12,S=8,N=8)", StringComparison.Ordinal)
                  && contactUser.Contains("[visible] watch_tower id=tower-mid name=\"Central Watchtower\" owner=neutral allegiance=neutral pos=12,8", StringComparison.Ordinal)
                  && contactUser.Contains("from_own=army-shu-1:2@W", StringComparison.Ordinal)
                  && contactUser.Contains("army=army-shu-1 -> [visible] neutral_city city-neutral at 13,8 dist=1 dir=W (dx=-1,dz=0)", StringComparison.Ordinal)
                  && !contactUser.Contains("army-wei", StringComparison.Ordinal)
                  && !contactUser.Contains("Luoyang", StringComparison.Ordinal),
                "prod Shu contact prompt accurately labels visible objectives with bearings and edge_dist while Wei stays hidden");
        }
        finally
        {
            PromptBuilder.ResetSpatialContextOptions();
        }
    }

    private static void SpatialProductionNormalRunnerDispatchAndFeatureOff()
    {
        try
        {
            PromptBuilder.ResetSpatialContextOptions();
            GameEngine engine = ScenarioEngine(CompactScenario.CompactContact, BalanceProfile.Standard, 22);
            var fake = new FakeLlmTransport();
            fake.EnqueueSuccess("{\"type\":\"end_turn\"}");
            fake.EnqueueSuccess("{\"type\":\"march\",\"target_id\":\"army-shu-1\",\"param_a\":16,\"param_b\":8,"
                + "\"reason\":\"Advance west toward Wancheng\",\"summary\":\"I march Shu Vanguard west to (16,8).\"}");
            fake.EnqueueSuccess("{\"type\":\"end_turn\"}");

            var runner = new LlmTurnRunner(fake, new LlmConfig { RequireVisibleMarchRoutes = true }, null);
            var session = new SpectatorSession(engine, runner, 1, 22);
            session.Start();
            SpectatorDrain(session);

            ArmyState shu1 = engine.State.FindArmy("army-shu-1");
            Check(session.State == SpectatorState.Completed && session.TotalAccepted == 1
                  && session.TotalRejected == 0 && shu1.X == 16 && shu1.Z == 8,
                "normal runner dispatch accepts advertised westward march for Shu Vanguard to (16,8)");

            GameEngine offEngineA = ScenarioEngine(CompactScenario.CompactContact, BalanceProfile.Standard, 23);
            FakeLlmTransport legacyFake = RunOneCompactSeasonAsEndTurn(offEngineA, false, 23);
            string legacyShu = legacyFake.Requests[1].User;

            PromptBuilder.ConfigureSpatialContext(new SpatialContextOptions { Level = SpatialDetailLevel.Off });
            GameEngine offEngineB = ScenarioEngine(CompactScenario.CompactContact, BalanceProfile.Standard, 23);
            FakeLlmTransport offSpatialFake = RunOneCompactSeasonAsEndTurn(offEngineB, true, 23);
            string offShu = offSpatialFake.Requests[1].User;

            Check(!legacyShu.Contains("SPATIAL CONTEXT", StringComparison.Ordinal)
                  && !legacyShu.Contains("spatial_truncation:", StringComparison.Ordinal)
                  && !offShu.Contains("SPATIAL CONTEXT", StringComparison.Ordinal)
                  && !offShu.Contains("context_config:", StringComparison.Ordinal)
                  && !offShu.Contains("spatial_truncation:", StringComparison.Ordinal)
                  && !offShu.Contains(" dir=", StringComparison.Ordinal)
                  && !offShu.Contains("[frontier]", StringComparison.Ordinal)
                  && !offShu.Contains("(goal:", StringComparison.Ordinal),
                "feature-off (RequireVisibleMarchRoutes=false or SpatialDetailLevel.Off) omits spatial sections and tags");
        }
        finally
        {
            PromptBuilder.ResetSpatialContextOptions();
        }
    }

    private static GameEngine BuildCrowdedRichOverflowWorld()
    {
        var engine = new GameEngine(909, "crowded-rich-overflow", BalanceProfile.Standard);
        engine.State.Map = SpatialStaggeredHexMap(42, 42);
        CompactScenario.CompactContact.Apply(engine);
        CityState cShu = engine.State.FindCity("city-shu"); cShu.X = 36; cShu.Z = 20;
        CityState cShu2 = engine.State.FindCity("city-shu-2"); cShu2.X = 38; cShu2.Z = 22;
        CityState cWei = engine.State.FindCity("city-wei"); cWei.X = 3; cWei.Z = 20;
        CityState cWei2 = engine.State.FindCity("city-wei-2"); cWei2.X = 4; cWei2.Z = 22;
        ArmyState aShu = engine.State.FindArmy("army-shu-1"); aShu.X = 34; aShu.Z = 20;
        ArmyState aShu2 = engine.State.FindArmy("army-shu-2"); aShu2.X = 35; aShu2.Z = 22;
        ArmyState aWei = engine.State.FindArmy("army-wei-1"); aWei.X = 6; aWei.Z = 20;
        ArmyState aWei2 = engine.State.FindArmy("army-wei-2"); aWei2.X = 5; aWei2.Z = 22;
        for (int i = 0; i < 20; i++)
        {
            string num = i.ToString("00", CultureInfo.InvariantCulture);
            engine.State.Armies.Add(new ArmyState
            {
                Id = "army-shu-crowd-" + num, Name = "Shu Crowd Army " + num, OwnerSeatId = "seat-1",
                X = 22 + (i % 5) * 3, Z = 4 + (i / 5) * 8, Troops = 1000, UnitType = UnitTypes.Cavalry,
            });
        }
        engine.BeginSeason();
        return engine;
    }

    private static void SpatialProductionConfigurableLevelsAndTruncation()
    {
        try
        {
            GameEngine stdEngine = ScenarioEngine(CompactScenario.CompactContact, BalanceProfile.Standard, 31);
            MovementView shuPlanPreview = MovementPlanner.BuildDiversified(stdEngine, "seat-1");
            int expectedTotal = shuPlanPreview.Armies[0].TotalReachable + shuPlanPreview.Armies[1].TotalReachable;
            int expectedStdDrop = Math.Max(0, expectedTotal - 24);

            PromptBuilder.ConfigureSpatialContext(new SpatialContextOptions { Level = SpatialDetailLevel.Standard });
            FakeLlmTransport stdFake = RunOneCompactSeasonAsEndTurn(stdEngine, true, 31);
            SpatialTruncationReport stdReport = PromptBuilder.LastSpatialTruncation;
            Check(stdReport != null && stdReport.Level == "standard" && stdReport.Truncated
                  && stdReport.Reason == "move_option_cap" && stdReport.MoveOptionsDropped == expectedStdDrop
                  && !stdFake.Requests[1].User.Contains("visible_cells (", StringComparison.Ordinal),
                "Standard level caps move options cleanly and records move_option_cap truncation");

            PromptBuilder.ConfigureSpatialContext(new SpatialContextOptions { Level = SpatialDetailLevel.Rich });
            GameEngine richEngine = ScenarioEngine(CompactScenario.CompactContact, BalanceProfile.Standard, 31);
            FakeLlmTransport richFake = RunOneCompactSeasonAsEndTurn(richEngine, true, 31);
            SpatialTruncationReport richReport = PromptBuilder.LastSpatialTruncation;
            string richUser = richFake.Requests[1].User;
            Check(richReport != null && richReport.Level == "rich" && !richReport.Truncated
                  && richReport.Reason == "none" && richReport.MoveOptionsDropped == 0
                  && richUser.Contains("context_config: level=rich provider_char_limit=40000", StringComparison.Ordinal)
                  && richUser.Contains("visible_cells (", StringComparison.Ordinal)
                  && richUser.Contains("  rows: ", StringComparison.Ordinal)
                  && richUser.Contains("  visible_terrain:", StringComparison.Ordinal)
                  && !richUser.Contains("[truncated:", StringComparison.Ordinal),
                "Rich level exposes full visible grid rows, terrain notes and all legal visible moves with truncated=false");

            PromptBuilder.ConfigureSpatialContext(new SpatialContextOptions
            {
                Level = SpatialDetailLevel.Rich,
                MaxSectionChars = 1400,
            });
            FakeLlmTransport boundedSecFake = RunOneCompactSeasonAsEndTurn(
                ScenarioEngine(CompactScenario.CompactContact, BalanceProfile.Standard, 31), true, 31);
            SpatialTruncationReport secReport = PromptBuilder.LastSpatialTruncation;
            Check(secReport != null && secReport.Truncated && secReport.Reason == "section_char_limit"
                  && secReport.MoveOptionsDropped > 0
                  && boundedSecFake.Requests[1].User.Contains("reason=section_char_limit", StringComparison.Ordinal)
                  && boundedSecFake.Requests[1].User.Contains("[truncated:", StringComparison.Ordinal),
                "Rich with tight MaxSectionChars truncates cleanly and records section_char_limit");

            PromptBuilder.ConfigureSpatialContext(new SpatialContextOptions
            {
                Level = SpatialDetailLevel.Rich,
                ProviderCharLimit = 10500,
            });
            GameEngine provEngine = ScenarioEngine(CompactScenario.CompactContact, BalanceProfile.Standard, 31);
            MovementView provMove = MovementPlanner.BuildDiversified(provEngine, "seat-1");
            Observation provObs = provEngine.Observe("seat-1");
            IReadOnlyList<ActionOption> provOpts = ActionCatalogue.Build(provObs);
            string provUser = PromptBuilder.UserPromptWithSpatial(provObs, provOpts, new List<string>(),
                6, 3, provMove, provMove.SpatialContext, spatialContextEnabled: true);
            SpatialTruncationReport provReport = PromptBuilder.LastSpatialTruncation;
            Check(provReport != null && provReport.Truncated && provReport.Reason == "provider_char_limit"
                  && provReport.TotalChars <= 10500 && provReport.MoveOptionsDropped > 0
                  && provUser.Contains("reason=provider_char_limit", StringComparison.Ordinal),
                $"Rich with tight ProviderCharLimit fits 10500 chars (got {provReport?.TotalChars}, dropped={provReport?.MoveOptionsDropped}) and records provider_char_limit");

            PromptBuilder.ConfigureSpatialContext(new SpatialContextOptions { Level = SpatialDetailLevel.Rich });
            FakeLlmTransport crowdFake = RunOneCompactSeasonAsEndTurn(BuildCrowdedRichOverflowWorld(), true, 909);
            SpatialTruncationReport crowdReport = PromptBuilder.LastSpatialTruncation;
            int totalChars = crowdFake.Requests[1].System.Length + crowdFake.Requests[1].User.Length;
            Check(totalChars <= PromptBuilder.MaxPromptChars && crowdReport != null && crowdReport.Truncated
                  && crowdReport.Reason == "provider_char_limit" && crowdReport.MoveOptionsDropped > 0,
                "crowded Rich prompt overflowing 40000 via LlmTurnRunner trims cleanly under MaxPromptChars");
        }
        finally
        {
            PromptBuilder.ResetSpatialContextOptions();
        }
    }

    private static void SpatialProductionWithPersonasAttackAndDeliberation()
    {
        try
        {
            PromptBuilder.ConfigureSpatialContext(new SpatialContextOptions { Level = SpatialDetailLevel.Standard });
            GameEngine personaEngine = ScenarioEngine(CompactScenario.CompactContact, BalanceProfile.Standard, 41);
            var personaFake = new FakeLlmTransport();
            personaFake.EnqueueSuccess("{\"type\":\"end_turn\"}");
            personaFake.EnqueueSuccess("{\"type\":\"end_turn\"}");
            var personaCfg = new LlmConfig { RequireVisibleMarchRoutes = true, EnablePersonas = true };
            var personaSession = new SpectatorSession(personaEngine, new LlmTurnRunner(personaFake, personaCfg, null), 1, 41);
            personaSession.Start();
            SpectatorDrain(personaSession);
            Check(personaFake.Requests[1].System.Contains("FACTION PERSONA", StringComparison.Ordinal)
                  && personaFake.Requests[1].System.Contains("Persona ID: shu-aggressive", StringComparison.Ordinal)
                  && personaFake.Requests[1].User.Contains("Prompt version: " + PromptBuilder.PersonaPromptVersion, StringComparison.Ordinal)
                  && personaFake.Requests[1].User.Contains("Persona ID: shu-aggressive", StringComparison.Ordinal)
                  && personaFake.Requests[1].User.Contains("SPATIAL CONTEXT", StringComparison.Ordinal)
                  && personaFake.Requests[1].User.Contains("16,8 cost=2 move dir=W", StringComparison.Ordinal),
                "spatial context composes cleanly with per-faction personas");

            GameEngine atkEngine = new GameEngine(42, "atk-spatial", BalanceProfile.Standard);
            atkEngine.State.Map = DemoMap();
            CompactScenario.CompactContact.Apply(atkEngine);
            atkEngine.State.FindArmy("army-wei-1").X = 17;
            atkEngine.State.FindArmy("army-wei-1").Z = 8;
            atkEngine.BeginSeason();
            var atkFake = new FakeLlmTransport();
            atkFake.EnqueueSuccess("{\"type\":\"end_turn\"}");
            atkFake.EnqueueSuccess(AtkJson("attack_army", "army-shu-1", 17, 8, "Strike adjacent Wei vanguard."));
            atkFake.EnqueueSuccess("{\"type\":\"end_turn\"}");
            var atkCfg = new LlmConfig { RequireVisibleMarchRoutes = true, EnableAttack = true };
            var atkSession = new SpectatorSession(atkEngine, new LlmTurnRunner(atkFake, atkCfg, null), 1, 42);
            atkSession.Start();
            SpectatorDrain(atkSession);
            string atkShuUser = atkFake.Requests[1].User;
            Check(atkSession.State == SpectatorState.Completed && atkSession.TotalAccepted == 1
                  && atkFake.Requests[1].System.Contains("attack_army", StringComparison.Ordinal)
                  && atkShuUser.Contains("type=attack_army target_id=army-shu-1 param_a=17 param_b=8", StringComparison.Ordinal)
                  && atkShuUser.Contains("SPATIAL CONTEXT", StringComparison.Ordinal)
                  && atkShuUser.Contains("[visible] army id=army-wei-1 name=\"Wei Vanguard\" owner=seat-0 allegiance=foreign pos=17,8", StringComparison.Ordinal)
                  && atkShuUser.Contains("from_own=army-shu-1:1@W", StringComparison.Ordinal),
                "spatial context composes with EnableAttack, shows adjacent enemy bearings and executes attack_army");

            PromptBuilder.ConfigureSpatialContext(new SpatialContextOptions { Level = SpatialDetailLevel.Rich });
            GameEngine delibEngine = ScenarioEngine(CompactScenario.CompactContact, BalanceProfile.Standard, 43);
            var noteStore = new MemoryLlmStrategicNoteStore();
            var delibFake = new FakeLlmTransport();
            delibFake.EnqueueSuccess("{\"type\":\"end_turn\"}");
            delibFake.EnqueueSuccess("{\"tool\":\"inspect_entity\",\"kind\":\"city\",\"id\":\"city-shu\"}");
            delibFake.EnqueueSuccess("{\"note\":\"March Shu Vanguard west toward Wancheng (13,8)\"}");
            delibFake.EnqueueSuccess("{\"type\":\"march\",\"target_id\":\"army-shu-1\",\"param_a\":16,\"param_b\":8}");
            delibFake.EnqueueSuccess("{\"type\":\"end_turn\"}");
            var delibCfg = new LlmConfig { RequireVisibleMarchRoutes = true, EnableDeliberation = true };
            var delibRunner = new LlmTurnRunner(delibFake, delibCfg, null, null, noteStore);
            var delibSession = new SpectatorSession(delibEngine, delibRunner, 1, 43);
            delibSession.Start();
            SpectatorDrain(delibSession);
            Check(delibSession.State == SpectatorState.Completed && delibSession.TotalAccepted == 1
                  && delibEngine.State.FindArmy("army-shu-1").X == 16 && delibEngine.State.FindArmy("army-shu-1").Z == 8
                  && delibFake.Requests.Count == 5
                  && delibFake.Requests[1].System.Contains("OPTIONAL DELIBERATION", StringComparison.Ordinal)
                  && delibFake.Requests[1].User.Contains("Prompt version: " + PromptBuilder.DeliberationPromptVersion, StringComparison.Ordinal)
                  && delibFake.Requests[1].User.Contains("visible_cells (", StringComparison.Ordinal)
                  && delibFake.Requests[2].User.Contains("PREVIOUS INQUIRY RESULT", StringComparison.Ordinal)
                  && delibFake.Requests[2].User.Contains("SPATIAL CONTEXT", StringComparison.Ordinal)
                  && delibFake.Requests[3].User.Contains("STRATEGIC NOTE FROM YOUR PREVIOUS TURN", StringComparison.Ordinal),
                "spatial context composes cleanly with EnableDeliberation, inquiry, strategic note and westward march");
        }
        finally
        {
            PromptBuilder.ResetSpatialContextOptions();
        }
    }
}
