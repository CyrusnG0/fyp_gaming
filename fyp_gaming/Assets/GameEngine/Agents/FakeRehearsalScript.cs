using System;
using System.Collections.Generic;

namespace LuanShi.Engine
{
    /// <summary>Which opt-in orders the scripted rehearsal may use. They mirror the LlmConfig switches.</summary>
    public sealed class FakeRehearsalOptions
    {
        public bool ManualLookup;
        public bool Research;
        public bool Diplomacy;
        public bool Garrison;

        /// <summary>The runner's per-turn caps. A scripted turn must stay inside them.</summary>
        public int MaxActionsPerTurn = 5;
        public int MaxRequestsPerTurn = 6;

        /// <summary>
        /// Most orders (end_turn excluded) one scripted turn holds, so the turn still closes on its own
        /// end_turn: each order is a request and an action, and end_turn needs one more of each.
        /// </summary>
        public int OrderCap => Math.Max(0, Math.Min(MaxActionsPerTurn, MaxRequestsPerTurn) - 1);

        /// <summary>False when the caps let the runner send no request at all, so nothing may be queued.</summary>
        public bool CanPlay => MaxActionsPerTurn > 0 && MaxRequestsPerTurn > 0;
    }

    /// <summary>
    /// Scripted two-seat rehearsal (FAKE, no provider). It wraps <see cref="FakeTurnPlan.Build"/> and adds
    /// the opt-in orders a spectator demo should exercise: one research order per turn, and in the opening
    /// season a treaty proposal from the first seat that the second seat answers. Every reply is scripted
    /// and labelled FAKE; none of it is a model decision. The engine remains the only judge: an order the
    /// rules refuse (for example research before the seat holds enough points) is still sent, so the
    /// refusal appears in the same log. An order whose feature switch is off is never scripted, because
    /// the runner would refuse it before the engine.
    /// </summary>
    public static class FakeRehearsalScript
    {
        public const string TreatyKind = TreatyType.Nap;
        public const int TreatyDurationSeasons = 4;
        public const string ResearchTrack = ResearchLine.Agri;

        public static IReadOnlyList<string> Build(MovementView movement, Observation observation,
            string seatId, IReadOnlyList<string> playOrder, bool openingSeason, FakeRehearsalOptions options)
        {
            options = options ?? new FakeRehearsalOptions();
            var plan = new List<string>(FakeTurnPlan.Build(movement, observation, seatId, options.ManualLookup));
            if (!options.Research && !options.Diplomacy) return plan;

            int ordersLeft = observation == null ? 0 : observation.CommandPoints;
            foreach (string line in plan)
            {
                if (line.Contains("\"type\":\"march\"")) ordersLeft -= BalanceConfig.CommandPointCost(ActionType.March);
                else if (line.Contains("\"type\":\"levy_tax\""))
                    ordersLeft -= BalanceConfig.CommandPointCost(ActionType.LevyTax);
            }

            // end_turn stays last so the turn still closes through the model's own end signal.
            plan.InsertRange(plan.Count - 1, Extras(seatId, playOrder, openingSeason, options, ordersLeft,
                options.OrderCap - (plan.Count - 1)));
            return plan;
        }

        internal static List<string> Extras(string seatId, IReadOnlyList<string> playOrder, bool openingSeason,
            FakeRehearsalOptions options, int ordersLeft, int room)
        {
            // Priority when the turn is full: the treaty exchange first, then research.
            var extra = new List<string>();
            if (options.Diplomacy && openingSeason && playOrder != null && playOrder.Count >= 2)
            {
                if (room > extra.Count && seatId == playOrder[0]) extra.Add(Propose(playOrder[1]));
                else if (room > extra.Count && seatId == playOrder[1]) extra.Add(Respond(playOrder[0]));
            }
            if (options.Research && room > extra.Count && ordersLeft >= BalanceConfig.CommandPointCost(ActionType.Research))
                extra.Add(Research());
            return extra;
        }

        private static string Research()
            => "{\"type\":\"research\",\"args\":{\"line\":\"" + ResearchTrack + "\"},\"summary\":\""
             + MiniJson.Escape(FakeTurnPlan.Label + ": research " + ResearchTrack) + "\"}";

        private static string Propose(string toSeat)
            => "{\"type\":\"propose_treaty\",\"target_id\":" + MiniJson.Str(toSeat)
             + ",\"args\":{\"treaty_type\":\"" + TreatyKind + "\",\"duration\":" + TreatyDurationSeasons
             + "},\"text\":\"" + MiniJson.Escape(FakeTurnPlan.Label + ": propose a short peace") + "\""
             + ",\"summary\":\"" + MiniJson.Escape(FakeTurnPlan.Label + ": propose " + TreatyKind) + "\"}";

        private static string Respond(string fromSeat)
            => "{\"type\":\"respond_treaty\",\"target_id\":" + MiniJson.Str(fromSeat)
             + ",\"args\":{\"response\":\"accept\"},\"summary\":\""
             + MiniJson.Escape(FakeTurnPlan.Label + ": accept " + TreatyKind) + "\"}";
    }
}
