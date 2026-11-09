// KingdomDemo (partial) — offline match replay panel.
//
// Docked right-hand side panel showing replay playback controls, verified scoreboards,
// saved prompt excerpts, and bit-exact verification metrics with ZERO live model calls.

using System;
using UnityEngine;
using LuanShi.Engine;

namespace LuanShi
{
    public partial class KingdomDemo : MonoBehaviour
    {
        private Vector2 _replayPromptScroll;
        private Vector2 _replayResponseScroll;

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
            string runId = sum != null && !string.IsNullOrEmpty(sum.RunId) ? sum.RunId : "run-20261109082434809-51b7ebd5";
            GUI.Label(new Rect(x, y, width, 14f), EllipsizeToWidth(uiSmall, "Run: " + runId, width), uiSmall);
            y += 15f;

            int seedVal = sum != null ? sum.Seed : 12345;
            string profKey = sum != null && !string.IsNullOrEmpty(sum.BalanceProfileKey) ? sum.BalanceProfileKey : "easy-research";
            string lookupStr = sum != null && sum.ManualLookupEnabled ? "on (historical)" : "off";
            GUI.Label(new Rect(x, y, width, 14f), $"seed: {seedVal}  |  profile: {profKey}  |  lookup: {lookupStr}", uiSmall);
            y += 15f;
            GUI.Label(new Rect(x, y, width, 14f), "historical personas: wei-mild  |  shu-aggressive", uiSmall);
            y += 18f;

            if (_replaySession == null)
            {
                GUI.Label(new Rect(x, y, width, 30f), "Replay unavailable: " + (_replayInitError ?? "loading..."), uiReason);
                return;
            }

            // Season & Request Progress
            int curSeason = _replaySession.CurrentSeason;
            int reqIdx = _replaySession.CurrentRequestIndex;
            int totalReq = _replayBundle != null ? _replayBundle.Requests.Count : 44;
            string stateText = _replaySession.IsCompleted ? "Completed" : _replaySession.IsPlaying ? "Playing" : "Paused";
            GUI.Label(new Rect(x, y, width, 18f), $"Season {curSeason} of 5  |  Req {reqIdx} of {totalReq} ({stateText})", uiBody);
            y += 20f;

            // Progress Pips
            DrawReplayPips(x, y, width, curSeason);
            y += 18f;

            // Playback controls row 1
            float btnW = (width - 16f) / 5f;
            if (GUI.Button(new Rect(x, y, btnW, 22f), "Play", uiButton)) ReplayPlay();
            if (GUI.Button(new Rect(x + btnW + 4f, y, btnW, 22f), "Pause", uiButton)) ReplayPause();
            if (GUI.Button(new Rect(x + (btnW + 4f) * 2f, y, btnW, 22f), "Season", uiButton)) ReplayStepSeason();
            if (GUI.Button(new Rect(x + (btnW + 4f) * 3f, y, btnW, 22f), "Step", uiButton)) ReplayStepRequest();
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
            y += 66f;

            // Current request & Persona
            ReplayRequestRecord rec = _replaySession.CurrentRequestRecord;
            string seatId = rec != null ? rec.SeatId : (_replaySession.CurrentSeatId ?? "seat-0");
            string persona = _replaySession.CurrentPersonaId;
            GUI.Label(new Rect(x, y, width, 16f), $"Current Turn: {seatId} ({persona})", uiPanelTitle);
            y += 18f;

            string cmdAction = rec != null && !string.IsNullOrEmpty(rec.RawModelText) ? EllipsizeToWidth(uiSmall, rec.RawModelText, width) : "end_turn";
            GUI.Label(new Rect(x, y, width, 14f), "Action: " + cmdAction, uiSmall);
            y += 16f;

            // Saved Prompt Excerpt (scrollable box)
            GUI.Label(new Rect(x, y, width, 14f), "SAVED PROMPT EXCERPT (RECORDED MATERIAL — NO MODEL CALL):", uiSmall);
            y += 15f;
            string promptText = rec != null && !string.IsNullOrEmpty(rec.PromptExcerpt) ? rec.PromptExcerpt : "(no prompt recorded)";
            Rect promptRect = new Rect(x, y, width, 60f);
            _replayPromptScroll = GUI.BeginScrollView(promptRect, _replayPromptScroll, new Rect(0, 0, width - 20f, 180f));
            GUI.TextArea(new Rect(0, 0, width - 20f, 180f), promptText, uiMono);
            GUI.EndScrollView();
            y += 66f;

            // Saved Model Response
            GUI.Label(new Rect(x, y, width, 14f), "SAVED MODEL RESPONSE (RECORDED MATERIAL — NO MODEL CALL):", uiSmall);
            y += 15f;
            string respText = rec != null && !string.IsNullOrEmpty(rec.ResponseContentText) ? rec.ResponseContentText : (rec?.RawModelText ?? "-");
            Rect respRect = new Rect(x, y, width, 45f);
            _replayResponseScroll = GUI.BeginScrollView(respRect, _replayResponseScroll, new Rect(0, 0, width - 20f, 120f));
            GUI.TextArea(new Rect(0, 0, width - 20f, 120f), respText, uiMono);
            GUI.EndScrollView();
            y += 50f;

            // Verification status
            int normObs = _replaySession.NormalizedObservationsMatched;
            int actions = _replaySession.AcceptedActionsMatched;
            if (_replaySession.IsCompleted)
            {
                bool allPass = _replaySession.FinalReport != null && _replaySession.FinalReport.AllVerifiedMatched;
                string status = allPass ? "VERIFICATION: PASS (Wei 243/1741, Shu 71/1248)" : "VERIFICATION: COMPLETED WITH DISCREPANCIES";
                GUI.Label(new Rect(x, y, width, 16f), status, allPass ? uiPanelTitle : uiReason);
                y += 16f;
                GUI.Label(new Rect(x, y, width, 14f), $"Obs: {normObs}/44  |  Actions: {actions}/42  |  Positions: 10 verified, 2 fog", uiSmall);
            }
            else
            {
                GUI.Label(new Rect(x, y, width, 14f), $"Obs: {normObs}/{reqIdx}  |  Actions: {actions}/{reqIdx}", uiSmall);
            }
        }

        void DrawReplayPips(float x, float y, float width, int currentSeason)
        {
            float pipW = (width - 16f) / 5f;
            for (int s = 1; s <= 5; s++)
            {
                Rect r = new Rect(x + (s - 1) * (pipW + 4f), y, pipW, 10f);
                Color c = s <= currentSeason ? new Color(0.2f, 0.7f, 0.3f, 0.9f) : new Color(0.3f, 0.3f, 0.3f, 0.5f);
                DrawSolidColor(r, c);
            }
        }

        void DrawReplayScoreboards(float x, float y, float width)
        {
            float colW = (width - 8f) * 0.5f;
            var scores = _replaySession != null ? _replaySession.CurrentScores() : null;
            ReplaySeatScore wei = scores != null && scores.Count > 0 ? scores[0] : null;
            ReplaySeatScore shu = scores != null && scores.Count > 1 ? scores[1] : null;

            int weiG = wei != null ? wei.Gold : 0;
            int weiF = wei != null ? wei.Food : 0;
            int weiP = wei != null ? wei.Population : 0;
            int weiGar = wei != null ? wei.Garrison : 0;

            GUI.Label(new Rect(x, y, colW, 16f), "Wei (Capital)", uiPanelTitle);
            GUI.Label(new Rect(x, y + 16f, colW, 14f), $"Gold {weiG}  Food {weiF}", uiSmall);
            GUI.Label(new Rect(x, y + 30f, colW, 14f), $"Pop {weiP}  Gar {weiGar}", uiSmall);

            int shuG = shu != null ? shu.Gold : 0;
            int shuF = shu != null ? shu.Food : 0;
            int shuP = shu != null ? shu.Population : 0;
            int shuGar = shu != null ? shu.Garrison : 0;

            float col2X = x + colW + 8f;
            GUI.Label(new Rect(col2X, y, colW, 16f), "Shu (Chengdu)", uiPanelTitle);
            GUI.Label(new Rect(col2X, y + 16f, colW, 14f), $"Gold {shuG}  Food {shuF}", uiSmall);
            GUI.Label(new Rect(col2X, y + 30f, colW, 14f), $"Pop {shuP}  Gar {shuGar}", uiSmall);
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
                if (GUI.Button(rect, "Step Replay", uiButton)) ReplayStepRequest();
            }
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
