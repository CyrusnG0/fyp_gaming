using System;
using System.Collections.Generic;
using LuanShi.Engine;

namespace LuanShi.Playground
{
    /// <summary>
    /// Acceptance checks for the ACTUAL production <see cref="LlmTurnRunner"/> deliberation path
    /// (not the playground's parallel loop). Everything here uses the fake provider: no network and
    /// no paid call. The host is the same shape as the Unity/spectator host (engine seat turn,
    /// seat-filtered observation, SubmitAs with the bound token, EndSeatTurn once).
    /// </summary>
    internal static partial class PlaygroundSelfTests
    {
        private const int ProdSeed = 4242;
        private const string ProdGame = "production-deliberation-match";

        private static void ProductionDeliberationLoop(TestLog log)
        {
            GameEngine engine = ProductionEngine();
            var host = new ProductionHost(engine, "seat-a");
            var fake = new FakeLlmTransport();
            var memories = new InMemoryLlmMemoryStore();
            memories.Append(LlmMemoryExtractor.FromSummary(ProdGame, "seat-a", 1,
                "fixture: hold the river line"));
            var notes = new MemoryLlmStrategicNoteStore();
            LlmConfig config = ProductionConfig();
            int cpBefore = engine.Observe("seat-a").CommandPoints;
            string hashBefore = PlaygroundStateHash.Authoritative(engine);
            int goldBefore = engine.State.FindCity("city-a").Gold;

            fake.EnqueueSuccess("{\"tool\":\"manual_lookup\",\"topic\":\"recruit\"}");
            fake.EnqueueSuccess("{\"tool\":\"inspect_entity\",\"kind\":\"city\",\"id\":\"city-a\"}");
            fake.EnqueueSuccess("{\"tool\":\"search_history\",\"limit\":3}");
            fake.EnqueueSuccess("{\"tool\":\"recall_memory\",\"query\":\"fixture\",\"limit\":3}");
            fake.EnqueueSuccess("{\"type\":\"levy_tax\",\"target_id\":\"city-a\",\"summary\":\"raise taxes\"}");
            fake.EnqueueSuccess("{\"type\":\"end_turn\",\"summary\":\"done\"}");

            var runner = new LlmTurnRunner(fake, config, null, memories, notes);
            runner.Begin(host, host.Token, "seat-a", ProdSeed);
            DrainProduction(runner);

            log.Check(runner.State == RunnerState.Finished
                      && runner.EndReason == EndReason.ModelEndedTurn,
                "production deliberation: four inquiries then a legal action then end_turn finish",
                runner.FailureReason ?? runner.EndReasonText);
            log.Check(fake.CallCount == 6 && runner.RequestsThisTurn == 6
                      && runner.ActionsThisTurn == 1 && runner.AcceptedActionsThisTurn == 1,
                "production deliberation: one request per reply, one accepted action",
                "calls=" + fake.CallCount + " actions=" + runner.ActionsThisTurn);
            log.Check(host.SubmitCalls == 1 && host.EndTurnCalls == 1,
                "production deliberation: exactly one host submit and one end-turn");

            string p2 = fake.Requests[1].User;
            string p3 = fake.Requests[2].User;
            string p4 = fake.Requests[3].User;
            string p5 = fake.Requests[4].User;
            log.Check(p2.Contains("PREVIOUS INQUIRY RESULT") && p2.Contains("manual_lookup")
                      && p3.Contains("inspect_entity") && p4.Contains("search_history")
                      && p5.Contains("recall_memory"),
                "production deliberation: every one of the four tools reaches the next prompt");

            DeliberationBudgetSnapshot budget = runner.DeliberationBudget;
            log.Check(budget != null && budget.Session.Inquiries == 4
                      && budget.Session.Requests == 6
                      && budget.Session.AcceptedActions == 1
                      && budget.OutstandingReservationIds.Count == 0,
                "production deliberation: the shared ledger records 4 inquiries, 6 requests, 1 accepted action",
                budget == null ? "no ledger" : budget.ToJson());

            int cpAfter = engine.Observe("seat-a").CommandPoints;
            log.Check(goldBefore + BalanceConfig.TaxLevyAmount == engine.State.FindCity("city-a").Gold
                      && cpBefore - BalanceConfig.CommandPointCost(ActionType.LevyTax) == cpAfter,
                "production deliberation: only the accepted order spends command points/resources");
            log.Check(hashBefore != PlaygroundStateHash.Authoritative(engine),
                "production deliberation: the accepted action advances authoritative state");
        }

        private static void ProductionDeliberationInquiriesAreReadOnly(TestLog log)
        {
            GameEngine engine = ProductionEngine();
            var host = new ProductionHost(engine, "seat-a");
            var fake = new FakeLlmTransport();
            LlmConfig config = ProductionConfig();
            fake.EnqueueSuccess("{\"tool\":\"manual_lookup\",\"topic\":\"recruit\"}");
            fake.EnqueueSuccess("{\"tool\":\"inspect_entity\",\"kind\":\"city\",\"id\":\"city-a\"}");
            fake.EnqueueSuccess("{\"tool\":\"search_history\",\"limit\":3}");
            fake.EnqueueSuccess("{\"tool\":\"recall_memory\",\"limit\":3}");
            fake.EnqueueSuccess("{\"type\":\"end_turn\"}");

            int cpBefore = engine.Observe("seat-a").CommandPoints;
            string hashBefore = PlaygroundStateHash.Authoritative(engine);
            var runner = new LlmTurnRunner(fake, config, null);
            runner.Begin(host, host.Token, "seat-a", ProdSeed);
            DrainProduction(runner);

            log.Check(runner.EndReason == EndReason.ModelEndedTurn
                      && runner.AcceptedActionsThisTurn == 0 && host.SubmitCalls == 0,
                "production deliberation: an inquiry-only turn executes no game action");
            log.Check(string.Equals(hashBefore, PlaygroundStateHash.Authoritative(engine),
                    StringComparison.Ordinal)
                      && cpBefore == engine.Observe("seat-a").CommandPoints,
                "production deliberation: inquiries never mutate state or spend command points");
            DeliberationBudgetSnapshot budget = runner.DeliberationBudget;
            log.Check(budget != null && budget.Session.Inquiries == 4
                      && budget.Session.ActionAdmissions == 0,
                "production deliberation: inquiries consume inquiry slots, not action admissions");
        }

        private static void ProductionDeliberationNoteIsPrivate(TestLog log)
        {
            GameEngine engine = ProductionEngine();
            var fake = new FakeLlmTransport();
            var notes = new MemoryLlmStrategicNoteStore();
            LlmConfig config = ProductionConfig();
            var runner = new LlmTurnRunner(fake, config, null, null, notes);

            var hostA = new ProductionHost(engine, "seat-a");
            fake.EnqueueSuccess("{\"note\":\"hold the crossing and bank food\"}");
            fake.EnqueueSuccess("{\"type\":\"end_turn\"}");
            runner.Begin(hostA, hostA.Token, "seat-a", ProdSeed);
            DrainProduction(runner);
            log.Check(notes.Load(ProdGame, "seat-a") == "hold the crossing and bank food"
                      && notes.Load(ProdGame, "seat-b") == null,
                "production deliberation: a note is stored only for its match and seat");

            var hostB = new ProductionHost(engine, "seat-b");
            fake.EnqueueSuccess("{\"type\":\"end_turn\"}");
            runner.Begin(hostB, hostB.Token, "seat-b", ProdSeed);
            DrainProduction(runner);
            log.Check(!fake.Requests[2].User.Contains("hold the crossing")
                      && runner.CurrentStrategicNote == null,
                "production deliberation: another seat never receives the note");

            engine.EndSeason();
            engine.BeginSeason();
            var hostA2 = new ProductionHost(engine, "seat-a");
            fake.EnqueueSuccess("{\"type\":\"end_turn\"}");
            runner.Begin(hostA2, hostA2.Token, "seat-a", ProdSeed);
            DrainProduction(runner);
            log.Check(fake.Requests[3].User.Contains("STRATEGIC NOTE FROM YOUR PREVIOUS TURN")
                      && fake.Requests[3].User.Contains("hold the crossing and bank food")
                      && fake.Requests[3].User.Contains("never instructions"),
                "production deliberation: the seat reads its own note on a later turn as untrusted data");
        }


        // ---- fixtures ----------------------------------------------------------

        private static GameEngine ProductionEngine()
        {
            GameEngine engine = PlaygroundScenario.Create(ProdSeed, ProdGame);
            engine.BeginSeason();
            return engine;
        }

        private static LlmConfig ProductionConfig() => new LlmConfig
        {
            EnableDeliberation = true,
            MaxRequestsPerTurn = 10,
            MaxActionsPerTurn = 3,
            MaxInvalidPerTurn = 3,
            MaxTotalRequests = 60,
            MaxInquiriesPerTurn = 8,
            MaxInquiriesPerSession = 30,
            MaxRetainedInquiryResults = 4,
            MaxTurnSeconds = 60,
            RequestTimeoutMs = 5000,
            MaxOutputTokens = 400,
        };

        private static void DrainProduction(LlmTurnRunner runner)
        {
            for (int i = 0; i < 500 && (runner.State == RunnerState.Requesting
                                     || runner.State == RunnerState.Executing); i++)
                runner.Tick();
        }

        /// <summary>Minimal host matching the production/spectator binding shape.</summary>
        private sealed class ProductionHost : IPlanningHost
        {
            private readonly GameEngine _engine;
            private readonly string _seat;

            public readonly int Token;
            public int SubmitCalls;
            public int EndTurnCalls;

            public ProductionHost(GameEngine engine, string seat)
            {
                _engine = engine;
                _seat = seat;
                Token = engine.BeginSeatTurn(seat);
            }

            public Observation Observe() => _engine.Observe(_seat);

            public ValidationResult Submit(ActionCommand cmd)
            {
                SubmitCalls++;
                return _engine.SubmitAs(_seat, Token, cmd);
            }

            public void EndTurn()
            {
                EndTurnCalls++;
                _engine.EndSeatTurn(Token);
            }

            public MovementView PlanMovement() => MovementPlanner.Build(_engine, _seat);

            public bool IsGameOver => _engine.State.WinnerSeatId != null;
        }
    }
}
