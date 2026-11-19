// KingdomDemo (partial) — readable current-event card and event feed for offline replay.

using System;
using UnityEngine;
using LuanShi.Engine;

namespace LuanShi
{
    public partial class KingdomDemo : MonoBehaviour
    {
        private Vector2 _replayCurrentEventScroll;

        void DrawReplayCurrentEventCard(float x, float y, float width, float height)
        {
            DrawSolidColor(new Rect(x, y, width, height), new Color(0.08f, 0.10f, 0.13f, 0.78f));

            var feed = _replaySession != null ? _replaySession.EventFeed : null;
            int count = feed != null ? feed.Count : 0;
            if (count == 0)
            {
                GUI.Label(new Rect(x + 8f, y + 8f, width - 16f, 22f), "CURRENT EVENT — READY AT SEASON 1 START", uiPanelTitle);
                GUI.Label(new Rect(x + 8f, y + 34f, width - 16f, height - 42f),
                    "Press Step (or Space / Play) to step through recorded model actions, battles, routs, and captures.", uiBodyWrap);
                return;
            }

            var evt = feed[count - 1];
            string mark = evt.ResultOk ? "✓" : "✗";
            string persona = !string.IsNullOrEmpty(evt.PersonaId) ? evt.PersonaId : "unknown";
            string title = $"#{evt.Sequence} S{evt.Season} R{evt.RequestIndex} | {evt.Faction} ({persona}) | {mark} {evt.ResultText}";
            GUI.Label(new Rect(x + 8f, y + 6f, width - 16f, 22f), EllipsizeToWidth(uiPanelTitle, title, width - 16f), uiPanelTitle);

            float innerX = x + 8f;
            float innerW = width - 28f;
            string headlineText = !string.IsNullOrEmpty(evt.Headline) ? evt.Headline : evt.ActionSummary;
            string outcomeText = !string.IsNullOrEmpty(evt.OutcomeDetails) ? evt.OutcomeDetails : "";
            string expText = !string.IsNullOrEmpty(evt.Explanation)
                ? $"Recorded explanation: \"{evt.Explanation}\""
                : "Recorded explanation: none recorded for this request";

            float headlineH = Mathf.Max(22f, uiBodyWrap.CalcHeight(new GUIContent(headlineText), innerW) + 2f);
            float outcomeH = !string.IsNullOrEmpty(outcomeText)
                ? Mathf.Max(22f, uiBodyWrap.CalcHeight(new GUIContent(outcomeText), innerW - 8f) + 8f)
                : 0f;
            float expH = Mathf.Max(22f, uiBodyWrap.CalcHeight(new GUIContent(expText), innerW) + 4f);
            float contentH = headlineH + (outcomeH > 0f ? outcomeH + 6f : 0f) + expH + 6f;

            Rect viewRect = new Rect(x + 4f, y + 30f, width - 8f, height - 34f);
            _replayCurrentEventScroll = GUI.BeginScrollView(viewRect, _replayCurrentEventScroll, new Rect(0, 0, innerW, contentH));

            float cy = 0f;
            GUI.Label(new Rect(4f, cy, innerW, headlineH), headlineText, uiBodyWrap);
            cy += headlineH + 4f;

            if (outcomeH > 0f)
            {
                DrawSolidColor(new Rect(4f, cy, innerW, outcomeH), new Color(0.24f, 0.16f, 0.08f, 0.85f));
                GUI.Label(new Rect(8f, cy + 4f, innerW - 8f, outcomeH - 6f), outcomeText, uiBodyWrap);
                cy += outcomeH + 4f;
            }

            GUI.Label(new Rect(4f, cy, innerW, expH), expText, uiBodyWrap);
            GUI.EndScrollView();
        }

        void DrawReplayEventFeed(float x, float y, float width, float height)
        {
            var feed = _replaySession != null ? _replaySession.EventFeed : null;
            int count = feed != null ? feed.Count : 0;
            GUI.Label(new Rect(x, y, width, 16f), $"EVENT FEED ({count} events replayed — click row for prompt/reply):", uiPanelTitle);
            y += 18f;
            float viewH = height - 18f;

            if (count == 0)
            {
                GUI.Label(new Rect(x, y, width, 30f), "Replay at start. Click Play or Step to replay recorded actions.", uiHint);
                return;
            }

            float rowHeight = 52f;
            float contentHeight = count * rowHeight;
            Rect viewRect = new Rect(x, y, width, viewH);
            _replayFeedScroll = GUI.BeginScrollView(viewRect, _replayFeedScroll, new Rect(0, 0, width - 20f, contentHeight));

            for (int i = 0; i < count; i++)
            {
                var evt = feed[i];
                float rowY = i * rowHeight;
                Rect r = new Rect(0, rowY, width - 20f, rowHeight - 2f);
                bool hasOutcome = !string.IsNullOrEmpty(evt.OutcomeDetails);
                Color rowBg = hasOutcome
                    ? new Color(0.20f, 0.14f, 0.08f, 0.75f)
                    : (i % 2 == 0 ? new Color(0.12f, 0.12f, 0.12f, 0.6f) : new Color(0.08f, 0.08f, 0.08f, 0.6f));
                DrawSolidColor(r, rowBg);

                string mark = evt.ResultOk ? "✓" : "✗";
                string link = !string.IsNullOrEmpty(evt.ExchangeId) ? $" | {evt.ExchangeId}" : "";
                string header = $"#{evt.Sequence} S{evt.Season} R{evt.RequestIndex}{link} | {evt.Faction} | {mark} {evt.ResultText}";
                GUI.Label(new Rect(4f, rowY + 1f, width - 28f, 15f), EllipsizeToWidth(uiSmall, header, width - 28f), uiSmall);

                string actText = !string.IsNullOrEmpty(evt.OutcomeDetails)
                    ? evt.OutcomeDetails.Replace("\n", " ; ")
                    : (!string.IsNullOrEmpty(evt.Headline) ? evt.Headline : evt.ActionSummary);
                GUI.Label(new Rect(4f, rowY + 16f, width - 28f, 16f), EllipsizeToWidth(uiBody, actText, width - 28f), uiBody);

                if (evt.RequestIndex > 0)
                {
                    string exp = !string.IsNullOrEmpty(evt.Explanation) ? evt.Explanation : "none recorded";
                    GUI.Label(new Rect(4f, rowY + 34f, width - 28f, 14f), EllipsizeToWidth(uiHint, $"Explanation: {exp}", width - 28f), uiHint);
                    if (GUI.Button(r, GUIContent.none, GUIStyle.none))
                    {
                        int idx = FindRequestIndexForFeedEvent(evt);
                        if (idx >= 0) { _replayInspectedRequestIndex = idx; _replaySubTab = "details"; }
                    }
                }
            }

            GUI.EndScrollView();
        }
    }
}
