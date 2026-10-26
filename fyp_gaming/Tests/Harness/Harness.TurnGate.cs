using System;
using System.Collections.Generic;
using LuanShi.Engine;

// [DEMO-A] Contract A: the host-driven seat turn gate. These tests are deliberately additive: the
// existing suite never calls BeginSeatTurn, so its 691 assertions exercise the gate-off legacy path.
// Every assertion below is tagged [DEMO-A] so it is distinguishable from the recovered/LS coverage.

internal static partial class Harness
{
    private static void RunTurnGate()
    {
        Section("[DEMO-A] turn gate (Contract A)");

        // ---- gate off: legacy Submit is unchanged until the host begins the first turn
        {
            var e = TurnGateScenario();
            Check(e.ActiveSeatId == null && e.TurnToken == 0,
                  "[DEMO-A] gate starts off: no active seat and token 0");

            var a = e.Submit(TurnGateOrder("seat-a", ActionType.LevyTax, "city-a"));
            var b = e.Submit(TurnGateOrder("seat-b", ActionType.LevyTax, "city-b"));
            Check(a.Ok && b.Ok,
                  "[DEMO-A] gate off: legacy Submit accepts every known seat (unchanged behaviour)");
            Check(e.ActiveSeatId == null && e.TurnToken == 0,
                  "[DEMO-A] gate off: legacy Submit neither activates a seat nor increments the token");
        }

        // ---- right seat vs wrong seat, plus the first token
        {
            var e = TurnGateScenario();
            int token = e.BeginSeatTurn("seat-a");
            Check(token == 1 && e.TurnToken == 1 && e.ActiveSeatId == "seat-a",
                  "[DEMO-A] BeginSeatTurn sets the active seat and returns token 1");

            int bGold = e.State.FindCity("city-b").Gold;
            var wrong = e.Submit(TurnGateOrder("seat-b", ActionType.LevyTax, "city-b"));
            Check(!wrong.Ok && wrong.Error == "not this seat's turn",
                  $"[DEMO-A] Submit from a non-active seat is rejected ({wrong.Error})");
            Check(e.State.FindCity("city-b").Gold == bGold,
                  "[DEMO-A] the wrong-seat rejection mutates nothing");

            var right = e.Submit(TurnGateOrder("seat-a", ActionType.LevyTax, "city-a"));
            Check(right.Ok, $"[DEMO-A] Submit from the active seat is accepted ({right.Error})");

            var wrongAs = e.SubmitAs("seat-b", token,
                TurnGateOrder("seat-b", ActionType.LevyTax, "city-b"));
            Check(!wrongAs.Ok && wrongAs.Error == "not this seat's turn",
                  $"[DEMO-A] SubmitAs with the wrong host-bound seat is rejected ({wrongAs.Error})");
        }

        // ---- tokens increment; a stale token is stale even for what is now the active seat
        {
            var e = TurnGateScenario();
            int first = e.BeginSeatTurn("seat-a");
            int second = e.BeginSeatTurn("seat-b");
            Check(second == first + 1 && e.TurnToken == second && e.ActiveSeatId == "seat-b",
                  "[DEMO-A] every BeginSeatTurn increments the token");

            var stale = e.SubmitAs("seat-b", first,
                TurnGateOrder("seat-b", ActionType.LevyTax, "city-b"));
            Check(!stale.Ok && stale.Error == "stale turn token",
                  $"[DEMO-A] an old token is rejected after a new BeginSeatTurn ({stale.Error})");

            e.EndSeatTurn(first);
            Check(e.ActiveSeatId == "seat-b" && e.TurnToken == second,
                  "[DEMO-A] EndSeatTurn with a stale token is ignored");
            Check(!e.Submit(TurnGateOrder("seat-a", ActionType.LevyTax, "city-a")).Ok,
                  "[DEMO-A] the ignored EndSeatTurn did not open the gate to another seat");

            var accepted = e.SubmitAs("seat-b", second,
                TurnGateOrder("seat-b", ActionType.LevyTax, "city-b"));
            Check(accepted.Ok,
                  $"[DEMO-A] the current seat/token still submits after an ignored stale EndSeatTurn ({accepted.Error})");
        }

        // ---- SubmitAs binds the actor; the model's claimed actor/controller never win
        {
            var e = TurnGateScenario();
            int token = e.BeginSeatTurn("seat-a");
            var cmd = new ActionCommand
            {
                Type = ActionType.LevyTax,
                ActorSeatId = "seat-b",
                ControllerType = "model_claimed_controller",
                TargetId = "city-a",
            };
            var r = e.SubmitAs("seat-a", token, cmd);
            Check(r.Ok, $"[DEMO-A] SubmitAs accepts the host-bound seat despite a spoofed actor ({r.Error})");
            Check(cmd.ActorSeatId == "seat-a",
                  "[DEMO-A] SubmitAs overwrites the command's model-supplied ActorSeatId");
            Check(cmd.ControllerType == ControllerType.Human,
                  "[DEMO-A] Submit still records the faction's controller on the command");

            var last = e.Log.Records[e.Log.Records.Count - 1];
            Check(last.EventType == "action" && last.ActorSeatId == "seat-a"
                  && last.ControllerType == ControllerType.Human,
                  "[DEMO-A] the action log carries the host-bound seat and faction controller");
        }

        // ---- a late response and a duplicate response after EndSeatTurn are both rejected
        {
            var e = TurnGateScenario();
            int token = e.BeginSeatTurn("seat-a");
            var first = TurnGateOrder("seat-a", ActionType.LevyTax, "city-a");
            Check(e.SubmitAs("seat-a", token, first).Ok,
                  "[DEMO-A] the first response before EndSeatTurn is accepted");

            int gold = e.State.FindCity("city-a").Gold;
            e.EndSeatTurn(token);
            Check(e.ActiveSeatId == null && e.TurnToken == token,
                  "[DEMO-A] EndSeatTurn clears the active seat but leaves the gate on");

            var late = e.SubmitAs("seat-a", token,
                TurnGateOrder("seat-a", ActionType.LightenLabor, "city-a"));
            Check(!late.Ok && late.Error == "not this seat's turn",
                  $"[DEMO-A] a late response after EndSeatTurn is rejected ({late.Error})");

            var duplicate = e.SubmitAs("seat-a", token, first);
            Check(!duplicate.Ok && duplicate.Error == "not this seat's turn",
                  $"[DEMO-A] a duplicate response after EndSeatTurn is rejected ({duplicate.Error})");

            var direct = e.Submit(TurnGateOrder("seat-a", ActionType.LevyTax, "city-a"));
            Check(!direct.Ok && direct.Error == "not this seat's turn",
                  $"[DEMO-A] Submit rejects every seat while no seat is active ({direct.Error})");
            Check(e.State.FindCity("city-a").Gold == gold,
                  "[DEMO-A] late, duplicate and no-seat rejections mutate nothing");
        }

        // ---- unknown seat: BeginSeatTurn throws without touching the gate
        {
            var e = TurnGateScenario();
            Exception caught = null;
            try { e.BeginSeatTurn("seat-x"); }
            catch (Exception ex) { caught = ex; }
            Check(caught is ArgumentException,
                  "[DEMO-A] BeginSeatTurn on an unknown seat throws ArgumentException");
            Check(e.ActiveSeatId == null && e.TurnToken == 0,
                  "[DEMO-A] the failed BeginSeatTurn leaves the gate off and the token unchanged");
        }

        // ---- rejections are logged through Reject, with no active-seat/token disclosure
        {
            var e = TurnGateScenario();
            e.BeginSeatTurn("seat-a");
            int before = e.Log.Records.Count;
            var r = e.Submit(TurnGateOrder("seat-b", ActionType.LevyTax, "city-b"));
            Check(!r.Ok && r.Error == "not this seat's turn",
                  "[DEMO-A] the logged rejection uses the fixed public error");

            Check(e.Log.Records.Count == before + 1,
                  "[DEMO-A] a gated rejection appends exactly one event");
            var record = e.Log.Records[before];
            Check(record.EventType == "action_rejected" && record.Visibility == "owner_only"
                  && record.Payload.Count == 2
                  && record.Payload["action"] == ActionType.LevyTax
                  && record.Payload["reason"] == "not this seat's turn",
                  "[DEMO-A] the rejection record carries only the attempted action and fixed reason");
            Check(!record.ToJson().Contains("seat-a"),
                  "[DEMO-A] the rejection record does not disclose the active seat");
        }

        // ---- a full season cycle: A, B, rollover, then A again in the new season
        {
            var e = TurnGateScenario();
            int a1 = e.BeginSeatTurn("seat-a");
            var actionA = e.SubmitAs("seat-a", a1,
                TurnGateOrder("seat-a", ActionType.LevyTax, "city-a"));
            e.EndSeatTurn(a1);

            int b1 = e.BeginSeatTurn("seat-b");
            var actionB = e.SubmitAs("seat-b", b1,
                TurnGateOrder("seat-b", ActionType.LevyTax, "city-b"));
            e.EndSeatTurn(b1);
            Check(actionA.Ok && actionB.Ok && b1 == a1 + 1,
                  "[DEMO-A] seat A then seat B both act within one season");

            e.EndSeason();
            var between = e.Submit(TurnGateOrder("seat-a", ActionType.LevyTax, "city-a"));
            Check(!between.Ok && between.Error == "not this seat's turn",
                  "[DEMO-A] EndSeason leaves the gate on but no seat active");

            e.BeginSeason();
            int a2 = e.BeginSeatTurn("seat-a");
            var nextSeason = e.SubmitAs("seat-a", a2,
                TurnGateOrder("seat-a", ActionType.LightenLabor, "city-a"));
            Check(a2 == b1 + 1 && nextSeason.Ok,
                  $"[DEMO-A] the new season begins with a higher token and a working host turn ({nextSeason.Error})");
            e.EndSeatTurn(a2);
        }

        // ---- determinism: the gate adds no events and changes no action ids/order
        // The full JSONL must match byte for byte; no ordering exclusion is needed or used.
        var ungated = TurnGateReplay(false);
        var gated = TurnGateReplay(true);
        Check(ungated.Length > 0 && ungated == gated,
              "[DEMO-A] same seed + commands replay byte-identically with and without the gate "
              + "(no gate events; action ids and order unchanged)");
    }

    /// <summary>Two seats, one city each, Summer. No watchtowers are needed for domestic orders or resolution.</summary>
    private static GameEngine TurnGateScenario(int seed = 23)
    {
        var e = new GameEngine(seed);
        e.State.Map = Corridor(12);
        e.State.Factions.Add(new FactionState
        {
            SeatId = "seat-a", Name = "Wei", Controller = ControllerType.Human,
        });
        e.State.Factions.Add(new FactionState
        {
            SeatId = "seat-b", Name = "Shu", Controller = ControllerType.Llm,
        });
        e.State.Cities.Add(new CityState
        {
            Id = "city-a", Name = "City A", X = 0, Z = 0, OwnerSeatId = "seat-a",
            Population = 10000, Food = 5000, Gold = 1000, Garrison = 0,
            Morale = BalanceConfig.MoraleStart,
        });
        e.State.Cities.Add(new CityState
        {
            Id = "city-b", Name = "City B", X = 8, Z = 0, OwnerSeatId = "seat-b",
            Population = 10000, Food = 5000, Gold = 1000, Garrison = 0,
            Morale = BalanceConfig.MoraleStart,
        });
        e.BeginSeason();
        e.State.Season = 2;   // Summer
        e.State.Phase = "player_orders";
        return e;
    }

    private static ActionCommand TurnGateOrder(string seat, string type, string target)
        => new ActionCommand { Type = type, ActorSeatId = seat, TargetId = target };

    /// <summary>The same seed and ordered actions, submitted either directly (legacy) or through the host gate.</summary>
    private static string TurnGateReplay(bool gated)
    {
        var e = TurnGateScenario(seed: 29);

        if (gated)
        {
            int token = e.BeginSeatTurn("seat-a");
            e.SubmitAs("seat-a", token, TurnGateOrder("seat-a", ActionType.LevyTax, "city-a"));
            e.EndSeatTurn(token);

            token = e.BeginSeatTurn("seat-b");
            e.SubmitAs("seat-b", token, TurnGateOrder("seat-b", ActionType.LevyTax, "city-b"));
            e.EndSeatTurn(token);
        }
        else
        {
            e.Submit(TurnGateOrder("seat-a", ActionType.LevyTax, "city-a"));
            e.Submit(TurnGateOrder("seat-b", ActionType.LevyTax, "city-b"));
        }

        e.EndSeason();
        e.BeginSeason();

        if (gated)
        {
            int token = e.BeginSeatTurn("seat-a");
            e.SubmitAs("seat-a", token, TurnGateOrder("seat-a", ActionType.LightenLabor, "city-a"));
            e.EndSeatTurn(token);
        }
        else
        {
            e.Submit(TurnGateOrder("seat-a", ActionType.LightenLabor, "city-a"));
        }

        e.EndSeason();
        return e.Log.ToJsonLines();
    }
}
