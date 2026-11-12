using System;
using System.Collections.Generic;

namespace LuanShi.Engine
{
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
        public string ActionCountsSummary = "none";
        public int Lookups;
    }

    public sealed class ReplaySeasonSummaryItem
    {
        public int Season;
        public ReplaySideSeasonSummary Wei;
        public ReplaySideSeasonSummary Shu;
    }

    public static class ReplaySeasonSummaryBuilder
    {
        public static List<ReplaySeasonSummaryItem> Build(ReplayBundle bundle)
        {
            var result = new List<ReplaySeasonSummaryItem>();
            if (bundle == null) return result;

            int totalSeasons = bundle.Summary != null ? Math.Max(bundle.Summary.SeasonsPlayed, bundle.Summary.SeasonsPlanned) : 5;
            if (totalSeasons <= 0) totalSeasons = 5;

            for (int s = 1; s <= totalSeasons; s++)
            {
                var item = new ReplaySeasonSummaryItem
                {
                    Season = s,
                    Wei = BuildSide(bundle, s, "seat-0", "Wei"),
                    Shu = BuildSide(bundle, s, "seat-1", "Shu")
                };
                result.Add(item);
            }
            return result;
        }

        private static ReplaySideSeasonSummary BuildSide(ReplayBundle bundle, int season, string seatId, string factionName)
        {
            var side = new ReplaySideSeasonSummary
            {
                SeatId = seatId,
                Faction = factionName
            };

            // 1. Action counts by type and lookups
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            int lookups = 0;

            if (bundle.Requests != null)
            {
                for (int i = 0; i < bundle.Requests.Count; i++)
                {
                    var req = bundle.Requests[i];
                    if (req.Season != season || !string.Equals(req.SeatId, seatId, StringComparison.Ordinal))
                        continue;

                    string actType = null;
                    if (!string.IsNullOrEmpty(req.RawModelText))
                    {
                        var p = LlmActionParser.Parse(req.RawModelText);
                        if (p.Ok && p.Action != null)
                        {
                            actType = p.Action.EndTurn ? "end_turn" : p.Action.Type;
                        }
                    }

                    if (string.IsNullOrEmpty(actType))
                    {
                        actType = req.ParseOk ? "unknown" : "invalid";
                    }

                    if (counts.TryGetValue(actType, out int cur)) counts[actType] = cur + 1;
                    else counts[actType] = 1;
                }
            }

            var parts = new List<string>();
            foreach (var kvp in counts) parts.Add($"{kvp.Key}: {kvp.Value}");
            parts.Sort(StringComparer.Ordinal);
            side.ActionCountsSummary = parts.Count > 0 ? string.Join(", ", parts) : "none";
            side.Lookups = lookups;

            // 2. Before -> After resources & troops
            ReplaySeatScore prevScore = FindScore(bundle, season - 1, seatId);
            ReplaySeatScore curScore = FindScore(bundle, season, seatId);

            if (season == 1)
            {
                side.BeforeFood = "1500";
                side.BeforeGold = "900";
                side.BeforeGarrison = "0";
            }
            else if (prevScore != null)
            {
                side.BeforeFood = FormatField(prevScore, "food", prevScore.Food);
                side.BeforeGold = FormatField(prevScore, "gold", prevScore.Gold);
                side.BeforeGarrison = FormatField(prevScore, "garrison", prevScore.Garrison);
            }
            else
            {
                side.BeforeFood = "unknown";
                side.BeforeGold = "unknown";
                side.BeforeGarrison = "unknown";
            }

            if (curScore != null)
            {
                side.AfterFood = FormatField(curScore, "food", curScore.Food);
                side.AfterGold = FormatField(curScore, "gold", curScore.Gold);
                side.AfterGarrison = FormatField(curScore, "garrison", curScore.Garrison);
            }
            else
            {
                side.AfterFood = "unknown";
                side.AfterGold = "unknown";
                side.AfterGarrison = "unknown";
            }

            side.TroopSummary = $"Garrison {side.BeforeGarrison} -> {side.AfterGarrison} | Field: unknown (not in scoreboard) | Total: unknown";
            return side;
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
            if (sc == null) return "unknown";
            if (sc.MissingFields != null)
            {
                for (int i = 0; i < sc.MissingFields.Count; i++)
                    if (string.Equals(sc.MissingFields[i], field, StringComparison.Ordinal))
                        return "unknown";
            }
            return val.ToString();
        }
    }
}
