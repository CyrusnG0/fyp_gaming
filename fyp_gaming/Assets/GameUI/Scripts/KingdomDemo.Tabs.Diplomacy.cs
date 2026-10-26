// KingdomDemo (partial) — diplomacy tab, read from the current seat's observation.

using System.Collections.Generic;
using System.Text;
using UnityEngine;
using LuanShi.Engine;

namespace LuanShi
{
    public partial class KingdomDemo : MonoBehaviour
    {
        float DrawDiplomacyPanel(Rect p, GUIStyle body)
        {
            var wrap = new GUIStyle(body) { wordWrap = true };
            var me = engine.State.FindFaction(currentSeat);
            var btn = new GUIStyle(uiButton) { fontSize = 14 };
            const int shownFactions = 3;      // placeholder layout: the rest are summarised

            float y = p.y + 52;
            y += WrappedLabel(wrap,
                $"Diplomacy ({me.Name})  Orders {me.CommandPoints}/{BalanceConfig.CommandPointsPerSeason}  Prestige {me.Prestige}" +
                $"  Treaties {(obs != null ? obs.Treaties.Count : 0)}" +
                $"  Pending proposals {(obs != null ? obs.Proposals.Count : 0)}", p.x + 20, y, 760, 22) + 4;

            // Envoy message: type here, then press Send message on a faction row to submit it.
            GUI.Label(new Rect(p.x + 36, y, 120, 22), "Envoy message", body);
            envoyText = GUI.TextField(new Rect(p.x + 160, y, 604, 24), envoyText, 120, uiField);
            if (envoyConfirmationUntil > Time.unscaledTime)
            {
                var toast = new Rect(p.x + 270, y + 34, 300, 52);
                DrawBackdrop(toast, AccentGold);
                GUI.Label(new Rect(toast.x + 12, toast.y + 15, toast.width - 24, 24),
                          "Message sent successfully", body);
            }
            y += 34;

            // Incoming offers first: they are the only diplomacy that demands an answer.
            var incoming = new List<ProposalView>();
            if (obs != null)
                foreach (var proposal in obs.Proposals)
                    if (proposal.ToSeatId == currentSeat) incoming.Add(proposal);

            y += WrappedLabel(wrap, $"Pending proposals ({incoming.Count})", p.x + 20, y, 760, 22) + 2;

            if (incoming.Count == 0)
                y += WrappedLabel(wrap, "No incoming proposals.", p.x + 36, y, 744, 20) + 4;
            foreach (var proposal in incoming)
            {
                y += WrappedLabel(wrap,
                    $"{SeatShort(proposal.FromSeatId)} proposes {TreatyLabel(proposal.Type)} for {proposal.DurationSeasons} seasons" +
                    (string.IsNullOrEmpty(proposal.Text) ? "" : $"  Note: {proposal.Text}"),
                    p.x + 36, y, 744, 20) + 2;
                OrderButton(new Rect(p.x + 36, y, 176, 30), ActionType.RespondTreaty, proposal.FromSeatId, "Accept",
                    null, 0, 0, btn, new Dictionary<string, string> { { "response", "accept" } });
                OrderButton(new Rect(p.x + 218, y, 176, 30), ActionType.RespondTreaty, proposal.FromSeatId, "Reject",
                    null, 0, 0, btn, new Dictionary<string, string> { { "response", "reject" } });
                y += 36;
            }

            // Relations and the actions available against each other seat.
            int drawn = 0;
            foreach (var other in engine.State.Factions)
            {
                if (other.SeatId == currentSeat) continue;
                if (drawn == shownFactions)
                {
                    WrappedLabel(wrap, "... (remaining factions omitted)", p.x + 36, y, 744, 20);
                    break;
                }

                var relation = obs != null ? obs.Relation(other.SeatId) : null;
                int myTrust = relation != null ? relation.MyTrust : 0;
                int theirTrust = relation != null ? relation.TheirTrust : 0;
                bool atWar = relation != null && relation.AtWar;

                y += WrappedLabel(wrap,
                    $"{other.Name}  Trust me→them {myTrust} / them→me {theirTrust}  {StatusLine(other.SeatId)}",
                    p.x + 20, y, 760, 20) + 2;

                string hint = null;
                hint = FirstReason(hint, OrderButton(new Rect(p.x + 36, y, 176, 30), ActionType.DeclareWar,
                    other.SeatId, "Declare war", atWar ? "Already at war" : null, 0, 0, btn));
                hint = FirstReason(hint, OrderButton(new Rect(p.x + 218, y, 176, 30), ActionType.Gift,
                    other.SeatId, $"Gift {BalanceConfig.GiftGoldPerTrustUnit}",
                    Treasury() < BalanceConfig.GiftGoldPerTrustUnit ? "Not enough gold" : null,
                    BalanceConfig.GiftGoldPerTrustUnit, 0, btn));
                hint = FirstReason(hint, OrderButton(new Rect(p.x + 400, y, 176, 30), ActionType.SendMessage,
                    other.SeatId, "Send message", string.IsNullOrEmpty(envoyText) ? "Enter a message first" : null,
                    0, 0, btn, null, envoyText, () =>
                    {
                        envoyText = "";
                        envoyConfirmationUntil = Time.unscaledTime + 2f;
                    }));
                hint = FirstReason(hint, OrderButton(new Rect(p.x + 582, y, 182, 30), ActionType.BreakTreaty,
                    other.SeatId, "Break treaty", BreakBlocked(other.SeatId), 0, 0, btn,
                    new Dictionary<string, string> { { "treaty_type", BreakableType(other.SeatId) } }));
                y += 34;

                hint = FirstReason(hint, OrderButton(new Rect(p.x + 36, y, 236, 30), ActionType.ProposeTreaty,
                    other.SeatId, "Non-aggression pact", ProposalBlocked(other.SeatId, TreatyType.Nap), 0, 0, btn,
                    new Dictionary<string, string> { { "treaty_type", TreatyType.Nap } }));
                hint = FirstReason(hint, OrderButton(new Rect(p.x + 282, y, 236, 30), ActionType.ProposeTreaty,
                    other.SeatId, "Alliance", ProposalBlocked(other.SeatId, TreatyType.Alliance), 0, 0, btn,
                    new Dictionary<string, string> { { "treaty_type", TreatyType.Alliance } }));
                hint = FirstReason(hint, OrderButton(new Rect(p.x + 528, y, 236, 30), ActionType.ProposeTreaty,
                    other.SeatId, "Propose truce", ProposalBlocked(other.SeatId, TreatyType.Truce), 0, 0, btn,
                    new Dictionary<string, string> { { "treaty_type", TreatyType.Truce } }));
                y += 34;

                if (hint != null)
                    y += WrappedLabel(uiReasonWrap, hint, p.x + 20, y, 760, 20) + 2;
                y += 4;
                drawn++;
            }
            return y;
        }

        /// <summary>Peace / war plus every treaty in force (with seasons left) and any pending offer,
        /// read from this seat's observation only.</summary>
        string StatusLine(string seatId)
        {
            var relation = obs != null ? obs.Relation(seatId) : null;
            var sb = new StringBuilder(relation != null && relation.AtWar ? "At war" : "Peace");
            foreach (var type in new[] { TreatyType.Nap, TreatyType.Alliance, TreatyType.Truce })
            {
                var treaty = MyTreaty(type, seatId);
                if (treaty == null) continue;
                int seasonsLeft = treaty.ExpirySeason - engine.State.Season + 1;
                if (seasonsLeft < 1) seasonsLeft = 1;
                sb.Append($"  [{TreatyLabel(type)} {seasonsLeft} seasons left]");
            }
            var proposal = MyProposal(seatId);
            if (proposal != null)
                                sb.Append(" [").Append(proposal.FromSeatId == currentSeat ? "Our proposal pending" : "Your response needed")
                                    .Append(": ").Append(TreatyLabel(proposal.Type)).Append(']');
            return sb.ToString();
        }

        /// <summary>This seat's own treaty of that type with another seat, from the observation.</summary>
        TreatyView MyTreaty(string type, string otherSeatId)
        {
            if (obs == null) return null;
            foreach (var treaty in obs.Treaties)
                if (treaty.Type == type && treaty.Covers(currentSeat, otherSeatId)) return treaty;
            return null;
        }

        /// <summary>The pending offer between this seat and another, in either direction.</summary>
        ProposalView MyProposal(string otherSeatId)
        {
            if (obs == null) return null;
            foreach (var proposal in obs.Proposals)
                if ((proposal.FromSeatId == currentSeat && proposal.ToSeatId == otherSeatId)
                    || (proposal.ToSeatId == currentSeat && proposal.FromSeatId == otherSeatId))
                    return proposal;
            return null;
        }

        /// <summary>Reason a treaty offer is obviously illegal, or null when it can be sent.</summary>
        string ProposalBlocked(string seatId, string type)
        {
            if (MyTreaty(type, seatId) != null) return "Already active";
            if (MyProposal(seatId) != null) return "Proposal already pending";
            return null;
        }

        string BreakBlocked(string seatId)
            => BreakableType(seatId) == null ? "No treaty to break" : null;

        /// <summary>First treaty in force with that seat (nap → alliance → truce), or null.</summary>
        string BreakableType(string seatId)
        {
            foreach (var type in new[] { TreatyType.Nap, TreatyType.Alliance, TreatyType.Truce })
                if (MyTreaty(type, seatId) != null) return type;
            return null;
        }

        int Treasury()
        {
            int gold = 0;
            if (obs == null) return 0;
            foreach (var city in obs.CitiesOf(currentSeat)) gold += city.Gold;
            return gold;
        }

    }
}
