using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LuanShi.Engine;

// [LS015-W3] Timeout / cancel / session-end / game-over / duplicate coverage for an accepted march.
//
// These tests use W3UncancellableTransport (see Harness.LlmMovementStale.Transport.cs): its deferred
// task deliberately ignores the runner's CancellationToken, so the late response really is delivered
// AFTER the turn has stopped. That is the case the old FakeLlmTransport could not exercise - it
// cancelled the deferred wait and the runner never saw a completable task.
//
// A late response must never be submitted, never counted and never mutate the engine. No network and
// no paid provider.

internal static partial class Harness
{
    private static void RunLlmMovementStale()
    {
        Section("[LS015-W3] march responses: timeout, cancel, reset, game over, duplicate");
        MovementLateAfterTimeout(true, 9511);
        MovementLateAfterTimeout(false, 9512);
        MovementLateAfterHostCancel(9513);
        MovementLateAfterReset(9514);
        MovementLateAfterGameOver(9515);
        MovementDuplicateAcceptedMarchExecutesOnce(9516);
    }

    private static string W3SeatLabel(bool sameSeat) => sameSeat ? "same seat" : "other seat";

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

    /// <summary>Bounded spin-wait: never an open-ended Thread.Sleep on an assumed timing.</summary>
    private static bool W3SpinUntil(Func<bool> done, int maxMs = 3000)
    {
        var clock = Stopwatch.StartNew();
        while (!done())
        {
            if (clock.ElapsedMilliseconds >= maxMs) return false;
            Thread.Yield();
        }
        return true;
    }

    /// <summary>Ticks until the runner reaches a terminal state, bounded by maxMs.</summary>
    private static bool W3RunUntilTerminal(LlmTurnRunner runner, int maxMs = 3000)
    {
        var clock = Stopwatch.StartNew();
        while (runner.State == RunnerState.Requesting || runner.State == RunnerState.Executing)
        {
            if (clock.ElapsedMilliseconds >= maxMs) return false;
            runner.Tick();
            Thread.Yield();
        }
        return true;
    }

    /// <summary>Completes the late task and returns true once it is observable as completed.</summary>
    private static bool W3Deliver(TaskCompletionSource<LlmResponse> task, LlmResponse response)
        => task.TrySetResult(response) && W3SpinUntil(() => task.Task.IsCompleted);

    // ---- (a) timeout -> new turn (same and other seat) -> late march ---------

    private static void MovementLateAfterTimeout(bool sameSeat, int seed)
    {
        string label = W3SeatLabel(sameSeat);
        var engine = MovementFixture(seed, 2, UnitTypes.Infantry);
        var hostA = new LlmPlanningHost(engine, "seat-a");
        var transport = new W3UncancellableTransport();
        var config = new LlmConfig
        {
            RequestTimeoutMs = 20, MaxTurnSeconds = 120,
            MaxRequestsPerTurn = 6, MaxActionsPerTurn = 4,
        };
        var runner = new LlmTurnRunner(transport, config, null);
        var old = transport.EnqueueDeferred();

        runner.Begin(hostA, hostA.Token, "seat-a", 7);
        W3Check(W3RunUntilTerminal(runner)
           && runner.State == RunnerState.Failed && runner.EndReason == EndReason.RequestTimeout
           && runner.FailureReason == "request timed out"
           && runner.EndReasonText == "Failed - request timed out"
           && hostA.EndTurnCalls == 1 && transport.CallCount == 1
           && runner.RequestsThisTurn == 1 && runner.ActionsThisTurn == 0
           && runner.InvalidThisTurn == 0 && runner.TotalRequests == 1,
            $"timeout ({label}): an elapsed RequestTimeoutMs fails the turn once with 'request timed out'");
        W3Check(!old.Task.IsCompleted && !old.Task.IsFaulted && !old.Task.IsCanceled,
            $"timeout ({label}): the transport ignores cancellation, so the old task is still pending");

        var army = engine.State.FindArmy("army-a-1");
        int orders = W3Orders(engine), moved = W3ArmyMovedCount(engine);

        // A genuinely new turn; its own request stays in flight (RequestTimeoutMs is off now).
        config.RequestTimeoutMs = 0;
        string newSeat = sameSeat ? "seat-a" : "seat-b";
        var hostNew = new LlmPlanningHost(engine, newSeat);
        var pending = transport.EnqueueDeferred();
        runner.Begin(hostNew, hostNew.Token, newSeat, 7);

        int requests = runner.RequestsThisTurn;
        int actions = runner.ActionsThisTurn;
        int invalid = runner.InvalidThisTurn;
        int activity = runner.Activity.Count;

        W3Check(W3Deliver(old, LlmResponse.Success(W2March("army-a-1", 2, 1), 0, 5, 2, "late-timeout")),
            $"timeout ({label}): the late march response is genuinely delivered after the new Begin");

        for (int i = 0; i < 5; i++) runner.Tick();

        W3Check(runner.State == RunnerState.Requesting && hostNew.SubmitCalls == 0
           && transport.CallCount == 2 && runner.RequestsThisTurn == requests
           && runner.ActionsThisTurn == actions && runner.InvalidThisTurn == invalid
           && runner.Activity.Count == activity,
            $"timeout ({label}): the late march is discarded and pollutes no counter or chat");
        W3Check(army.X == 1 && army.Z == 1 && W3Orders(engine) == orders
           && W3ArmyMovedCount(engine) == moved && !pending.Task.IsCompleted,
            $"timeout ({label}): no army, order or army_moved log changed and the new request stays in flight");
    }

    // ---- (b) host Cancel -> new turn -> late march --------------------------

    private static void MovementLateAfterHostCancel(int seed)
    {
        var engine = MovementFixture(seed, 2, UnitTypes.Infantry);
        var hostA = new LlmPlanningHost(engine, "seat-a");
        var transport = new W3UncancellableTransport();
        var runner = new LlmTurnRunner(transport,
            new LlmConfig { RequestTimeoutMs = 0, MaxTurnSeconds = 120 }, null);
        var old = transport.EnqueueDeferred();

        runner.Begin(hostA, hostA.Token, "seat-a", 8);
        runner.Cancel("host cancelled");

        W3Check(runner.State == RunnerState.Cancelled && runner.EndReason == EndReason.Cancelled
           && hostA.EndTurnCalls == 0 && W3ArmyMovedCount(engine) == 0
           && !old.Task.IsCompleted && !old.Task.IsFaulted && !old.Task.IsCanceled,
            "host Cancel ends the turn without EndTurn, executes nothing and leaves the ignored-cancel task pending");

        var hostB = new LlmPlanningHost(engine, "seat-b");
        var pending = transport.EnqueueDeferred();
        runner.Begin(hostB, hostB.Token, "seat-b", 8);
        int requests = runner.RequestsThisTurn;
        int actions = runner.ActionsThisTurn;
        int activity = runner.Activity.Count;

        W3Check(W3Deliver(old, LlmResponse.Success(W2March("army-a-1", 2, 1), 0, 5, 2, "late-cancel")),
            "Cancel: the late march response is genuinely delivered after the next Begin");
        for (int i = 0; i < 5; i++) runner.Tick();

        var army = engine.State.FindArmy("army-a-1");
        W3Check(runner.State == RunnerState.Requesting && hostB.SubmitCalls == 0
           && transport.CallCount == 2 && runner.RequestsThisTurn == requests
           && runner.ActionsThisTurn == actions && runner.Activity.Count == activity
           && !pending.Task.IsCompleted,
            "a late march after Cancel is discarded and pollutes no counter or chat");
        W3Check(army.X == 1 && army.Z == 1 && W3Orders(engine) == 5
           && W3ArmyMovedCount(engine) == 0,
            "the late march after Cancel changes no army position, order or log");
    }

    // ---- (c) ResetSession -> late march ------------------------------------

    private static void MovementLateAfterReset(int seed)
    {
        var engine = MovementFixture(seed, 2, UnitTypes.Infantry);
        var host = new LlmPlanningHost(engine, "seat-a");
        var transport = new W3UncancellableTransport();
        var runner = new LlmTurnRunner(transport,
            new LlmConfig { RequestTimeoutMs = 0, MaxTurnSeconds = 120 }, null);
        var old = transport.EnqueueDeferred();

        runner.Begin(host, host.Token, "seat-a", 9);
        runner.Cancel("session reset");
        runner.ResetSession();

        W3Check(runner.State == RunnerState.Idle && runner.RequestsThisTurn == 0
           && runner.ActionsThisTurn == 0 && !old.Task.IsCompleted,
            "ResetSession leaves an idle runner while the ignored-cancel task is still pending");

        W3Check(W3Deliver(old, LlmResponse.Success(W2March("army-a-1", 2, 1), 0, 5, 2, "late-reset")),
            "ResetSession: the late march response is genuinely delivered after the reset");
        runner.Tick();

        var army = engine.State.FindArmy("army-a-1");
        W3Check(runner.State == RunnerState.Idle && runner.RequestsThisTurn == 0
           && runner.ActionsThisTurn == 0 && host.SubmitCalls == 0
           && army.X == 1 && army.Z == 1 && W3Orders(engine) == 5
           && W3ArmyMovedCount(engine) == 0,
            "a late march after ResetSession is inert: no submit, no move, no counter");
    }

    // ---- (d) game over, in flight, then a new Begin ------------------------

    private static void MovementLateAfterGameOver(int seed)
    {
        var engine = MovementFixture(seed, 2, UnitTypes.Infantry);
        var host = new LlmPlanningHost(engine, "seat-a");
        var transport = new W3UncancellableTransport();
        var runner = new LlmTurnRunner(transport,
            new LlmConfig { RequestTimeoutMs = 0, MaxTurnSeconds = 120 }, null);
        var old = transport.EnqueueDeferred();

        runner.Begin(host, host.Token, "seat-a", 9);
        engine.State.WinnerSeatId = "seat-a";   // declared while the request is in flight
        W3Check(W3Deliver(old, LlmResponse.Success(W2March("army-a-1", 2, 1), 0, 5, 2, "late-over")),
            "game over: the in-flight response is genuinely delivered");
        runner.Tick();

        var army = engine.State.FindArmy("army-a-1");
        W3Check(runner.State == RunnerState.Finished && runner.EndReason == EndReason.GameOver
           && host.SubmitCalls == 0 && host.EndTurnCalls == 1
           && army.X == 1 && army.Z == 1 && W3Orders(engine) == 5
           && W3ArmyMovedCount(engine) == 0,
            "the in-flight march when the game ends is discarded, not executed");

        // A later duplicate delivery and repeated ticks are inert, and a decided game never dispatches.
        W3Check(!old.TrySetResult(LlmResponse.Success(W2March("army-a-1", 2, 1), 0, 5, 2, "late-over-2")),
            "a second delivery of the same game-over response is refused by the transport");
        transport.EnqueueDeferred();
        runner.Tick();
        runner.Tick();
        W3Check(runner.State == RunnerState.Finished && runner.EndReason == EndReason.GameOver
           && transport.CallCount == 1 && host.SubmitCalls == 0 && army.X == 1 && army.Z == 1
           && W3ArmyMovedCount(engine) == 0,
            "after game over a late/duplicate delivery and repeated ticks stay inert");

        // A NEW Begin after game over: the runner stops before dispatch. The host opened one engine
        // turn, so it is notified exactly once - not twice for the same Begin.
        var hostNew = new LlmPlanningHost(engine, "seat-a");
        transport.EnqueueDeferred();
        runner.Begin(hostNew, hostNew.Token, "seat-a", 9);
        W3Check(runner.State == RunnerState.Finished && runner.EndReason == EndReason.GameOver
           && hostNew.EndTurnCalls == 1 && host.EndTurnCalls == 1
           && hostNew.SubmitCalls == 0 && transport.CallCount == 1,
            "a Begin after game over dispatches nothing and calls EndTurn exactly once for that Begin");
    }

    // ---- (e) one logical march delivered through two different tasks --------

    private static void MovementDuplicateAcceptedMarchExecutesOnce(int seed)
    {
        var engine = MovementFixture(seed, 2, UnitTypes.Infantry);
        var host = new LlmPlanningHost(engine, "seat-a");
        var transport = new W3UncancellableTransport();
        var first = transport.EnqueueDeferred();
        var second = transport.EnqueueDeferred();
        var runner = new LlmTurnRunner(transport, new LlmConfig
        {
            RequestTimeoutMs = 0, MaxTurnSeconds = 120,
            MaxRequestsPerTurn = 6, MaxActionsPerTurn = 4, MaxInvalidPerTurn = 4,
        }, null);

        runner.Begin(host, host.Token, "seat-a", 10);
        var response = LlmResponse.Success(W2March("army-a-1", 2, 1), 0, 5, 2, "dup");
        W3Check(W3Deliver(first, response) && W3Deliver(second, response),
            "the controllable transport accepts the same march on two different tasks");

        runner.Tick();   // executes the march once, then requests the next decision (already delivered)
        runner.Tick();   // the duplicate response is re-validated but must not move the army again
        runner.Tick();

        var army = engine.State.FindArmy("army-a-1");
        W3Check(host.SubmitCalls == 2 && runner.ActionsThisTurn == 2
           && army.X == 2 && army.Z == 1 && W3Orders(engine) == 4
           && W3ArmyMovedCount(engine) == 1 && runner.InvalidThisTurn == 1
           && runner.RecentHistory.Contains("march army-a-1 -> rejected: already marched this season"),
            "one logical march delivered twice executes exactly once and spends one order");
        W3Check(transport.CallCount == 3,
            "the runner dispatches one fresh decision after the accepted march");
    }
}
