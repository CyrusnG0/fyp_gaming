// KingdomDemo (partial) -- offline match replay comparison and season summary subtabs.

using System;
using System.Collections.Generic;
using UnityEngine;
using LuanShi.Engine;

namespace LuanShi
{
    public partial class KingdomDemo : MonoBehaviour
    {
        void DrawReplayComparison(float x, float y, float width, float height)
        {
            GUI.Label(new Rect(x, y, width, 16f), "FINAL-STATE COMPARISON:", uiPanelTitle);
            y += 18f;
            float viewH = height - 18f;

            var rpt = _replaySession != null ? _replaySession.FinalReport : null;
            if (rpt == null)
            {
                string hint = (_replaySession != null && _replaySession.IsPlaying)
                    ? "Replaying match... Complete playback to view final comparison."
                    : "Step or Play replay to completion to view final comparison.";
                GUI.Label(new Rect(x, y, width, 40f), hint, uiHint);
                return;
            }

            float contentHeight = 360f;
            Rect viewRect = new Rect(x, y, width, viewH);
            _replayComparisonScroll = GUI.BeginScrollView(viewRect, _replayComparisonScroll, new Rect(0, 0, width - 20f, contentHeight));

            float rowY = 0f;
            float innerW = width - 20f;

            // Computed Status for Current Replay
            bool exactPass = rpt.RawExactMatch;
            Color headerBg = exactPass ? new Color(0.15f, 0.25f, 0.15f, 0.8f) : new Color(0.25f, 0.18f, 0.15f, 0.8f);
            DrawSolidColor(new Rect(0, rowY, innerW, 16f), headerBg);
            GUI.Label(new Rect(4f, rowY, innerW - 8f, 16f), "[CURRENT REPLAY COMPUTED STATUS]", uiPanelTitle);
            rowY += 18f;

            string statusStr = exactPass
                ? "Status: PASS (all observation hashes and scoreboards match recorded summary)"
                : "Status: FAIL (raw mismatches reported honestly; not smoothed over)";
            GUI.Label(new Rect(4f, rowY, innerW - 8f, 14f), statusStr, exactPass ? uiSmall : uiReason);
            rowY += 15f;
            GUI.Label(new Rect(4f, rowY, innerW - 8f, 14f), $"Observation Hashes: {rpt.ObservationHashesMatched}/{rpt.TotalRequests} matched ({rpt.ObservationHashMismatches} mismatches)", uiSmall);
            rowY += 15f;

            string weiG = FormatDiffField(rpt.WeiFinalGold, rpt.WeiExpectedGold, rpt.WeiExpectedScore, "gold");
            string weiF = FormatDiffField(rpt.WeiFinalFood, rpt.WeiExpectedFood, rpt.WeiExpectedScore, "food");
            string weiP = FormatDiffField(rpt.WeiFinalPop, rpt.WeiExpectedPop, rpt.WeiExpectedScore, "population");
            string weiGar = FormatDiffField(rpt.WeiFinalGarrison, rpt.WeiExpectedGarrison, rpt.WeiExpectedScore, "garrison");
            GUI.Label(new Rect(4f, rowY, innerW - 8f, 14f), $"Wei Final: Gold {weiG}, Food {weiF}, Pop {weiP}, Gar {weiGar}", uiSmall);
            rowY += 15f;

            string shuG = FormatDiffField(rpt.ShuFinalGold, rpt.ShuExpectedGold, rpt.ShuExpectedScore, "gold");
            string shuF = FormatDiffField(rpt.ShuFinalFood, rpt.ShuExpectedFood, rpt.ShuExpectedScore, "food");
            string shuP = FormatDiffField(rpt.ShuFinalPop, rpt.ShuExpectedPop, rpt.ShuExpectedScore, "population");
            string shuGar = FormatDiffField(rpt.ShuFinalGarrison, rpt.ShuExpectedGarrison, rpt.ShuExpectedScore, "garrison");
            GUI.Label(new Rect(4f, rowY, innerW - 8f, 14f), $"Shu Final: Gold {shuG}, Food {shuF}, Pop {shuP}, Gar {shuGar}", uiSmall);
            rowY += 15f;

            if (rpt.RawScoreboardMismatches != null && rpt.RawScoreboardMismatches.Count > 0)
            {
                GUI.Label(new Rect(4f, rowY, innerW - 8f, 14f), $"Scoreboard Deltas: {rpt.RawScoreboardMismatches.Count} mismatching entries detected", uiReason);
                rowY += 15f;
            }
            if (rpt.ProposedFloatingPointNotes != null)
            {
                for (int n = 0; n < rpt.ProposedFloatingPointNotes.Count; n++)
                {
                    GUI.Label(new Rect(4f, rowY, innerW - 8f, 14f), rpt.ProposedFloatingPointNotes[n], uiHint);
                    rowY += 15f;
                }
            }
            rowY += 20f;

            // Entity Position Breakdown
            DrawSolidColor(new Rect(0, rowY, innerW, 16f), new Color(0.18f, 0.18f, 0.22f, 0.8f));
            GUI.Label(new Rect(4f, rowY, innerW - 8f, 16f), "[ENTITY POSITION VERIFICATION]", uiPanelTitle);
            rowY += 18f;
            GUI.Label(new Rect(4f, rowY, innerW - 8f, 14f), $"Direct Observations: {rpt.DirectObservationMatched}/{rpt.DirectObservationCount} matched", uiSmall);
            rowY += 15f;
            GUI.Label(new Rect(4f, rowY, innerW - 8f, 14f), $"Command Inferred: {rpt.CommandInferredMatched}/{rpt.CommandInferredCount} matched (army-shu-4 moved to (21,1))", uiSmall);
            rowY += 15f;
            GUI.Label(new Rect(4f, rowY, innerW - 8f, 14f), $"Unverified Scene Setup: {rpt.UnverifiedSceneSetupCount} neutral entities (city-south, watch-tower-1 in fog)", uiSmall);

            GUI.EndScrollView();
        }

        private static string FormatDiffField(int actual, int expected, ReplaySeatScore expScore, string field)
        {
            if (expScore == null) return $"{actual} (exp unknown, delta unknown)";
            if (expScore.MissingFields != null)
            {
                for (int i = 0; i < expScore.MissingFields.Count; i++)
                    if (string.Equals(expScore.MissingFields[i], field, StringComparison.Ordinal))
                        return $"{actual} (exp unknown, delta unknown)";
            }
            int delta = actual - expected;
            string dStr = delta == 0 ? "0" : (delta > 0 ? $"+{delta}" : delta.ToString());
            return $"{actual} (exp {expected}, delta {dStr})";
        }

        void DrawReplaySeasonSummaries(float x, float y, float width, float height)
        {
            GUI.Label(new Rect(x, y, width, 16f), "SEASON-BY-SEASON SUMMARY:", uiPanelTitle);
            y += 18f;
            float viewH = height - 18f;

            var summaries = _replaySession != null ? _replaySession.SeasonSummaries : null;
            if (summaries == null || summaries.Count == 0)
            {
                GUI.Label(new Rect(x, y, width, 40f), "No season summaries available.", uiHint);
                return;
            }

            float contentHeight = summaries.Count * 85f + 10f;
            Rect viewRect = new Rect(x, y, width, viewH);
            _replaySeasonsScroll = GUI.BeginScrollView(viewRect, _replaySeasonsScroll, new Rect(0, 0, width - 20f, contentHeight));

            float rowY = 0f;
            float innerW = width - 20f;

            for (int i = 0; i < summaries.Count; i++)
            {
                var s = summaries[i];
                DrawSolidColor(new Rect(0, rowY, innerW, 16f), new Color(0.2f, 0.2f, 0.25f, 0.8f));
                GUI.Label(new Rect(4f, rowY, innerW - 8f, 16f), $"[SEASON {s.Season} SUMMARY]", uiPanelTitle);
                rowY += 18f;

                if (s.Wei != null)
                {
                    GUI.Label(new Rect(4f, rowY, innerW - 8f, 14f),
                        $"Wei: Food {s.Wei.BeforeFood}->{s.Wei.AfterFood}, Gold {s.Wei.BeforeGold}->{s.Wei.AfterGold} | Actions: {s.Wei.ActionCountsSummary} | Lookups: {s.Wei.Lookups}", uiSmall);
                    rowY += 15f;
                    GUI.Label(new Rect(12f, rowY, innerW - 16f, 14f),
                        $"Troops: {s.Wei.TroopSummary}", uiHint);
                    rowY += 15f;
                }

                if (s.Shu != null)
                {
                    GUI.Label(new Rect(4f, rowY, innerW - 8f, 14f),
                        $"Shu: Food {s.Shu.BeforeFood}->{s.Shu.AfterFood}, Gold {s.Shu.BeforeGold}->{s.Shu.AfterGold} | Actions: {s.Shu.ActionCountsSummary} | Lookups: {s.Shu.Lookups}", uiSmall);
                    rowY += 15f;
                    GUI.Label(new Rect(12f, rowY, innerW - 16f, 14f),
                        $"Troops: {s.Shu.TroopSummary}", uiHint);
                    rowY += 15f;
                }

                rowY += 6f;
            }

            GUI.EndScrollView();
        }
    }
}
