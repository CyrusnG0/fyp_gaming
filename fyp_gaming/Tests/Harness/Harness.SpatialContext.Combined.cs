using System;
using LuanShi.Engine;

internal static partial class Harness
{
    private static void SpatialProductionCombinedRichDeliberationAndResearch()
    {
        // 1. Default-off parity with RichDeliberation + EnableResearch
        LlmConfig defCfg = LlmConfig.RichDeliberation();
        defCfg.RequireVisibleMarchRoutes = true;
        defCfg.EnableResearch = true;
        Check(defCfg.SpatialContext != null && !defCfg.SpatialContext.IsEnabled,
            "combined: RichDeliberation config leaves SpatialContext Off by default");

        LlmConfig expOffCfg = LlmConfig.RichDeliberation();
        expOffCfg.RequireVisibleMarchRoutes = true;
        expOffCfg.EnableResearch = true;
        expOffCfg.SpatialContext = new SpatialContextOptions { Level = SpatialDetailLevel.Off };

        var defFake = new FakeLlmTransport();
        defFake.EnqueueSuccess("{\"type\":\"end_turn\"}");
        defFake.EnqueueSuccess("{\"type\":\"end_turn\"}");
        var defRunner = new LlmTurnRunner(defFake, defCfg, null, null, new MemoryLlmStrategicNoteStore());
        var defSession = new SpectatorSession(
            ScenarioEngine(CompactScenario.CompactContact, BalanceProfile.Standard, 61), defRunner, 1, 61);
        defSession.Start();
        SpectatorDrain(defSession);

        var offFake = new FakeLlmTransport();
        offFake.EnqueueSuccess("{\"type\":\"end_turn\"}");
        offFake.EnqueueSuccess("{\"type\":\"end_turn\"}");
        var offRunner = new LlmTurnRunner(offFake, expOffCfg, null, null, new MemoryLlmStrategicNoteStore());
        var offSession = new SpectatorSession(
            ScenarioEngine(CompactScenario.CompactContact, BalanceProfile.Standard, 61), offRunner, 1, 61);
        offSession.Start();
        SpectatorDrain(offSession);

        Check(string.Equals(defFake.Requests[1].System, offFake.Requests[1].System, StringComparison.Ordinal)
              && string.Equals(defFake.Requests[1].User, offFake.Requests[1].User, StringComparison.Ordinal)
              && defRunner.LastSpatialTruncation == null
              && defFake.Requests[1].User.Contains("Prompt version: " + PromptBuilder.CombinedVersionFor(
                  true, false, null, false, true, true, true, false), StringComparison.Ordinal),
            "combined off: RichDeliberation + Research prompt, version and null spatial truncation stay byte-identical");

        // 2. Combined RichDeliberation + Research + Spatial (Rich): identity, prompt, actions & truncation
        LlmConfig combCfg = LlmConfig.RichDeliberation();
        combCfg.RequireVisibleMarchRoutes = true;
        combCfg.EnableResearch = true;
        combCfg.SpatialContext = new SpatialContextOptions { Level = SpatialDetailLevel.Rich };

        string expectedVer = PromptBuilder.CombinedVersionFor(true, false, null, false, true, true, true, true);
        string expectedHash = PromptBuilder.CombinedSchemaHashFor(true, false, null, false, true, true, true, true);
        string baseHash = PromptBuilder.CombinedSchemaHashFor(true, false, null, false, true, true, true, false);
        Check(expectedVer == "demo-slice-8-rich-research-1+spatial"
              && !string.Equals(expectedHash, baseHash, StringComparison.Ordinal),
            "combined identity: version and schema hash distinguish spatial+rich+research");

        // 3. Fog-of-war & hidden-world equivalence through the NEXT prompt after a march + research + inquiry
        FakeLlmTransport twinA = RunCombinedMarchSequence(BuildCompactTwinWorld(false), combCfg, out LlmTurnRunner runA);
        FakeLlmTransport twinB = RunCombinedMarchSequence(BuildCompactTwinWorld(true), combCfg, out LlmTurnRunner runB);

        Check(twinA.Requests.Count == 6 && twinB.Requests.Count == 6
              && string.Equals(twinA.Requests[1].System, twinB.Requests[1].System, StringComparison.Ordinal)
              && string.Equals(twinA.Requests[1].User, twinB.Requests[1].User, StringComparison.Ordinal)
              && string.Equals(twinA.Requests[4].System, twinB.Requests[4].System, StringComparison.Ordinal)
              && string.Equals(twinA.Requests[4].User, twinB.Requests[4].User, StringComparison.Ordinal)
              && string.Equals(twinA.Requests[5].User, twinB.Requests[5].User, StringComparison.Ordinal)
              && string.Equals(runA.LastContextReportJson, runB.LastContextReportJson, StringComparison.Ordinal)
              && runA.LastSpatialTruncation != null && !runA.LastSpatialTruncation.Truncated
              && runA.LastSpatialTruncation.MoveOptionsDropped == 0
              && twinA.Requests[1].System.Contains("RICH DELIBERATION", StringComparison.Ordinal)
              && twinA.Requests[1].System.Contains("SPATIAL CONTEXT AND SIGHT BOUNDARIES", StringComparison.Ordinal)
              && twinA.Requests[4].User.Contains("Prompt version: " + expectedVer, StringComparison.Ordinal)
              && twinA.Requests[4].User.Contains("pos=17,8", StringComparison.Ordinal)
              && twinA.Requests[5].User.Contains("PREVIOUS INQUIRY RESULT", StringComparison.Ordinal)
              && runA.LastContextReportJson.Contains("\"spatial_truncation\":", StringComparison.Ordinal),
            "combined twin worlds: RichDeliberation + Research + Spatial preserves byte-identical next prompts after march & search_history");
        AssertNoHiddenCompactLeaks(twinA.Requests[4].User, "combined post-march");
        AssertNoHiddenCompactLeaks(twinA.Requests[5].User, "combined post-search_history");

        // 4. Combined tight ProviderCharLimit accurately trims and reports both LastSpatialTruncation and LastContextReportJson
        LlmConfig tightCfg = LlmConfig.RichDeliberation();
        tightCfg.RequireVisibleMarchRoutes = true;
        tightCfg.EnableResearch = true;
        tightCfg.SpatialContext = new SpatialContextOptions
        {
            Level = SpatialDetailLevel.Rich,
            ProviderCharLimit = 14000,
        };
        var tightFake = new FakeLlmTransport();
        tightFake.EnqueueSuccess("{\"type\":\"end_turn\"}");
        tightFake.EnqueueSuccess("{\"type\":\"end_turn\"}");
        var tightRunner = new LlmTurnRunner(tightFake, tightCfg, null, null, new MemoryLlmStrategicNoteStore());
        var tightSession = new SpectatorSession(
            ScenarioEngine(CompactScenario.CompactContact, BalanceProfile.Standard, 63), tightRunner, 1, 63);
        tightSession.Start();
        SpectatorDrain(tightSession);

        int tightTotal = tightFake.Requests[1].System.Length + tightFake.Requests[1].User.Length;
        Check(tightSession.State == SpectatorState.Completed
              && tightTotal <= 14000
              && tightRunner.LastSpatialTruncation != null
              && tightRunner.LastSpatialTruncation.Truncated
              && tightRunner.LastSpatialTruncation.ProviderCharLimit == 14000
              && tightRunner.LastSpatialTruncation.TotalChars == tightTotal
              && tightRunner.LastSpatialTruncation.MoveOptionsDropped > 0
              && tightRunner.LastContextReportJson != null
              && tightRunner.LastContextReportJson.Contains("\"spatial_truncation\":", StringComparison.Ordinal)
              && tightRunner.LastContextReportJson.Contains("\"truncated\":true", StringComparison.Ordinal),
            $"combined tight budget (got {tightTotal} <= 14000, dropped={tightRunner.LastSpatialTruncation?.MoveOptionsDropped}) records accurate truncation");
    }

    private static FakeLlmTransport RunCombinedMarchSequence(GameEngine engine, LlmConfig cfg, out LlmTurnRunner runner)
    {
        var fake = new FakeLlmTransport();
        fake.EnqueueSuccess("{\"type\":\"end_turn\"}");
        fake.EnqueueSuccess("{\"type\":\"research\",\"args\":{\"line\":\"agri\"}}");
        fake.EnqueueSuccess("{\"note\":\"Advance Shu Vanguard to (17,8) after agri research\"}");
        fake.EnqueueSuccess("{\"type\":\"march\",\"target_id\":\"army-shu-1\",\"param_a\":17,\"param_b\":8}");
        fake.EnqueueSuccess("{\"tool\":\"search_history\",\"query\":\"march\",\"limit\":10,\"offset\":0}");
        fake.EnqueueSuccess("{\"type\":\"end_turn\"}");
        runner = new LlmTurnRunner(fake, cfg, null, null, new MemoryLlmStrategicNoteStore());
        var session = new SpectatorSession(engine, runner, 1, 62);
        session.Start();
        SpectatorDrain(session);
        Check(session.State == SpectatorState.Completed && session.TotalAccepted == 2
              && engine.State.FindArmy("army-shu-1").X == 17 && engine.State.FindArmy("army-shu-1").Z == 8,
            "combined sequence executed research + note + march to (17,8) + search_history");
        return fake;
    }
}
