using System;
using System.IO;
using System.Linq;
using LuanShi.Engine;

// [LS015-W2] Contract B: authoritative terminal stop.
//
// A winner is checked in Begin, before dispatch and right after each executed command. Once true,
// the runner finishes with EndReason.GameOver and issues ZERO further SendAsync calls; an in-flight
// response is discarded, never executed. The spectator session completes without starting the next
// seat, and its summary reports game over distinctly.

internal static partial class Harness
{
    private static void RunLlmTerminal()
    {
        Section("[LS015-W2] terminal stop: no request after game over");
        TerminalStopInBegin();
        TerminalStopWinningCapture();
        TerminalStopInFlightDiscard();
        TerminalStopCancellation();
        TerminalStopSpectatorCompletes();
    }

    private static void TerminalStopInBegin()
    {
        var engine = W2VictoryWorld(9311);
        engine.State.WinnerSeatId = "seat-a";
        var host = new LlmPlanningHost(engine, "seat-a");
        var fake = new FakeLlmTransport();
        var runner = new LlmTurnRunner(fake, new LlmConfig(), null);

        runner.Begin(host, host.Token, "seat-a", 4);

        W2Check(runner.State == RunnerState.Finished
           && runner.EndReason == EndReason.GameOver
           && runner.EndReasonText == "Finished - game over"
           && fake.CallCount == 0 && host.EndTurnCalls == 1,
            "Begin on a decided game finishes GameOver with zero provider calls");
        W2Check(runner.Activity.Any(e => e.Kind == "finished"
                  && e.Text == "Finished - game over"),
            "the chat trace records the distinct game-over end reason");
    }

    private static void TerminalStopWinningCapture()
    {
        var engine = W2VictoryWorld(9312);
        var host = new LlmPlanningHost(engine, "seat-a");
        var fake = new FakeLlmTransport();
        var runner = new LlmTurnRunner(fake,
            new LlmConfig { MaxRequestsPerTurn = 8, MaxActionsPerTurn = 5 }, null);
        fake.EnqueueSuccess(W2March("army-a-1", 2, 1));   // capture => unification victory
        fake.EnqueueSuccess(W2EndTurn());                 // must never be requested
        fake.EnqueueSuccess(W2March("army-a-1", 0, 1));   // nor this

        runner.Begin(host, host.Token, "seat-a", 4);
        LlmDrain(runner);

        W2Check(string.Equals(engine.State.WinnerSeatId, "seat-a", StringComparison.Ordinal)
           && runner.EndReason == EndReason.GameOver
           && runner.EndReasonText == "Finished - game over",
            "the game-winning capture sets the winner and stops with GameOver");
        W2Check(fake.CallCount == 1 && host.SubmitCalls == 1 && host.EndTurnCalls == 1,
            "a game-winning order sends NO further provider request");
        W2Check(runner.Activity.Any(e => e.Kind == "result"
                  && e.Text.Contains("Captured Neutral N")),
            "the winning capture is reported once from the seat observation");
    }

    private static void TerminalStopInFlightDiscard()
    {
        var engine = MovementFixture(9313, 2, UnitTypes.Infantry);
        var host = new LlmPlanningHost(engine, "seat-a");
        var fake = new FakeLlmTransport();
        var deferred = fake.EnqueueDeferred();
        var runner = new LlmTurnRunner(fake,
            new LlmConfig { MaxRequestsPerTurn = 6, MaxActionsPerTurn = 4 }, null);

        runner.Begin(host, host.Token, "seat-a", 4);
        engine.State.WinnerSeatId = "seat-a";   // declared while the request is in flight
        deferred.SetResult(LlmResponse.Success(W2March("army-a-1", 2, 1), 0, 4, 2, "late"));
        System.Threading.Thread.Sleep(20);
        runner.Tick();

        var army = engine.State.FindArmy("army-a-1");
        W2Check(runner.State == RunnerState.Finished
           && runner.EndReason == EndReason.GameOver
           && host.SubmitCalls == 0 && army.X == 1 && army.Z == 1,
            "a response arriving after game over is discarded, not executed");
        W2Check(fake.CallCount == 1 && runner.Activity.Any(e => e.Kind == "validation"
                  && e.Text.Contains("discarded")),
            "the discarded response costs no new request and is visible in chat");
    }

    private static void TerminalStopCancellation()
    {
        var engine = MovementFixture(9314, 2, UnitTypes.Infantry);
        var host = new LlmPlanningHost(engine, "seat-a");
        var fake = new FakeLlmTransport();
        var deferred = fake.EnqueueDeferred();
        var runner = new LlmTurnRunner(fake, new LlmConfig(), null);

        runner.Begin(host, host.Token, "seat-a", 4);
        runner.Cancel("user cancelled");
        deferred.SetResult(LlmResponse.Success(W2March("army-a-1", 2, 1), 0, 4, 2, "late"));
        System.Threading.Thread.Sleep(20);
        runner.Tick();

        var army = engine.State.FindArmy("army-a-1");
        W2Check(runner.State == RunnerState.Cancelled
           && runner.EndReason == EndReason.Cancelled
           && host.SubmitCalls == 0 && army.X == 1 && army.Z == 1
           && fake.CallCount == 1 && host.EndTurnCalls == 0,
            "host cancellation discards the late march and leaves the turn with the host");
    }

    private static void TerminalStopSpectatorCompletes()
    {
        string dir = LlmTempDir("ls015-w2-terminal");
        try
        {
            var engine = W2VictoryWorld(9315);
            var fake = new FakeLlmTransport();
            fake.EnqueueSuccess(W2March("army-a-1", 2, 1));
            fake.EnqueueSuccess(W2EndTurn());   // seat-b must never be started
            var runner = new LlmTurnRunner(fake,
                new LlmConfig { MaxRequestsPerTurn = 8, MaxActionsPerTurn = 5 }, null);
            var session = new SpectatorSession(engine, runner, 2, 4242);

            session.Start();
            SpectatorDrain(session);

            W2Check(session.State == SpectatorState.Completed
               && session.StopReason == "game_over"
               && fake.CallCount == 1
               && session.TurnRecords.Count == 1
               && session.TurnRecords[0].EndReason == EndReason.GameOver
               && session.TurnRecords[0].EndReasonText == "Finished - game over"
               && session.TurnRecords[0].EndedBy == "game over"
               && session.GameOverEndings == 1,
                "the spectator treats a winner as Completed and never starts the next seat");
            W2Check(session.ActiveSeatId == null && runner.TotalRequests == 1,
                "the decided session leaves no active seat and one provider call");

            var config = new LlmConfig();
            string path = SpectatorSummary.Write(dir, session, config, fake, runner);
            bool parsed = StrictJson.TryParse(File.ReadAllText(path), out JsonNode root, out _);
            var obj = root as JsonObject;
            JsonNode turns = obj == null ? null : LlmNode(obj, "turns");
            JsonNode totals = obj == null ? null : LlmNode(obj, "totals");
            var turn = turns is JsonArray array && array.Count == 1
                ? array.Items[0] as JsonObject : null;
            W2Check(parsed && LlmStr(obj, "stop_reason") == "game_over"
               && totals is JsonObject totalsObj && LlmInt(totalsObj, "game_over_endings") == 1
               && turn != null && LlmStr(turn, "end_reason") == "GameOver"
               && LlmStr(turn, "end_reason_text") == "Finished - game over",
                "the summary JSON reports game over distinctly");
        }
        finally { LlmDelete(dir); }
    }
}
