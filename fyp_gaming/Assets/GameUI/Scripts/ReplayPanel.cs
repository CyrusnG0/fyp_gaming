// KingdomDemo (partial) — offline match replay panel.
//
// Docked right-hand side panel showing replay playback controls, verified scoreboards,
// saved prompt excerpts, and bit-exact verification metrics with ZERO live model calls.

using System;
using System.Collections.Generic;
using UnityEngine;
using LuanShi.Engine;

namespace LuanShi
{
    public partial class KingdomDemo : MonoBehaviour
    {
        private Vector2 _replayPromptScroll;
        private Vector2 _replayResponseScroll;
        private Vector2 _replayFeedScroll;
        private Vector2 _replayComparisonScroll;
        private Vector2 _replaySeasonsScroll;
        private string _replaySubTab = "feed";
        private int _replayInspectedRequestIndex = -1;

        static Rect ReplayPanelArea()
            => new Rect(SidePanelX, SidePanelY, SidePanelWidth, Mathf.Min(580f, Screen.height - SidePanelY - 40f));

        void DrawReplayPanel()
        {
            Rect area = ReplayPanelArea();
            uiRects.Add(area);
            DrawBackdrop(area, CurrentSeatAccent());

            float x = area.x + PanelPadX;
            float width = area.width - PanelPadX * 2f;
            float y = area.y + 10f;

            // Header & Metadata
            GUI.Label(new Rect(x, y, width, 20f), "OFFLINE REPLAY — ZERO API CALLS", uiPanelTitle);
            y += 22f;

            ReplaySummaryData sum = _replayBundle != null ? _replayBundle.Summary : null;
            string runId = sum != null && !string.IsNullOrEmpty(sum.RunId) ? sum.RunId : "unknown";
            GUI.Label(new Rect(x, y, width, 14f), EllipsizeToWidth(uiSmall, "Run: " + runId, width), uiSmall);
            y += 15f;

            string sSeed = (sum != null && sum.Seed != 0) ? sum.Seed.ToString() : "unknown";
            string profKey = sum != null && !string.IsNullOrEmpty(sum.BalanceProfileKey) ? sum.BalanceProfileKey : "unknown";
            string lookupStr = sum != null ? (sum.ManualLookupEnabled ? "on (historical)" : "off") : "unknown";
            GUI.Label(new Rect(x, y, width, 14f), $"seed: {sSeed}  |  profile: {profKey}  |  lookup: {lookupStr}", uiSmall);
            y += 15f;

            string personaInfo = "";
            if (sum != null && sum.Turns != null)
            {
                var personas = new HashSet<string>(StringComparer.Ordinal);
                foreach (var t in sum.Turns) if (!string.IsNullOrEmpty(t.PersonaId)) personas.Add(t.PersonaId);
                personaInfo = string.Join("  |  ", personas);
            }
            if (string.IsNullOrEmpty(personaInfo)) personaInfo = "unknown";
            string setupTag = _replayBundle != null && _replayBundle.SetupSource == "scenario_fallback"
                ? "  |  setup: scenario.json fallback (no bootstrap)"
                : "";
            GUI.Label(new Rect(x, y, width, 14f), EllipsizeToWidth(uiSmall, "historical personas: " + personaInfo + setupTag, width), uiSmall);
            y += 18f;

            if (_replaySession == null)
            {
                GUI.Label(new Rect(x, y, width, 30f), "Replay unavailable: " + (_replayInitError ?? "loading..."), uiReason);
                return;
            }

            // Season & Request Progress
            int curSeason = _replaySession.CurrentSeason;
            int reqIdx = _replaySession.CurrentRequestIndex;
            int totalSeasons = _replaySession.TotalSeasons;
            int totalReq = _replaySession.TotalRequests;
            string sSeasons = totalSeasons > 0 ? totalSeasons.ToString() : "unknown";
            string sReqs = totalReq > 0 ? totalReq.ToString() : "unknown";
            string stateText = _replaySession.IsCompleted
                ? (_replaySession.IsIncompleteRun ? "Paused (incomplete)" : "Completed")
                : (_replaySession.IsPlaying ? "Playing" : "Paused");
            GUI.Label(new Rect(x, y, width, 18f), $"Season {curSeason} of {sSeasons}  |  Req {reqIdx} of {sReqs} ({stateText})", uiBody);
            y += 20f;

            if (_replaySession.HasDivergence && !string.IsNullOrEmpty(_replaySession.DivergenceSummary))
            {
                GUI.Label(new Rect(x, y, width, 15f), EllipsizeToWidth(uiReason, "DIVERGENCE: " + _replaySession.DivergenceSummary, width), uiReason);
                y += 16f;
            }
            else if (_replaySession.IsCompleted && _replaySession.IsIncompleteRun && !string.IsNullOrEmpty(_replaySession.StopReason))
            {
                GUI.Label(new Rect(x, y, width, 15f), EllipsizeToWidth(uiHint, "Stopped: " + _replaySession.StopReason, width), uiHint);
                y += 16f;
            }

            // Progress Pips
            DrawReplayPips(x, y, width, curSeason, totalSeasons);
            y += 18f;

            // Playback controls row 1
            float btnW = (width - 16f) / 5f;
            GUIStyle playStyle = _replaySession.IsPlaying ? uiTabActive : uiButton;
            GUIStyle pauseStyle = (!_replaySession.IsPlaying && !_replaySession.IsCompleted && _replaySession.State == ReplayState.Paused) ? uiTabActive : uiButton;
            if (GUI.Button(new Rect(x, y, btnW, 22f), "Play", playStyle)) ReplayPlay();
            if (GUI.Button(new Rect(x + btnW + 4f, y, btnW, 22f), "Pause", pauseStyle)) ReplayPause();
            if (GUI.Button(new Rect(x + (btnW + 4f) * 2f, y, btnW, 22f), "Season", uiButton)) ReplayStepSeason();
            if (GUI.Button(new Rect(x + (btnW + 4f) * 3f, y, btnW, 22f), "Step req", uiButton)) ReplayStepRequest();
            if (GUI.Button(new Rect(x + (btnW + 4f) * 4f, y, btnW, 22f), "Reset", uiButton)) ReplayReset();
            y += 26f;

            // Playback speed row 2
            float spdW = (width - 12f) / 4f;
            float curSpd = _replaySession.Speed;
            if (GUI.Button(new Rect(x, y, spdW, 18f), "0.5x", Math.Abs(curSpd - 0.5f) < 0.05f ? uiTabActive : uiButton)) ReplaySetSpeed(0.5f);
            if (GUI.Button(new Rect(x + spdW + 4f, y, spdW, 18f), "1.0x", Math.Abs(curSpd - 1.0f) < 0.05f ? uiTabActive : uiButton)) ReplaySetSpeed(1.0f);
            if (GUI.Button(new Rect(x + (spdW + 4f) * 2f, y, spdW, 18f), "2.0x", Math.Abs(curSpd - 2.0f) < 0.05f ? uiTabActive : uiButton)) ReplaySetSpeed(2.0f);
            if (GUI.Button(new Rect(x + (spdW + 4f) * 3f, y, spdW, 18f), "4.0x", Math.Abs(curSpd - 4.0f) < 0.05f ? uiTabActive : uiButton)) ReplaySetSpeed(4.0f);
            y += 24f;

            // Faction Scoreboards
            DrawReplayScoreboards(x, y, width);
            y += _replaySession.IsCompleted ? 78f : 66f;

            // Sub-navigation: Event Feed vs Exchanges vs Seasons vs Comparison
            float tabW = (width - 12f) / 4f;
            if (GUI.Button(new Rect(x, y, tabW, 20f), "Event Feed", _replaySubTab == "feed" ? uiTabActive : uiButton)) _replaySubTab = "feed";
            if (GUI.Button(new Rect(x + tabW + 4f, y, tabW, 20f), "Exchanges", _replaySubTab == "details" ? uiTabActive : uiButton)) _replaySubTab = "details";
            if (GUI.Button(new Rect(x + (tabW + 4f) * 2f, y, tabW, 20f), "Seasons", _replaySubTab == "seasons" ? uiTabActive : uiButton)) _replaySubTab = "seasons";
            if (GUI.Button(new Rect(x + (tabW + 4f) * 3f, y, tabW, 20f), "Comparison", _replaySubTab == "comparison" ? uiTabActive : uiButton)) _replaySubTab = "comparison";
            y += 24f;

            if (_replaySubTab == "feed")
            {
                DrawReplayEventFeed(x, y, width, 180f);
                y += 184f;
            }
            else if (_replaySubTab == "seasons")
            {
                DrawReplaySeasonSummaries(x, y, width, 180f);
                y += 184f;
            }
            else if (_replaySubTab == "comparison")
            {
                DrawReplayComparison(x, y, width, 180f);
                y += 184f;
            }
            else
            {
                int reqCount = _replayBundle?.Requests?.Count ?? 0;
                int activeIdx = (_replayInspectedRequestIndex >= 0 && _replayInspectedRequestIndex < reqCount)
                    ? _replayInspectedRequestIndex
                    : Mathf.Clamp(_replaySession.CurrentRequestIndex, 0, Math.Max(0, reqCount - 1));
                ReplayRequestRecord rec = reqCount > 0 ? _replayBundle.Requests[activeIdx] : _replaySession.CurrentRequestRecord;
                int firstInquiryIdx = FindFirstInquiryRequestIndex();

                // Request navigator (<, >, Latest, Inquiry)
                float navW = firstInquiryIdx >= 0 ? (width - 12f) / 4f : (width - 8f) / 3f;
                if (GUI.Button(new Rect(x, y, navW, 18f), "< Prev", uiButton) && reqCount > 0)
                    _replayInspectedRequestIndex = Mathf.Max(0, activeIdx - 1);
                if (GUI.Button(new Rect(x + navW + 4f, y, navW, 18f), "Next >", uiButton) && reqCount > 0)
                    _replayInspectedRequestIndex = Mathf.Min(reqCount - 1, activeIdx + 1);
                if (GUI.Button(new Rect(x + (navW + 4f) * 2f, y, navW, 18f), "Follow", _replayInspectedRequestIndex < 0 ? uiTabActive : uiButton))
                    _replayInspectedRequestIndex = -1;
                if (firstInquiryIdx >= 0 && GUI.Button(new Rect(x + (navW + 4f) * 3f, y, navW, 18f), "Inquiry", _replayInspectedRequestIndex == firstInquiryIdx ? uiTabActive : uiButton))
                    _replayInspectedRequestIndex = firstInquiryIdx;
                y += 20f;

                string seatId = rec != null && !string.IsNullOrEmpty(rec.SeatId) ? rec.SeatId : (_replaySession.CurrentSeatId ?? "unknown");
                string persona = _replaySession.GetPersonaIdForRequest(rec);
                string reqLink = rec != null ? $"S{rec.Season} T{rec.TurnToken} R{rec.RequestIndex} ({activeIdx + 1}/{reqCount})" : "-";
                string exLink = rec != null && !string.IsNullOrEmpty(rec.ExchangeId) ? $" | ex={rec.ExchangeId}" : "";
                string inqTag = rec != null && !string.IsNullOrEmpty(rec.InquiryTool) ? $" | INQUIRY:{rec.InquiryTool}" : "";
                GUI.Label(new Rect(x, y, width, 16f), EllipsizeToWidth(uiPanelTitle, $"{seatId} ({persona}) | {reqLink}{exLink}{inqTag}", width), uiPanelTitle);
                y += 17f;

                string cmdAction = EllipsizeToWidth(uiSmall, _replaySession.FormatRecordedActionLabel(rec), width);
                GUI.Label(new Rect(x, y, width, 14f), "Action: " + cmdAction, uiSmall);
                y += 15f;

                string sExp = (rec != null && !string.IsNullOrEmpty(rec.RecordedExplanation)) ? rec.RecordedExplanation : "none";
                GUI.Label(new Rect(x, y, width, 14f), "Recorded explanation (not private thought):", uiSmall);
                y += 14f;
                GUI.TextArea(new Rect(x, y, width, 28f), sExp, uiMonoTiny);
                y += 30f;

                string sLatency = (rec != null && rec.LatencyMs > 0) ? $"{rec.LatencyMs}ms" : "unknown";
                string sInTok = (rec != null && rec.InputTokens > 0) ? rec.InputTokens.ToString() : "unknown";
                string sOutTok = (rec != null && rec.OutputTokens > 0) ? rec.OutputTokens.ToString() : "unknown";
                GUI.Label(new Rect(x, y, width, 14f), $"Tokens: in {sInTok} | out {sOutTok}  |  Latency: {sLatency}", uiSmall);
                y += 15f;

                GUI.Label(new Rect(x, y, width, 14f), "SAVED PROMPT EXCERPT (RECORDED MATERIAL — NO MODEL CALL):", uiSmall);
                y += 14f;
                string promptText = rec != null && !string.IsNullOrEmpty(rec.PromptExcerpt) ? rec.PromptExcerpt : "(no prompt recorded)";
                Rect promptRect = new Rect(x, y, width, 40f);
                _replayPromptScroll = GUI.BeginScrollView(promptRect, _replayPromptScroll, new Rect(0, 0, width - 20f, 180f));
                GUI.TextArea(new Rect(0, 0, width - 20f, 180f), promptText, uiMono);
                GUI.EndScrollView();
                y += 43f;

                GUI.Label(new Rect(x, y, width, 14f), "SAVED MODEL RESPONSE (RECORDED MATERIAL — NO MODEL CALL):", uiSmall);
                y += 14f;
                string respText = rec != null && !string.IsNullOrEmpty(rec.ResponseContentText)
                    ? rec.ResponseContentText
                    : (rec != null && !string.IsNullOrEmpty(rec.RawModelText) ? rec.RawModelText : "(empty recorded response)");
                Rect respRect = new Rect(x, y, width, 34f);
                _replayResponseScroll = GUI.BeginScrollView(respRect, _replayResponseScroll, new Rect(0, 0, width - 20f, 120f));
                GUI.TextArea(new Rect(0, 0, width - 20f, 120f), respText, uiMono);
                GUI.EndScrollView();
                y += 37f;
            }

            // Verification status
            int obs = _replaySession.ObservationHashesMatched;
            int acts = _replaySession.AcceptedActionsReplayed;
            int expActs = _replaySession.ExpectedAcceptedActions;
            int rejs = _replaySession.RejectedActionsReplayed;
            int expRejs = _replaySession.ExpectedRejectedActions;
            int mismatches = _replaySession.ActionOutcomeMismatches;
            if (_replaySession.IsCompleted)
            {
                bool rawPass = _replaySession.FinalReport != null && _replaySession.FinalReport.RawExactMatch;
                string status = rawPass ? "VERIFICATION: PASS (RAW EXACT)" : "VERIFICATION: COMPLETED WITH DISCREPANCIES";
                GUI.Label(new Rect(x, y, width, 16f), status, rawPass ? uiPanelTitle : uiReason);
                y += 16f;
                int dirObs = _replaySession.FinalReport != null ? _replaySession.FinalReport.DirectObservationMatched : 0;
                int dirCount = _replaySession.FinalReport != null ? _replaySession.FinalReport.DirectObservationCount : 0;
                int cmdInf = _replaySession.FinalReport != null ? _replaySession.FinalReport.CommandInferredMatched : 0;
                int cmdCount = _replaySession.FinalReport != null ? _replaySession.FinalReport.CommandInferredCount : 0;
                int unverified = _replaySession.FinalReport != null ? _replaySession.FinalReport.UnverifiedSceneSetupCount : 0;
                GUI.Label(new Rect(x, y, width, 14f), $"Obs: {obs}/{totalReq}  |  Accepted: {acts}/{expActs} (Rej: {rejs}/{expRejs}, Mismatch: {mismatches})", uiSmall);
                y += 14f;
                GUI.Label(new Rect(x, y, width, 14f), $"Positions: {dirObs}/{dirCount} obs, {cmdInf}/{cmdCount} cmd-inferred, {unverified} unverified (fog/setup)", uiSmall);
            }
            else
            {
                GUI.Label(new Rect(x, y, width, 14f), $"Obs: {obs}/{reqIdx}  |  Accepted: {acts}/{reqIdx} (Rej: {rejs}, Mismatch: {mismatches})", uiSmall);
            }
        }

        void DrawReplayPips(float x, float y, float width, int currentSeason, int totalSeasons)
        {
            int count = Math.Max(1, totalSeasons);
            float pipW = (width - (count - 1) * 4f) / count;
            for (int s = 1; s <= count; s++)
            {
                Rect r = new Rect(x + (s - 1) * (pipW + 4f), y, pipW, 10f);
                Color c = s <= currentSeason ? new Color(0.2f, 0.7f, 0.3f, 0.9f) : new Color(0.3f, 0.3f, 0.3f, 0.5f);
                DrawSolidColor(r, c);
            }
        }

        static string FormatScoreField(ReplaySeatScore sc, string field, int val)
        {
            if (sc == null) return "unknown";
            if (sc.MissingFields != null)
            {
                for (int i = 0; i < sc.MissingFields.Count; i++)
                {
                    if (string.Equals(sc.MissingFields[i], field, System.StringComparison.Ordinal))
                        return "unknown";
                }
            }
            return val.ToString();
        }

        void DrawReplayScoreboards(float x, float y, float width)
        {
            float colW = (width - 8f) * 0.5f;
            var scores = _replaySession != null ? _replaySession.CurrentScores() : null;
            ReplaySeatScore wei = scores != null && scores.Count > 0 ? scores[0] : null;
            ReplaySeatScore shu = scores != null && scores.Count > 1 ? scores[1] : null;

            string weiG = FormatScoreField(wei, "gold", wei?.Gold ?? 0);
            string weiF = FormatScoreField(wei, "food", wei?.Food ?? 0);
            string weiP = FormatScoreField(wei, "population", wei?.Population ?? 0);
            string weiGar = FormatScoreField(wei, "garrison", wei?.Garrison ?? 0);

            string weiName = wei != null && !string.IsNullOrEmpty(wei.Faction) ? $"{wei.Faction} ({wei.SeatId})" : "Seat 0";
            GUI.Label(new Rect(x, y, colW, 16f), weiName, uiPanelTitle);
            GUI.Label(new Rect(x, y + 16f, colW, 14f), $"Gold {weiG}  Food {weiF}", uiSmall);
            GUI.Label(new Rect(x, y + 30f, colW, 14f), $"Pop {weiP}  Gar {weiGar}", uiSmall);

            string shuG = FormatScoreField(shu, "gold", shu?.Gold ?? 0);
            string shuF = FormatScoreField(shu, "food", shu?.Food ?? 0);
            string shuP = FormatScoreField(shu, "population", shu?.Population ?? 0);
            string shuGar = FormatScoreField(shu, "garrison", shu?.Garrison ?? 0);

            float col2X = x + colW + 8f;
            string shuName = shu != null && !string.IsNullOrEmpty(shu.Faction) ? $"{shu.Faction} ({shu.SeatId})" : "Seat 1";
            GUI.Label(new Rect(col2X, y, colW, 16f), shuName, uiPanelTitle);
            GUI.Label(new Rect(col2X, y + 16f, colW, 14f), $"Gold {shuG}  Food {shuF}", uiSmall);
            GUI.Label(new Rect(col2X, y + 30f, colW, 14f), $"Pop {shuP}  Gar {shuGar}", uiSmall);

            if (_replaySession != null && _replaySession.IsCompleted && _replaySession.FinalReport != null)
            {
                var rpt = _replaySession.FinalReport;
                string expG = FormatScoreField(rpt.WeiExpectedScore, "gold", rpt.WeiExpectedGold);
                string expF = FormatScoreField(rpt.WeiExpectedScore, "food", rpt.WeiExpectedFood);
                int dWeiG = rpt.WeiFinalGold - rpt.WeiExpectedGold;
                int dWeiF = rpt.WeiFinalFood - rpt.WeiExpectedFood;
                string dG = expG == "unknown" ? "unknown" : (dWeiG >= 0 ? $"+{dWeiG}" : dWeiG.ToString());
                string dF = expF == "unknown" ? "unknown" : (dWeiF >= 0 ? $"+{dWeiF}" : dWeiF.ToString());
                GUI.Label(new Rect(x, y + 44f, colW, 14f), $"Summary G:{expG} ({dG}) F:{expF} ({dF})", uiSmall);

                string sExpG = FormatScoreField(rpt.ShuExpectedScore, "gold", rpt.ShuExpectedGold);
                string sExpF = FormatScoreField(rpt.ShuExpectedScore, "food", rpt.ShuExpectedFood);
                int dShuG = rpt.ShuFinalGold - rpt.ShuExpectedGold;
                int dShuF = rpt.ShuFinalFood - rpt.ShuExpectedFood;
                string sdG = sExpG == "unknown" ? "unknown" : (dShuG >= 0 ? $"+{dShuG}" : dShuG.ToString());
                string sdF = sExpF == "unknown" ? "unknown" : (dShuF >= 0 ? $"+{dShuF}" : dShuF.ToString());
                GUI.Label(new Rect(col2X, y + 44f, colW, 14f), $"Summary G:{sExpG} ({sdG}) F:{sExpF} ({sdF})", uiSmall);
            }
        }

        void DrawReplayTurnControl()
        {
            var rect = new Rect(EndTurnX, EndTurnY, EndTurnWidth, EndTurnHeight);
            uiRects.Add(rect);

            if (_replaySession == null)
            {
                GUI.Button(rect, "Replay...", uiButtonDisabled);
                return;
            }

            if (_replaySession.IsCompleted)
            {
                if (GUI.Button(rect, "Replay Reset", uiButton)) ReplayReset();
            }
            else if (_replaySession.IsPlaying)
            {
                if (GUI.Button(rect, "Pause Replay", uiButton)) ReplayPause();
            }
            else
            {
                if (GUI.Button(rect, "Step request", uiButton)) ReplayStepRequest();
            }
        }

        void DrawReplayEventFeed(float x, float y, float width, float height)
        {
            var feed = _replaySession != null ? _replaySession.EventFeed : null;
            int count = feed != null ? feed.Count : 0;
            GUI.Label(new Rect(x, y, width, 16f), $"EVENT FEED ({count} events replayed):", uiPanelTitle);
            y += 18f;
            float viewH = height - 18f;

            if (count == 0)
            {
                GUI.Label(new Rect(x, y, width, 30f), "Replay at start. Click Play or Step to replay recorded actions.", uiHint);
                return;
            }

            float rowHeight = 44f;
            float contentHeight = count * rowHeight;
            Rect viewRect = new Rect(x, y, width, viewH);
            _replayFeedScroll = GUI.BeginScrollView(viewRect, _replayFeedScroll, new Rect(0, 0, width - 20f, contentHeight));

            for (int i = 0; i < count; i++)
            {
                var evt = feed[i];
                float rowY = i * rowHeight;
                Rect r = new Rect(0, rowY, width - 20f, rowHeight - 2f);
                DrawSolidColor(r, i % 2 == 0 ? new Color(0.12f, 0.12f, 0.12f, 0.6f) : new Color(0.08f, 0.08f, 0.08f, 0.6f));

                string mark = evt.ResultOk ? "✓" : "✗";
                string link = !string.IsNullOrEmpty(evt.ExchangeId) ? $" | {evt.ExchangeId}" : "";
                string header = $"#{evt.Sequence} S{evt.Season} T{evt.TurnToken} R{evt.RequestIndex}{link} | {evt.Faction} | {mark} {evt.ResultText}";
                GUI.Label(new Rect(4f, rowY + 1f, width - 28f, 14f), EllipsizeToWidth(uiSmall, header, width - 28f), uiSmall);

                string actLine = $"Action: {evt.ActionSummary}";
                GUI.Label(new Rect(4f, rowY + 14f, width - 28f, 13f), EllipsizeToWidth(uiMonoTiny, actLine, width - 28f), uiMonoTiny);

                if (evt.RequestIndex > 0)
                {
                    string exp = !string.IsNullOrEmpty(evt.Explanation) ? evt.Explanation : "unknown";
                    string expLine = $"Recorded explanation: \"{exp}\"";
                    GUI.Label(new Rect(4f, rowY + 26f, width - 28f, 14f), EllipsizeToWidth(uiHint, expLine, width - 28f), uiHint);
                    if (GUI.Button(r, GUIContent.none, GUIStyle.none))
                    {
                        int idx = FindRequestIndexForFeedEvent(evt);
                        if (idx >= 0) { _replayInspectedRequestIndex = idx; _replaySubTab = "details"; }
                    }
                }
            }

            GUI.EndScrollView();
        }

        int FindFirstInquiryRequestIndex()
        {
            if (_replayBundle?.Requests == null) return -1;
            for (int i = 0; i < _replayBundle.Requests.Count; i++)
                if (!string.IsNullOrEmpty(_replayBundle.Requests[i].InquiryTool)) return i;
            return -1;
        }

        int FindRequestIndexForFeedEvent(ReplayFeedEvent evt)
        {
            if (evt == null || _replayBundle?.Requests == null) return -1;
            for (int i = 0; i < _replayBundle.Requests.Count; i++)
            {
                var r = _replayBundle.Requests[i];
                if (r.Season == evt.Season && r.TurnToken == evt.TurnToken && r.RequestIndex == evt.RequestIndex
                    && (string.IsNullOrEmpty(evt.ExchangeId) || string.Equals(r.ExchangeId, evt.ExchangeId, StringComparison.Ordinal)))
                    return i;
            }
            return -1;
        }

        void DrawSolidColor(Rect rect, Color color)
        {
            Color prev = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = prev;
        }
    }
}
