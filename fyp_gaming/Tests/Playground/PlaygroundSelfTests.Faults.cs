using System;
using LuanShi.Engine;

namespace LuanShi.Playground
{
    internal static partial class PlaygroundSelfTests
    {
        private static void MalformedJsonExecutesNothing(TestLog log)
        {
            PlaygroundConfig config = FaultConfig();
            var fake = new FakeLlmTransport();
            fake.EnqueueSuccess("{ this is not json", 0, 1200, 60);
            fake.EnqueueSuccess(EndTurnJson("seat-b done"), 0, 1200, 60);

            PlaygroundRunRecord record = RunSession(config, fake);
            AssertFaultTurn(log, record, "malformed JSON",
                "invalid_limit", "invalid_json");
        }

        private static void EmptyResponseExecutesNothing(TestLog log)
        {
            PlaygroundConfig config = FaultConfig();
            var fake = new FakeLlmTransport();
            fake.EnqueueSuccess("", 0, 1200, 60);
            fake.EnqueueSuccess(EndTurnJson("seat-b done"), 0, 1200, 60);

            PlaygroundRunRecord record = RunSession(config, fake);
            AssertFaultTurn(log, record, "empty response",
                "invalid_limit", "empty_response");
        }

        private static void TimeoutExecutesNothing(TestLog log)
        {
            PlaygroundConfig config = FaultConfig();
            var fake = new FakeLlmTransport();
            fake.EnqueueError(LlmErrorKind.Timeout, "simulated timeout");
            fake.EnqueueSuccess(EndTurnJson("seat-b done"), 0, 1200, 60);

            PlaygroundRunRecord record = RunSession(config, fake);
            AssertFaultTurn(log, record, "timeout",
                "timeout", "provider_error:timeout");
        }

        private static void ExhaustedInquiryBudgetExecutesNothing(TestLog log)
        {
            PlaygroundConfig config = FaultConfig();
            config.MaxInquiryPerTurn = 1;
            config.MaxRequestsPerTurn = 5;
            config.MaxInvalidPerTurn = 3;
            var fake = new FakeLlmTransport();
            fake.EnqueueSuccess("{\"tool\":\"manual_lookup\",\"topic\":\"recruit\"}", 0, 1200, 60);
            fake.EnqueueSuccess("{\"tool\":\"manual_lookup\",\"topic\":\"march\"}", 0, 1200, 60);
            fake.EnqueueSuccess(EndTurnJson("seat-b done"), 0, 1200, 60);

            PlaygroundRunRecord record = RunSession(config, fake);
            PlaygroundTurnRecord turn = record.FindTurn("seat-a", 1);
            log.Check(turn != null && turn.EndReason.StartsWith("inquiry_budget", StringComparison.Ordinal),
                "exhausted inquiry budget stops the turn with a bounded reason",
                turn == null ? "no turn" : turn.EndReason);
            log.Check(NoExecutedActions(record) && SameTurnHashes(turn),
                "exhausted inquiry budget executes no action and changes no state");
            log.Check(record.Inquiries.Count == 2 && record.Inquiries[1].Ok == false
                && record.Inquiries[1].ErrorCode != null,
                "the denied inquiry is visible in results");
        }

        private static void ExhaustedRequestBudgetExecutesNothing(TestLog log)
        {
            PlaygroundConfig config = FaultConfig();
            config.MaxRequestsPerTurn = 1;
            config.MaxInquiryPerTurn = 4;
            config.MaxInvalidPerTurn = 3;
            var fake = new FakeLlmTransport();
            fake.EnqueueSuccess("{\"tool\":\"manual_lookup\",\"topic\":\"recruit\"}", 0, 1200, 60);
            fake.EnqueueSuccess(EndTurnJson("seat-b done"), 0, 1200, 60);

            PlaygroundRunRecord record = RunSession(config, fake);
            PlaygroundTurnRecord turn = record.FindTurn("seat-a", 1);
            log.Check(turn != null && turn.EndReason == "request_limit",
                "an exhausted per-turn request budget stops before another dispatch",
                turn == null ? "no turn" : turn.EndReason);
            log.Check(NoExecutedActions(record) && SameTurnHashes(turn),
                "an inquiry-only turn that hits the request limit executes no action");
        }

        private static void ExhaustedWallTimeBudgetExecutesNothing(TestLog log)
        {
            PlaygroundConfig config = FaultConfig();
            config.MaxWallMillisPerTurn = 150;
            config.SimulatedRequestMs = 100;
            config.MaxRequestsPerTurn = 5;
            config.MaxInvalidPerTurn = 3;
            var fake = new FakeLlmTransport();
            fake.EnqueueSuccess("{\"tool\":\"manual_lookup\",\"topic\":\"recruit\"}", 0, 1200, 60);
            fake.EnqueueSuccess("{\"note\":\"keep the reserve\"}", 0, 1200, 60);
            fake.EnqueueSuccess(EndTurnJson("seat-b done"), 0, 1200, 60);

            PlaygroundRunRecord record = RunSession(config, fake);
            PlaygroundTurnRecord turn = record.FindTurn("seat-a", 1);
            log.Check(turn != null && turn.EndReason.StartsWith("request_budget", StringComparison.Ordinal)
                && turn.EndReason.Contains("turn_wall_time_exhausted"),
                "an exhausted per-turn wall-time budget is visible and bounded",
                turn == null ? "no turn" : turn.EndReason);
            log.Check(NoExecutedActions(record) && SameTurnHashes(turn),
                "wall-time exhaustion executes no action and changes no state");
        }

        private static void AssertFaultTurn(TestLog log, PlaygroundRunRecord record,
            string label, string expectedEndReason, string expectedOutcome)
        {
            PlaygroundTurnRecord turn = record.FindTurn("seat-a", 1);
            PlaygroundRequestRecord request = null;
            foreach (PlaygroundRequestRecord candidate in record.Requests)
                if (candidate.SeatId == "seat-a") { request = candidate; break; }

            log.Check(turn != null && turn.EndReason == expectedEndReason,
                label + " stops with the bounded turn reason " + expectedEndReason,
                turn == null ? "no turn" : turn.EndReason);
            log.Check(request != null && request.Outcome == expectedOutcome,
                label + " records the bounded outcome " + expectedOutcome,
                request == null ? "no request" : request.Outcome);
            log.Check(NoExecutedActions(record), label + " executes no action");
            log.Check(SameTurnHashes(turn), label + " leaves the authoritative state unchanged");
        }

        private static bool NoExecutedActions(PlaygroundRunRecord record)
        {
            foreach (PlaygroundActionRecord action in record.Actions)
                if (action.Executed) return false;
            return true;
        }

        private static bool SameTurnHashes(PlaygroundTurnRecord turn)
            => turn != null && string.Equals(turn.StateHashBefore, turn.StateHashAfter,
                StringComparison.Ordinal);
    }
}
