using System;
using System.Collections.Generic;
using LuanShi.Engine;

// [LS015-W3] Timeout / cancel / session-end / duplicate coverage for an accepted march.
//
// A late response must never be submitted, never counted and never mutate the engine: the runner's
// generation counter makes stale work inert. All fixtures use the controllable FakeLlmTransport;
// no network and no paid provider.

internal static partial class Harness
{
    private static void RunLlmMovementStale()
    {
        Section("[LS015-W3] march responses: timeout, cancel, reset, game over, duplicate");
        MovementLateAfterTimeout();
        MovementLateAfterHostCancel();
        MovementLateAfterReset();
        MovementLateAfterGameOver();
        MovementDuplicateAcceptedMarchExecutesOnce();
    }

    private static int W3ArmyMovedCount(GameEngine engine)
    {
        int count = 0;
        if (engine == null || engine.Log == null) return 0;
        foreach (EventRecord record in engine.Log.Records)
            if (record != null && record.EventType == "army_moved") count++;
        return count;
    }

    private static int W3Orders(GameEngine engine)
        => engine.State.FindFaction("seat-a").CommandPoints;

    private static void MovementLateAfterTimeout()
    {
        var engine = MovementFixture(9501, 2, UnitTypes.Infantry);
        var hostA = new LlmPlanningHost(engine, "seat-a");
        var fake = new FakeLlmTransport();
        var config = new LlmConfig
        {
            RequestTimeoutMs = 20, MaxTurnSeconds = 120,
            MaxRequestsPerTurn = 6, MaxActionsPerTurn = 4,
        };
        var runner = new LlmTurnRunner(fake, config, null);
        var old = fake.EnqueueDeferred();

        runner.Begin(hostA, hostA.Token, "seat-a", 7);
        System.Threading.Thread.Sleep(60);
        runner.Tick();

        var army = engine.State.FindArmy("army-a-1");
        int orders = W3Orders(engine);
        W3Check(runner.State == RunnerState.Failed && runner.EndReason == EndReason.RequestTimeout
           && runner.FailureReason == "request timed out"
           && runner.EndReasonText == "Failed - request timed out"
           && hostA.EndTurnCalls == 1 && fake.CallCount == 1
           && runner.RequestsThisTurn == 1 && runner.ActionsThisTurn == 0
           && runner.InvalidThisTurn == 0 && runner.TotalRequests == 1,
            "an elapsed RequestTimeoutMs fails the turn once with 'request timed out'");
        W3Check(army.X == 1 && army.Z == 1 && orders == 5 && W3ArmyMovedCount(engine) == 0,
            "the timeout itself leaves the army, orders and army_moved log unchanged");

        // A genuinely new turn, for the other seat, with its own request still in flight.
        config.RequestTimeoutMs = 0;
        var hostB = new LlmPlanningHost(engine, "seat-b");
        fake.EnqueueDeferred();
        runner.Begin(hostB, hostB.Token, "seat-b", 7);

        int requests = runner.RequestsThisTurn;
        int actions = runner.ActionsThisTurn;
        int invalid = runner.InvalidThisTurn;
        int activity = runner.Activity.Count;

        old.TrySetResult(LlmResponse.Success(W2March("army-a-1", 2, 1), 0, 5, 2, "late-timeout"));
        System.Threading.Thread.Sleep(20);
        runner.Tick();
        runner.Tick();

        W3Check(runner.State == RunnerState.Requesting && hostB.SubmitCalls == 0
           && fake.CallCount == 2 && runner.RequestsThisTurn == requests
           && runner.ActionsThisTurn == actions && runner.InvalidThisTurn == invalid
           && runner.Activity.Count == activity,
            "a late march after a timeout is discarded and pollutes no counters or chat");
        W3Check(army.X == 1 && army.Z == 1 && W3Orders(engine) == orders
           && W3ArmyMovedCount(engine) == 0,
            "the late march after a timeout moves no army, spends no order and logs no army_moved");
    }

    private static void MovementLateAfterHostCancel()
    {
        var engine = MovementFixture(9502, 2, UnitTypes.Infantry);
        var hostA = new LlmPlanningHost(engine, "seat-a");
        var fake = new FakeLlmTransport();
        var runner = new LlmTurnRunner(fake,
            new LlmConfig { RequestTimeoutMs = 0, MaxTurnSeconds = 120 }, null);
        var old = fake.EnqueueDeferred();

        runner.Begin(hostA, hostA.Token, "seat-a", 8);
        runner.Cancel("host cancelled");

        W3Check(runner.State == RunnerState.Cancelled && runner.EndReason == EndReason.Cancelled
           && hostA.EndTurnCalls == 0 && W3ArmyMovedCount(engine) == 0,
            "host Cancel ends the turn without EndTurn and executes nothing");

        var hostB = new LlmPlanningHost(engine, "seat-b");
        fake.EnqueueDeferred();
        runner.Begin(hostB, hostB.Token, "seat-b", 8);
        int activity = runner.Activity.Count;

        old.TrySetResult(LlmResponse.Success(W2March("army-a-1", 2, 1), 0, 5, 2, "late-cancel"));
        System.Threading.Thread.Sleep(20);
        runner.Tick();
        runner.Tick();

        var army = engine.State.FindArmy("army-a-1");
        W3Check(runner.State == RunnerState.Requesting && hostB.SubmitCalls == 0
           && fake.CallCount == 2 && runner.RequestsThisTurn == 1
           && runner.ActionsThisTurn == 0 && runner.InvalidThisTurn == 0
           && runner.Activity.Count == activity,
            "a late march after Cancel is discarded and pollutes no counters or chat");
        W3Check(army.X == 1 && army.Z == 1 && W3Orders(engine) == 5
           && W3ArmyMovedCount(engine) == 0,
            "the late march after Cancel changes no army position, order or log");
    }

    private static void MovementLateAfterReset()
    {
        var engine = MovementFixture(9503, 2, UnitTypes.Infantry);
        var host = new LlmPlanningHost(engine, "seat-a");
        var fake = new FakeLlmTransport();
        var runner = new LlmTurnRunner(fake,
            new LlmConfig { RequestTimeoutMs = 0, MaxTurnSeconds = 120 }, null);
        var old = fake.EnqueueDeferred();

        runner.Begin(host, host.Token, "seat-a", 9);
        runner.Cancel("session reset");
        runner.ResetSession();

        old.TrySetResult(LlmResponse.Success(W2March("army-a-1", 2, 1), 0, 5, 2, "late-reset"));
        System.Threading.Thread.Sleep(20);
        runner.Tick();

        var army = engine.State.FindArmy("army-a-1");
        W3Check(runner.State == RunnerState.Idle && runner.RequestsThisTurn == 0
           && runner.ActionsThisTurn == 0 && host.SubmitCalls == 0
           && army.X == 1 && army.Z == 1 && W3Orders(engine) == 5
           && W3ArmyMovedCount(engine) == 0,
            "a late march after ResetSession is inert: no submit, no move, no counter");
    }

    private static void MovementLateAfterGameOver()
    {
        var engine = MovementFixture(9504, 2, UnitTypes.Infantry);
        var host = new LlmPlanningHost(engine, "seat-a");
        var fake = new FakeLlmTransport();
        var runner = new LlmTurnRunner(fake,
            new LlmConfig { RequestTimeoutMs = 0, MaxTurnSeconds = 120 }, null);
        var old = fake.EnqueueDeferred();

        runner.Begin(host, host.Token, "seat-a", 9);
        engine.State.WinnerSeatId = "seat-a";   // declared while the request is in flight
        old.TrySetResult(LlmResponse.Success(W2March("army-a-1", 2, 1), 0, 5, 2, "late-over"));
        System.Threading.Thread.Sleep(20);
        runner.Tick();

        var army = engine.State.FindArmy("army-a-1");
        W3Check(runner.State == RunnerState.Finished && runner.EndReason == EndReason.GameOver
           && host.SubmitCalls == 0 && host.EndTurnCalls == 1
           && army.X == 1 && army.Z == 1 && W3Orders(engine) == 5
           && W3ArmyMovedCount(engine) == 0,
            "the in-flight march when the game ends is discarded, not executed");

        // A later duplicate delivery and repeated ticks are inert, and a decided game never dispatches.
        old.TrySetResult(LlmResponse.Success(W2March("army-a-1", 2, 1), 0, 5, 2, "late-over-2"));
        fake.EnqueueDeferred();
        runner.Tick();
        runner.Tick();
        runner.Begin(host, host.Token, "seat-a", 9);
        W3Check(runner.State == RunnerState.Finished && runner.EndReason == EndReason.GameOver
           && fake.CallCount == 1 && army.X == 1 && army.Z == 1
           && W3ArmyMovedCount(engine) == 0,
            "after game over a late/duplicate delivery and a new Begin stay inert");
    }

    private static void MovementDuplicateAcceptedMarchExecutesOnce()
    {
        var engine = MovementFixture(9505, 2, UnitTypes.Infantry);
        var host = new LlmPlanningHost(engine, "seat-a");
        var fake = new FakeLlmTransport();
        var deferred = fake.EnqueueDeferred();
        fake.EnqueueSuccess(W2EndTurn());   // the post-march decision
        var runner = new LlmTurnRunner(fake, new LlmConfig
        {
            RequestTimeoutMs = 0, MaxTurnSeconds = 120,
            MaxRequestsPerTurn = 6, MaxActionsPerTurn = 4,
        }, null);

        runner.Begin(host, host.Token, "seat-a", 10);
        var response = LlmResponse.Success(W2March("army-a-1", 2, 1), 0, 5, 2, "dup");
        W3Check(deferred.TrySetResult(response), "the controllable transport accepts the response");
        System.Threading.Thread.Sleep(20);
        runner.Tick();   // executes the march once, then requests the next decision
        W3Check(!deferred.TrySetResult(response),
            "a second delivery of the same response is refused by the transport");
        runner.Tick();   // end_turn
        runner.Tick();
        runner.Tick();

        var army = engine.State.FindArmy("army-a-1");
        W3Check(host.SubmitCalls == 1 && runner.ActionsThisTurn == 1
           && army.X == 2 && army.Z == 1 && W3Orders(engine) == 4
           && W3ArmyMovedCount(engine) == 1 && fake.CallCount == 2
           && runner.State == RunnerState.Finished,
            "one accepted march delivered twice executes once and spends one order");
    }
}
