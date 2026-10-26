using System;
using System.Collections.Generic;
using UnityEngine;
using LuanShi.Engine;

namespace LuanShi
{
    /// <summary>
    /// Observer-only live session chat. It renders the runner's bounded, redacted activity trace;
    /// nothing here is read back by the engine, the recorder or a prompt.
    /// </summary>
    public partial class KingdomDemo
    {
        private Vector2 agentChatScroll;
        private bool agentChatFollow = true;
        private bool agentChatOpen = true;
        private string agentChatFilterSeat;
        private int agentChatSequence;
        private string agentChatRun;
        private GUIStyle agentChatText;
        private GUIStyle agentChatToggle;
        private const float ChatWheelStep = 24f;

        void DrawAgentChat()
        {
            // Single-LLM transparency is an explicit opt-in. Spectator chat is a researcher view.
            if (agentRunner == null) return;
            if (!spectatorMode && (!agentHasSeat || !showAgentDetails)) return;

            float top = (spectatorMode ? SpectatorPanelArea() : AgentPanelArea()).yMax;
            float available = ChatBottomY - top - ChatGap;
            if (available < ChatHeaderHeight) return;

            bool open = agentChatOpen
                && available >= ChatHeaderHeight + ChatFooterHeight + ChatMinBodyHeight;
            float height = open ? available : ChatHeaderHeight;
            Rect area = new Rect(SidePanelX, top + ChatGap, SidePanelWidth, height);
            uiRects.Add(area);
            DrawBackdrop(area, spectatorMode ? CurrentSeatAccent() : AccentGold);

            string title = spectatorMode
                ? "Spectator view (researcher)"
                : "LLM session chat" + (agentFakeMode ? " (FAKE)" : "");
            GUI.Label(new Rect(area.x + 12f, area.y + 5f, area.width - 160f, 24f), title, uiPanelTitle);
            if (GUI.Button(new Rect(area.xMax - 76f, area.y + 4f, 66f, 22f),
                open ? "Hide" : "Show", uiButton)
                && available >= ChatHeaderHeight + ChatFooterHeight + ChatMinBodyHeight)
                agentChatOpen = !agentChatOpen;
            if (!open) return;

            PrepareChatStyles();
            float filterHeight = spectatorMode ? ChatFilterHeight : 0f;
            Rect filterRect = new Rect(area.x + ChatPadX, area.y + ChatHeaderHeight,
                area.width - ChatPadX * 2f, filterHeight);
            if (spectatorMode) DrawChatFilter(filterRect);

            Rect viewport = new Rect(area.x + ChatPadX,
                area.y + ChatHeaderHeight + filterHeight,
                area.width - ChatPadX * 2f,
                area.height - ChatHeaderHeight - filterHeight - ChatFooterHeight);
            IReadOnlyList<LlmActivity> entries = ChatEntries();

            int sequence = entries.Count == 0 ? 0 : entries[entries.Count - 1].Sequence;
            if (agentChatRun != agentRunner.RunId)
            {
                agentChatRun = agentRunner.RunId;
                agentChatScroll = Vector2.zero;
                agentChatSequence = 0;
                agentChatFilterSeat = null;
            }

            // The vertical bar lives inside the viewport. Reserve its width before wrapping or
            // measuring, otherwise long lines vanish under the bar and a horizontal scrollbar appears.
            float scrollbarWidth = GUI.skin.verticalScrollbar.fixedWidth;
            if (scrollbarWidth <= 0f) scrollbarWidth = 16f;
            float contentWidth = Mathf.Max(1f, viewport.width - scrollbarWidth);
            float textWidth = Mathf.Max(1f, contentWidth - ChatTextPad);

            float contentHeight = 0f;
            foreach (LlmActivity entry in entries)
                contentHeight += ChatEntryHeight(entry, textWidth);
            contentHeight = Mathf.Max(viewport.height, contentHeight);
            Rect content = new Rect(0f, 0f, contentWidth, contentHeight);
            float maxScroll = ChatScrollLogic.MaxScroll(contentHeight, viewport.height);

            bool newEntries = sequence != agentChatSequence;
            if (agentChatFollow && newEntries) agentChatScroll.y = maxScroll;
            HandleChatWheel(viewport, maxScroll);
            // Follow sticks to the newest entry on every frame, which also covers the first frame
            // after the panel opens. A wheel/drag away from the bottom clears follow below.
            agentChatScroll.y = agentChatFollow
                ? maxScroll
                : ChatScrollLogic.ClampOffset(agentChatScroll.y, maxScroll);

            float before = agentChatScroll.y;
            agentChatScroll = GUI.BeginScrollView(viewport, agentChatScroll, content,
                false, false, GUIStyle.none, GUI.skin.verticalScrollbar);
            if (Mathf.Abs(agentChatScroll.y - before) > 0.5f)
                agentChatFollow = ChatScrollLogic.FollowsAfterScroll(agentChatScroll.y, maxScroll);

            float y = 0f;
            foreach (LlmActivity entry in entries)
            {
                float entryHeight = ChatEntryHeight(entry, textWidth);
                Color previous = uiSmall.normal.textColor;
                uiSmall.normal.textColor = factionColors.TryGetValue(entry.SeatId, out Color color)
                    ? color : Ink;
                GUI.Label(new Rect(0f, y, textWidth, 18f), ChatEntryHeading(entry), uiSmall);
                uiSmall.normal.textColor = previous;
                GUI.Label(new Rect(0f, y + 18f, textWidth, entryHeight - 18f),
                    ChatDisplayText(entry), agentChatText);
                y += entryHeight;
            }
            if (entries.Count == 0)
                GUI.Label(new Rect(0f, 0f, textWidth, 50f),
                    "Turn progress, brief explanations and engine results will appear here.",
                    agentChatText);
            GUI.EndScrollView();
            agentChatSequence = sequence;

            bool wasFollow = agentChatFollow;
            Rect followRect = new Rect(area.x + 12f, area.yMax - ChatFooterHeight + 4f,
                104f, 24f);
            agentChatFollow = GUI.Toggle(followRect, agentChatFollow, "Follow live", agentChatToggle);
            DrawChatCheck(new Rect(followRect.x + 2f, followRect.y + 4f, 15f, 15f), agentChatFollow);
            if (!wasFollow && agentChatFollow) agentChatScroll.y = maxScroll;

            string footer = spectatorMode
                ? "Not shown to the models\nFilter one seat"
                : "Observer view\nBrief explanations only";
            GUI.Label(new Rect(area.x + 120f, area.yMax - ChatFooterHeight + 2f,
                area.width - 130f, ChatFooterHeight - 2f), footer, uiSmallWrap);
        }

        /// <summary>
        /// Explicit wheel handling for the chat viewport. The TBTK guard only disables camera zoom;
        /// without consuming this event the ScrollView would move by a different step and follow
        /// could not distinguish a deliberate upward scroll from a new entry.
        /// </summary>
        private void HandleChatWheel(Rect viewport, float maxScroll)
        {
            Event e = Event.current;
            if (e == null || e.type != EventType.ScrollWheel) return;
            UiPointerMapping.ToDesignSpaceFromTopLeft(e.mousePosition.x, e.mousePosition.y,
                Screen.width, out float mouseX, out float mouseY);
            if (!viewport.Contains(new Vector2(mouseX, mouseY))) return;
            agentChatScroll.y = ChatScrollLogic.OffsetAfterWheel(
                agentChatScroll.y, e.delta.y, ChatWheelStep, maxScroll);
            agentChatFollow = ChatScrollLogic.FollowsAfterScroll(agentChatScroll.y, maxScroll);
            e.Use();
        }

        /// <summary>Small ink checkbox; the skin toggle has no readable box on the parchment panel.</summary>
        private void DrawChatCheck(Rect box, bool on)
        {
            FillRect(new Rect(box.x, box.y, box.width, 1f), Ink);
            FillRect(new Rect(box.x, box.yMax - 1f, box.width, 1f), Ink);
            FillRect(new Rect(box.x, box.y, 1f, box.height), Ink);
            FillRect(new Rect(box.xMax - 1f, box.y, 1f, box.height), Ink);
            if (!on) return;
            FillRect(new Rect(box.x + 3f, box.y + 9f, 2f, 2f), Ink);
            FillRect(new Rect(box.x + 5f, box.y + 11f, 2f, 2f), Ink);
            FillRect(new Rect(box.x + 7f, box.y + 9f, 2f, 2f), Ink);
            FillRect(new Rect(box.x + 9f, box.y + 7f, 2f, 2f), Ink);
            FillRect(new Rect(box.x + 11f, box.y + 5f, 2f, 2f), Ink);
        }

        void PrepareChatStyles()
        {
            if (agentChatText != null) return;
            agentChatText = new GUIStyle(uiSmallWrap) { richText = false, clipping = TextClipping.Clip };
            agentChatToggle = new GUIStyle(GUI.skin.toggle)
            {
                fontSize = 14,
                alignment = TextAnchor.MiddleLeft,
                wordWrap = false,
                clipping = TextClipping.Clip,
                padding = new RectOffset(22, 0, 0, 0),
            };
            // Draw the box ourselves so it is visible in ink on the parchment plate.
            agentChatToggle.normal.background = null;
            agentChatToggle.onNormal.background = null;
            agentChatToggle.hover.background = null;
            agentChatToggle.onHover.background = null;
            agentChatToggle.active.background = null;
            agentChatToggle.onActive.background = null;
            agentChatToggle.focused.background = null;
            agentChatToggle.onFocused.background = null;
            agentChatToggle.normal.textColor = Ink;
            agentChatToggle.onNormal.textColor = Ink;
            agentChatToggle.hover.textColor = Ink;
            agentChatToggle.onHover.textColor = Ink;
            agentChatToggle.active.textColor = Ink;
            agentChatToggle.onActive.textColor = Ink;
            agentChatToggle.focused.textColor = Ink;
            agentChatToggle.onFocused.textColor = Ink;
        }

        private void DrawChatFilter(Rect rect)
        {
            // Spectator chat is a researcher view, but even here the filter names come from the
            // observation data captured on activity entries, never from canonical engine state.
            // Single-LLM mode therefore cannot reach another seat's canonical identity through chat.
            var options = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>(null, "All"),
            };
            if (agentRunner != null)
            {
                var seats = new List<string>();
                var names = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (LlmActivity entry in agentRunner.Activity)
                {
                    if (string.IsNullOrEmpty(entry.SeatId)) continue;
                    if (!names.ContainsKey(entry.SeatId))
                    {
                        seats.Add(entry.SeatId);
                        names[entry.SeatId] = null;
                    }
                    if (!string.IsNullOrEmpty(entry.FactionName)) names[entry.SeatId] = entry.FactionName;
                }
                foreach (string seat in seats)
                    options.Add(new KeyValuePair<string, string>(seat,
                        string.IsNullOrEmpty(names[seat]) ? "LLM seat " + seat : names[seat]));
            }

            float gap = 4f;
            float width = (rect.width - gap * (options.Count - 1)) / options.Count;
            for (int i = 0; i < options.Count; i++)
            {
                var button = new Rect(rect.x + i * (width + gap), rect.y + 1f, width, 22f);
                bool selected = string.Equals(agentChatFilterSeat, options[i].Key, StringComparison.Ordinal);
                if (GUI.Button(button, options[i].Value, selected ? uiTabActive : uiButton))
                    agentChatFilterSeat = options[i].Key;
            }
        }

        private IReadOnlyList<LlmActivity> ChatEntries()
        {
            if (!spectatorMode) return agentRunner.ActivityForSeat(agentSeatId);
            return string.IsNullOrEmpty(agentChatFilterSeat)
                ? agentRunner.Activity
                : agentRunner.ActivityForSeat(agentChatFilterSeat);
        }

        private string ChatEntryHeading(LlmActivity entry)
        {
            string faction = null;
            if (obs != null)
            {
                FactionView view = obs.Faction(entry.SeatId);
                if (view != null) faction = view.Name;
            }
            // Never fall back to canonical state here: in single-LLM mode the chat may only show
            // observation-authorized names, or a neutral label derived from the entry itself.
            if (string.IsNullOrEmpty(faction)) faction = entry.DisplayFactionName;
            string season = entry.Season > 0 ? "season " + entry.Season : "season";
            return faction + " | " + season + " | " + ChatKindLabel(entry.Kind);
        }

        private static string ChatKindLabel(string kind)
        {
            switch (kind)
            {
                case "decision": return "decision explanation";
                case "command": return "command";
                case "result": return "engine result";
                case "validation": return "validation";
                case "request": return "request";
                case "error": return "error";
                case "finished": return "turn end";
                default: return "status";
            }
        }

        private static string ChatDisplayText(LlmActivity entry) => entry.Text ?? "";

        private float ChatEntryHeight(LlmActivity entry, float width)
            => 18f + Mathf.Max(18f,
                   agentChatText.CalcHeight(new GUIContent(ChatDisplayText(entry)), width))
               + ChatEntryGap;
    }
}
