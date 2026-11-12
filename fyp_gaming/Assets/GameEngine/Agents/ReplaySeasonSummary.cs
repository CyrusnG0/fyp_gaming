using System;
using System.Collections.Generic;

namespace LuanShi.Engine
{
    /// <summary>
    /// One seat's recorded result for one season. Every value comes from the saved run; a value the
    /// records do not establish is "unknown" (strings) or null (<see cref="Lookups"/>), never zero.
    /// </summary>
    public sealed class ReplaySideSeasonSummary
    {
        public string SeatId;
        public string Faction;
        public string BeforeFood = "unknown";
        public string AfterFood = "unknown";
        public string BeforeGold = "unknown";
        public string AfterGold = "unknown";
        public string BeforeGarrison = "unknown";
        public string AfterGarrison = "unknown";
        public string TroopSummary = "unknown";
        public string ActionCountsSummary = "unknown";

        /// <summary>Game actions whose recorded result is accepted (end_turn is never counted here).</summary>
        public int AcceptedActions;
        /// <summary>Parsed game actions whose recorded result is rejected by the engine.</summary>
        public int RejectedActions;
        /// <summary>Requests that produced no valid game action (unparseable reply or unusable inquiry).</summary>
        public int InvalidRequests;
        /// <summary>Voluntary end_turn replies, reported apart from game actions.</summary>
        public int EndTurns;
        /// <summary>Parsed game actions whose record carries no result, so acceptance is not established.</summary>
        public int UnrecordedOutcomes;

        /// <summary>Answered manual_lookup inquiries; null when the records do not establish the count.</summary>
        public int? Lookups;
        public string LookupsDisplay => Lookups.HasValue ? Lookups.Value.ToString() : "unknown";
    }

    public sealed class ReplaySeasonSummaryItem
    {
        public int Season;
        public ReplaySideSeasonSummary Wei;
        public ReplaySideSeasonSummary Shu;
    }

    public static class ReplaySeasonSummaryBuilder
    {
        private const string Unknown = "unknown";
        private const string ManualLookupTool = "manual_lookup";

        public static List<ReplaySeasonSummaryItem> Build(ReplayBundle bundle)
        {
            var result = new List<ReplaySeasonSummaryItem>();
            if (bundle == null) return result;

            int totalSeasons = bundle.Summary != null ? bundle.Summary.SeasonsPlayed : 0;
            if (totalSeasons <= 0) totalSeasons = MaxRecordedSeason(bundle);

            for (int s = 1; s <= totalSeasons; s++)
            {
                result.Add(new ReplaySeasonSummaryItem
                {
                    Season = s,
                    Wei = BuildSide(bundle, s, "seat-0", "Wei"),
                    Shu = BuildSide(bundle, s, "seat-1", "Shu")
                });
            }
            return result;
        }

        private static int MaxRecordedSeason(ReplayBundle bundle)
        {
            int max = 0;
            if (bundle.Requests != null)
                for (int i = 0; i < bundle.Requests.Count; i++)
                    if (bundle.Requests[i] != null && bundle.Requests[i].Season > max) max = bundle.Requests[i].Season;
            return max;
        }

        private static ReplaySideSeasonSummary BuildSide(ReplayBundle bundle, int season, string seatId, string factionName)
        {
            var side = new ReplaySideSeasonSummary { SeatId = seatId, Faction = factionName };
            CountRequests(bundle, season, seatId, side);

            ReplaySeatScore prevScore = FindScore(bundle, season - 1, seatId);
            ReplaySeatScore curScore = FindScore(bundle, season, seatId);

            if (prevScore != null)
            {
                side.BeforeFood = FormatField(prevScore, "food", prevScore.Food);
                side.BeforeGold = FormatField(prevScore, "gold", prevScore.Gold);
                side.BeforeGarrison = FormatField(prevScore, "garrison", prevScore.Garrison);
            }
            else
            {
                ReplayRequestRecord opening = FindOpeningRecord(bundle, seatId);
                if (opening != null && opening.Season == season)
                {
                    side.BeforeFood = OpeningTotal(opening, seatId, "food");
                    side.BeforeGold = OpeningTotal(opening, seatId, "gold");
                    side.BeforeGarrison = OpeningTotal(opening, seatId, "garrison");
                }
            }

            if (curScore != null)
            {
                side.AfterFood = FormatField(curScore, "food", curScore.Food);
                side.AfterGold = FormatField(curScore, "gold", curScore.Gold);
                side.AfterGarrison = FormatField(curScore, "garrison", curScore.Garrison);
            }

            side.TroopSummary = $"Garrison {side.BeforeGarrison} -> {side.AfterGarrison} | Field: unknown (not in scoreboard) | Total: unknown";
            return side;
        }

        // Classifies each recorded request of the seat for the season. Acceptance is claimed only
        // from a recorded result_ok; an inquiry is counted only when its own record shows it was
        // answered; a record that cannot be classified leaves the lookup count unknown.
        private static void CountRequests(ReplayBundle bundle, int season, string seatId, ReplaySideSeasonSummary side)
        {
            var accepted = new Dictionary<string, int>(StringComparer.Ordinal);
            int rows = 0, lookups = 0;
            bool lookupsKnown = true;

            if (bundle.Requests != null)
            {
                for (int i = 0; i < bundle.Requests.Count; i++)
                {
                    var req = bundle.Requests[i];
                    if (req == null || req.Season != season || !string.Equals(req.SeatId, seatId, StringComparison.Ordinal))
                        continue;
                    rows++;

                    string tool = InquiryToolOf(req);
                    if (tool != null)
                    {
                        if (!IsKnownInquiryTool(tool))
                        {
                            lookupsKnown = false;
                            side.InvalidRequests++;
                        }
                        else if (string.Equals(tool, ManualLookupTool, StringComparison.Ordinal))
                        {
                            // Successful inquiry records use result_ok:null; inquiry_result_chars
                            // is the affirmative evidence that the host produced an answer.
                            if (req.InquiryResultChars.HasValue && string.IsNullOrEmpty(req.ResultError)) lookups++;
                            else if (!string.IsNullOrEmpty(req.ResultError)) side.InvalidRequests++;
                            else
                            {
                                lookupsKnown = false;
                                side.UnrecordedOutcomes++;
                            }
                        }
                        else if (!req.ResultRecorded && !req.InquiryResultChars.HasValue
                                 && string.IsNullOrEmpty(req.ResultError))
                        {
                            lookupsKnown = false;
                            side.UnrecordedOutcomes++;
                        }
                        else if (!req.ResultOk && req.ResultRecorded) side.InvalidRequests++;
                        continue;
                    }

                    if (!req.ParseOk) { lookupsKnown = false; side.InvalidRequests++; continue; }
                    if (string.IsNullOrEmpty(req.RawModelText))
                    {
                        // Parsed but the text was not kept: neither the action nor a lookup is established.
                        side.UnrecordedOutcomes++;
                        lookupsKnown = false;
                        continue;
                    }

                    ParseResult p = LlmActionParser.Parse(req.RawModelText);
                    if (!p.Ok || p.Action == null) { lookupsKnown = false; side.InvalidRequests++; continue; }
                    if (p.Action.EndTurn) { side.EndTurns++; continue; }

                        if (!req.ResultRecorded) side.UnrecordedOutcomes++;
                    else if (!req.ResultOk) side.RejectedActions++;
                    else
                    {
                        side.AcceptedActions++;
                        string type = string.IsNullOrEmpty(p.Action.Type) ? Unknown : p.Action.Type;
                        accepted.TryGetValue(type, out int cur);
                        accepted[type] = cur + 1;
                    }
                }
            }

            if (rows == 0) return; // nothing recorded for this seat/season: counts stay unknown
            side.Lookups = lookupsKnown ? (int?)lookups : null;

            var parts = new List<string>();
            foreach (var kvp in accepted) parts.Add($"{kvp.Key}: {kvp.Value}");
            parts.Sort(StringComparer.Ordinal);
            string detail = parts.Count > 0 ? " (" + string.Join(", ", parts) + ")" : "";
            string summary = $"accepted {side.AcceptedActions}{detail} | rejected {side.RejectedActions} | invalid {side.InvalidRequests} | end_turn {side.EndTurns}";
            if (side.UnrecordedOutcomes > 0) summary += $" | outcome unrecorded {side.UnrecordedOutcomes}";
            side.ActionCountsSummary = summary;
        }

        // Tool name when the record is an inquiry (a reply object carrying a "tool" field, or a
        // recorded inquiry_tool); null for an ordinary action reply.
        private static string InquiryToolOf(ReplayRequestRecord req)
        {
            if (!string.IsNullOrEmpty(req.InquiryTool)) return req.InquiryTool;
            if (string.IsNullOrEmpty(req.RawModelText)) return null;
            if (!StrictJson.TryParse(req.RawModelText, out JsonNode root, out _)) return Unknown;
            if (!(root is JsonObject obj)) return null;
            if (!obj.TryGet("tool", out JsonNode toolNode)) return null;
            if (!(toolNode is JsonString s) || string.IsNullOrWhiteSpace(s.Value)) return Unknown;
            return s.Value;
        }

        private static bool IsKnownInquiryTool(string tool)
            => string.Equals(tool, InvestigationRequestContract.ManualLookup, StringComparison.Ordinal)
               || string.Equals(tool, InvestigationRequestContract.InspectEntity, StringComparison.Ordinal)
               || string.Equals(tool, InvestigationRequestContract.SearchHistory, StringComparison.Ordinal)
               || string.Equals(tool, InvestigationRequestContract.RecallMemory, StringComparison.Ordinal);

        private static ReplayRequestRecord FindOpeningRecord(ReplayBundle bundle, string seatId)
        {
            if (bundle.Requests == null) return null;
            for (int i = 0; i < bundle.Requests.Count; i++)
            {
                var req = bundle.Requests[i];
                if (req != null && string.Equals(req.SeatId, seatId, StringComparison.Ordinal)) return req;
            }
            return null;
        }

        // Sum of one field over the seat's own cities in its recorded opening observation, or
        // "unknown" when the observation is absent, unreadable, or lacks the field on any own city.
        private static string OpeningTotal(ReplayRequestRecord opening, string seatId, string field)
        {
            if (string.IsNullOrEmpty(opening.ObservationJson)) return Unknown;
            if (!StrictJson.TryParseLenientNumbers(opening.ObservationJson, out JsonNode root, out _) || !(root is JsonObject obs)) return Unknown;
            if (!obs.TryGet("seat_id", out JsonNode seatNode) || !(seatNode is JsonString seat)
                || !string.Equals(seat.Value, seatId, StringComparison.Ordinal)) return Unknown;
            if (!obs.TryGet("cities", out JsonNode citiesNode) || !(citiesNode is JsonArray cities)) return Unknown;

            long total = 0;
            int own = 0;
            foreach (JsonNode node in cities.Items)
            {
                if (!(node is JsonObject city)) continue;
                if (!city.TryGet("owner_seat_id", out JsonNode ownerNode) || !(ownerNode is JsonString owner)
                    || !string.Equals(owner.Value, seatId, StringComparison.Ordinal)) continue;
                if (!city.TryGet(field, out JsonNode valueNode) || !(valueNode is JsonInt value)) return Unknown;
                total += value.Value;
                own++;
            }
            return own > 0 ? total.ToString() : Unknown;
        }

        private static ReplaySeatScore FindScore(ReplayBundle bundle, int season, string seatId)
        {
            if (bundle?.Summary?.Scoreboards == null) return null;
            for (int i = 0; i < bundle.Summary.Scoreboards.Count; i++)
            {
                var sb = bundle.Summary.Scoreboards[i];
                if (sb.Season == season && sb.Scores != null)
                {
                    for (int j = 0; j < sb.Scores.Count; j++)
                    {
                        var sc = sb.Scores[j];
                        if (string.Equals(sc.SeatId, seatId, StringComparison.Ordinal))
                            return sc;
                    }
                }
            }
            return null;
        }

        private static string FormatField(ReplaySeatScore sc, string field, int val)
        {
            if (sc == null) return Unknown;
            if (sc.MissingFields != null)
            {
                for (int i = 0; i < sc.MissingFields.Count; i++)
                    if (string.Equals(sc.MissingFields[i], field, StringComparison.Ordinal))
                        return Unknown;
            }
            return val.ToString();
        }
    }
}
