using System;
using System.Linq;
using LuanShi.Engine;

internal static partial class Harness
{
    private static void RunLlmRepairAndLimits()
    {
        Section("LLM runner: repair, rejection and every bound [DEMO-B2]");

        var engine = LlmEngine();
        var host = new LlmHarnessHost(engine, "seat-a");
        var fake = new FakeLlmTransport();
        var runner = new LlmTurnRunner(fake, new LlmConfig(), null);
        fake.EnqueueSuccess("this is not JSON");
        fake.EnqueueSuccess("{\"type\":\"end_turn\"}");
        runner.Begin(host, 1, "seat-a", 5);
        LlmDrain(runner);
        Check(runner.State == RunnerState.Finished && runner.InvalidThisTurn == 1
              && runner.RequestsThisTurn == 2 && host.SubmitCalls == 0
              && host.EndTurnCalls == 1
              && runner.RecentHistory.Any(h => h.Contains("invalid model response")),
            "malformed output gets one repair retry, then end_turn finishes");

        engine = LlmEngine();
        host = new LlmHarnessHost(engine, "seat-a");
        fake = new FakeLlmTransport();
        runner = new LlmTurnRunner(fake, new LlmConfig(), null);
        fake.EnqueueSuccess("bad one");
        fake.EnqueueSuccess("bad two");
        runner.Begin(host, 1, "seat-a", 5);
        LlmDrain(runner);
        Check(runner.State == RunnerState.Finished && runner.InvalidThisTurn == 2
              && runner.TotalRequests == 2 && host.EndTurnCalls == 1
              && runner.FailureReason != null && runner.FailureReason.Contains("invalid-response"),
            "malformed twice stops at the invalid-response limit");

        engine = LlmEngine();
        host = new LlmHarnessHost(engine, "seat-a");
        fake = new FakeLlmTransport();
        var config = new LlmConfig { MaxActionsPerTurn = 5, MaxInvalidPerTurn = 2 };
        runner = new LlmTurnRunner(fake, config, null);
        fake.EnqueueSuccess("{\"type\":\"reclaim\",\"target_id\":\"city-a\"}");
        fake.EnqueueSuccess("{\"type\":\"reclaim\",\"target_id\":\"city-a\"}");
        fake.EnqueueSuccess("{\"type\":\"end_turn\"}");
        runner.Begin(host, 1, "seat-a", 5);
        LlmDrain(runner);
        Check(runner.State == RunnerState.Finished && runner.ActionsThisTurn == 2
              && runner.InvalidThisTurn == 1 && host.EndTurnCalls == 1
              && engine.State.Cities[0].ReclaimPct == BalanceConfig.ReclaimPctPerUse
              && runner.RecentHistory.Any(h => h.Contains("rejected")),
            "engine rejection is recorded as feedback and counted as invalid");

        engine = LlmEngine();
        host = new LlmHarnessHost(engine, "seat-a");
        fake = new FakeLlmTransport();
        runner = new LlmTurnRunner(fake, new LlmConfig { MaxRequestsPerTurn = 1 }, null);
        fake.EnqueueSuccess("{\"type\":\"reclaim\",\"target_id\":\"city-a\"}");
        fake.EnqueueSuccess("{\"type\":\"end_turn\"}");
        runner.Begin(host, 1, "seat-a", 5);
        LlmDrain(runner);
        Check(runner.State == RunnerState.Finished && runner.TotalRequests == 1
              && fake.CallCount == 1 && host.EndTurnCalls == 1
              && runner.FailureReason.Contains("request limit"),
            "request limit terminates before another provider call");

        engine = LlmEngine();
        host = new LlmHarnessHost(engine, "seat-a");
        fake = new FakeLlmTransport();
        runner = new LlmTurnRunner(fake,
            new LlmConfig { MaxRequestsPerTurn = 5, MaxActionsPerTurn = 1 }, null);
        fake.EnqueueSuccess("{\"type\":\"reclaim\",\"target_id\":\"city-a\"}");
        fake.EnqueueSuccess("{\"type\":\"end_turn\"}");
        runner.Begin(host, 1, "seat-a", 5);
        LlmDrain(runner);
        Check(runner.State == RunnerState.Finished && runner.TotalRequests == 1
              && runner.ActionsThisTurn == 1 && host.EndTurnCalls == 1
              && runner.FailureReason.Contains("action limit"),
            "action limit terminates the turn");

        engine = LlmEngine();
        host = new LlmHarnessHost(engine, "seat-a");
        fake = new FakeLlmTransport();
        runner = new LlmTurnRunner(fake, new LlmConfig { MaxTotalRequests = 1 }, null);
        fake.EnqueueSuccess("{\"type\":\"end_turn\"}");
        runner.Begin(host, 1, "seat-a", 5);
        LlmDrain(runner);
        runner.Begin(host, 2, "seat-a", 5);
        Check(runner.State == RunnerState.Finished && runner.TotalRequests == 1
              && fake.CallCount == 1 && host.EndTurnCalls == 2
              && runner.FailureReason.Contains("session request limit"),
            "session request total stops a second turn before dispatch");

        engine = LlmEngine();
        host = new LlmHarnessHost(engine, "seat-a");
        fake = new FakeLlmTransport();
        runner = new LlmTurnRunner(fake, new LlmConfig { MaxTurnSeconds = 0 }, null);
        runner.Begin(host, 1, "seat-a", 5);
        Check(runner.State == RunnerState.Finished && fake.CallCount == 0
              && host.EndTurnCalls == 1 && runner.FailureReason.Contains("time limit"),
            "turn-time limit terminates without a provider call");

        engine = LlmEngine();
        for (int i = 0; i < 100; i++)
            engine.State.Cities.Add(new CityState
            {
                Id = "city-pad-" + i, Name = new string('N', 500), X = 0, Z = 0,
                OwnerSeatId = "seat-b", Population = 1000, Food = 0, Gold = 0, Garrison = 0,
            });
        host = new LlmHarnessHost(engine, "seat-a");
        fake = new FakeLlmTransport();
        runner = new LlmTurnRunner(fake, new LlmConfig(), null);
        runner.Begin(host, 1, "seat-a", 5);
        Check(runner.State == RunnerState.Failed && runner.FailureReason == "prompt too large"
              && fake.CallCount == 0 && host.EndTurnCalls == 1,
            "oversize system+user prompt is rejected as Failed 'prompt too large'");
    }

    private static void RunLlmProviderErrors()
    {
        Section("LLM runner: provider failures end the turn once [DEMO-B2]");

        LlmErrorKind[] kinds =
        {
            LlmErrorKind.Timeout, LlmErrorKind.RateLimited,
            LlmErrorKind.Server, LlmErrorKind.Auth,
        };
        foreach (LlmErrorKind kind in kinds)
        {
            var engine = LlmEngine();
            var host = new LlmHarnessHost(engine, "seat-a");
            var fake = new FakeLlmTransport();
            var runner = new LlmTurnRunner(fake, LlmKeyedConfig(), null);
            fake.EnqueueError(kind, "detail " + LlmFakeKey);
            runner.Begin(host, 1, "seat-a", 2);
            LlmDrain(runner);
            Check(runner.State == RunnerState.Failed && host.EndTurnCalls == 1
                  && runner.TotalRequests == 1 && host.SubmitCalls == 0
                  && runner.FailureReason.Contains("provider")
                  && !runner.LastResult.Contains(LlmFakeKey)
                  && !runner.LastResult.Contains("body-echo"),
                kind + " => Failed, EndTurn once, safe detail");
        }
    }

    private static void RunLlmCancelAndLateResponse()
    {
        Section("LLM runner: cancellation and stale responses are inert [DEMO-B2]");

        var engine = LlmEngine();
        var host = new LlmHarnessHost(engine, "seat-a");
        var fake = new FakeLlmTransport();
        var runner = new LlmTurnRunner(fake, new LlmConfig(), null);
        var old = fake.EnqueueDeferred();
        runner.Begin(host, 1, "seat-a", 2);
        bool secondBeginThrew = false;
        try { runner.Begin(host, 2, "seat-a", 2); }
        catch (InvalidOperationException) { secondBeginThrew = true; }
        Check(secondBeginThrew, "second Begin while requesting throws");

        runner.Cancel("handover");
        Check(runner.State == RunnerState.Cancelled && host.EndTurnCalls == 0,
            "host cancellation does not call EndTurn");

        fake.EnqueueSuccess("{\"type\":\"end_turn\"}");
        runner.Begin(host, 2, "seat-a", 2);
        LlmDrain(runner);
        Check(runner.State == RunnerState.Finished && host.SubmitCalls == 0
              && host.EndTurnCalls == 1,
            "a new turn can start after cancellation");

        old.TrySetResult(LlmResponse.Success(
            "{\"type\":\"reclaim\",\"target_id\":\"city-a\"}", 1, 1, 1, "late"));
        runner.Tick();
        Check(host.SubmitCalls == 0 && engine.State.Cities[0].ReclaimPct == 0
              && host.EndTurnCalls == 1 && runner.State == RunnerState.Finished,
            "late response after Cancel/Finish is discarded without execution");
    }

}
