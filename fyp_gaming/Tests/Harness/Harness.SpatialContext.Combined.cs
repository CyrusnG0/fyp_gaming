using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LuanShi.Engine;

internal static partial class Harness
{
    private sealed class CombinedFakeTransport : ILlmTransport
    {
        public string Provider => "fake";
        public string Model => "fake";
        public readonly List<LlmRequest> Requests = new List<LlmRequest>();
        private readonly Queue<string> _replies = new Queue<string>();

        public void EnqueueOk(string json) => _replies.Enqueue(json);

        public Task<LlmResponse> SendAsync(LlmRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            string json = _replies.Count > 0 ? _replies.Dequeue() : "{\"type\":\"end_turn\"}";
            return Task.FromResult(LlmResponse.Success(json, 1, 100, 20, "req-combined", null));
        }
    }

    private static LlmConfig MakeCombinedConfig(SpatialContextOptions spatial = null)
    {
        LlmConfig cfg = LlmConfig.RichDeliberation();
        cfg.RequireVisibleMarchRoutes = true;
        cfg.EnableResearch = true;
        if (spatial != null) cfg.SpatialContext = spatial;
        return cfg;
    }

    private static void SpatialProductionCombinedRichDeliberationAndResearch()
    {
        // 1. Default-off parity with RichDeliberation + EnableResearch
        LlmConfig defCfg = MakeCombinedConfig();
        Check(defCfg.SpatialContext != null && !defCfg.SpatialContext.IsEnabled,
            "combined: RichDeliberation config leaves SpatialContext Off by default");

        LlmConfig expOffCfg = MakeCombinedConfig(new SpatialContextOptions { Level = SpatialDetailLevel.Off });

        var defFake = new CombinedFakeTransport();
        defFake.EnqueueOk("{\"type\":\"end_turn\"}");
        defFake.EnqueueOk("{\"type\":\"end_turn\"}");
        var defRunner = new LlmTurnRunner(defFake, defCfg, null, null, new MemoryLlmStrategicNoteStore());
        var defSession = new SpectatorSession(
            ScenarioEngine(CompactScenario.CompactContact, BalanceProfile.Standard, 61), defRunner, 1, 61);
        defSession.Start();
        SpectatorDrain(defSession);

        var offFake = new CombinedFakeTransport();
        offFake.EnqueueOk("{\"type\":\"end_turn\"}");
        offFake.EnqueueOk("{\"type\":\"end_turn\"}");
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
        LlmConfig combCfg = MakeCombinedConfig(new SpatialContextOptions { Level = SpatialDetailLevel.Rich });

        string expectedVer = PromptBuilder.CombinedVersionFor(true, false, null, false, true, true, true, true);
        string expectedHash = PromptBuilder.CombinedSchemaHashFor(true, false, null, false, true, true, true, true);
        string baseHash = PromptBuilder.CombinedSchemaHashFor(true, false, null, false, true, true, true, false);
        Check(expectedVer == "demo-slice-8-rich-research-2+spatial"
              && !string.Equals(expectedHash, baseHash, StringComparison.Ordinal),
            "combined identity: version and schema hash distinguish spatial+rich+research");

        // 3. Fog-of-war & hidden-world equivalence through the NEXT prompt after a march + inquiry + research
        CombinedFakeTransport twinA = RunCombinedMarchSequence(BuildCompactTwinWorld(false), combCfg, out LlmTurnRunner runA);
        CombinedFakeTransport twinB = RunCombinedMarchSequence(BuildCompactTwinWorld(true), combCfg, out LlmTurnRunner runB);

        Check(twinA.Requests.Count == 8 && twinB.Requests.Count == 8
              && string.Equals(twinA.Requests[1].System, twinB.Requests[1].System, StringComparison.Ordinal)
              && string.Equals(twinA.Requests[1].User, twinB.Requests[1].User, StringComparison.Ordinal)
              && string.Equals(twinA.Requests[3].System, twinB.Requests[3].System, StringComparison.Ordinal)
              && string.Equals(twinA.Requests[3].User, twinB.Requests[3].User, StringComparison.Ordinal)
              && string.Equals(twinA.Requests[4].User, twinB.Requests[4].User, StringComparison.Ordinal)
              && string.Equals(twinA.Requests[6].User, twinB.Requests[6].User, StringComparison.Ordinal)
              && string.Equals(twinA.Requests[7].User, twinB.Requests[7].User, StringComparison.Ordinal)
              && string.Equals(runA.LastContextReportJson, runB.LastContextReportJson, StringComparison.Ordinal)
              && runA.LastSpatialTruncation != null && !runA.LastSpatialTruncation.Truncated
              && runA.LastSpatialTruncation.MoveOptionsDropped == 0
              && twinA.Requests[1].System.Contains("RICH DELIBERATION", StringComparison.Ordinal)
              && twinA.Requests[1].System.Contains("SPATIAL CONTEXT AND SIGHT BOUNDARIES", StringComparison.Ordinal)
              && twinA.Requests[3].User.Contains("Prompt version: " + expectedVer, StringComparison.Ordinal)
              && twinA.Requests[3].User.Contains("pos=17,8", StringComparison.Ordinal)
              && twinA.Requests[4].User.Contains("PREVIOUS INQUIRY RESULT", StringComparison.Ordinal)
              && runA.LastContextReportJson.Contains("\"spatial_truncation\":", StringComparison.Ordinal),
            "combined twin worlds: RichDeliberation + Research + Spatial preserves byte-identical next prompts after march, search_history & research");
        AssertNoHiddenCompactLeaks(twinA.Requests[3].User, "combined post-march");
        AssertNoHiddenCompactLeaks(twinA.Requests[4].User, "combined post-search_history");

        // 4. Combined tight ProviderCharLimit accurately trims and reports both LastSpatialTruncation and LastContextReportJson
        int tightLimit = 22300;
        LlmConfig tightCfg = MakeCombinedConfig(new SpatialContextOptions
        {
            Level = SpatialDetailLevel.Rich,
            ProviderCharLimit = tightLimit,
        });
        var tightFake = new CombinedFakeTransport();
        tightFake.EnqueueOk("{\"type\":\"end_turn\"}");
        tightFake.EnqueueOk("{\"type\":\"end_turn\"}");
        var tightRunner = new LlmTurnRunner(tightFake, tightCfg, null, null, new MemoryLlmStrategicNoteStore());
        var tightSession = new SpectatorSession(
            ScenarioEngine(CompactScenario.CompactContact, BalanceProfile.Standard, 63), tightRunner, 1, 63);
        tightSession.Start();
        SpectatorDrain(tightSession);

        int tightTotal = tightFake.Requests.Count > 1
            ? tightFake.Requests[1].System.Length + tightFake.Requests[1].User.Length
            : -1;
        Check(tightSession.State == SpectatorState.Completed
              && tightFake.Requests.Count == 2
              && tightTotal > 0 && tightTotal <= tightLimit
              && tightRunner.LastSpatialTruncation != null
              && tightRunner.LastSpatialTruncation.Truncated
              && tightRunner.LastSpatialTruncation.ProviderCharLimit == tightLimit
              && tightRunner.LastSpatialTruncation.TotalChars == tightTotal
              && (tightRunner.LastSpatialTruncation.MoveOptionsDropped > 0
                  || tightRunner.LastSpatialTruncation.VisibleCellRangesDropped > 0)
              && tightRunner.LastContextReportJson != null
              && tightRunner.LastContextReportJson.Contains("\"spatial_truncation\":", StringComparison.Ordinal)
              && tightRunner.LastContextReportJson.Contains("\"truncated\":true", StringComparison.Ordinal),
            $"combined tight budget (got {tightTotal} <= {tightLimit}, rowsDropped={tightRunner.LastSpatialTruncation?.VisibleCellRangesDropped}, moveDropped={tightRunner.LastSpatialTruncation?.MoveOptionsDropped}) records accurate truncation");
    }

    private static CombinedFakeTransport RunCombinedMarchSequence(GameEngine engine, LlmConfig cfg, out LlmTurnRunner runner)
    {
        var fake = new CombinedFakeTransport();
        // Season 1: Wei ends turn, Shu saves note + marches to (17,8) + searches history + ends turn
        fake.EnqueueOk("{\"type\":\"end_turn\"}");
        fake.EnqueueOk("{\"note\":\"Advance Shu Vanguard to (17,8)\"}");
        fake.EnqueueOk("{\"type\":\"march\",\"target_id\":\"army-shu-1\",\"param_a\":17,\"param_b\":8}");
        fake.EnqueueOk("{\"tool\":\"search_history\",\"query\":\"march\",\"limit\":10,\"offset\":0}");
        fake.EnqueueOk("{\"type\":\"end_turn\"}");
        // Season 2: Wei ends turn, Shu has 30 research points and buys agri L1 (cost 20)
        fake.EnqueueOk("{\"type\":\"end_turn\"}");
        fake.EnqueueOk("{\"type\":\"research\",\"args\":{\"line\":\"agri\"}}");
        fake.EnqueueOk("{\"type\":\"end_turn\"}");
        runner = new LlmTurnRunner(fake, cfg, null, null, new MemoryLlmStrategicNoteStore());
        var session = new SpectatorSession(engine, runner, 2, 62);
        session.Start();
        SpectatorDrain(session);
        Check(session.State == SpectatorState.Completed && session.TotalAccepted == 2
              && engine.State.FindArmy("army-shu-1").X == 17 && engine.State.FindArmy("army-shu-1").Z == 8,
            "combined sequence executed note + march to (17,8) + search_history + research");
        return fake;
    }
}
