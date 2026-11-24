// HandoverBriefingBuilder.cs — constructs a strictly seat-filtered human reclaim briefing
// from Observation + seat-scoped LlmActivity captures + prompt truncation metadata.
// Never reads canonical engine state or hidden cross-seat event counts.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using LuanShi.Engine;

namespace LuanShi
{
    public sealed class HandoverReclaimBriefing
    {
        public string SeatId;
        public string FactionName;
        public int CurrentSeason;
        public string Title;
        public readonly List<string> ResourceLines = new List<string>();
        public readonly List<string> DiplomacyLines = new List<string>();
        public readonly List<string> TurnAndActionLines = new List<string>();
        public readonly List<string> PermittedEventLines = new List<string>();
        public readonly List<string> CompletenessAndPrivacyLines = new List<string>();

        public string FormatPlainText()
        {
            var sb = new StringBuilder(1024);
            AppendSection(sb, "Resources & State", ResourceLines);
            AppendSection(sb, "Diplomacy Standing", DiplomacyLines);
            AppendSection(sb, "LLM Turns, Outcomes & Model Statements", TurnAndActionLines);
            AppendSection(sb, "Permitted Events During Delegation", PermittedEventLines);
            AppendSection(sb, "Completeness, Truncation & Privacy", CompletenessAndPrivacyLines);
            return sb.ToString().TrimEnd();
        }

        private static void AppendSection(StringBuilder sb, string heading, List<string> lines)
        {
            sb.Append("== ").Append(heading).Append(" ==\n");
            if (lines.Count == 0)
                sb.Append("- (none)\n");
            else
                for (int i = 0; i < lines.Count; i++)
                    sb.Append("- ").Append(lines[i]).Append('\n');
            sb.Append('\n');
        }
    }

    public static class HandoverBriefingBuilder
    {
        public static HandoverReclaimBriefing Build(
            string seatId,
            Observation obs,
            HandoverDelegationEpisode episode,
            int promptEventCap = PromptBuilder.MaxPromptObservationEvents)
        {
            string factionName = obs?.Faction(seatId)?.Name ?? episode?.FactionName ?? seatId ?? "Faction";
            int season = obs != null ? obs.Season : (episode != null ? episode.StartedSeason : 1);

            var briefing = new HandoverReclaimBriefing
            {
                SeatId = seatId,
                FactionName = factionName,
                CurrentSeason = season,
                Title = $"{factionName} — Control Reclaimed (Season {season})",
            };

            BuildResourceSection(briefing, seatId, obs, episode);
            BuildDiplomacySection(briefing, seatId, obs);
            BuildTurnsAndStatementsSection(briefing, seatId, obs, episode);
            BuildPermittedEventsSection(briefing, seatId, obs, episode);
            BuildCompletenessSection(briefing, obs, episode, promptEventCap);
            return briefing;
        }

        private static void BuildResourceSection(
            HandoverReclaimBriefing briefing, string seatId, Observation obs, HandoverDelegationEpisode episode)
        {
            if (obs == null)
            {
                briefing.ResourceLines.Add("Missing current observation snapshot for " + briefing.FactionName + ".");
                return;
            }

            int cities = 0, food = 0, gold = 0, pop = 0;
            foreach (CityView c in obs.CitiesOf(seatId))
            {
                cities++;
                food += c.Food;
                gold += c.Gold;
                pop += c.Population;
            }

            briefing.ResourceLines.Add(
                $"Current: Cities {cities} | Food {food} | Gold {gold} | Pop {pop} | Prestige {obs.Prestige} | Orders {obs.CommandPoints}/{BalanceConfig.CommandPointsPerSeason}");

            if (episode != null && episode.HasPreHandoverBaseline)
            {
                briefing.ResourceLines.Add(
                    $"Change since Season {episode.StartedSeason} handover: Cities {FormatDelta(cities - episode.PreCitiesCount)}, " +
                    $"Food {FormatDelta(food - episode.PreTotalFood)}, Gold {FormatDelta(gold - episode.PreTotalGold)}, " +
                    $"Pop {FormatDelta(pop - episode.PreTotalPopulation)}, Prestige {FormatDelta(obs.Prestige - episode.PrePrestige)}");
            }
            else
            {
                briefing.ResourceLines.Add("Pre-delegation resource baseline: unavailable (missing pre-handover snapshot).");
            }
        }

        private static void BuildDiplomacySection(HandoverReclaimBriefing briefing, string seatId, Observation obs)
        {
            if (obs == null) return;

            int incomingCount = 0;
            for (int i = 0; i < obs.Proposals.Count; i++)
            {
                ProposalView p = obs.Proposals[i];
                if (!string.Equals(p.ToSeatId, seatId, StringComparison.Ordinal)) continue;
                incomingCount++;
                string sender = FactionLabel(obs, p.FromSeatId);
                string note = string.IsNullOrEmpty(p.Text) ? "" : $" Note: \"{p.Text}\"";
                briefing.DiplomacyLines.Add(
                    $"Incoming proposal: {sender} offers {p.Type} ({p.DurationSeasons} seasons).{note}");
            }
            if (incomingCount == 0)
                briefing.DiplomacyLines.Add("Incoming treaty proposals: none.");

            for (int i = 0; i < obs.Treaties.Count; i++)
            {
                TreatyView t = obs.Treaties[i];
                if (!string.Equals(t.FactionA, seatId, StringComparison.Ordinal)
                    && !string.Equals(t.FactionB, seatId, StringComparison.Ordinal))
                    continue;
                string other = string.Equals(t.FactionA, seatId, StringComparison.Ordinal) ? t.FactionB : t.FactionA;
                briefing.DiplomacyLines.Add(
                    $"Active treaty with {FactionLabel(obs, other)}: {t.Type} (expires season {t.ExpirySeason}).");
            }

            for (int i = 0; i < obs.Relations.Count; i++)
            {
                RelationView r = obs.Relations[i];
                if (string.Equals(r.SeatId, seatId, StringComparison.Ordinal)) continue;
                briefing.DiplomacyLines.Add(
                    $"{FactionLabel(obs, r.SeatId)}: {(r.AtWar ? "At war" : "Peace")} (Trust me->them {r.MyTrust}, them->me {r.TheirTrust}).");
            }
        }

        private static void BuildTurnsAndStatementsSection(
            HandoverReclaimBriefing briefing, string seatId, Observation obs, HandoverDelegationEpisode episode)
        {
            if (episode == null || episode.Turns.Count == 0)
            {
                briefing.TurnAndActionLines.Add("No completed LLM turn captures recorded during this delegation.");
                AppendFallbackLlmEvents(briefing, seatId, obs, episode != null ? episode.BaselineEventSeq : 0);
                return;
            }

            for (int t = 0; t < episode.Turns.Count; t++)
            {
                HandoverSeatTurnCapture turn = episode.Turns[t];
                string summaryHeader = $"Season {turn.Season}: {turn.EndReasonText ?? "Turn closed"} " +
                                       $"({turn.ActionsThisTurn} action(s), {turn.RequestsThisTurn} request(s))";
                if (!string.IsNullOrEmpty(turn.FailureReason))
                    summaryHeader += $" | Failure: {turn.FailureReason}";
                briefing.TurnAndActionLines.Add(summaryHeader);

                bool hasDecision = false;
                for (int a = 0; a < turn.Activities.Count; a++)
                {
                    LlmActivity act = turn.Activities[a];
                    if (act.Kind == "decision")
                    {
                        hasDecision = true;
                        briefing.TurnAndActionLines.Add($"  [Model statement] {act.Text}");
                    }
                    else if (act.Kind == "command")
                    {
                        briefing.TurnAndActionLines.Add($"  [Command] {act.Text}");
                    }
                    else if (act.Kind == "result" || act.Kind == "validation" || act.Kind == "error")
                    {
                        briefing.TurnAndActionLines.Add($"  [Outcome:{act.Kind}] {act.Text}");
                    }
                }

                if (turn.ActivityGapDetected)
                    briefing.TurnAndActionLines.Add(
                        "  [Missing] Runner activity trace was unavailable or evicted for this turn; check permitted events below.");
                else if (!hasDecision && turn.ActionsThisTurn > 0)
                    briefing.TurnAndActionLines.Add(
                        "  [Missing] No model-authored decision statement recorded for this turn.");
            }
        }

        private static void AppendFallbackLlmEvents(
            HandoverReclaimBriefing briefing, string seatId, Observation obs, int baselineSeq)
        {
            if (obs == null) return;
            for (int i = 0; i < obs.Events.Count; i++)
            {
                EventView ev = obs.Events[i];
                if (ev.Seq <= baselineSeq) continue;
                if (string.Equals(ev.ActorSeatId, seatId, StringComparison.Ordinal)
                    && string.Equals(ev.ControllerType, ControllerType.Llm, StringComparison.Ordinal))
                {
                    briefing.TurnAndActionLines.Add(
                        $"[Authoritative event #{ev.Seq}, S{ev.Season}] {FormatEventLine(obs, ev)} (model explanation unavailable)");
                }
            }
        }

        private static void BuildPermittedEventsSection(
            HandoverReclaimBriefing briefing, string seatId, Observation obs, HandoverDelegationEpisode episode)
        {
            if (obs == null) return;
            int baselineSeq = episode != null ? episode.BaselineEventSeq : 0;
            const int MaxShownEvents = 30;

            var relevant = new List<EventView>();
            for (int i = 0; i < obs.Events.Count; i++)
            {
                EventView ev = obs.Events[i];
                if (ev.Seq > baselineSeq) relevant.Add(ev);
            }

            if (relevant.Count == 0)
            {
                briefing.PermittedEventLines.Add("No new permitted events recorded since handover.");
                return;
            }

            int start = Math.Max(0, relevant.Count - MaxShownEvents);
            if (start > 0)
                briefing.PermittedEventLines.Add(
                    $"Showing newest {relevant.Count - start} of {relevant.Count} permitted events for {briefing.FactionName} (older entries in History tab).");

            for (int i = start; i < relevant.Count; i++)
                briefing.PermittedEventLines.Add(FormatEventLine(obs, relevant[i]));
        }

        private static void BuildCompletenessSection(
            HandoverReclaimBriefing briefing, Observation obs, HandoverDelegationEpisode episode, int promptEventCap)
        {
            if (episode == null || episode.Turns.Count == 0)
            {
                briefing.CompletenessAndPrivacyLines.Add("LLM prompt context diagnostics: unavailable (no runner turn captured).");
            }
            else
            {
                if (episode.ActivityRetentionDropped)
                    briefing.CompletenessAndPrivacyLines.Add(
                        $"UI activity buffer truncated: retained latest {HandoverDelegationEpisode.MaxCapturedActivities} entries for {briefing.FactionName}; older statements omitted.");

                for (int t = 0; t < episode.Turns.Count; t++)
                    AppendTurnTruncationLine(briefing.CompletenessAndPrivacyLines, episode.Turns[t], promptEventCap);
            }

            // Explicitly never count canonical / foreign hidden events.
            briefing.CompletenessAndPrivacyLines.Add(
                "Model explanations are model-authored statements (unverified intent).");
            briefing.CompletenessAndPrivacyLines.Add(
                "Other factions' private tech, notes, and unseen events: hidden (not shown and never counted).");
        }

        private static void AppendTurnTruncationLine(
            List<string> lines, HandoverSeatTurnCapture turn, int promptEventCap)
        {
            if (!string.IsNullOrEmpty(turn.ContextReportJson)
                && StrictJson.TryParse(turn.ContextReportJson, out JsonNode root, out _)
                && root is JsonObject obj)
            {
                long trimLevel = ReadInt(obj, "trim_level", 0);
                bool fits = ReadBool(obj, "fits", true);
                var truncatedParts = new List<string>();
                if (obj.TryGet("sections", out JsonNode secNode) && secNode is JsonArray arr)
                {
                    for (int i = 0; i < arr.Count; i++)
                    {
                        if (!(arr.Items[i] is JsonObject sec)) continue;
                        if (!ReadBool(sec, "truncated", false)) continue;
                        string name = ReadStr(sec, "name") ?? "section";
                        long inc = ReadInt(sec, "included", 0);
                        long avail = ReadInt(sec, "available", -1);
                        if (avail >= 0) truncatedParts.Add($"{name} ({inc}/{avail})");
                    }
                }
                string status = fits ? $"trim level {trimLevel}" : $"trim level {trimLevel}, prompt exceeded limit";
                string detail = truncatedParts.Count > 0
                    ? "Truncated sections: " + string.Join(", ", truncatedParts.ToArray())
                    : "All measured own-faction sections fit without section truncation";
                lines.Add($"Season {turn.Season} prompt completeness ({status}): {detail}.");
            }
            else if (turn.ObservationEventsAtTurnEnd > promptEventCap)
            {
                lines.Add(
                    $"Season {turn.Season} prompt completeness: standard mode capped observation events at {promptEventCap} of {turn.ObservationEventsAtTurnEnd} permitted events.");
            }
            else
            {
                lines.Add($"Season {turn.Season} prompt completeness: rich section breakdown unavailable (standard mode).");
            }

            if (turn.SpatialTruncation != null && turn.SpatialTruncation.Truncated)
            {
                lines.Add(
                    $"Season {turn.Season} spatial truncation: omitted {turn.SpatialTruncation.MoveOptionsDropped} own move options, " +
                    $"{turn.SpatialTruncation.VisibleCellRangesDropped} visible hex rows, {turn.SpatialTruncation.StaleIntelDropped} stale intel items.");
            }
        }

        private static string FormatEventLine(Observation obs, EventView ev)
        {
            string actor = FactionLabel(obs, ev.ActorSeatId);
            string ctrl = string.IsNullOrEmpty(ev.ControllerType) ? "-" : ev.ControllerType;
            string detail = !string.IsNullOrEmpty(ev.Text) ? $" \"{ev.Text}\"" : "";
            return $"#{ev.Seq} S{ev.Season} [{actor} · {ctrl}] {ev.EventType}{detail}";
        }

        private static string FactionLabel(Observation obs, string seatId)
        {
            if (string.IsNullOrEmpty(seatId)) return "System";
            FactionView view = obs != null ? obs.Faction(seatId) : null;
            return view != null && !string.IsNullOrEmpty(view.Name) ? view.Name : seatId;
        }

        private static string FormatDelta(int delta)
            => delta >= 0 ? "+" + delta.ToString(CultureInfo.InvariantCulture) : delta.ToString(CultureInfo.InvariantCulture);

        private static long ReadInt(JsonObject obj, string key, long fallback)
            => obj.TryGet(key, out JsonNode n) && n is JsonInt i ? i.Value : fallback;

        private static bool ReadBool(JsonObject obj, string key, bool fallback)
            => obj.TryGet(key, out JsonNode n) && n is JsonBool b ? b.Value : fallback;

        private static string ReadStr(JsonObject obj, string key)
            => obj.TryGet(key, out JsonNode n) && n is JsonString s ? s.Value : null;
    }
}
