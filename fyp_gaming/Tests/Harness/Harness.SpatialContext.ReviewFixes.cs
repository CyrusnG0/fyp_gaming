using System;
using System.Collections.Generic;
using System.IO;
using LuanShi.Engine;

internal static partial class Harness
{
    private sealed class MemoryRecorderSink : ILlmRunRecorder, ILlmExchangeRecorder
    {
        public readonly List<string> RunLines = new List<string>();
        public readonly List<string> ExchangeLines = new List<string>();
        public bool CaptureExchanges => true;
        public string DirectoryPath => null;
        public void Record(string runId, string jsonLine) => RunLines.Add(jsonLine);
        public void RecordExchange(string runId, string jsonLine) => ExchangeLines.Add(jsonLine);
    }

    private static void SpatialReviewFixes()
    {
        SpatialSingleLlmHostReachesDispatchedPrompt();
        SpatialSpectatorSummaryMatchesDispatchedVersionAndHash();
        SpatialTruncationPersistedInRichAndNonRichModes();
        SpatialEnabledWithRequireVisibleFalseOnSingleLlmHost();
        SpatialEnabledWithRequireVisibleFalseOnSpectatorHost();
        SpatialHiddenShortcutEnforcesVisibleRouteAcrossBothHosts();
    }

    private static void SpatialSingleLlmHostReachesDispatchedPrompt()
    {
        GameEngine engine = ScenarioEngine(CompactScenario.CompactContact, BalanceProfile.Standard, 70);
        engine.BeginSeason();
        string shuSeat = engine.State.Factions[1].SeatId;
        int token = engine.BeginSeatTurn(shuSeat);
        var cfg = new LlmConfig
        {
            Provider = "fake",
            Model = "fake-single",
            RequireVisibleMarchRoutes = true,
            SpatialContext = new SpatialContextOptions { Level = SpatialDetailLevel.Rich },
            MaxRequestsPerTurn = 4,
            MaxActionsPerTurn = 2,
        };
        bool endTurnCalled = false;
        var host = new SingleLlmTurnHost(
            engine,
            cfg,
            shuSeat,
            () => engine.Observe(shuSeat),
            cmd => engine.SubmitAs(shuSeat, token, cmd),
            () => endTurnCalled = true);

        var fake = new CombinedFakeTransport();
        fake.EnqueueOk("{\"type\":\"march\",\"target_id\":\"army-shu-1\",\"param_a\":17,\"param_b\":8}");
        fake.EnqueueOk("{\"type\":\"end_turn\"}");

        var recorder = new MemoryRecorderSink();
        var runner = new LlmTurnRunner(fake, cfg, recorder);
        runner.Begin(host, token, shuSeat, 70);
        LlmDrain(runner);
        engine.EndSeatTurn(token);

        Check(host is ISpatialPlanningHost
              && runner.State == RunnerState.Finished
              && endTurnCalled
              && fake.Requests.Count == 2
              && fake.Requests[0].System.Contains("SPATIAL CONTEXT AND SIGHT BOUNDARIES", StringComparison.Ordinal)
              && fake.Requests[0].User.Contains("SPATIAL CONTEXT (seat-filtered map", StringComparison.Ordinal)
              && fake.Requests[0].User.Contains("MOVEMENT OPTIONS", StringComparison.Ordinal)
              && fake.Requests[0].User.Contains("dir=", StringComparison.Ordinal)
              && fake.Requests[0].User.Contains("dist=", StringComparison.Ordinal)
              && fake.Requests[1].User.Contains("pos=17,8", StringComparison.Ordinal),
            "single-LLM host (SingleLlmTurnHost) wires ISpatialPlanningHost into dispatched prompts");

        // Also confirm feature-off on the same host omits spatial context
        GameEngine offEngine = ScenarioEngine(CompactScenario.CompactContact, BalanceProfile.Standard, 71);
        offEngine.BeginSeason();
        string offShuSeat = offEngine.State.Factions[1].SeatId;
        int offToken = offEngine.BeginSeatTurn(offShuSeat);
        var offCfg = new LlmConfig
        {
            Provider = "fake",
            Model = "fake-single",
            RequireVisibleMarchRoutes = true,
            SpatialContext = new SpatialContextOptions { Level = SpatialDetailLevel.Off },
            MaxRequestsPerTurn = 2,
            MaxActionsPerTurn = 1,
        };
        var offHost = new SingleLlmTurnHost(
            offEngine,
            offCfg,
            offShuSeat,
            () => offEngine.Observe(offShuSeat),
            cmd => offEngine.SubmitAs(offShuSeat, offToken, cmd),
            () => { });
        var offFake = new CombinedFakeTransport();
        offFake.EnqueueOk("{\"type\":\"end_turn\"}");
        var offRunner = new LlmTurnRunner(offFake, offCfg, null);
        offRunner.Begin(offHost, offToken, offShuSeat, 71);
        LlmDrain(offRunner);
        offEngine.EndSeatTurn(offToken);

        Check(offRunner.State == RunnerState.Finished
              && offFake.Requests.Count == 1
              && !offFake.Requests[0].System.Contains("SPATIAL CONTEXT AND SIGHT BOUNDARIES", StringComparison.Ordinal)
              && !offFake.Requests[0].User.Contains("SPATIAL CONTEXT (seat-filtered map", StringComparison.Ordinal),
            "single-LLM host with spatial Off preserves non-spatial prompt");
    }

    private static void SpatialSpectatorSummaryMatchesDispatchedVersionAndHash()
    {
        bool[] bools = { false, true };
        string tempDir = Path.Combine(Path.GetTempPath(), "luanshi-spatial-sum-" + Guid.NewGuid().ToString("N"));
        try
        {
            foreach (bool spatial in bools)
            {
                foreach (bool research in bools)
                {
                    foreach (bool rich in bools)
                    {
                        LlmConfig cfg = rich ? LlmConfig.RichDeliberation() : new LlmConfig();
                        cfg.Provider = "fake";
                        cfg.Model = "fake-sum";
                        cfg.RequireVisibleMarchRoutes = true;
                        cfg.EnableResearch = research;
                        cfg.SpatialContext = new SpatialContextOptions
                        {
                            Level = spatial ? SpatialDetailLevel.Rich : SpatialDetailLevel.Off,
                        };
                        if (!rich)
                        {
                            cfg.MaxRequestsPerTurn = 4;
                            cfg.MaxActionsPerTurn = 2;
                        }
                        var fake = new CombinedFakeTransport();
                        fake.EnqueueOk("{\"type\":\"end_turn\"}");
                        fake.EnqueueOk("{\"type\":\"end_turn\"}");
                        var recorder = new MemoryRecorderSink();
                        var runner = new LlmTurnRunner(fake, cfg, recorder, null,
                            rich ? new MemoryLlmStrategicNoteStore() : null);
                        var session = new SpectatorSession(
                            ScenarioEngine(CompactScenario.CompactContact, BalanceProfile.Standard, 72),
                            runner, 1, 72);
                        session.Start();
                        SpectatorDrain(session);

                        string summaryPath = SpectatorSummary.Write(tempDir, session, cfg, fake, runner);
                        string summaryJson = File.ReadAllText(summaryPath);

                        string reqVer = ExtractJsonStringField(recorder.ExchangeLines[0], "prompt_version");
                        string reqHash = ExtractJsonStringField(recorder.ExchangeLines[0], "schema_hash");
                        string sumVer = ExtractJsonStringField(summaryJson, "prompt_version");
                        string sumHash = ExtractJsonStringField(summaryJson, "schema_hash");

                        Check(session.State == SpectatorState.Completed
                              && !string.IsNullOrEmpty(reqVer) && reqVer == sumVer
                              && !string.IsNullOrEmpty(reqHash) && reqHash == sumHash
                              && (spatial ? sumVer.Contains("spatial", StringComparison.Ordinal) : !sumVer.Contains("spatial", StringComparison.Ordinal)),
                            $"spectator summary version/hash match dispatched request (spatial={spatial}, research={research}, rich={rich}, ver={sumVer})");
                    }
                }
            }
        }
        finally
        {
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
        }
    }

    private static void SpatialTruncationPersistedInRichAndNonRichModes()
    {
        // 1. Rich mode with tight limit -> persisted in run record, exchange request, exchange result, and context_report
        LlmConfig richCfg = LlmConfig.RichDeliberation();
        richCfg.Provider = "fake";
        richCfg.Model = "fake-trunc-rich";
        richCfg.RequireVisibleMarchRoutes = true;
        richCfg.EnableResearch = true;
        richCfg.SpatialContext = new SpatialContextOptions
        {
            Level = SpatialDetailLevel.Rich,
            ProviderCharLimit = 22300,
        };
        var richFake = new CombinedFakeTransport();
        richFake.EnqueueOk("{\"type\":\"end_turn\"}");
        richFake.EnqueueOk("{\"type\":\"end_turn\"}");
        var richRecorder = new MemoryRecorderSink();
        var richRunner = new LlmTurnRunner(richFake, richCfg, richRecorder, null, new MemoryLlmStrategicNoteStore());
        var richSession = new SpectatorSession(
            ScenarioEngine(CompactScenario.CompactContact, BalanceProfile.Standard, 73),
            richRunner, 1, 73);
        richSession.Start();
        SpectatorDrain(richSession);

        Check(richRecorder.RunLines.Exists(l => l.Contains("\"spatial_truncation\":{", StringComparison.Ordinal)
                                              && l.Contains("\"truncated\":true", StringComparison.Ordinal))
              && richRecorder.ExchangeLines.Exists(l => l.Contains("\"exchange_event\":\"request\"", StringComparison.Ordinal)
                                                        && l.Contains("\"spatial_truncation\":{", StringComparison.Ordinal)
                                                        && l.Contains("\"truncated\":true", StringComparison.Ordinal))
              && richRecorder.ExchangeLines.Exists(l => l.Contains("\"exchange_event\":\"result\"", StringComparison.Ordinal)
                                                        && l.Contains("\"spatial_truncation\":{", StringComparison.Ordinal)
                                                        && l.Contains("\"truncated\":true", StringComparison.Ordinal)),
            "rich mode persists structured spatial_truncation in run record and exchange request/result");

        // 2. Non-rich mode with spatial enabled -> persisted in run record, exchange request, and exchange result
        var nonRichCfg = new LlmConfig
        {
            Provider = "fake",
            Model = "fake-trunc-std",
            RequireVisibleMarchRoutes = true,
            EnableDeliberation = false,
            MaxPromptChars = 25000,
            SpatialContext = new SpatialContextOptions
            {
                Level = SpatialDetailLevel.Rich,
                ProviderCharLimit = 12000,
            },
            MaxRequestsPerTurn = 4,
            MaxActionsPerTurn = 2,
        };
        var nonRichFake = new CombinedFakeTransport();
        nonRichFake.EnqueueOk("{\"type\":\"end_turn\"}");
        nonRichFake.EnqueueOk("{\"type\":\"end_turn\"}");
        var nonRichRecorder = new MemoryRecorderSink();
        var nonRichRunner = new LlmTurnRunner(nonRichFake, nonRichCfg, nonRichRecorder);
        var nonRichSession = new SpectatorSession(
            ScenarioEngine(CompactScenario.CompactContact, BalanceProfile.Standard, 74),
            nonRichRunner, 1, 74);
        nonRichSession.Start();
        SpectatorDrain(nonRichSession);

        Check(nonRichRecorder.RunLines.Exists(l => l.Contains("\"spatial_truncation\":{", StringComparison.Ordinal)
                                                 && l.Contains("\"provider_char_limit\":12000", StringComparison.Ordinal))
              && nonRichRecorder.ExchangeLines.Exists(l => l.Contains("\"exchange_event\":\"request\"", StringComparison.Ordinal)
                                                           && l.Contains("\"spatial_truncation\":{", StringComparison.Ordinal)
                                                           && l.Contains("\"provider_char_limit\":12000", StringComparison.Ordinal))
              && nonRichRecorder.ExchangeLines.Exists(l => l.Contains("\"exchange_event\":\"result\"", StringComparison.Ordinal)
                                                           && l.Contains("\"spatial_truncation\":{", StringComparison.Ordinal)
                                                           && l.Contains("\"provider_char_limit\":12000", StringComparison.Ordinal)),
            "non-rich mode persists structured spatial_truncation in run record and exchange request/result");

        // 3. Spatial Off -> spatial_truncation omitted
        var offCfg = new LlmConfig
        {
            Provider = "fake",
            Model = "fake-trunc-off",
            RequireVisibleMarchRoutes = true,
            SpatialContext = new SpatialContextOptions { Level = SpatialDetailLevel.Off },
            MaxRequestsPerTurn = 4,
            MaxActionsPerTurn = 2,
        };
        var offFake = new CombinedFakeTransport();
        offFake.EnqueueOk("{\"type\":\"end_turn\"}");
        offFake.EnqueueOk("{\"type\":\"end_turn\"}");
        var offRecorder = new MemoryRecorderSink();
        var offRunner = new LlmTurnRunner(offFake, offCfg, offRecorder);
        var offSession = new SpectatorSession(
            ScenarioEngine(CompactScenario.CompactContact, BalanceProfile.Standard, 75),
            offRunner, 1, 75);
        offSession.Start();
        SpectatorDrain(offSession);

        Check(offRecorder.RunLines.Count >= 1
              && !offRecorder.RunLines[0].Contains("\"spatial_truncation\":", StringComparison.Ordinal)
              && !offRecorder.ExchangeLines[0].Contains("\"spatial_truncation\":", StringComparison.Ordinal)
              && !offRecorder.ExchangeLines[1].Contains("\"spatial_truncation\":", StringComparison.Ordinal),
            "spatial Off omits spatial_truncation from run record and exchange capture");
    }

    private static void SpatialEnabledWithRequireVisibleFalseOnSingleLlmHost()
    {
        GameEngine engine = ScenarioEngine(CompactScenario.CompactContact, BalanceProfile.Standard, 76);
        engine.BeginSeason();
        string shuSeat = engine.State.Factions[1].SeatId;
        int token = engine.BeginSeatTurn(shuSeat);
        var cfg = new LlmConfig
        {
            Provider = "fake",
            Model = "fake-single-novis",
            RequireVisibleMarchRoutes = false,
            MaxPromptChars = 25000,
            SpatialContext = new SpatialContextOptions { Level = SpatialDetailLevel.Standard },
            MaxRequestsPerTurn = 4,
            MaxActionsPerTurn = 2,
        };
        var host = new SingleLlmTurnHost(
            engine,
            cfg,
            shuSeat,
            () => engine.Observe(shuSeat),
            cmd => engine.SubmitAs(shuSeat, token, cmd),
            () => { });
        var fake = new CombinedFakeTransport();
        fake.EnqueueOk("{\"type\":\"end_turn\"}");
        var recorder = new MemoryRecorderSink();
        var runner = new LlmTurnRunner(fake, cfg, recorder);
        runner.Begin(host, token, shuSeat, 76);
        LlmDrain(runner);
        engine.EndSeatTurn(token);

        string expectedVer = PromptBuilder.CombinedVersionFor(true, false, null, false, false, false, false, true);
        string expectedHash = PromptBuilder.CombinedSchemaHashFor(true, false, null, false, false, false, false, true);
        string reqVer = ExtractJsonStringField(recorder.ExchangeLines[0], "prompt_version");
        string reqHash = ExtractJsonStringField(recorder.ExchangeLines[0], "schema_hash");

        Check(runner.State == RunnerState.Finished
              && fake.Requests.Count == 1
              && fake.Requests[0].System.Contains("SPATIAL CONTEXT AND SIGHT BOUNDARIES", StringComparison.Ordinal)
              && fake.Requests[0].User.Contains("SPATIAL CONTEXT (seat-filtered map", StringComparison.Ordinal)
              && fake.Requests[0].User.Contains("MOVEMENT OPTIONS", StringComparison.Ordinal)
              && fake.Requests[0].User.Contains("dir=", StringComparison.Ordinal)
              && fake.Requests[0].User.Contains("dist=", StringComparison.Ordinal)
              && reqVer == expectedVer
              && reqHash == expectedHash,
            "single-LLM host with spatial enabled and require_visible_march_routes=false dispatches seat-safe spatial prompt and identity");

        // Hidden-world equivalence with require_visible_march_routes=false
        GameEngine alt = ScenarioEngine(CompactScenario.CompactContact, BalanceProfile.Standard, 76);
        ArmyState hiddenWei = alt.State.Armies.Find(a => a != null && a.OwnerSeatId != shuSeat);
        if (hiddenWei != null) hiddenWei.Troops += 500;
        alt.BeginSeason();
        int altToken = alt.BeginSeatTurn(shuSeat);
        var altHost = new SingleLlmTurnHost(
            alt,
            cfg,
            shuSeat,
            () => alt.Observe(shuSeat),
            cmd => alt.SubmitAs(shuSeat, altToken, cmd),
            () => { });
        var altFake = new CombinedFakeTransport();
        altFake.EnqueueOk("{\"type\":\"end_turn\"}");
        var altRunner = new LlmTurnRunner(altFake, cfg, null);
        altRunner.Begin(altHost, altToken, shuSeat, 76);
        LlmDrain(altRunner);
        alt.EndSeatTurn(altToken);

        Check(altRunner.State == RunnerState.Finished
              && altFake.Requests.Count == 1
              && fake.Requests[0].System == altFake.Requests[0].System
              && fake.Requests[0].User == altFake.Requests[0].User,
            "single-LLM host with spatial enabled and require_visible_march_routes=false preserves hidden-world equivalence");
    }

    private static void SpatialEnabledWithRequireVisibleFalseOnSpectatorHost()
    {
        var cfg = new LlmConfig
        {
            Provider = "fake",
            Model = "fake-spec-novis",
            RequireVisibleMarchRoutes = false,
            EnableResearch = true,
            MaxPromptChars = 25000,
            SpatialContext = new SpatialContextOptions { Level = SpatialDetailLevel.Standard },
            MaxRequestsPerTurn = 4,
            MaxActionsPerTurn = 2,
        };
        var fake = new CombinedFakeTransport();
        fake.EnqueueOk("{\"type\":\"end_turn\"}");
        fake.EnqueueOk("{\"type\":\"end_turn\"}");
        var recorder = new MemoryRecorderSink();
        var runner = new LlmTurnRunner(fake, cfg, recorder);
        var session = new SpectatorSession(
            ScenarioEngine(CompactScenario.CompactContact, BalanceProfile.Standard, 77),
            runner, 1, 77);
        session.Start();
        SpectatorDrain(session);

        string tempDir = Path.Combine(Path.GetTempPath(), "luanshi-spatial-novis-" + Guid.NewGuid().ToString("N"));
        try
        {
            string summaryPath = SpectatorSummary.Write(tempDir, session, cfg, fake, runner);
            string summaryJson = File.ReadAllText(summaryPath);

            string expectedVer = PromptBuilder.CombinedVersionFor(true, false, null, false, false, false, true, true);
            string expectedHash = PromptBuilder.CombinedSchemaHashFor(true, false, null, false, false, false, true, true);
            string reqVer = ExtractJsonStringField(recorder.ExchangeLines[0], "prompt_version");
            string reqHash = ExtractJsonStringField(recorder.ExchangeLines[0], "schema_hash");
            string sumVer = ExtractJsonStringField(summaryJson, "prompt_version");
            string sumHash = ExtractJsonStringField(summaryJson, "schema_hash");

            Check(session.State == SpectatorState.Completed
                  && fake.Requests.Count == 2
                  && fake.Requests[0].System.Contains("SPATIAL CONTEXT AND SIGHT BOUNDARIES", StringComparison.Ordinal)
                  && fake.Requests[0].User.Contains("SPATIAL CONTEXT (seat-filtered map", StringComparison.Ordinal)
                  && fake.Requests[0].User.Contains("MOVEMENT OPTIONS", StringComparison.Ordinal)
                  && reqVer == expectedVer
                  && reqHash == expectedHash
                  && sumVer == reqVer
                  && sumHash == reqHash,
                "spectator host with spatial enabled and require_visible_march_routes=false dispatches spatial prompt and matches summary version/hash");
        }
        finally
        {
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
        }
    }

    private static void SpatialHiddenShortcutEnforcesVisibleRouteAcrossBothHosts()
    {
        // 1. Single-LLM host across hidden-shortcut paired worlds
        string prevSingleReq1Sys = null;
        string prevSingleReq1User = null;
        string prevSingleReq2User = null;
        string prevSingleHistory = null;
        string prevSingleFirstResult = null;
        foreach (bool shortcutOpen in new[] { true, false })
        {
            var engine = BuildUnseenShortcutWorld(707, shortcutOpen);
            string seatId = engine.State.FindArmy(ShortcutArmyId).OwnerSeatId;
            int token = engine.BeginSeatTurn(seatId);
            var cfg = new LlmConfig
            {
                Provider = "fake",
                Model = "fake-shortcut-single",
                RequireVisibleMarchRoutes = false,
                MaxPromptChars = 25000,
                SpatialContext = new SpatialContextOptions { Level = SpatialDetailLevel.Standard },
                MaxRequestsPerTurn = 6,
                MaxActionsPerTurn = 3,
            };
            var host = new SingleLlmTurnHost(
                engine,
                cfg,
                seatId,
                () => engine.Observe(seatId),
                cmd => engine.SubmitAs(seatId, token, cmd),
                () => { });
            var fake = new CombinedFakeTransport();
            fake.EnqueueOk("{\"type\":\"march\",\"target_id\":\"" + ShortcutArmyId + "\",\"param_a\":3,\"param_b\":5}");
            fake.EnqueueOk("{\"type\":\"march\",\"target_id\":\"" + ShortcutArmyId + "\",\"param_a\":4,\"param_b\":5}");
            fake.EnqueueOk("{\"type\":\"end_turn\"}");
            var recorder = new MemoryRecorderSink();
            var runner = new LlmTurnRunner(fake, cfg, recorder);
            runner.Begin(host, token, seatId, 707);
            LlmDrain(runner);
            engine.EndSeatTurn(token);

            var army = engine.State.FindArmy(ShortcutArmyId);
            string expectedVer = PromptBuilder.CombinedVersionFor(true, false, null, false, false, false, false, true);
            string expectedHash = PromptBuilder.CombinedSchemaHashFor(true, false, null, false, false, false, false, true);
            string reqVer = ExtractJsonStringField(recorder.ExchangeLines[0], "prompt_version");
            string reqHash = ExtractJsonStringField(recorder.ExchangeLines[0], "schema_hash");
            string historyJoined = string.Join("\n", runner.RecentHistory);
            string firstResult = recorder.ExchangeLines.Count > 1
                ? ExtractJsonStringField(recorder.ExchangeLines[1], "result_error")
                : null;

            Check(cfg.EffectiveVisibleMarchRoutes
                  && cfg.Redacted().Contains("visible_march_routes=enforced_by_spatial", StringComparison.Ordinal)
                  && runner.State == RunnerState.Finished
                  && fake.Requests.Count == 3
                  && army.X == 4 && army.Z == 5
                  && firstResult == FeedbackPolicy.DestinationNotReachable
                  && historyJoined.Contains(FeedbackPolicy.DestinationNotReachable, StringComparison.Ordinal)
                  && reqVer == expectedVer
                  && reqHash == expectedHash,
                $"single-LLM host with spatial+RequireVisibleMarchRoutes=false rejects unlisted shortcut (shortcutOpen={shortcutOpen})");

            if (prevSingleReq1User != null)
            {
                Check(prevSingleReq1Sys == fake.Requests[1].System
                      && prevSingleReq1User == fake.Requests[1].User
                      && prevSingleHistory == historyJoined
                      && prevSingleFirstResult == firstResult,
                    "single-LLM host produces identical result, subsequent prompt, and history across hidden-shortcut paired worlds");
            }
            prevSingleReq1Sys = fake.Requests[1].System;
            prevSingleReq1User = fake.Requests[1].User;
            prevSingleReq2User = fake.Requests[2].User;
            prevSingleHistory = historyJoined;
            prevSingleFirstResult = firstResult;
        }

        // 2. Spectator host across hidden-shortcut paired worlds
        string tempDir = Path.Combine(Path.GetTempPath(), "luanshi-spatial-shortcut-" + Guid.NewGuid().ToString("N"));
        try
        {
            string prevSpecReq1Sys = null;
            string prevSpecReq1User = null;
            string prevSpecReq2User = null;
            string prevSpecHistory = null;
            string prevSpecFirstResult = null;
            foreach (bool shortcutOpen in new[] { true, false })
            {
                var engine = BuildUnseenShortcutWorld(707, shortcutOpen);
                var cfg = new LlmConfig
                {
                    Provider = "fake",
                    Model = "fake-shortcut-spec",
                    RequireVisibleMarchRoutes = false,
                    MaxPromptChars = 25000,
                    SpatialContext = new SpatialContextOptions { Level = SpatialDetailLevel.Standard },
                    MaxRequestsPerTurn = 6,
                    MaxActionsPerTurn = 3,
                };
                var fake = new CombinedFakeTransport();
                fake.EnqueueOk("{\"type\":\"march\",\"target_id\":\"" + ShortcutArmyId + "\",\"param_a\":3,\"param_b\":5}");
                fake.EnqueueOk("{\"type\":\"march\",\"target_id\":\"" + ShortcutArmyId + "\",\"param_a\":4,\"param_b\":5}");
                fake.EnqueueOk("{\"type\":\"end_turn\"}");
                fake.EnqueueOk("{\"type\":\"end_turn\"}");
                var recorder = new MemoryRecorderSink();
                var runner = new LlmTurnRunner(fake, cfg, recorder);
                var session = new SpectatorSession(engine, runner, 1, 707);
                session.Start();
                SpectatorDrain(session);

                string summaryPath = SpectatorSummary.Write(tempDir, session, cfg, fake, runner);
                string summaryJson = File.ReadAllText(summaryPath);

                var army = engine.State.FindArmy(ShortcutArmyId);
                string expectedVer = PromptBuilder.CombinedVersionFor(true, false, null, false, false, false, false, true);
                string expectedHash = PromptBuilder.CombinedSchemaHashFor(true, false, null, false, false, false, false, true);
                string reqVer = ExtractJsonStringField(recorder.ExchangeLines[0], "prompt_version");
                string reqHash = ExtractJsonStringField(recorder.ExchangeLines[0], "schema_hash");
                string sumVer = ExtractJsonStringField(summaryJson, "prompt_version");
                string sumHash = ExtractJsonStringField(summaryJson, "schema_hash");
                string historyJoined = string.Join("\n", runner.RecentHistory);
                string firstResult = recorder.ExchangeLines.Count > 1
                    ? ExtractJsonStringField(recorder.ExchangeLines[1], "result_error")
                    : null;

                Check(session.State == SpectatorState.Completed
                      && session.TotalAccepted == 1
                      && session.TotalRejected == 1
                      && army.X == 4 && army.Z == 5
                      && firstResult == FeedbackPolicy.DestinationNotReachable
                      && reqVer == expectedVer
                      && reqHash == expectedHash
                      && sumVer == reqVer
                      && sumHash == reqHash
                      && summaryJson.Contains("\"visible_march_routes_enforced\":true", StringComparison.Ordinal)
                      && summaryJson.Contains("\"spatial_context_level\":\"standard\"", StringComparison.Ordinal)
                      && summaryJson.Contains("visible_march_routes=enforced_by_spatial", StringComparison.Ordinal),
                    $"spectator host with spatial+RequireVisibleMarchRoutes=false rejects unlisted shortcut and records summary evidence (shortcutOpen={shortcutOpen})");

                if (prevSpecReq1User != null)
                {
                    Check(prevSpecReq1Sys == fake.Requests[1].System
                          && prevSpecReq1User == fake.Requests[1].User
                          && prevSpecHistory == historyJoined
                          && prevSpecFirstResult == firstResult,
                        "spectator host produces identical result, subsequent prompt, and history across hidden-shortcut paired worlds");
                }
                prevSpecReq1Sys = fake.Requests[1].System;
                prevSpecReq1User = fake.Requests[1].User;
                prevSpecReq2User = fake.Requests[2].User;
                prevSpecHistory = historyJoined;
                prevSpecFirstResult = firstResult;
            }
        }
        finally
        {
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
        }
    }

    private static string ExtractJsonStringField(string json, string field)
    {
        if (string.IsNullOrEmpty(json)) return null;
        string needle = "\"" + field + "\":\"";
        int idx = json.IndexOf(needle, StringComparison.Ordinal);
        if (idx < 0) return null;
        int start = idx + needle.Length;
        int end = json.IndexOf('"', start);
        return end > start ? json.Substring(start, end - start) : null;
    }
}
