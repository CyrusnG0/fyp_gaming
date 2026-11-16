using System;
using System.Collections.Generic;

namespace LuanShi.Engine
{
    /// <summary>
    /// Seat-safe diplomacy candidates for the first LLM diplomacy slice. Kept in its own partial so the
    /// catalogue is unchanged when the flag is off, and so the diplomacy block reads only the acting
    /// seat's own <see cref="Observation"/>: other seats from <c>factions</c>, treaties and offers from
    /// <c>treaties</c>/<c>proposals</c>. A pending offer between two other seats therefore cannot be
    /// advertised, and no hidden state is consulted.
    /// </summary>
    public static partial class ActionCatalogue
    {
        /// <summary>
        /// As <see cref="Build(Observation, AttackView, bool)"/>, plus the opt-in diplomacy
        /// candidates: one 遣使 per other seat, one 提出條約 per other seat and treaty type that is
        /// neither in force nor already pending, and accept/reject answers for each unanswered inbound
        /// offer. Every partner, treaty and offer comes from this seat's own observation, so no other
        /// seat's pending offer can be advertised. With <paramref name="includeDiplomacy"/> false this
        /// is the unchanged three-argument catalogue, byte for byte.
        /// </summary>
        public static List<ActionOption> Build(Observation observation, AttackView attacks,
            bool includeResearch, bool includeDiplomacy)
        {
            List<ActionOption> options = Build(observation, attacks, includeResearch);
            if (!includeDiplomacy || observation == null || string.IsNullOrEmpty(observation.SeatId))
                return options;

            // end_turn is always the last entry, so the diplomacy block is inserted directly before
            // it: the existing blocks keep their order and only the enabled flag changes the list.
            List<ActionOption> diplomacy = BuildDiplomacy(observation);
            if (diplomacy.Count > 0) options.InsertRange(options.Count - 1, diplomacy);
            return options;
        }

        /// <summary>The three treaty types the engine validates, in catalogue order (design §7.2).</summary>
        private static readonly string[] ProbeTreatyTypes =
        {
            TreatyType.Nap,
            TreatyType.Alliance,
            TreatyType.Truce,
        };

        /// <summary>
        /// Diplomacy candidates for the first slice: 遣使, 提出條約 and an accept/reject answer to
        /// each unanswered inbound offer. Everything is read from the seat's own observation —
        /// other seats from <c>factions</c>, treaties and offers from <c>treaties</c>/
        /// <c>proposals</c> — so a pending offer between two other seats can never be advertised and
        /// no hidden state is consulted. The order is fixed for byte-stability.
        /// </summary>
        private static List<ActionOption> BuildDiplomacy(Observation observation)
        {
            var options = new List<ActionOption>();
            int sendCost = BalanceConfig.CommandPointCost(ActionType.SendMessage);
            int proposeCost = BalanceConfig.CommandPointCost(ActionType.ProposeTreaty);
            int respondCost = BalanceConfig.CommandPointCost(ActionType.RespondTreaty);
            bool hasOrders = observation.CommandPoints >= sendCost;

            // 遣使 to every other seat the observation lists.
            foreach (FactionView faction in observation.Factions)
            {
                if (faction == null || string.IsNullOrEmpty(faction.SeatId)
                    || string.Equals(faction.SeatId, observation.SeatId, StringComparison.Ordinal))
                    continue;
                options.Add(new ActionOption
                {
                    Type = ActionType.SendMessage,
                    TargetId = faction.SeatId,
                    Label = "Send a message to " + NameOf(faction.Name, faction.SeatId),
                    CommandPointCost = sendCost,
                    LikelyAffordable = hasOrders,
                    Note = CandidateNote,
                });
            }

            // 提出條約 per other seat and type: only while that type is not in force and the pair has
            // no pending offer in either direction (the validator's two preconditions).
            foreach (FactionView faction in observation.Factions)
            {
                if (faction == null || string.IsNullOrEmpty(faction.SeatId)
                    || string.Equals(faction.SeatId, observation.SeatId, StringComparison.Ordinal))
                    continue;
                if (HasPendingProposal(observation, faction.SeatId)) continue;
                foreach (string type in ProbeTreatyTypes)
                {
                    if (HasTreaty(observation, type, faction.SeatId)) continue;
                    options.Add(new ActionOption
                    {
                        Type = ActionType.ProposeTreaty,
                        TargetId = faction.SeatId,
                        Label = "Propose " + TreatyTypeLabel(type) + " to "
                            + NameOf(faction.Name, faction.SeatId),
                        CommandPointCost = proposeCost,
                        LikelyAffordable = observation.CommandPoints >= proposeCost,
                        Note = CandidateNote,
                        Args = new Dictionary<string, string>(StringComparer.Ordinal)
                            { { "treaty_type", type } },
                    });
                }
            }

            // Answer each unanswered inbound offer. A counteroffer needs a treaty type the model would
            // have to invent, so it is described in the prompt addendum, not enumerated here.
            foreach (ProposalView proposal in observation.Proposals)
            {
                if (proposal == null || proposal.Accepted) continue;
                if (!string.Equals(proposal.ToSeatId, observation.SeatId, StringComparison.Ordinal)) continue;
                string partner = proposal.FromSeatId;
                foreach (string response in new[] { "accept", "reject" })
                {
                    options.Add(new ActionOption
                    {
                        Type = ActionType.RespondTreaty,
                        TargetId = proposal.FromSeatId,
                        Label = (response == "accept" ? "Accept " : "Reject ")
                            + TreatyTypeLabel(proposal.Type) + " offered by " + partner
                            + " (" + proposal.ProposalId + ")",
                        CommandPointCost = respondCost,
                        LikelyAffordable = observation.CommandPoints >= respondCost,
                        Note = CandidateNote,
                        Args = new Dictionary<string, string>(StringComparer.Ordinal)
                            { { "response", response } },
                    });
                }
            }
            return options;
        }

        /// <summary>A pending offer between the pair in either direction, as this seat sees it.</summary>
        private static bool HasPendingProposal(Observation observation, string otherSeatId)
        {
            foreach (ProposalView proposal in observation.Proposals)
            {
                if (proposal == null) continue;
                if (string.Equals(proposal.FromSeatId, otherSeatId, StringComparison.Ordinal)
                    || string.Equals(proposal.ToSeatId, otherSeatId, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        /// <summary>A treaty of this type between the pair, order-independent and seat-visible only.</summary>
        private static bool HasTreaty(Observation observation, string type, string otherSeatId)
        {
            foreach (TreatyView treaty in observation.Treaties)
                if (treaty != null && treaty.Type == type && treaty.Covers(observation.SeatId, otherSeatId))
                    return true;
            return false;
        }

        private static string TreatyTypeLabel(string type)
        {
            switch (type)
            {
                case TreatyType.Nap: return "a non-aggression pact (nap)";
                case TreatyType.Alliance: return "an alliance";
                case TreatyType.Truce: return "a truce";
                default: return type;
            }
        }
    }
}
