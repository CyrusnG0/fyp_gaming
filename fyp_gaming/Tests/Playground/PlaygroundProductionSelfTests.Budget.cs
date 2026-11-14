using System;
using LuanShi.Engine;

namespace LuanShi.Playground
{
    /// <summary>Budget, dispatch, cancellation and isolation checks for the production runner.</summary>
    internal static partial class PlaygroundSelfTests
    {
        private static void ProductionDeliberationBudgetLimits(TestLog log)
        {
            // Inquiry cap.
            {
                GameEngine engine = ProductionEngine();
                var host = new ProductionHost(engine, "seat-a");
                var fake = new FakeLlmTransport();
                LlmConfig config = ProductionConfig();
                config.MaxInquiriesPerTurn = 1;
                fake.EnqueueSuccess("{\"tool\":\"manual_lookup\",\"topic\":\"recruit\"}");
                fake.EnqueueSuccess("{\"tool\":\"manual_lookup\",\"topic\":\"march\"}");
                fake.EnqueueSuccess("{\"type\":\"end_turn\"}");
                var runner = new LlmTurnRunner(fake, config, null);
                runner.Begin(host, host.Token, "seat-a", ProdSeed);
                DrainProduction(runner);
                log.Check(runner.EndReason == EndReason.InquiryLimit && fake.CallCount == 2
                          && host.SubmitCalls == 0,
                    "production deliberation: the inquiry cap denies the second read without an action");
                log.Check(runner.DeliberationBudget.Session.Inquiries == 1,
                    "production deliberation: a denied inquiry is not counted as executed");
            }

            // Per-turn request cap.
            {
                GameEngine engine = ProductionEngine();
                var host = new ProductionHost(engine, "seat-a");
                var fake = new FakeLlmTransport();
                LlmConfig config = ProductionConfig();
                config.MaxRequestsPerTurn = 1;
                config.MaxTotalRequests = 10;
                fake.EnqueueSuccess("{\"tool\":\"manual_lookup\",\"topic\":\"recruit\"}");
                fake.EnqueueSuccess("{\"type\":\"end_turn\"}");
                var runner = new LlmTurnRunner(fake, config, null);
                runner.Begin(host, host.Token, "seat-a", ProdSeed);
                DrainProduction(runner);
                log.Check(runner.EndReason == EndReason.RequestLimit && fake.CallCount == 1,
                    "production deliberation: the per-turn request cap stops the next dispatch",
                    runner.EndReasonText);
            }

            // Shared session request cap.
            {
                GameEngine engine = ProductionEngine();
                var host = new ProductionHost(engine, "seat-a");
                var fake = new FakeLlmTransport();
                LlmConfig config = ProductionConfig();
                config.MaxRequestsPerTurn = 5;
                config.MaxTotalRequests = 2;
                fake.EnqueueSuccess("{\"tool\":\"manual_lookup\",\"topic\":\"recruit\"}");
                fake.EnqueueSuccess("{\"tool\":\"manual_lookup\",\"topic\":\"march\"}");
                fake.EnqueueSuccess("{\"type\":\"end_turn\"}");
                var runner = new LlmTurnRunner(fake, config, null);
                runner.Begin(host, host.Token, "seat-a", ProdSeed);
                DrainProduction(runner);
                log.Check(runner.EndReason == EndReason.SessionRequestLimit && fake.CallCount == 2,
                    "production deliberation: the session request cap stops before a third dispatch",
                    runner.EndReasonText);
            }

            // Accepted-action cap ignores rejected attempts.
            {
                GameEngine engine = ProductionEngine();
                var host = new ProductionHost(engine, "seat-a");
                var fake = new FakeLlmTransport();
                LlmConfig config = ProductionConfig();
                config.MaxActionsPerTurn = 1;
                config.MaxInvalidPerTurn = 3;
                fake.EnqueueSuccess("{\"type\":\"train\",\"target_id\":\"city-b\"}"); // rejected
                fake.EnqueueSuccess("{\"type\":\"levy_tax\",\"target_id\":\"city-a\"}");
                fake.EnqueueSuccess("{\"type\":\"end_turn\"}");
                var runner = new LlmTurnRunner(fake, config, null);
                runner.Begin(host, host.Token, "seat-a", ProdSeed);
                DrainProduction(runner);
                log.Check(runner.EndReason == EndReason.ActionLimit && fake.CallCount == 2
                          && runner.ActionsThisTurn == 2 && runner.AcceptedActionsThisTurn == 1
                          && runner.DeliberationBudget.Session.AcceptedActions == 1,
                    "production deliberation: rejected attempts do not consume accepted-action slots",
                    runner.EndReasonText + " calls=" + fake.CallCount);
            }

            // Oversized note is rejected and never saved.
            {
                GameEngine engine = ProductionEngine();
                var host = new ProductionHost(engine, "seat-a");
                var fake = new FakeLlmTransport();
                var notes = new MemoryLlmStrategicNoteStore();
                LlmConfig config = ProductionConfig();
                config.MaxNoteChars = 10;
                config.MaxInvalidPerTurn = 1;
                fake.EnqueueSuccess("{\"note\":\"this note is deliberately too long\"}");
                var runner = new LlmTurnRunner(fake, config, null, null, notes);
                runner.Begin(host, host.Token, "seat-a", ProdSeed);
                DrainProduction(runner);
                log.Check(runner.EndReason == EndReason.InvalidResponseLimit
                          && string.IsNullOrEmpty(notes.Load(ProdGame, "seat-a"))
                          && runner.CurrentStrategicNote == null,
                    "production deliberation: an oversized note is rejected without a save or action");
            }
        }

        private static void ProductionDeliberationDispatchAndCancellation(TestLog log)
        {
            // Dispatch denial: the injected clock expires the admission after admission but before
            // MarkDispatched, so the provider is never called.
            {
                GameEngine engine = ProductionEngine();
                var host = new ProductionHost(engine, "seat-a");
                var fake = new FakeLlmTransport();
                LlmConfig config = ProductionConfig();
                config.MaxDeliberationTurnMillis = 5;
                var clock = new PlaygroundClock { AdvanceOnRead = 4, AdvanceMillisOnRead = 5 };
                fake.EnqueueSuccess("{\"type\":\"end_turn\"}");
                var runner = new LlmTurnRunner(fake, config, null, null, null, null, clock);
                runner.Begin(host, host.Token, "seat-a", ProdSeed);
                DrainProduction(runner);
                DeliberationBudgetSnapshot budget = runner.DeliberationBudget;
                log.Check(runner.EndReason == EndReason.DispatchDenied && fake.CallCount == 0,
                    "production deliberation: a denied dispatch never reaches the provider",
                    runner.EndReasonText);
                log.Check(budget != null && budget.Session.Requests == 1
                          && budget.Session.ReservedOutputTokens == 0
                          && budget.OutstandingReservationIds.Count == 0,
                    "production deliberation: a denied reservation is cancelled and releases its hold");
            }

            // Host cancellation with a stale response delivered later is inert.
            {
                GameEngine engine = ProductionEngine();
                var host = new ProductionHost(engine, "seat-a");
                var fake = new FakeLlmTransport();
                LlmConfig config = ProductionConfig();
                string hashBefore = PlaygroundStateHash.Authoritative(engine);
                var deferred = fake.EnqueueDeferred();
                var runner = new LlmTurnRunner(fake, config, null);
                runner.Begin(host, host.Token, "seat-a", ProdSeed);
                runner.Tick();
                runner.Cancel("test cancellation");
                deferred.SetResult(LlmResponse.Success(
                    "{\"type\":\"levy_tax\",\"target_id\":\"city-a\"}", 0, 100, 20, "late"));
                runner.Tick();
                DeliberationBudgetSnapshot budget = runner.DeliberationBudget;
                log.Check(runner.State == RunnerState.Cancelled && host.SubmitCalls == 0
                          && host.EndTurnCalls == 0 && fake.CallCount == 1,
                    "production deliberation: a cancelled turn never executes its stale response");
                log.Check(string.Equals(hashBefore, PlaygroundStateHash.Authoritative(engine),
                        StringComparison.Ordinal)
                          && budget != null && budget.OutstandingReservationIds.Count == 0,
                    "production deliberation: cancellation settles the dispatched reservation once");
            }

            // Provider failure response is settled as a failure with no outstanding hold.
            {
                GameEngine engine = ProductionEngine();
                var host = new ProductionHost(engine, "seat-a");
                var fake = new FakeLlmTransport();
                LlmConfig config = ProductionConfig();
                fake.EnqueueError(LlmErrorKind.Timeout, "simulated provider timeout");
                var runner = new LlmTurnRunner(fake, config, null);
                runner.Begin(host, host.Token, "seat-a", ProdSeed);
                DrainProduction(runner);
                DeliberationBudgetSnapshot budget = runner.DeliberationBudget;
                log.Check(runner.EndReason == EndReason.ProviderError
                          && budget != null && budget.Session.Failures == 1
                          && budget.OutstandingReservationIds.Count == 0,
                    "production deliberation: a provider timeout settles exactly once and leaves no hold");
            }

            // Real request-timeout path: the transport never completes and the runner's own
            // deadline fires, settling the dispatched reservation as failed.
            {
                GameEngine engine = ProductionEngine();
                var host = new ProductionHost(engine, "seat-a");
                var fake = new FakeLlmTransport();
                LlmConfig config = ProductionConfig();
                config.RequestTimeoutMs = 1;
                fake.EnqueueDeferred();
                var runner = new LlmTurnRunner(fake, config, null);
                runner.Begin(host, host.Token, "seat-a", ProdSeed);
                System.Threading.Thread.Sleep(20);
                DrainProduction(runner);
                DeliberationBudgetSnapshot budget = runner.DeliberationBudget;
                log.Check(runner.EndReason == EndReason.RequestTimeout
                          && budget != null && budget.Session.Failures == 1
                          && budget.OutstandingReservationIds.Count == 0
                          && host.SubmitCalls == 0,
                    "production deliberation: the request deadline stops and settles without executing",
                    runner.EndReasonText);
            }
        }

        private static void ProductionDeliberationTwoPlayerAccounting(TestLog log)
        {
            GameEngine engine = ProductionEngine();
            var fake = new FakeLlmTransport();
            var memories = new InMemoryLlmMemoryStore();
            memories.Append(LlmMemoryExtractor.FromSummary(ProdGame, "seat-a", 1,
                "alpha-only-memory"));
            var notes = new MemoryLlmStrategicNoteStore();
            LlmConfig config = ProductionConfig();
            config.MaxRequestsPerTurn = 4;
            config.MaxTotalRequests = 4;
            config.MaxInquiriesPerTurn = 4;
            var runner = new LlmTurnRunner(fake, config, null, memories, notes);

            var hostA = new ProductionHost(engine, "seat-a");
            fake.EnqueueSuccess("{\"tool\":\"inspect_entity\",\"kind\":\"city\",\"id\":\"city-a\"}");
            fake.EnqueueSuccess("{\"note\":\"alpha private note\"}");
            fake.EnqueueSuccess("{\"type\":\"end_turn\"}");
            runner.Begin(hostA, hostA.Token, "seat-a", ProdSeed);
            DrainProduction(runner);
            log.Check(notes.Load(ProdGame, "seat-a") == "alpha private note",
                "two-player deliberation: seat-a's note is stored");

            var hostB = new ProductionHost(engine, "seat-b");
            fake.EnqueueSuccess("{\"tool\":\"manual_lookup\",\"topic\":\"recruit\"}");
            fake.EnqueueSuccess("{\"type\":\"end_turn\"}");
            runner.Begin(hostB, hostB.Token, "seat-b", ProdSeed);
            DrainProduction(runner);

            var bPrompts = new System.Text.StringBuilder();
            for (int i = 3; i < fake.Requests.Count; i++) bPrompts.Append(fake.Requests[i].User).Append('\n');
            log.Check(!bPrompts.ToString().Contains("alpha private note")
                      && !bPrompts.ToString().Contains("alpha-only-memory")
                      && !bPrompts.ToString().Contains("Wei Capital")
                      && notes.Load(ProdGame, "seat-b") == null,
                "two-player deliberation: notes, memories and inquiry results never cross seats");
            log.Check(runner.EndReason == EndReason.SessionRequestLimit && fake.CallCount == 4,
                "two-player deliberation: the session request budget is shared across seats",
                runner.EndReasonText);
            DeliberationBudgetSnapshot budget = runner.DeliberationBudget;
            int seatARequests = 0, seatBRequests = 0;
            foreach (DeliberationSeatTotals seat in budget.Seats)
            {
                if (seat.SeatId == "seat-a") seatARequests = (int)seat.Requests;
                if (seat.SeatId == "seat-b") seatBRequests = (int)seat.Requests;
            }
            log.Check(budget.Session.Requests == 4 && seatARequests == 3 && seatBRequests == 1,
                "two-player deliberation: per-seat evidence is separate while the session cap is shared",
                "a=" + seatARequests + " b=" + seatBRequests + " total=" + budget.Session.Requests);
        }

        private static void ProductionDeliberationFeatureOffIsLegacy(TestLog log)
        {
            GameEngine engine = ProductionEngine();
            var host = new ProductionHost(engine, "seat-a");
            var fake = new FakeLlmTransport();
            fake.EnqueueSuccess("{\"type\":\"end_turn\"}");
            var runner = new LlmTurnRunner(fake, new LlmConfig(), null);
            runner.Begin(host, host.Token, "seat-a", ProdSeed);
            DrainProduction(runner);
            log.Check(!runner.DeliberationActive && runner.DeliberationLedger == null
                      && fake.Requests[0].User.Contains("Prompt version: demo-slice-4")
                      && !fake.Requests[0].User.Contains("demo-slice-7")
                      && !fake.Requests[0].System.Contains("OPTIONAL DELIBERATION"),
                "feature off: legacy prompt/version and no deliberation ledger are used");
        }
    }
}
