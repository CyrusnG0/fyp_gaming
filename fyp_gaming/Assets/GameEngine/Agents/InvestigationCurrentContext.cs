using System;
using System.Collections.Generic;
using System.Text;

namespace LuanShi.Engine
{
    /// <summary>
    /// Builds concise current research/diplomacy context only from a validated, detached seat
    /// observation. It is data, not an action list or a claim that any order is currently legal.
    /// </summary>
    internal static class InvestigationCurrentContextBuilder
    {
        private const string ContextScope = "current_observation_data_not_action_availability";

        public static ManualLookupCurrentContext Build(string kind, Observation observation,
            string expectedSeatId)
        {
            if (observation == null || string.IsNullOrEmpty(expectedSeatId)
                || !string.Equals(observation.SeatId, expectedSeatId, StringComparison.Ordinal))
                return null;

            var context = new ManualLookupCurrentContext
            {
                Kind = kind,
                SeatId = observation.SeatId,
                Season = observation.Season,
                Scope = ContextScope,
            };

            if (kind == "research")
            {
                context.ResearchPoints = observation.ResearchPoints;
                context.Research = new List<ManualLookupResearchLine>();
                foreach (string line in ResearchLine.All)
                {
                    int level = observation.ResearchLevel(line);
                    context.Research.Add(new ManualLookupResearchLine
                    {
                        Line = line,
                        Level = level,
                        NextLevelCost = level >= BalanceConfig.ResearchMaxLevel
                            ? (int?)null : BalanceConfig.ResearchLevelCost(level + 1),
                    });
                }
                return context;
            }

            if (kind != "diplomacy") return null;

            context.Relations = new List<ManualLookupRelation>();
            if (observation.Relations != null)
            {
                var relations = new List<RelationView>();
                foreach (RelationView relation in observation.Relations)
                    if (relation != null && !string.IsNullOrEmpty(relation.SeatId)
                        && !string.Equals(relation.SeatId, expectedSeatId, StringComparison.Ordinal))
                        relations.Add(relation);
                relations.Sort((a, b) => StringComparer.Ordinal.Compare(a.SeatId, b.SeatId));
                foreach (RelationView relation in relations)
                    context.Relations.Add(new ManualLookupRelation
                    {
                        SeatId = relation.SeatId,
                        MyTrust = relation.MyTrust,
                        TheirTrust = relation.TheirTrust,
                        AtWar = relation.AtWar,
                    });
            }

            context.Treaties = new List<ManualLookupTreaty>();
            if (observation.Treaties != null)
            {
                var treaties = new List<TreatyView>();
                foreach (TreatyView treaty in observation.Treaties)
                    if (treaty != null && !string.IsNullOrEmpty(PartnerOf(treaty, expectedSeatId)))
                        treaties.Add(treaty);
                treaties.Sort((a, b) =>
                {
                    int partner = StringComparer.Ordinal.Compare(PartnerOf(a, expectedSeatId), PartnerOf(b, expectedSeatId));
                    if (partner != 0) return partner;
                    int type = StringComparer.Ordinal.Compare(a.Type, b.Type);
                    return type != 0 ? type : a.ExpirySeason.CompareTo(b.ExpirySeason);
                });
                foreach (TreatyView treaty in treaties)
                {
                    string partner = PartnerOf(treaty, expectedSeatId);
                    if (string.IsNullOrEmpty(partner)) continue;
                    context.Treaties.Add(new ManualLookupTreaty
                    {
                        Type = treaty.Type,
                        PartnerSeatId = partner,
                        ExpirySeason = treaty.ExpirySeason,
                        SeasonsRemainingIncludingCurrent = Math.Max(0, treaty.ExpirySeason - observation.Season + 1),
                    });
                }
            }

            context.Proposals = new List<ManualLookupProposal>();
            if (observation.Proposals != null)
            {
                var proposals = new List<ProposalView>();
                foreach (ProposalView proposal in observation.Proposals)
                    if (proposal != null && !string.IsNullOrEmpty(proposal.ProposalId)
                        && (string.Equals(proposal.FromSeatId, expectedSeatId, StringComparison.Ordinal)
                            || string.Equals(proposal.ToSeatId, expectedSeatId, StringComparison.Ordinal)))
                        proposals.Add(proposal);
                proposals.Sort((a, b) =>
                {
                    int proposed = a.ProposedSeason.CompareTo(b.ProposedSeason);
                    if (proposed != 0) return proposed;
                    int direction = StringComparer.Ordinal.Compare(DirectionOf(a, expectedSeatId), DirectionOf(b, expectedSeatId));
                    if (direction != 0) return direction;
                    return StringComparer.Ordinal.Compare(a.ProposalId, b.ProposalId);
                });
                foreach (ProposalView proposal in proposals)
                {
                    string counterparty = string.Equals(proposal.FromSeatId, expectedSeatId, StringComparison.Ordinal)
                        ? proposal.ToSeatId : proposal.FromSeatId;
                    if (string.IsNullOrEmpty(counterparty)) continue;
                    context.Proposals.Add(new ManualLookupProposal
                    {
                        Direction = DirectionOf(proposal, expectedSeatId),
                        CounterpartySeatId = counterparty,
                        TreatyType = proposal.Type,
                        DurationSeasons = proposal.DurationSeasons,
                        ProposedSeason = proposal.ProposedSeason,
                        SeasonsSinceProposed = Math.Max(0, observation.Season - proposal.ProposedSeason),
                        Accepted = proposal.Accepted,
                    });
                }
            }
            return context;
        }

        public static bool IsExactTopic(string query, string topicId)
        {
            if (string.IsNullOrEmpty(query) || (topicId != "research" && topicId != "diplomacy")) return false;
            string normalized = InvestigationText.Normalize(query);
            foreach (InvestigationTopicDefinition topic in InvestigationManualTopics.All)
            {
                if (!string.Equals(topic.Id, topicId, StringComparison.Ordinal)) continue;
                if (InvestigationText.Normalize(topic.Id) == normalized) return true;
                if (topic.Aliases == null) return false;
                foreach (string alias in topic.Aliases)
                    if (InvestigationText.Normalize(alias) == normalized) return true;
                return false;
            }
            return false;
        }

        private static string DirectionOf(ProposalView proposal, string seatId)
            => string.Equals(proposal.ToSeatId, seatId, StringComparison.Ordinal) ? "inbound" : "outbound";

        private static string PartnerOf(TreatyView treaty, string seatId)
        {
            if (treaty == null) return null;
            if (string.Equals(treaty.FactionA, seatId, StringComparison.Ordinal)) return treaty.FactionB;
            if (string.Equals(treaty.FactionB, seatId, StringComparison.Ordinal)) return treaty.FactionA;
            return null;
        }

    }

    /// <summary>Structured current seat context attached to exact research/diplomacy manual answers.</summary>
    internal sealed class ManualLookupCurrentContext
    {
        public string Kind;
        public string SeatId;
        public int Season;
        public string Scope;
        public int? ResearchPoints;
        public List<ManualLookupResearchLine> Research;
        public List<ManualLookupRelation> Relations;
        public List<ManualLookupTreaty> Treaties;
        public List<ManualLookupProposal> Proposals;

        public string ToJson()
        {
            var sb = new StringBuilder(512);
            sb.Append('{').Append("\"kind\":").Append(MiniJson.Str(Kind));
            sb.Append(",\"seat_id\":").Append(MiniJson.Str(SeatId));
            sb.Append(",\"season\":").Append(MiniJson.Num(Season));
            sb.Append(",\"scope\":").Append(MiniJson.Str(Scope));
            if (ResearchPoints.HasValue)
            {
                sb.Append(",\"research_points\":").Append(MiniJson.Num(ResearchPoints.Value));
                var lines = new List<string>();
                if (Research != null)
                    foreach (ManualLookupResearchLine line in Research) lines.Add(line.ToJson());
                sb.Append(",\"research\":").Append(ObjectArray(lines));
            }
            if (Relations != null)
            {
                var items = new List<string>();
                foreach (ManualLookupRelation relation in Relations) items.Add(relation.ToJson());
                sb.Append(",\"relations\":").Append(ObjectArray(items));
            }
            if (Treaties != null)
            {
                var items = new List<string>();
                foreach (ManualLookupTreaty treaty in Treaties) items.Add(treaty.ToJson());
                sb.Append(",\"treaties\":").Append(ObjectArray(items));
            }
            if (Proposals != null)
            {
                var items = new List<string>();
                foreach (ManualLookupProposal proposal in Proposals) items.Add(proposal.ToJson());
                sb.Append(",\"proposals\":").Append(ObjectArray(items));
            }
            return sb.Append('}').ToString();
        }

        internal bool TrimForResponse()
        {
            // Preserve incoming and outgoing decisions before general relation data.
            if (Relations != null && Relations.Count > 0) { Relations.RemoveAt(Relations.Count - 1); return true; }
            if (Treaties != null && Treaties.Count > 0) { Treaties.RemoveAt(Treaties.Count - 1); return true; }
            if (Proposals != null && Proposals.Count > 0) { Proposals.RemoveAt(Proposals.Count - 1); return true; }
            return false;
        }

        private static string ObjectArray(IEnumerable<string> values) => MiniJson.Array(values);
    }

    internal sealed class ManualLookupResearchLine
    {
        public string Line;
        public int Level;
        public int? NextLevelCost;
        internal string ToJson()
            => "{\"line\":" + MiniJson.Str(Line) + ",\"level\":" + MiniJson.Num(Level)
                + ",\"next_level_cost\":" + (NextLevelCost.HasValue ? MiniJson.Num(NextLevelCost.Value) : "null") + "}";
    }

    internal sealed class ManualLookupRelation
    {
        public string SeatId;
        public int MyTrust;
        public int TheirTrust;
        public bool AtWar;
        internal string ToJson()
            => "{\"seat_id\":" + MiniJson.Str(SeatId) + ",\"my_trust\":" + MiniJson.Num(MyTrust)
                + ",\"their_trust\":" + MiniJson.Num(TheirTrust) + ",\"at_war\":" + (AtWar ? "true" : "false") + "}";
    }

    internal sealed class ManualLookupTreaty
    {
        public string Type;
        public string PartnerSeatId;
        public int ExpirySeason;
        public int SeasonsRemainingIncludingCurrent;
        internal string ToJson()
            => "{\"type\":" + MiniJson.Str(Type) + ",\"partner_seat_id\":" + MiniJson.Str(PartnerSeatId)
                + ",\"expiry_season\":" + MiniJson.Num(ExpirySeason)
                + ",\"seasons_remaining_including_current\":" + MiniJson.Num(SeasonsRemainingIncludingCurrent) + "}";
    }

    internal sealed class ManualLookupProposal
    {
        public string Direction;
        public string CounterpartySeatId;
        public string TreatyType;
        public int DurationSeasons;
        public int ProposedSeason;
        public int SeasonsSinceProposed;
        public bool Accepted;
        internal string ToJson()
            => "{\"direction\":" + MiniJson.Str(Direction) + ",\"counterparty_seat_id\":" + MiniJson.Str(CounterpartySeatId)
                + ",\"treaty_type\":" + MiniJson.Str(TreatyType)
                + ",\"duration_seasons\":" + MiniJson.Num(DurationSeasons)
                + ",\"proposed_season\":" + MiniJson.Num(ProposedSeason)
                + ",\"seasons_since_proposed\":" + MiniJson.Num(SeasonsSinceProposed)
                + ",\"accepted\":" + (Accepted ? "true" : "false") + "}";
    }
}
