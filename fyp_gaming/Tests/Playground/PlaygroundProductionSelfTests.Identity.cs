using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using LuanShi.Engine;

namespace LuanShi.Playground
{
    /// <summary>
    /// Production-runner checks for the wall-time stop, the request-record ordering and the
    /// spectator summary identity/endings. Fake provider only; no network and no paid call.
    /// </summary>
    internal static partial class PlaygroundSelfTests
    {
        /// <summary>Captures exchange lines so record ordering against dispatch is observable.</summary>
        private sealed class CapturingRecorder : ILlmRunRecorder, ILlmExchangeRecorder
        {
            public readonly List<string> Exchanges = new List<string>();
            public bool CaptureExchanges => true;
            public void Record(string runId, string jsonLine) { }
            public void RecordExchange(string runId, string jsonLine) => Exchanges.Add(jsonLine);

            public int RequestRecords()
            {
                int count = 0;
                foreach (string line in Exchanges) if (line.Contains("\"request\":{")) count++;
                return count;
            }
        }

        private static void ProductionDeliberationWallTimeStopsInFlight(TestLog log)
        {
            GameEngine engine = ProductionEngine();
            var host = new ProductionHost(engine, "seat-a");
            var fake = new FakeLlmTransport();
            LlmConfig config = ProductionConfig();
            config.MaxDeliberationTurnMillis = 40;
            config.MaxTurnSeconds = 60;
            config.RequestTimeoutMs = 60000;
            string hashBefore = PlaygroundStateHash.Authoritative(engine);
            var deferred = fake.EnqueueDeferred();
            var runner = new LlmTurnRunner(fake, config, null);
            runner.Begin(host, host.Token, "seat-a", ProdSeed);
            runner.Tick();
            log.Check(runner.State == RunnerState.Requesting && fake.CallCount == 1,
                "wall time: the request is still in flight before the limit");
            for (int i = 0; i < 300 && runner.State == RunnerState.Requesting; i++)
            {
                Thread.Sleep(10);
                runner.Tick();
            }
            log.Check(runner.State == RunnerState.Finished && runner.EndReason == EndReason.TurnTimeLimit
                      && host.EndTurnCalls == 1,
                "wall time: the configured deliberation limit stops an in-flight request",
                runner.State + " " + runner.EndReasonText);
            DeliberationBudgetSnapshot budget = runner.DeliberationBudget;
            log.Check(budget != null && budget.OutstandingReservationIds.Count == 0
                      && budget.Session.Requests == 1,
                "wall time: the in-flight reservation is settled once with no outstanding hold");
            deferred.SetResult(LlmResponse.Success(
                "{\"type\":\"levy_tax\",\"target_id\":\"city-a\"}", 0, 100, 20, "late"));
            runner.Tick();
            log.Check(host.SubmitCalls == 0 && host.EndTurnCalls == 1
                      && string.Equals(hashBefore, PlaygroundStateHash.Authoritative(engine),
                          StringComparison.Ordinal),
                "wall time: a response arriving after the stop is inert");
        }

        private static void ProductionDeliberationRecordsRequestOnlyAfterDispatch(TestLog log)
        {
            // Dispatch denied (same injected-clock recipe as the dispatch-denial test): no record.
            {
                GameEngine engine = ProductionEngine();
                var host = new ProductionHost(engine, "seat-a");
                var fake = new FakeLlmTransport();
                var recorder = new CapturingRecorder();
                LlmConfig config = ProductionConfig();
                config.MaxDeliberationTurnMillis = 5;
                var clock = new PlaygroundClock { AdvanceOnRead = 4, AdvanceMillisOnRead = 5 };
                fake.EnqueueSuccess("{\"type\":\"end_turn\"}");
                var runner = new LlmTurnRunner(fake, config, recorder, null, null, null, clock);
                runner.Begin(host, host.Token, "seat-a", ProdSeed);
                DrainProduction(runner);
                log.Check(runner.EndReason == EndReason.DispatchDenied && fake.CallCount == 0
                          && recorder.RequestRecords() == 0,
                    "record order: a dispatch-denied request leaves no provider-request record",
                    "records=" + recorder.RequestRecords() + " " + runner.EndReasonText);
            }
            // Admission denied by the session cap: the approved request is recorded, the denied is not.
            {
                GameEngine engine = ProductionEngine();
                var host = new ProductionHost(engine, "seat-a");
                var fake = new FakeLlmTransport();
                var recorder = new CapturingRecorder();
                LlmConfig config = ProductionConfig();
                config.MaxTotalRequests = 1;
                var runner = new LlmTurnRunner(fake, config, recorder);
                fake.EnqueueSuccess("{\"tool\":\"inspect_entity\",\"kind\":\"city\",\"id\":\"city-a\"}");
                runner.Begin(host, host.Token, "seat-a", ProdSeed);
                DrainProduction(runner);
                log.Check(fake.CallCount == 1 && recorder.RequestRecords() == 1
                          && runner.EndReason == EndReason.SessionRequestLimit,
                    "record order: only the approved request is recorded; the denied one is not",
                    "calls=" + fake.CallCount + " records=" + recorder.RequestRecords());
            }
        }

        private static void ProductionDeliberationSpectatorSummary(TestLog log)
        {
            string dir = Path.Combine(Path.GetTempPath(), "luanshi-delib-summary-" + Guid.NewGuid().ToString("N"));
            try
            {
                GameEngine engine = ProductionEngine();
                var fake = new FakeLlmTransport();
                LlmConfig config = ProductionConfig();
                config.MaxInquiriesPerTurn = 1;
                // Seat A: an inquiry, then a second inquiry (denied -> inquiry limit). Seat B: end_turn.
                fake.EnqueueSuccess("{\"tool\":\"inspect_entity\",\"kind\":\"city\",\"id\":\"city-a\"}");
                fake.EnqueueSuccess("{\"tool\":\"search_history\",\"limit\":3}");
                fake.EnqueueSuccess("{\"type\":\"end_turn\"}");
                var runner = new LlmTurnRunner(fake, config, new LlmRunRecorder(dir));
                var session = new SpectatorSession(engine, runner, 1, ProdSeed);
                session.Start();
                for (int i = 0; i < 1000 && session.State == SpectatorState.Running; i++) session.Tick();

                string json = File.ReadAllText(new SpectatorSummary(dir).Write(session, config, fake, runner));
                string version = PromptBuilder.DeliberationPromptVersion;
                string hash = PromptBuilder.SchemaHashFor(true, false, null, false, true);
                log.Check(json.Contains("\"prompt_version\":\"" + version + "\"")
                          && json.Contains("\"schema_hash\":\"" + hash + "\"")
                          && json.Contains("\"deliberation_enabled\":true"),
                    "spectator summary: deliberation prompt version, schema hash and flag are recorded");
                string lines = "";
                foreach (string file in Directory.GetFiles(dir, "*.jsonl")) lines += File.ReadAllText(file);
                log.Check(lines.Contains("\"prompt_version\":\"" + version + "\"")
                          && lines.Contains("\"schema_hash\":\"" + hash + "\""),
                    "spectator summary: identity matches the dispatched request log lines");
                log.Check(session.LimitEndings == 1 && session.VoluntaryEndTurns == 1
                          && json.Contains("\"limit_endings\":1")
                          && json.Contains("\"voluntary_end_turns\":1"),
                    "spectator summary: the inquiry-limit ending is counted as a limit ending",
                    "limit=" + session.LimitEndings + " voluntary=" + session.VoluntaryEndTurns);
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch (Exception) { }
            }

            // Feature off: the summary keeps the legacy identity.
            string dir2 = Path.Combine(Path.GetTempPath(), "luanshi-delib-summary-" + Guid.NewGuid().ToString("N"));
            try
            {
                GameEngine engine = ProductionEngine();
                var fake = new FakeLlmTransport();
                fake.EnqueueSuccess("{\"type\":\"end_turn\"}");
                fake.EnqueueSuccess("{\"type\":\"end_turn\"}");
                var config = new LlmConfig();
                var runner = new LlmTurnRunner(fake, config, new LlmRunRecorder(dir2));
                var session = new SpectatorSession(engine, runner, 1, ProdSeed);
                session.Start();
                for (int i = 0; i < 1000 && session.State == SpectatorState.Running; i++) session.Tick();
                string json = File.ReadAllText(new SpectatorSummary(dir2).Write(session, config, fake, runner));
                log.Check(!json.Contains("demo-slice-7") && json.Contains("\"deliberation_enabled\":false"),
                    "spectator summary: feature off keeps the legacy identity");
            }
            finally
            {
                try { Directory.Delete(dir2, true); } catch (Exception) { }
            }
        }
    }
}
