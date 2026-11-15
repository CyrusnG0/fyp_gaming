using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using LuanShi.Engine;

namespace LuanShi.Playground
{
    /// <summary>Retention, observable thinking, multi-season play and ceiling checks (fake provider only).</summary>
    internal static partial class PlaygroundSelfTests
    {
        private static void ExperimentRetentionAcrossTurns(TestLog log)
        {
            foreach (bool experiment in new[] { true, false })
            {
                GameEngine engine = ProductionEngine();
                var fake = new FakeLlmTransport();
                var notes = new MemoryLlmStrategicNoteStore();
                LlmConfig config = experiment ? RichConfig() : ProductionConfig();
                fake.EnqueueSuccess("{\"tool\":\"manual_lookup\",\"topic\":\"research\"}", 0, 100, 10);
                fake.EnqueueSuccess("{\"note\":\"NOTE-A-91 keep the granary full\"}", 0, 100, 10);
                fake.EnqueueSuccess("{\"type\":\"end_turn\"}", 0, 100, 10);
                var runner = new LlmTurnRunner(fake, config, null, null, notes);
                var hostA = new ProductionHost(engine, "seat-a");
                runner.Begin(hostA, hostA.Token, "seat-a", ProdSeed);
                DrainProduction(runner);

                var hostB = new ProductionHost(engine, "seat-b");
                fake.EnqueueSuccess("{\"tool\":\"manual_lookup\",\"topic\":\"diplomacy\"}", 0, 100, 10);
                fake.EnqueueSuccess("{\"type\":\"end_turn\"}", 0, 100, 10);
                runner.Begin(hostB, hostB.Token, "seat-b", ProdSeed);
                DrainProduction(runner);

                engine.EndSeason();
                engine.BeginSeason();
                var hostA2 = new ProductionHost(engine, "seat-a");
                fake.EnqueueSuccess("{\"type\":\"end_turn\"}", 0, 100, 10);
                runner.Begin(hostA2, hostA2.Token, "seat-a", ProdSeed);
                DrainProduction(runner);
                string nextTurn = UserOfCall(fake, fake.CallCount - 1);
                string shuTurn = UserOfCall(fake, 4);   // Shu's request after its own inquiry
                string label = experiment ? "experiment" : "plain deliberation";
                if (experiment)
                {
                    log.Check(nextTurn.Contains("PRIOR INQUIRY RESULTS") && nextTurn.Contains("[season 1, manual_lookup")
                              && nextTurn.Contains(ResearchResultMarker) && !nextTurn.Contains(DiplomacyResultMarker),
                        label + ": the seat's own earlier result is revisitable on a later turn, labelled with its season");
                    log.Check(nextTurn.Contains("YOUR PRIVATE STRATEGIC NOTE") && nextTurn.Contains("NOTE-A-91"),
                        label + ": the seat's note returns on its next turn");
                    log.Check(!shuTurn.Contains(ResearchResultMarker) && !shuTurn.Contains("NOTE-A-91"),
                        label + ": the other seat sees neither the result nor the note");
                    log.Check(nextTurn.Contains("prior inquiry results (newest kept): 1 of 1"),
                        label + ": the completeness report lists the retained result");
                }
                else
                {
                    log.Check(!nextTurn.Contains(ResearchResultMarker) && nextTurn.Contains("NOTE-A-91"),
                        label + ": today's behaviour: only the note survives to the next turn, the result is gone");
                }
            }
        }

        private static string Lines(string path) => File.Exists(path) ? File.ReadAllText(path) : "";

        private static void ExperimentThinkingIsObservableAndFree(TestLog log)
        {
            string dir = Path.Combine(Path.GetTempPath(), "luanshi-think-" + Guid.NewGuid().ToString("N"));
            try
            {
                GameEngine engine = ProductionEngine();
                var recorder = new LlmRunRecorder(dir);
                var fake = new FakeLlmTransport();
                LlmConfig config = RichConfig();
                EnqueueTelemetry(fake, "{\"tool\":\"manual_lookup\",\"topic\":\"research\"}", 900, 40, 31, "stop");
                EnqueueTelemetry(fake, "{\"note\":\"NOTE-PRIVATE-77 plan\"}", 1100, 30, 12, "stop");
                EnqueueTelemetry(fake, "{\"type\":\"levy_tax\",\"target_id\":\"city-a\",\"summary\":\"raise\"}", 1200, 50, 20, "stop");
                EnqueueTelemetry(fake, "{\"type\":\"end_turn\"}", 1300, 10, 0, "stop");
                var runner = new LlmTurnRunner(fake, config, recorder);
                var host = new ProductionHost(engine, "seat-a");
                int cp0 = engine.Observe("seat-a").CommandPoints;
                runner.Begin(host, host.Token, "seat-a", ProdSeed);
                DrainProduction(runner);

                string run = Lines(recorder.FilePath);
                string exch = Lines(recorder.ExchangeFilePathFor(runner.RunId));
                string[] rows = run.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                var kinds = new List<string>();
                foreach (string row in rows)
                {
                    Match k = Regex.Match(row, "\"deliberation_event\":\\{\"kind\":\"([a-z_]+)\"");
                    if (k.Success) kinds.Add(k.Groups[1].Value);
                }
                log.Check(string.Join(",", kinds) == "inquiry,note,action,end_turn",
                    "thinking: each choice is recorded as inquiry, note, action, end_turn in order",
                    string.Join(",", kinds));

                Match inquiry = Regex.Match(rows[0],
                    "\"kind\":\"inquiry\",\"tool\":\"manual_lookup\",\"outcome\":\"answered\",\"exec_ms\":(\\d+),\"result_chars\":(\\d+),\"command_points_before\":(\\d+),\"command_points_after\":(\\d+)");
                Match note = Regex.Match(rows[1],
                    "\"kind\":\"note\",\"outcome\":\"saved\",\"exec_ms\":(\\d+),\"result_chars\":(\\d+),\"command_points_before\":(\\d+),\"command_points_after\":(\\d+)");
                Match action = Regex.Match(rows[2],
                    "\"kind\":\"action\",\"outcome\":\"accepted\",\"exec_ms\":\\d+,\"result_chars\":null,\"command_points_before\":(\\d+),\"command_points_after\":(\\d+)");
                log.Check(inquiry.Success && inquiry.Groups[3].Value == cp0.ToString()
                          && inquiry.Groups[3].Value == inquiry.Groups[4].Value
                          && note.Success && note.Groups[3].Value == note.Groups[4].Value
                          && note.Groups[2].Value == "20",
                    "thinking: an inquiry and a note record timing and leave command points unchanged",
                    inquiry.Value + " | " + note.Value);
                int levyCost = BalanceConfig.CommandPointCost(ActionType.LevyTax);
                log.Check(action.Success && int.Parse(action.Groups[1].Value) - int.Parse(action.Groups[2].Value) == levyCost
                          && runner.AcceptedActionsThisTurn == 1 && runner.InquiriesThisTurn == 1
                          && runner.NotesSavedThisTurn == 1,
                    "thinking: only the accepted order spends command points and an action slot", action.Value);
                bool eventHoldsText = false;
                foreach (Match m in Regex.Matches(run, "\"deliberation_event\":\\{[^}]*\\}"))
                    if (m.Value.Contains("NOTE-PRIVATE-77") || m.Value.Contains(ResearchResultMarker))
                        eventHoldsText = true;
                log.Check(!eventHoldsText && !run.Contains(ResearchResultMarker),
                    "thinking: the event holds tool, sizes and timing only; no inquiry result enters the run record");
                log.Check(rows[0].Contains("\"finish_reason\":\"stop\"") && rows[0].Contains("\"reasoning_tokens\":31")
                          && rows[0].Contains("\"input_tokens\":900") && rows[0].Contains("\"output_tokens\":40")
                          && !rows[0].Contains("reasoning_content\""),
                    "telemetry: finish reason, reasoning/input/output tokens are recorded as reported counts only");
                log.Check(runner.TotalInputTokens == 4500 && runner.TotalOutputTokens == 130,
                    "telemetry: session token totals sum the provider-reported usage",
                    runner.TotalInputTokens + "/" + runner.TotalOutputTokens);
                log.Check(Regex.Matches(exch, "\"exchange_event\":\"experiment_config\"").Count == 1
                          && exch.Contains("\"max_session_seconds\":5400")
                          && exch.Contains("\"exchange_event\":\"turn_end\"")
                          && exch.Contains("\"stop_ceiling\":null") && exch.Contains("\"stopped_by_ceiling\":false")
                          && exch.Contains("\"end_reason\":\"ModelEndedTurn\""),
                    "logging: one settings line states every ceiling; the turn-end line says no ceiling stopped it");
            }
            finally { try { Directory.Delete(dir, true); } catch (Exception) { } }
        }

        private static void ExperimentLargeBudgetTwoSeatsMultiSeason(TestLog log)
        {
            GameEngine engine = ProductionEngine();
            var fake = new FakeLlmTransport();
            LlmConfig config = RichConfig();
            var notes = new MemoryLlmStrategicNoteStore();
            string[] topics = { "research", "diplomacy", "economy" };
            int seasons = 3;
            for (int season = 1; season <= seasons; season++)
                foreach (string seat in new[] { "a", "b" })
                {
                    string marker = seat == "a" ? "WEI" : "SHU";
                    foreach (string topic in topics)
                        fake.EnqueueSuccess("{\"tool\":\"manual_lookup\",\"topic\":\"" + topic + "\"}", 0, 800, 40);
                    fake.EnqueueSuccess("{\"tool\":\"inspect_entity\",\"kind\":\"city\",\"id\":\"city-" + seat + "\"}", 0, 800, 40);
                    fake.EnqueueSuccess("{\"tool\":\"search_history\",\"limit\":5}", 0, 800, 40);
                    fake.EnqueueSuccess("{\"note\":\"" + marker + "-NOTE-S" + season + " plan\"}", 0, 800, 40);
                    fake.EnqueueSuccess("{\"type\":\"levy_tax\",\"target_id\":\"city-" + seat + "\"}", 0, 800, 40);
                    fake.EnqueueSuccess("{\"type\":\"fortify\",\"target_id\":\"city-" + seat + "\"}", 0, 800, 40);
                    fake.EnqueueSuccess("{\"type\":\"end_turn\"}", 0, 800, 40);
                }
            var runner = new LlmTurnRunner(fake, config, null, null, notes);
            var session = new SpectatorSession(engine, runner, seasons, ProdSeed);
            session.Start();
            for (int i = 0; i < 5000 && session.State == SpectatorState.Running; i++) session.Tick();

            DeliberationBudgetSnapshot budget = runner.DeliberationBudget;
            log.Check(session.State == SpectatorState.Completed && session.SeasonsPlayed == seasons,
                "large budget: both seats complete three seasons", session.State.ToString());
            log.Check(fake.CallCount == 6 * 9 && budget != null && budget.Session.Requests == 54
                      && budget.Session.Inquiries == 30 && budget.Session.AcceptedActions == 12
                      && budget.OutstandingReservationIds.Count == 0,
                "large budget: 54 requests, 30 inquiries and 12 accepted actions reconcile in the shared ledger",
                budget == null ? "no ledger" : budget.ToJson());
            log.Check(session.VoluntaryEndTurns == 6 && session.LimitEndings == 0,
                "large budget: every turn ended by the model's own end_turn, none by a limit");

            bool crossed = false;
            for (int i = 0; i < fake.CallCount; i++)
            {
                string user = UserOfCall(fake, i);
                // Requests 0-8 in each 9-request turn alternate seats a,b per season.
                bool weiTurn = (i / 9) % 2 == 0;
                if (weiTurn && user.Contains("SHU-NOTE")) crossed = true;
                if (!weiTurn && user.Contains("WEI-NOTE")) crossed = true;
            }
            log.Check(!crossed, "large budget: no note ever appears in the other faction's prompt");
            string wei3 = UserOfCall(fake, 4 * 9);   // Wei, season 3, first request
            Match prior = Regex.Match(wei3, @"prior inquiry results \(newest kept\): (\d+) of (\d+)");
            log.Check(wei3.Contains("WEI-NOTE-S2") && !wei3.Contains("WEI-NOTE-S1")
                      && prior.Success && prior.Groups[2].Value == "10"
                      && int.Parse(prior.Groups[1].Value) >= 1
                      && (prior.Groups[1].Value == "10" || wei3.Contains("(INCOMPLETE)")),
                "large budget: by season three a seat keeps its latest note and reports its retained results honestly",
                prior.Value);
        }

        private static LlmTurnRunner RunRichTurn(LlmConfig config, FakeLlmTransport fake, string seat,
            out ProductionHost host, IDeliberationClock clock = null, ILlmRunRecorder recorder = null,
            GameEngine engine = null)
        {
            engine = engine ?? ProductionEngine();
            host = new ProductionHost(engine, seat);
            var runner = new LlmTurnRunner(fake, config, recorder, null, null, null, clock);
            runner.Begin(host, host.Token, seat, ProdSeed);
            DrainProduction(runner);
            return runner;
        }

        private static void CheckStop(TestLog log, string name, LlmTurnRunner runner, EndReason reason,
            string ceiling, int calls, FakeLlmTransport fake, ProductionHost host)
        {
            log.Check(runner.EndReason == reason && runner.StopCeiling == ceiling && fake.CallCount == calls
                      && host.EndTurnCalls == 1 && runner.EndReasonText.Contains("[ceiling: " + ceiling + "]"),
                "ceiling " + name + ": ends the turn once and names the ceiling '" + ceiling + "'",
                runner.EndReason + " " + runner.StopCeiling + " calls=" + fake.CallCount + " " + runner.EndReasonText);
        }

        private static void ExperimentCeilings(TestLog log)
        {
            string inquiry = "{\"tool\":\"manual_lookup\",\"topic\":\"recruit\"}";
            ProductionHost host;
            FakeLlmTransport fake;
            LlmConfig c;
            LlmTurnRunner r;

            c = RichConfig(); c.MaxRequestsPerTurn = 3; c.MaxTotalRequests = 400;
            fake = new FakeLlmTransport(); for (int i = 0; i < 9; i++) fake.EnqueueSuccess(inquiry, 0, 100, 10);
            string recDir = Path.Combine(Path.GetTempPath(), "luanshi-ceil-" + Guid.NewGuid().ToString("N"));
            try
            {
                var ceilingRecorder = new LlmRunRecorder(recDir);
                r = RunRichTurn(c, fake, "seat-a", out host, null, ceilingRecorder);
                CheckStop(log, "turn requests", r, EndReason.RequestLimit, "turn_requests_exhausted", 3, fake, host);
                string exch = Lines(ceilingRecorder.ExchangeFilePathFor(r.RunId));
                log.Check(exch.Contains("\"exchange_event\":\"turn_end\"")
                          && exch.Contains("\"stop_ceiling\":\"turn_requests_exhausted\"")
                          && exch.Contains("\"stopped_by_ceiling\":true")
                          && exch.Contains("\"requests_this_turn\":3") && exch.Contains("\"inquiries_this_turn\":3"),
                    "logging: the turn-end line records which ceiling stopped the turn and the turn counters");
            }
            finally { try { Directory.Delete(recDir, true); } catch (Exception) { } }

            c = RichConfig(); c.MaxRequestsPerTurn = 3; c.MaxTotalRequests = 4;
            fake = new FakeLlmTransport(); for (int i = 0; i < 9; i++) fake.EnqueueSuccess(inquiry, 0, 100, 10);
            GameEngine sessionEngine = ProductionEngine();
            var sessionRunner = new LlmTurnRunner(fake, c, null);
            var hostS1 = new ProductionHost(sessionEngine, "seat-a");
            sessionRunner.Begin(hostS1, hostS1.Token, "seat-a", ProdSeed);
            DrainProduction(sessionRunner);
            host = new ProductionHost(sessionEngine, "seat-b");
            sessionRunner.Begin(host, host.Token, "seat-b", ProdSeed);
            DrainProduction(sessionRunner);
            CheckStop(log, "session requests", sessionRunner, EndReason.SessionRequestLimit, "session_requests_exhausted", 4, fake, host);

            c = RichConfig(); c.MaxInquiriesPerTurn = 2;
            fake = new FakeLlmTransport(); for (int i = 0; i < 9; i++) fake.EnqueueSuccess(inquiry, 0, 100, 10);
            r = RunRichTurn(c, fake, "seat-a", out host);
            CheckStop(log, "turn inquiries", r, EndReason.InquiryLimit, "turn_inquiry_exhausted", 3, fake, host);

            c = RichConfig(); c.MaxActionsPerTurn = 2;
            fake = new FakeLlmTransport(); for (int i = 0; i < 4; i++) fake.EnqueueSuccess("{\"type\":\"fortify\",\"target_id\":\"city-a\"}", 0, 100, 10);
            r = RunRichTurn(c, fake, "seat-a", out host);
            CheckStop(log, "turn accepted actions", r, EndReason.ActionLimit, "turn_actions", 2, fake, host);

            c = RichConfig(); c.MaxAcceptedActionsPerSession = 4; c.MaxActionsPerTurn = 3;
            fake = new FakeLlmTransport();
            for (int i = 0; i < 3; i++) fake.EnqueueSuccess("{\"type\":\"fortify\",\"target_id\":\"city-a\"}", 0, 100, 10);
            for (int i = 0; i < 3; i++) fake.EnqueueSuccess("{\"type\":\"fortify\",\"target_id\":\"city-b\"}", 0, 100, 10);
            GameEngine shared = ProductionEngine();
            var sharedRunner = new LlmTurnRunner(fake, c, null);
            var hostSa = new ProductionHost(shared, "seat-a");
            sharedRunner.Begin(hostSa, hostSa.Token, "seat-a", ProdSeed);
            DrainProduction(sharedRunner);
            host = new ProductionHost(shared, "seat-b");
            sharedRunner.Begin(host, host.Token, "seat-b", ProdSeed);
            DrainProduction(sharedRunner);
            CheckStop(log, "session accepted actions", sharedRunner, EndReason.ActionLimit, "session_actions_exhausted", 5, fake, host);

            // Provider ceilings, checked before the next dispatch.
            c = RichConfig(); c.MaxSessionOutputTokens = 100; c.MaxOutputTokens = 60;
            fake = new FakeLlmTransport(); for (int i = 0; i < 4; i++) fake.EnqueueSuccess(inquiry, 0, 500, 50);
            r = RunRichTurn(c, fake, "seat-a", out host);
            CheckStop(log, "session output tokens", r, EndReason.DeliberationLimit, "session_output_tokens", 1, fake, host);

            c = RichConfig(); c.MaxSessionInputTokens = 1000;
            fake = new FakeLlmTransport(); for (int i = 0; i < 4; i++) fake.EnqueueSuccess(inquiry, 0, 1200, 20);
            r = RunRichTurn(c, fake, "seat-a", out host);
            CheckStop(log, "session input tokens", r, EndReason.DeliberationLimit, "session_input_tokens", 1, fake, host);

            c = RichConfig();
            fake = new FakeLlmTransport(); for (int i = 0; i < 4; i++) fake.EnqueueSuccess(inquiry);   // no usage reported
            r = RunRichTurn(c, fake, "seat-a", out host);
            CheckStop(log, "unknown usage", r, EndReason.DeliberationLimit, "session_usage_unknown", 1, fake, host);

            // Session wall clock through the injected clock: admission and in-flight.
            var clock = new PlaygroundClock();
            c = RichConfig(); c.MaxSessionSeconds = 1; c.MaxTurnSeconds = 1; c.MaxDeliberationTurnMillis = 1000;
            fake = new FakeLlmTransport();
            var deferred = fake.EnqueueDeferred();
            GameEngine eng = ProductionEngine();
            host = new ProductionHost(eng, "seat-a");
            r = new LlmTurnRunner(fake, c, null, null, null, null, clock);
            r.Begin(host, host.Token, "seat-a", ProdSeed);
            r.Tick();
            clock.Advance(1500);
            DrainProduction(r);
            CheckStop(log, "session seconds (in flight)", r, EndReason.TurnTimeLimit, "session_seconds", 1, fake, host);
            deferred.SetResult(LlmResponse.Success(inquiry, 1, 1, 1, null));

            clock = new PlaygroundClock();
            c = RichConfig(); c.MaxSessionSeconds = 1; c.MaxTurnSeconds = 1; c.MaxDeliberationTurnMillis = 1000;
            fake = new FakeLlmTransport();
            fake.EnqueueSuccess(inquiry, 0, 100, 10); fake.EnqueueSuccess(inquiry, 0, 100, 10);
            host = new ProductionHost(ProductionEngine(), "seat-a");
            r = new LlmTurnRunner(fake, c, null, null, null, null, clock);
            r.Begin(host, host.Token, "seat-a", ProdSeed);
            clock.Advance(2000);   // the ledger sees the session end at the next admission
            DrainProduction(r);
            log.Check(r.EndReason == EndReason.TurnTimeLimit && r.StopCeiling != null
                      && (r.StopCeiling == "session_seconds" || r.StopCeiling == "session_wall_time_exhausted"),
                "ceiling session seconds (admission): the turn ends and names the session wall clock",
                r.EndReason + " " + r.StopCeiling);

            // Per-turn wall clock, real time (one second), in flight.
            c = RichConfig(); c.MaxTurnSeconds = 1; c.MaxDeliberationTurnMillis = 600000;
            fake = new FakeLlmTransport(); fake.EnqueueDeferred();
            host = new ProductionHost(ProductionEngine(), "seat-a");
            r = new LlmTurnRunner(fake, c, null);
            r.Begin(host, host.Token, "seat-a", ProdSeed);
            for (int i = 0; i < 400 && r.State == RunnerState.Requesting; i++) { Thread.Sleep(10); r.Tick(); }
            CheckStop(log, "turn seconds", r, EndReason.TurnTimeLimit, "turn_seconds", 1, fake, host);

            // An invalid experiment configuration never reaches the provider.
            c = RichConfig(); c.MaxSessionSeconds = 0;
            fake = new FakeLlmTransport(); fake.EnqueueSuccess(inquiry, 0, 100, 10);
            r = RunRichTurn(c, fake, "seat-a", out host);
            log.Check(fake.CallCount == 0 && r.State == RunnerState.Failed && host.EndTurnCalls == 1
                      && r.FailureReason.StartsWith("experiment configuration invalid")
                      && r.FailureReason.Contains("max_session_seconds_not_set"),
                "ceiling configuration: an unset ceiling refuses the run before any provider call", r.FailureReason);
        }

        private static void ExperimentEmptyRepliesAndTelemetry(TestLog log)
        {
            string dir = Path.Combine(Path.GetTempPath(), "luanshi-empty-" + Guid.NewGuid().ToString("N"));
            try
            {
                var recorder = new LlmRunRecorder(dir);
                LlmConfig c = RichConfig(); c.MaxInvalidPerTurn = 2;
                var fake = new FakeLlmTransport();
                EnqueueTelemetry(fake, "", 1000, 20, 0, "stop");
                EnqueueTelemetry(fake, "{\"type\":\"end_turn\"}", 1000, 10, 0, "stop");
                LlmTurnRunner r = RunRichTurn(c, fake, "seat-a", out ProductionHost host, null, recorder);
                log.Check(r.EndReason == EndReason.ModelEndedTurn && r.InvalidThisTurn == 1 && fake.CallCount == 2,
                    "empty reply: one empty reply is repaired within the invalid allowance", r.EndReasonText);

                fake = new FakeLlmTransport();
                EnqueueTelemetry(fake, "", 1000, 20, 0, "stop");
                EnqueueTelemetry(fake, "   ", 1000, 20, 0, "stop");
                r = RunRichTurn(c, fake, "seat-a", out host);
                log.Check(r.EndReason == EndReason.InvalidResponseLimit && r.StopCeiling == "invalid_responses"
                          && fake.CallCount == 2,
                    "empty reply: consecutive empty replies stop at the invalid allowance and name it", r.EndReasonText);

                fake = new FakeLlmTransport();
                EnqueueTelemetry(fake, "", 1000, c.MaxOutputTokens, c.MaxOutputTokens - 10, "length");
                r = RunRichTurn(c, fake, "seat-a", out host, null, recorder);
                log.Check(r.EndReason == EndReason.InvalidResponseLimit && r.StopCeiling == "output_tokens_per_reply"
                          && fake.CallCount == 1,
                    "empty reply: an empty reply that used the whole output bound stops at once and names it",
                    r.EndReasonText);
                string run = Lines(recorder.FilePath);
                log.Check(File.ReadAllText(recorder.ExchangeFilePathFor(r.RunId)).Contains("\"finish_reason\":\"length\""),
                    "empty reply: the length finish reason is captured beside the stop");
            }
            finally { try { Directory.Delete(dir, true); } catch (Exception) { } }
        }
    }
}
