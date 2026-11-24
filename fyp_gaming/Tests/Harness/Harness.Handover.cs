using System;
using System.Collections.Generic;
using System.Linq;
using LuanShi.Engine;

// [HANDOVER] Host-authorized human <-> LLM controller handover (GameEngine.ControllerHandover.cs).
// Fake provider only; the LLM path is the production LlmTurnRunner + FakeLlmTransport.

internal static partial class Harness
{
    private sealed class HandoverLlmHost : ITurnHost
    {
        private readonly GameEngine _engine;
        private readonly string _seat;
        public readonly int Token;
        public int SubmitCalls;
        public readonly List<ValidationResult> Results = new List<ValidationResult>();

        public HandoverLlmHost(GameEngine engine, string seat)
        {
            _engine = engine; _seat = seat; Token = engine.BeginSeatTurn(seat);
        }

        public Observation Observe() => _engine.Observe(_seat);

        public ValidationResult Submit(ActionCommand cmd)
        {
            SubmitCalls++;
            var r = _engine.SubmitAs(_seat, Token, ControllerType.Llm, cmd);
            Results.Add(r);
            return r;
        }

        public void EndTurn() => _engine.EndSeatTurn(Token);
    }

    private static ActionCommand HandoverTax(string reason = null) => new ActionCommand
    {
        Type = ActionType.LevyTax, TargetId = "city-a", Reason = reason,
    };

    private static List<EventRecord> HandoverRecords(GameEngine e, string type)
        => e.Log.Records.Where(r => r.EventType == type).ToList();

    private static void RunHandover()
    {
        Section("[HANDOVER] controller handover (host-authorized, between turns)");
        HandoverCycle();
        HandoverRequestRules();
        HandoverBoundaries();
        HandoverStaleReplies();
        HandoverBoundedHistory();
    }

    private static void HandoverCycle()
    {
        var e = TurnGateScenario();
        var op = e.CreateHandoverOperator(new[] { "seat-a" });
        var cfg = new LlmConfig { MaxRequestsPerTurn = 8, MaxActionsPerTurn = 5, MaxTurnSeconds = 30 };
        int cost = BalanceConfig.CommandPointCost(ActionType.LevyTax);
        var tokens = new List<int>();

        // Season 1: human acts, then asks to delegate while the turn is still open.
        int t1 = e.BeginSeatTurn("seat-a"); tokens.Add(t1);
        int cp0 = e.State.FindFaction("seat-a").CommandPoints;
        Check(e.SubmitAs("seat-a", t1, ControllerType.Human, HandoverTax("human-opening-marker")).Ok,
            "[HANDOVER] human acts through the controller-bound path");
        int cp1 = e.State.FindFaction("seat-a").CommandPoints;
        var req = op.Request("seat-a", ControllerType.Llm);
        var st = e.GetHandoverStatus("seat-a");
        Check(req.Ok && req.RequestId != null && st.HasPending && st.Controller == ControllerType.Human
              && st.PendingToController == ControllerType.Llm,
            "[HANDOVER] request during an active turn is pending; the effective controller is unchanged");
        Check(e.State.FindFaction("seat-a").CommandPoints == cp1 && cp0 - cp1 == cost,
            "[HANDOVER] a request costs no command points (only the action did)");
        Check(e.ActiveSeatId == "seat-a" && e.TurnToken == t1,
            "[HANDOVER] a request neither ends the turn nor changes the token");
        Check(!e.SubmitAs("seat-a", t1, ControllerType.Llm, HandoverTax()).Ok,
            "[HANDOVER] the pending LLM cannot act during the human's turn (controller mismatch)");
        Check(op.Request("seat-a", ControllerType.Human).Error == "handover already pending",
            "[HANDOVER] a second request is rejected, not superseded");

        Observation obsB = e.Observe("seat-b");
        Check(!obsB.Events.Any(v => v.EventType == "controller_handover_requested"
                                    || v.EventType == "controller_handover_rejected"),
            "[HANDOVER] other seats never receive requested/rejected records");
        Check(e.Observe("seat-a").Events.Any(v => v.EventType == "controller_handover_requested"),
            "[HANDOVER] the owning seat sees its pending request");

        e.EndSeatTurn(t1);
        st = e.GetHandoverStatus("seat-a");
        Check(!st.HasPending && st.Controller == ControllerType.Llm,
            "[HANDOVER] closing the target seat's own turn makes the handover effective");
        var eff = HandoverRecords(e, "controller_handover_effective");
        Check(eff.Count == 1 && eff[0].ControllerType == ControllerType.Llm
              && eff[0].Payload["applied_via"] == "after_turn" && eff[0].Payload["request_id"] == req.RequestId,
            "[HANDOVER] one effective event, stamped with the new controller, naming the request");
        Observation obsB2 = e.Observe("seat-b");
        var effB = obsB2.Events.Where(v => v.EventType == "controller_handover_effective").ToList();
        Check(effB.Count == 1 && effB[0].Payload.Count == 2 && effB[0].Payload.ContainsKey("to_controller")
              && !obsB2.ToJson().Contains(req.RequestId),
            "[HANDOVER] other seats see only seat + new controller, never the request id");

        int tb = e.BeginSeatTurn("seat-b"); tokens.Add(tb); e.EndSeatTurn(tb);
        e.EndSeason(); e.BeginSeason();

        // Season 2: the LLM plays the seat with the previous human's history.
        Check(!e.PrepareSeatTurn("seat-a") && TurnFlowPlanner.First(e.State).Action == TurnFlowAction.LlmSeat,
            "[HANDOVER] planner picks the LLM runner for the handed-over seat");
        var fake = new FakeLlmTransport();
        fake.EnqueueSuccess("{\"type\":\"levy_tax\",\"target_id\":\"city-a\",\"summary\":\"llm tax\"}");
        fake.EnqueueSuccess("{\"type\":\"end_turn\"}");
        var host = new HandoverLlmHost(e, "seat-a"); tokens.Add(host.Token);
        int cpBefore = e.State.FindFaction("seat-a").CommandPoints;
        var runner = new LlmTurnRunner(fake, cfg, null);
        runner.Begin(host, host.Token, "seat-a", 5);
        var reclaim = op.Request("seat-a", ControllerType.Human);
        Check(reclaim.Ok && e.GetHandoverStatus("seat-a").Controller == ControllerType.Llm,
            "[HANDOVER] reclaim during the LLM turn is pending, no mid-turn control change");
        LlmDrain(runner);
        string prompt = fake.Requests[0].User;
        Check(runner.State == RunnerState.Finished && host.Results.Count == 1 && host.Results[0].Ok
              && cpBefore - e.State.FindFaction("seat-a").CommandPoints == cost,
            "[HANDOVER] LLM action accepted once, one action's command points charged");
        Check(prompt.Contains("human-opening-marker") && prompt.Contains("controller_handover_effective")
              && !prompt.Contains("city-b"),
            "[HANDOVER] incoming LLM receives the previous human action and handover state, no opponent-private data");
        Check(fake.CallCount == 2 && e.GetHandoverStatus("seat-a").Controller == ControllerType.Human,
            "[HANDOVER] runner finish closes the turn and the reclaim becomes effective (2 provider calls)");

        int tb2 = e.BeginSeatTurn("seat-b"); tokens.Add(tb2); e.EndSeatTurn(tb2);
        e.EndSeason(); e.BeginSeason();

        // Season 3: the human acts again.
        Check(TurnFlowPlanner.First(e.State).Action == TurnFlowAction.HumanSeat,
            "[HANDOVER] planner returns to the human seat");
        int t3 = e.BeginSeatTurn("seat-a"); tokens.Add(t3);
        Check(e.SubmitAs("seat-a", t3, ControllerType.Human, HandoverTax()).Ok
              && !e.SubmitAs("seat-a", t3, ControllerType.Llm, HandoverTax()).Ok,
            "[HANDOVER] reclaimed human acts; an LLM-stamped submit is now refused");
        e.EndSeatTurn(t3);

        var acts = e.Log.Records.Where(r => r.EventType == "action" && r.ActorSeatId == "seat-a").ToList();
        Check(acts.Select(a => a.ControllerType).SequenceEqual(
                  new[] { ControllerType.Human, ControllerType.Llm, ControllerType.Human }),
            "[HANDOVER] accepted-action controller stamps run human, llm, human");
        Check(acts.Select(a => a.SourceActionId).Distinct().Count() == 3,
            "[HANDOVER] no duplicate actions");
        Check(tokens.Zip(tokens.Skip(1), (a, b) => b > a).All(x => x) && e.TurnToken == tokens.Last(),
            "[HANDOVER] turn tokens strictly increase across the cycle");
        Check(!e.Log.Records.Any(r => r.EventType == "action_rejected" && r.ControllerType != null),
            "[HANDOVER] no rejected record carries a controller stamp");
        string finalB = e.Observe("seat-b").ToJson();
        Check(!finalB.Contains("human-opening-marker") && !finalB.Contains("h-1") && !finalB.Contains("h-2"),
            "[HANDOVER] seat-b's final observation holds no seat-a private action text or request ids");
    }

    private static void HandoverRequestRules()
    {
        var e = TurnGateScenario();
        var op = e.CreateHandoverOperator(new[] { "seat-a" });
        bool second = false;
        try { e.CreateHandoverOperator(new[] { "seat-a" }); } catch (InvalidOperationException) { second = true; }
        Check(second, "[HANDOVER] the operator capability can be minted only once");
        bool unknown = false;
        try { TurnGateScenario().CreateHandoverOperator(new[] { "seat-x" }); }
        catch (ArgumentException) { unknown = true; }
        Check(unknown, "[HANDOVER] an unknown seat cannot be made eligible");

        Check(op.Request("seat-b", ControllerType.Human).Error == "handover not authorized",
            "[HANDOVER] a seat outside the eligible set is refused");
        Check(op.Request("seat-a", ControllerType.Human).Error == "already controlled by target"
              && op.Request("seat-a", ControllerType.ScriptedAi).Error == "invalid target controller"
              && op.Request("nope", ControllerType.Llm).Error == "handover not authorized"
              && op.Request(null, ControllerType.Llm).Error == "handover not authorized",
            "[HANDOVER] no-op, invalid controller and unknown/null seat are refused");
        Check(op.Cancel("seat-a").Error == "no pending handover"
              && op.Cancel("seat-b").Error == "handover not authorized",
            "[HANDOVER] cancel with nothing pending, or for an ineligible seat, is refused");
        Check(e.GetHandoverStatus("seat-a").Controller == ControllerType.Human && !e.GetHandoverStatus("seat-a").HasPending,
            "[HANDOVER] refused calls change nothing");
        var rej = HandoverRecords(e, "controller_handover_rejected");
        Check(rej.Count == 7 && !e.Observe("seat-b").Events.Any(v => v.EventType == "controller_handover_rejected")
              && rej.Where(r => r.Payload.ContainsKey("target_seat_id")).All(r => r.ActorSeatId == null),
            "[HANDOVER] rejections are logged canonically; unauthorized ones reach no seat");

        var r1 = op.Request("seat-a", ControllerType.Llm);
        var c = op.Cancel("seat-a");
        Check(r1.Ok && c.Ok && c.RequestId == r1.RequestId && !e.GetHandoverStatus("seat-a").HasPending
              && HandoverRecords(e, "controller_handover_cancelled").Count == 1,
            "[HANDOVER] explicit cancel clears the single pending request and logs it");
        e.State.WinnerSeatId = "seat-b";
        Check(op.Request("seat-a", ControllerType.Llm).Error == "game over",
            "[HANDOVER] no handover after game over");
    }

    private static void HandoverBoundaries()
    {
        var e = TurnGateScenario();
        var op = e.CreateHandoverOperator(new[] { "seat-a" });
        // Idle seat: pending until its own boundary; another seat's turn must not apply it.
        var r = op.Request("seat-a", ControllerType.Llm);
        Check(r.Ok && e.Log.Records.Last().Payload["timing"] == "before_next_turn",
            "[HANDOVER] request between turns is pending with before_next_turn timing");
        int tb = e.BeginSeatTurn("seat-b");
        Check(!e.PrepareSeatTurn("seat-b") && e.GetHandoverStatus("seat-a").HasPending,
            "[HANDOVER] preparing another seat applies nothing");
        e.EndSeatTurn(tb);
        Check(e.GetHandoverStatus("seat-a").HasPending && e.GetHandoverStatus("seat-a").Controller == ControllerType.Human,
            "[HANDOVER] another seat ending its turn does not apply the request");
        int ta = e.BeginSeatTurn("seat-a");
        Check(!e.PrepareSeatTurn("seat-a") && e.GetHandoverStatus("seat-a").HasPending,
            "[HANDOVER] Prepare during the seat's own active turn is a no-op");
        e.EndSeatTurn(ta - 1);   // stale token
        Check(e.ActiveSeatId == "seat-a" && e.GetHandoverStatus("seat-a").HasPending,
            "[HANDOVER] a stale EndSeatTurn neither ends the turn nor applies the request");
        e.EndSeatTurn(ta);
        Check(e.GetHandoverStatus("seat-a").Controller == ControllerType.Llm
              && HandoverRecords(e, "controller_handover_effective")[0].Payload["applied_via"] == "after_turn",
            "[HANDOVER] the seat's own close applies a request that was queued while idle");

        var e2 = TurnGateScenario();
        var op2 = e2.CreateHandoverOperator(new[] { "seat-a" });
        op2.Request("seat-a", ControllerType.Llm);
        Check(e2.PrepareSeatTurn("seat-a") && e2.GetHandoverStatus("seat-a").Controller == ControllerType.Llm
              && HandoverRecords(e2, "controller_handover_effective")[0].Payload["applied_via"] == "before_turn"
              && e2.TurnToken == 0,
            "[HANDOVER] PrepareSeatTurn applies an idle seat's request without touching the token");
        Check(!e2.PrepareSeatTurn("seat-a"), "[HANDOVER] applying is idempotent");

        var e3 = TurnGateScenario();
        var op3 = e3.CreateHandoverOperator(new[] { "seat-a" });
        op3.Request("seat-a", ControllerType.Llm); op3.Cancel("seat-a");
        int t3 = e3.BeginSeatTurn("seat-a"); e3.EndSeatTurn(t3);
        Check(e3.GetHandoverStatus("seat-a").Controller == ControllerType.Human
              && HandoverRecords(e3, "controller_handover_effective").Count == 0,
            "[HANDOVER] a cancelled request never becomes effective");
    }

    private static void HandoverStaleReplies()
    {
        var e = TurnGateScenario();
        e.State.FindFaction("seat-a").Controller = ControllerType.Llm;
        var op = e.CreateHandoverOperator(new[] { "seat-a" });
        var fake = new FakeLlmTransport();
        var deferred = fake.EnqueueDeferred();
        var host = new HandoverLlmHost(e, "seat-a");
        var runner = new LlmTurnRunner(fake, new LlmConfig { MaxTurnSeconds = 30 }, null);
        runner.Begin(host, host.Token, "seat-a", 3);
        op.Request("seat-a", ControllerType.Human);
        runner.Cancel("user cancelled");
        Check(runner.State == RunnerState.Cancelled && e.GetHandoverStatus("seat-a").HasPending,
            "[HANDOVER] cancelling the runner does not end the turn or apply the request");
        e.EndSeatTurn(host.Token);
        Check(e.GetHandoverStatus("seat-a").Controller == ControllerType.Human,
            "[HANDOVER] the host closing the turn applies the reclaim");

        int cp = e.State.FindFaction("seat-a").CommandPoints;
        int tHuman = e.BeginSeatTurn("seat-a");
        deferred.TrySetResult(new LlmResponse { Ok = true, Text = "{\"type\":\"levy_tax\",\"target_id\":\"city-a\"}" });
        for (int i = 0; i < 20; i++) runner.Tick();
        Check(host.SubmitCalls == 0 && e.State.FindFaction("seat-a").CommandPoints == cp
              && !e.Log.Records.Any(r => r.EventType == "action" && r.ControllerType == ControllerType.Llm),
            "[HANDOVER] a reply that arrives after cancel + handover never executes");

        var stale = new ActionCommand { Type = ActionType.LevyTax, TargetId = "city-a" };
        var res = e.SubmitAs("seat-a", host.Token, ControllerType.Llm, stale);
        var rejected = e.Log.Records.Last(r => r.EventType == "action_rejected");
        Check(!res.Ok && res.Error == "stale turn token" && rejected.ControllerType == null
              && rejected.ActorSeatId == "seat-a" && e.State.FindFaction("seat-a").CommandPoints == cp,
            "[HANDOVER] stale token: refused, logged against the seat with no controller stamp, no cost");
        var spoof = new ActionCommand { Type = ActionType.LevyTax, TargetId = "city-a", ControllerType = ControllerType.Human };
        var res2 = e.SubmitAs("seat-a", tHuman, ControllerType.Llm, spoof);
        Check(!res2.Ok && res2.Error == "controller mismatch" && e.Log.Records.Last().ControllerType == null,
            "[HANDOVER] current token but wrong controller class: refused, claim cleared, not re-stamped");
        e.State.FindFaction("seat-a").Controller = ControllerType.Llm;   // direct flip mid-turn (not a supported path)
        Check(!e.SubmitAs("seat-a", tHuman, ControllerType.Llm, new ActionCommand { Type = ActionType.LevyTax, TargetId = "city-a" }).Ok,
            "[HANDOVER] a controller change not made at a boundary cannot ride on the open turn");
    }

    private static void HandoverBoundedHistory()
    {
        var e = TurnGateScenario();
        var op = e.CreateHandoverOperator(new[] { "seat-a" });
        int t = e.BeginSeatTurn("seat-a");
        e.SubmitAs("seat-a", t, ControllerType.Human, HandoverTax("early-human-marker"));
        e.EndSeatTurn(t);
        for (int i = 0; i < 20; i++) { e.EndSeason(); e.BeginSeason(); }
        op.Request("seat-a", ControllerType.Llm);
        e.PrepareSeatTurn("seat-a");
        int total = e.Observe("seat-a").Events.Count;
        var fake = new FakeLlmTransport();
        fake.EnqueueSuccess("{\"type\":\"end_turn\"}");
        var host = new HandoverLlmHost(e, "seat-a");
        var runner = new LlmTurnRunner(fake, new LlmConfig(), null);
        runner.Begin(host, host.Token, "seat-a", 1);
        LlmDrain(runner);
        string prompt = fake.Requests[0].User;
        Check(total > PromptBuilder.MaxPromptObservationEvents
              && e.Observe("seat-a").Events.Any(v => v.Payload.TryGetValue("reason", out var r) && r == "early-human-marker")
              && !prompt.Contains("early-human-marker"),
            $"[HANDOVER] bounded history is real: observation holds {total} events, the prompt carries the newest "
            + $"{PromptBuilder.MaxPromptObservationEvents}; the earlier human action is omitted from the prompt");
    }
}
