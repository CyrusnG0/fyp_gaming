using System;
using System.Collections.Generic;
using System.IO;
using LuanShi.Engine;

namespace LuanShi.Playground
{
    /// <summary>
    /// Two-season fake rehearsal and the six-request boundary, both through the production
    /// <see cref="LlmTurnRunner"/> and <see cref="SpectatorSession"/>. Scripted replies are REHEARSAL
    /// EVIDENCE of plumbing, never model behaviour. Fake provider only: no network, no paid call.
    /// </summary>
    internal static partial class PlaygroundSelfTests
    {
        private const string RehearsalNote = "REHEARSAL-NOTE-WEI-7f3a91 hold the river, bank food";
        private const string RehearsalNoteId = "REHEARSAL-NOTE-WEI-7f3a91";
        // Fact ids that appear only in one manual topic's result (verified against the prompts).
        private const string ResearchMarker = "level_costs";
        private const string DiplomacyMarker = "treaty_types";

        private static void ProductionRehearsalTwoSeasonNoteAndInquiries(TestLog log)
        {
            string dir = Path.Combine(Path.GetTempPath(), "luanshi-rehearsal-" + Guid.NewGuid().ToString("N"));
            try
            {
                GameEngine engine = ProductionEngine();
                var fake = new FakeLlmTransport();
                var notes = new MemoryLlmStrategicNoteStore();
                LlmConfig config = ProductionConfig();
                var recorder = new LlmRunRecorder(dir);

                // Season 1 Wei (1-4), Shu (5-6); season 2 Wei (7), Shu (8).
                string[] script =
                {
                    "{\"tool\":\"manual_lookup\",\"topic\":\"research\"}",
                    "{\"note\":\"" + RehearsalNote + "\"}",
                    "{\"type\":\"levy_tax\",\"target_id\":\"city-a\",\"summary\":\"REHEARSAL levy\"}",
                    "{\"type\":\"end_turn\",\"summary\":\"REHEARSAL end\"}",
                    "{\"tool\":\"manual_lookup\",\"topic\":\"diplomacy\"}",
                    "{\"type\":\"end_turn\",\"summary\":\"REHEARSAL end\"}",
                    "{\"type\":\"end_turn\",\"summary\":\"REHEARSAL end\"}",
                    "{\"type\":\"end_turn\",\"summary\":\"REHEARSAL end\"}",
                };
                foreach (string reply in script) fake.EnqueueSuccess(reply, 0, 100, 20);

                var runner = new LlmTurnRunner(fake, config, recorder, null, notes);
                var session = new SpectatorSession(engine, runner, 2, ProdSeed);
                int goldBefore = engine.State.FindCity("city-a").Gold;
                int cpStart = engine.Observe(PlaygroundScenario.SeatA).CommandPoints;
                int levyCost = BalanceConfig.CommandPointCost(ActionType.LevyTax);

                // Per-request trace: each Tick handles one reply (fake replies are already complete).
                var trace = new List<string>();
                var cpAt = new Dictionary<int, int>();
                var goldAt = new Dictionary<int, int>();
                var acceptedAt = new Dictionary<int, long>();
                var inquiriesAt = new Dictionary<int, long>();
                int lastCalls = 0;
                // One snapshot per newly dispatched request. "dispatched=N" means replies 1..N-1
                // have been handled and request N is in flight.
                Action snapshot = () =>
                {
                    int calls = fake.CallCount;
                    DeliberationBudgetSnapshot snap = runner.DeliberationBudget;
                    if (calls == lastCalls || snap == null) return;
                    lastCalls = calls;
                    string active = session.ActiveSeatId ?? "(between turns)";
                    cpAt[calls] = engine.Observe(session.ActiveSeatId ?? PlaygroundScenario.SeatA).CommandPoints;
                    goldAt[calls] = engine.State.FindCity("city-a").Gold;
                    acceptedAt[calls] = snap.Session.AcceptedActions;
                    inquiriesAt[calls] = snap.Session.Inquiries;
                    trace.Add("dispatched=" + calls + " active=" + active + " cp=" + cpAt[calls]
                        + " ledger(requests=" + snap.Session.Requests + " inquiries=" + snap.Session.Inquiries
                        + " accepted=" + snap.Session.AcceptedActions + ")");
                };
                session.Start();
                snapshot();
                for (int i = 0; i < 1000 && session.State == SpectatorState.Running; i++)
                {
                    session.Tick();
                    snapshot();
                }
                Console.WriteLine("  TRACE (REHEARSAL, scripted; one line per dispatch seen after a Tick)");
                foreach (string line in trace) Console.WriteLine("  TRACE  " + line);

                log.Check(session.State == SpectatorState.Completed && session.SeasonsPlayed == 2
                          && fake.CallCount == 8,
                    "rehearsal: two seasons, four turns and eight dispatched requests complete",
                    session.State + " calls=" + fake.CallCount);

                IReadOnlyList<LlmRequest> prompts = fake.Requests;
                // Wei request 2 carries the research result; Shu never sees it.
                string weiAfterResearch = prompts[1].User;
                log.Check(!prompts[0].User.Contains(ResearchMarker) && !prompts[0].User.Contains(DiplomacyMarker)
                          && weiAfterResearch.Contains("PREVIOUS INQUIRY RESULT")
                          && weiAfterResearch.Contains(ResearchMarker)
                          && !weiAfterResearch.Contains(DiplomacyMarker),
                    "rehearsal: Wei's research result reaches Wei's next prompt only");
                string shuAfterDiplomacy = prompts[5].User;
                log.Check(!prompts[4].User.Contains(DiplomacyMarker)
                          && shuAfterDiplomacy.Contains(DiplomacyMarker)
                          && !shuAfterDiplomacy.Contains(ResearchMarker),
                    "rehearsal: Shu's diplomacy result reaches Shu's next prompt only");
                bool shuSawWei = false, weiSawShu = false;
                for (int i = 4; i <= 5; i++)
                    if (prompts[i].User.Contains(ResearchMarker) || prompts[i].User.Contains(RehearsalNoteId))
                        shuSawWei = true;
                for (int i = 0; i <= 3; i++)
                    if (prompts[i].User.Contains(DiplomacyMarker)) weiSawShu = true;
                if (prompts[7].User.Contains(ResearchMarker) || prompts[7].User.Contains(RehearsalNoteId)
                    || prompts[7].User.Contains(DiplomacyMarker)) shuSawWei = true;
                log.Check(!shuSawWei && !weiSawShu,
                    "rehearsal: no inquiry result or note crosses between the two factions");

                // Note: saved after request 2, loaded for Wei next season, never for Shu.
                string note1 = prompts[2].User;
                log.Check(note1.Contains(RehearsalNoteId) && !prompts[1].User.Contains(RehearsalNoteId),
                    "rehearsal: the note is saved by request 2 and visible to Wei from request 3");
                log.Check(prompts[6].User.Contains("STRATEGIC NOTE FROM YOUR PREVIOUS TURN")
                          && prompts[6].User.Contains(RehearsalNoteId),
                    "rehearsal: next season Wei (request 7) receives its own note");
                log.Check(!prompts[7].User.Contains("STRATEGIC NOTE FROM YOUR PREVIOUS TURN")
                          && !prompts[7].User.Contains(RehearsalNoteId)
                          && notes.Load(ProdGame, PlaygroundScenario.SeatB) == null
                          && notes.Load(ProdGame, PlaygroundScenario.SeatA) == RehearsalNote,
                    "rehearsal: next season Shu (request 8) never sees Wei's note; the store holds it for Wei only");

                // Inquiries and notes spend nothing: CP and accepted slots move only with the levy.
                log.Check(cpAt[2] == cpStart && cpAt[3] == cpStart
                          && acceptedAt[2] == 0 && acceptedAt[3] == 0
                          && inquiriesAt[2] == 1 && inquiriesAt[3] == 1,
                    "rehearsal: the inquiry and the note spend zero command points and zero accepted-action slots",
                    "cp2=" + cpAt[2] + " cp3=" + cpAt[3] + " start=" + cpStart);
                log.Check(cpAt[4] == cpStart - levyCost && acceptedAt[4] == 1
                          && goldAt[4] == goldBefore + BalanceConfig.TaxLevyAmount,
                    "rehearsal: only the accepted levy spends command points (" + levyCost + ") and gold",
                    "cp4=" + cpAt[4] + " gold4=" + goldAt[4]);
                DeliberationBudgetSnapshot budget = runner.DeliberationBudget;
                log.Check(budget != null && budget.Session.Requests == 8
                          && budget.Session.Inquiries == 2 && budget.Session.AcceptedActions == 1
                          && budget.Session.ActionAdmissions == 1
                          && budget.OutstandingReservationIds.Count == 0,
                    "rehearsal: engine and ledger agree: 2 inquiries, 8 requests, 1 accepted action",
                    budget == null ? "no ledger" : budget.ToJson());
                log.Check(levyCost > 0 && session.TotalAccepted == 1 && session.TotalRejected == 0
                          && session.VoluntaryEndTurns == 4,
                    "rehearsal: four turns end by end_turn with one accepted and no rejected action");

                // Summary and request logs reconcile.
                string json = File.ReadAllText(new SpectatorSummary(dir).Write(session, config, fake, runner));
                int logged = CountRequestRecords(recorder, runner.RunId);
                log.Check(json.Contains("\"requests\":8") && logged == 8 && fake.CallCount == 8
                          && json.Contains("\"accepted\":1") && json.Contains("\"voluntary_end_turns\":4")
                          && json.Contains("\"deliberation_enabled\":true"),
                    "rehearsal: summary requests (8) = request log lines (" + logged + ") = fake provider calls (8)");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch (Exception) { }
            }
        }

        private static int CountRequestRecords(LlmRunRecorder recorder, string runId)
        {
            string path = recorder.ExchangeFilePathFor(runId);
            if (!File.Exists(path)) return -1;
            int count = 0;
            foreach (string line in File.ReadAllLines(path))
                if (line.Contains("\"request\":{")) count++;
            return count;
        }

        // One fixture, two request caps. Requests 1-6 are four inquiry tools, a note and one more
        // inquiry; request 7 is a legal order and request 8 is end_turn.
        private static void ProductionRehearsalSixRequestBoundary(TestLog log)
        {
            RunBoundaryFixture(log, 6);
            RunBoundaryFixture(log, 8);
        }

        private static void RunBoundaryFixture(TestLog log, int cap)
        {
            GameEngine engine = ProductionEngine();
            var host = new ProductionHost(engine, PlaygroundScenario.SeatA);
            var fake = new FakeLlmTransport();
            LlmConfig config = ProductionConfig();
            config.MaxRequestsPerTurn = cap;
            // Every other limit stays clear of this fixture.
            config.MaxActionsPerTurn = 3;
            config.MaxInvalidPerTurn = 3;
            config.MaxInquiriesPerTurn = 8;
            config.MaxInquiriesPerSession = 30;
            config.MaxTotalRequests = 60;
            config.MaxTurnSeconds = 60;
            config.MaxDeliberationTurnMillis = 0;
            config.RequestTimeoutMs = 60000;

            string[] script =
            {
                "{\"tool\":\"manual_lookup\",\"topic\":\"research\"}",
                "{\"tool\":\"inspect_entity\",\"kind\":\"city\",\"id\":\"city-a\"}",
                "{\"tool\":\"search_history\",\"limit\":3}",
                "{\"tool\":\"recall_memory\",\"limit\":3}",
                "{\"note\":\"boundary fixture note\"}",
                "{\"tool\":\"manual_lookup\",\"topic\":\"diplomacy\"}",
                "{\"type\":\"levy_tax\",\"target_id\":\"city-a\",\"summary\":\"REHEARSAL levy\"}",
                "{\"type\":\"end_turn\",\"summary\":\"REHEARSAL end\"}",
            };
            foreach (string reply in script) fake.EnqueueSuccess(reply, 0, 100, 20);

            int goldBefore = engine.State.FindCity("city-a").Gold;
            var runner = new LlmTurnRunner(fake, config, null, null, new MemoryLlmStrategicNoteStore());
            runner.Begin(host, host.Token, PlaygroundScenario.SeatA, ProdSeed);
            DrainProduction(runner);
            DeliberationBudgetSnapshot budget = runner.DeliberationBudget;
            string tag = "boundary cap=" + cap + ": ";

            if (cap == 6)
            {
                log.Check(runner.State == RunnerState.Finished && runner.EndReason == EndReason.RequestLimit
                          && fake.CallCount == 6 && runner.RequestsThisTurn == 6,
                    tag + "request 7 never dispatches; the turn ends with RequestLimit after 6 requests",
                    runner.EndReason + " calls=" + fake.CallCount);
                log.Check(runner.AcceptedActionsThisTurn == 0 && host.SubmitCalls == 0
                          && engine.State.FindCity("city-a").Gold == goldBefore && host.EndTurnCalls == 1,
                    tag + "the legal order is never submitted; state is unchanged and the host ends the turn once");
                log.Check(budget != null && budget.Session.Requests == 6 && budget.Session.Inquiries == 5
                          && budget.Session.AcceptedActions == 0 && budget.OutstandingReservationIds.Count == 0,
                    tag + "ledger: 6 requests, 5 inquiries, 0 accepted actions, no outstanding hold",
                    budget == null ? "no ledger" : budget.ToJson());
            }
            else
            {
                log.Check(runner.State == RunnerState.Finished && runner.EndReason == EndReason.ModelEndedTurn
                          && fake.CallCount == 8 && runner.RequestsThisTurn == 8,
                    tag + "request 7 and request 8 dispatch; the model ends the turn after 8 requests",
                    runner.EndReason + " calls=" + fake.CallCount);
                log.Check(runner.AcceptedActionsThisTurn == 1 && host.SubmitCalls == 1
                          && engine.State.FindCity("city-a").Gold == goldBefore + BalanceConfig.TaxLevyAmount
                          && host.EndTurnCalls == 1,
                    tag + "the engine accepts the order (1 accepted action) and the turn ends once");
                log.Check(budget != null && budget.Session.Requests == 8 && budget.Session.Inquiries == 5
                          && budget.Session.AcceptedActions == 1 && budget.OutstandingReservationIds.Count == 0,
                    tag + "ledger: 8 requests, 5 inquiries, 1 accepted action, no outstanding hold",
                    budget == null ? "no ledger" : budget.ToJson());
            }
        }
    }
}
