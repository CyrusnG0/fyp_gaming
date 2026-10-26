using System;
using System.Linq;
using System.Collections.Generic;
using LuanShi.Engine;

internal static partial class Harness
{
    private static void RunLlmActivity()
    {
        Section("LLM session chat: ordered, bounded, redacted, seat-private [CHAT]");
        var engine = LlmEngine();
        var fake = new FakeLlmTransport();
        var config = LlmKeyedConfig();
        config.MaxRequestsPerTurn = 100;
        config.MaxActionsPerTurn = 100;
        config.MaxTotalRequests = 200;
        config.MaxTurnSeconds = 30;
        var runner = new LlmTurnRunner(fake, config, null);
        var host = new LlmHarnessHost(engine, "seat-a");
        fake.EnqueueSuccess("{\"type\":\"train\",\"target_id\":\"city-a\",\"summary\":\"I will train to strengthen my garrison.\"}");
        fake.EnqueueSuccess("{\"type\":\"end_turn\"}");
        runner.Begin(host, host.Token, "seat-a", 99);
        LlmDrain(runner);
        var entries = runner.Activity;
        Check(entries.Any(e => e.Kind == "request") && entries.Any(e => e.Kind == "finished"),
            "chat includes request progress and turn termination");
        Check(entries.Any(e => e.Kind == "decision" && e.Text.StartsWith("I will train")),
            "chat retains the model-provided decision summary");
        var decision = entries.First(e => e.Kind == "decision");
        var command = entries.First(e => e.Kind == "command");
        var result = entries.First(e => e.Kind == "result");
        Check(decision.Sequence < command.Sequence && command.Sequence < result.Sequence,
            "decision, submitted command and engine result have ordered distinct entries");
        Check(entries.All(e => e.SeatId == "seat-a" && e.TurnToken == host.Token),
            "every message is attributed to the host-bound seat and turn");
        Check(runner.ActivityForSeat("seat-b").Count == 0,
            "another seat cannot obtain private chat through the filtered accessor");
        var retained = runner.ActivityForSeat("seat-a");
        engine.State.Factions.Add(new FactionState { SeatId = "seat-b", Name = "Shu", Controller = ControllerType.Llm });
        var second = new LlmHarnessHost(engine, "seat-b");
        fake.EnqueueSuccess("{\"type\":\"end_turn\",\"summary\":\"" + LlmFakeKey + "\"}");
        runner.Begin(second, second.Token, "seat-b", 99);
        LlmDrain(runner);
        Check(runner.ActivityForSeat("seat-b").Any(e => e.Kind == "decision")
            && runner.ActivityForSeat("seat-a").Count == retained.Count,
            "spectator trace retains separate seat histories across turns");
        Check(!string.Join(" ", runner.Activity.Select(e => e.Text)).Contains(LlmFakeKey),
            "chat redacts the configured API key even if echoed by the model");
        Check(fake.Requests.Last().User.IndexOf("I will train", StringComparison.Ordinal) < 0,
            "cross-seat observer chat is never inserted into model prompts");
        bool readOnly = false;
        try { ((IList<LlmActivity>)runner.Activity).Clear(); }
        catch (NotSupportedException) { readOnly = true; }
        Check(readOnly, "the observer cannot mutate the activity collection");
        for (int i = 0; i < 45; i++)
        {
            var turn = new LlmHarnessHost(engine, "seat-a");
            fake.EnqueueSuccess("{\"type\":\"end_turn\"}");
            runner.Begin(turn, turn.Token, "seat-a", 99);
            LlmDrain(runner);
        }
        Check(runner.Activity.Count == LlmTurnRunner.MaxActivityEntries
            && runner.Activity[0].Sequence > 1,
            "long sessions evict old entries at a fixed memory bound");
        Check(retained.Count > 0 && retained[0].Sequence == 1,
            "retained filtered traces survive buffer eviction unchanged");
        var cancelHost = new LlmHarnessHost(engine, "seat-a");
        var deferred = fake.EnqueueDeferred();
        runner.Begin(cancelHost, cancelHost.Token, "seat-a", 99);
        runner.Cancel("user cancelled");
        int cancelledCount = runner.Activity.Last().Sequence;
        deferred.TrySetResult(LlmResponse.Success("{\"type\":\"end_turn\"}", 0, null, null, null));
        runner.Tick();
        Check(runner.Activity.Last().Kind == "finished" && runner.Activity.Last().Sequence == cancelledCount,
            "cancel is visible and late responses add no chat or action");
        runner.ResetSession();
        Check(runner.Activity.Count == 0, "reset clears the observer session chat");
    }
}
