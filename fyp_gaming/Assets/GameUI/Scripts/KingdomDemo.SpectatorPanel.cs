// KingdomDemo (partial) — the researcher panel for the optional LLM-vs-LLM spectator pilot.
//
// It replaces the single-agent panel while spectatorMode is on and is docked on the right as a compact
// side panel, leaving the centre of the map clear. Everything here is display only: the scoreboard
// reads canonical state for the human researcher and is NEVER handed to the runner, a prompt or a
// model. The map shows only the active seat's filtered observation (see KingdomDemo.Spectator.cs).

using System;
using System.Globalization;
using UnityEngine;
using LuanShi.Engine;

namespace LuanShi
{
    public partial class KingdomDemo : MonoBehaviour
    {
        static Rect SpectatorPanelArea()
            => new Rect(SidePanelX, SidePanelY, SidePanelWidth, SpectatorPanelHeight);

        void DrawSpectatorPanel()
        {
            Rect area = SpectatorPanelArea();
            uiRects.Add(area);
            DrawBackdrop(area, CurrentSeatAccent());

            SpectatorSession session = spectatorSession;
            float x = area.x + PanelPadX;
            float width = area.width - PanelPadX * 2f;

            GUI.Label(new Rect(area.x + PanelHeaderPadX, area.y + SpectatorTitleRowY,
                               area.width - PanelHeaderPadX * 2f, 20f),
                EllipsizeToWidth(uiPanelTitle, "LLM vs LLM pilot - domestic management",
                                 area.width - PanelHeaderPadX * 2f), uiPanelTitle);

            string provider = agentTransport != null
                ? agentTransport.Provider + "/" + agentTransport.Model
                : agentConfig != null ? agentConfig.Provider + "/" + agentConfig.Model
                : "not configured";
            provider = (agentFakeMode ? "FAKE  " : "LIVE  ") + provider;
            GUI.Label(new Rect(x, area.y + SpectatorProviderRowY, width, 14f),
                EllipsizeToWidth(uiSmall, provider, width), uiSmall);

            if (session == null)
            {
                GUI.Label(new Rect(x, area.y + SpectatorStateRowY, width, 16f),
                    EllipsizeToWidth(uiReason, "Unavailable: " + (spectatorSetupError ?? "spectator has not started"), width),
                    uiReason);
                GUI.Label(new Rect(x, area.y + SpectatorContextRowY, width, 16f),
                    "Viewing " + ViewedFactionNameFromObservation() + "'s filtered observation", uiSmall);
                DrawSpectatorControls(area, null);
                return;
            }

            string state = SpectatorStateName(session.State);
            state += "  " + SpectatorElapsed(session);
            GUI.Label(new Rect(x, area.y + SpectatorStateRowY, width, 16f),
                EllipsizeToWidth(uiBody, state, width), uiBody);

            DrawSeasonPips(area, session);
            DrawFactionColumns(area, x, width, session);
            DrawSpectatorRecent(area, x, width, session);
            DrawSpectatorControls(area, session);
        }

        /// <summary>
        /// Two named, faction-coloured columns. The scoreboard comes from SpectatorSession, the
        /// researcher-only aggregate; it is never given to a model. Each faction's last action and
        /// decision wrap inside its own column.
        /// </summary>
        void DrawFactionColumns(Rect area, float x, float width, SpectatorSession session)
        {
            int factions = Mathf.Min(2, engine.State.Factions.Count);
            float columnWidth = (width - SpectatorColumnGap) * 0.5f;
            var scores = session.Scoreboard();

            for (int i = 0; i < factions; i++)
            {
                FactionState faction = engine.State.Factions[i];
                SpectatorScore score = scores.Find(s => s.SeatId == faction.SeatId);
                float columnX = x + i * (columnWidth + SpectatorColumnGap);
                Color color = factionColors.TryGetValue(faction.SeatId, out var factionColor)
                    ? factionColor : Ink;

                var previous = uiPanelTitle.normal.textColor;
                uiPanelTitle.normal.textColor = color;
                string name = faction.Name + " (" + SpectatorControllerLabel(faction.SeatId) + ")";
                GUI.Label(new Rect(columnX, area.y + SpectatorColumnsRowY, columnWidth, 20f),
                          EllipsizeToWidth(uiPanelTitle, name, columnWidth), uiPanelTitle);
                uiPanelTitle.normal.textColor = previous;

                var previousSmall = uiSmall.normal.textColor;
                uiSmall.normal.textColor = color;
                string[] lines =
                {
                    $"Gold {score?.Gold ?? 0}  -  Food {score?.Food ?? 0}",
                    $"Population {score?.Population ?? 0}",
                    $"Garrison {score?.Garrison ?? 0}",
                };
                for (int row = 0; row < lines.Length; row++)
                    GUI.Label(new Rect(columnX, area.y + SpectatorScoreRowY + row * SpectatorScoreRowGap,
                                       columnWidth, SpectatorRowHeight),
                              EllipsizeToWidth(uiSmall, lines[row], columnWidth), uiSmall);
                uiSmall.normal.textColor = previousSmall;

                bool active = string.Equals(session.ActiveSeatId, faction.SeatId, StringComparison.Ordinal);
                LlmTurnRunner runner = active ? session.Runner : null;
                SpectatorTurnRecord record = LastRecordFor(session, faction.SeatId);
                // An active seat uses only the current runner turn. Falling back to its previous
                // record here would show stale decision/command/result rows at the start of a turn.
                string commandJson = active ? runner?.LastCommandJson : record?.LastCommandJson;
                string result = active ? runner?.LastResult : record?.LastResult;
                string summary = active ? runner?.LastSummary : record?.LastSummary;
                string endReason = active
                    ? (runner != null && runner.EndReason != EndReason.None ? runner.EndReasonText : null)
                    : record?.EndReasonText;

                string type = SpectatorCommandField(commandJson, "type");
                string target = SpectatorCommandField(commandJson, "target_id");
                string action = string.IsNullOrEmpty(type) ? "-" : type
                    + (string.IsNullOrEmpty(target) ? "" : " " + target);
                GUI.Label(new Rect(columnX, area.y + SpectatorLastRowY, columnWidth, SpectatorRowHeight),
                          EllipsizeToWidth(uiSmall, "Last: " + action, columnWidth), uiSmall);
                string resultLine = "Result: " + SpectatorResultLabel(result)
                    + (string.IsNullOrEmpty(endReason) ? "" : " | " + endReason);
                float resultHeight = Mathf.Max(SpectatorDecisionLineHeight, TextLineHeight(uiSmallWrap))
                                     * SpectatorResultLines;
                DrawWrappedClampedLabel(
                    new Rect(columnX, area.y + SpectatorResultRowY, columnWidth, resultHeight),
                    resultLine, uiSmallWrap, SpectatorResultLines, endReason);
                float decisionHeight = Mathf.Max(SpectatorDecisionLineHeight, TextLineHeight(uiSmallWrap))
                                       * SpectatorDecisionLines;
                DrawWrappedClampedLabel(
                    new Rect(columnX, area.y + SpectatorDecisionRowY, columnWidth, decisionHeight),
                    "Decision: " + (string.IsNullOrEmpty(summary) ? "-" : summary),
                    uiSmallWrap, SpectatorDecisionLines, summary);
            }
        }

        void DrawSpectatorRecent(Rect area, float x, float width, SpectatorSession session)
        {
            GUI.Label(new Rect(x, area.y + SpectatorRecentHeaderRowY, width, 14f), "Recent actions", uiSmall);
            var recent = session.RecentActions;
            int firstRecent = Mathf.Max(0, recent.Count - SpectatorRecentLines);
            for (int i = firstRecent; i < recent.Count; i++)
            {
                float rowY = area.y + SpectatorRecentFirstRowY
                             + (i - firstRecent) * SpectatorRecentLineHeight;
                string line = (agentFakeMode ? "FAKE  " : "") + recent[i];
                GUI.Label(new Rect(x, rowY, width, SpectatorRecentLineHeight),
                          EllipsizeToWidth(uiMono, line, width), uiMono);
            }

            int cap = agentConfig != null ? agentConfig.MaxTotalRequests : 0;
            bool tokensKnown = session.TotalInputTokens.HasValue && session.TotalOutputTokens.HasValue;
            string tokens = tokensKnown
                ? CompactCount(session.TotalInputTokens.Value) + " in / "
                  + CompactCount(session.TotalOutputTokens.Value) + " out"
                : "unknown";
            string totals = $"Req {session.TotalRequests}/{cap} | tokens {tokens}";
            GUI.Label(new Rect(x, area.y + SpectatorRequestsRowY, width, SpectatorTotalsRowHeight),
                      totals, uiSmall);

            string cost = session.CostUsd.HasValue
                ? "cost: $" + session.CostUsd.Value.ToString("0.######", CultureInfo.InvariantCulture)
                : "cost: unknown (provider)";
            GUI.Label(new Rect(x, area.y + SpectatorCostRowY, width, SpectatorTotalsRowHeight),
                      cost, uiSmall);
        }

        void DrawSpectatorControls(Rect area, SpectatorSession session)
        {
            float x = area.x + PanelPadX;
            float width = area.width - PanelPadX * 2f;
            float y = area.y + SpectatorControlsRowY;
            int cap = agentConfig != null ? agentConfig.MaxTotalRequests : 0;
            int requests = session != null ? session.TotalRequests : 0;
            bool running = session != null && session.State == SpectatorState.Running;
            bool paused = session != null && session.State == SpectatorState.Paused;
            bool canExtend = session != null && session.State != SpectatorState.Failed
                && cap - requests >= 12 && engine.State.WinnerSeatId == null;

            float buttonWidth = (width - SpectatorButtonGap * 2f) / 3f;
            SpectatorButton(new Rect(x, y, buttonWidth, SpectatorButtonHeight), "Stop", running, StopSpectator);
            SpectatorButton(new Rect(x + buttonWidth + SpectatorButtonGap, y, buttonWidth, SpectatorButtonHeight),
                            "Resume", paused, ResumeSpectator);
            SpectatorButton(new Rect(x + (buttonWidth + SpectatorButtonGap) * 2f, y, buttonWidth, SpectatorButtonHeight),
                            "+3 seasons", canExtend, () => ExtendSpectator(3));

            string saved = string.IsNullOrEmpty(spectatorSummaryPath)
                ? "Run record: not saved"
                : spectatorSummaryPath.StartsWith("runs/", StringComparison.Ordinal)
                    ? "Run record saved" : "Run record could not be saved";
            DrawFittedLabel(new Rect(x, area.y + SpectatorSummaryRowY, width, SpectatorSummaryHeight),
                            EllipsizeToWidth(uiMono, saved, width), uiMono, spectatorSummaryPath);
        }

        void DrawSpectatorTurnControl()
        {
            var rect = new Rect(EndTurnX, EndTurnY, EndTurnWidth, EndTurnHeight);
            uiRects.Add(rect);
            SpectatorSession session = spectatorSession;
            if (session != null && session.State == SpectatorState.Running)
            {
                if (GUI.Button(rect, "Stop pilot", uiButton)) StopSpectator();
            }
            else if (session != null && session.State == SpectatorState.Paused)
            {
                if (GUI.Button(rect, "Resume", uiButton)) ResumeSpectator();
            }
            else
            {
                var previous = GUI.enabled;
                GUI.enabled = false;
                string label = session == null ? "Spectator"
                    : session.State == SpectatorState.Completed ? "Pilot completed"
                    : session.State == SpectatorState.Failed ? "Pilot failed"
                    : "Pilot starting";
                GUI.Button(rect, label, uiButtonDisabled);
                GUI.enabled = previous;
            }
        }

        void DrawSeasonPips(Rect area, SpectatorSession session)
        {
            int planned = Mathf.Min(session.SeasonsPlanned, 12);
            for (int i = 0; i < planned; i++)
            {
                var pip = new Rect(area.x + PanelPadX + i * SpectatorPipWidth,
                                   area.y + SpectatorPipsRowY, 18f, SpectatorPipHeight);
                bool played = i < session.SeasonsPlayed;
                bool current = !played && i == session.SeasonsPlayed
                    && session.State != SpectatorState.Completed;
                FillRect(pip, played ? AccentGold : current ? TabSelected : ButtonDisabled);
            }

            float pipsEnd = area.x + PanelPadX + planned * SpectatorPipWidth;
            if (session.SeasonsPlanned > 12)
            {
                GUI.Label(new Rect(pipsEnd, area.y + SpectatorPipsRowY - 5f, 30f, 14f),
                    "+" + (session.SeasonsPlanned - 12), uiSmall);
                pipsEnd += 30f;
            }

            string caption = $"Season {session.CurrentSeason} of {session.SeasonsPlanned}";
            float captionX = pipsEnd + 6f;
            float captionWidth = Mathf.Max(0f, area.x + area.width - PanelPadX - captionX);
            GUI.Label(new Rect(captionX, area.y + SpectatorPipsRowY - 5f, captionWidth, 14f),
                EllipsizeToWidth(uiSmall, caption, captionWidth), uiSmall);
        }

        void SpectatorButton(Rect rect, string label, bool enabled, Action action)
        {
            if (GUI.Button(rect, label, enabled ? uiButton : uiButtonDisabledSmall) && enabled) action?.Invoke();
        }

        string SpectatorControllerLabel(string seatId)
        {
            if (engine == null || string.IsNullOrEmpty(seatId)) return "LLM";
            int index = engine.State.Factions.FindIndex(f => f.SeatId == seatId);
            return index >= 0 && index < 26 ? "LLM " + (char)('A' + index) : "LLM";
        }

        static string SpectatorStateName(SpectatorState state)
        {
            switch (state)
            {
                case SpectatorState.Running: return "Running";
                case SpectatorState.Paused: return "Paused";
                case SpectatorState.Completed: return "Completed";
                case SpectatorState.Failed: return "Failed";
                default: return "Starting";
            }
        }

        string SpectatorElapsed(SpectatorSession session)
        {
            DateTime end = session.EndedUtc ?? DateTime.UtcNow;
            double seconds = (end - session.StartedUtc).TotalSeconds;
            if (seconds < 0) seconds = 0;
            return Math.Round(seconds).ToString("0", CultureInfo.InvariantCulture) + "s";
        }

        static SpectatorTurnRecord LastRecordFor(SpectatorSession session, string seatId)
        {
            if (session == null || string.IsNullOrEmpty(seatId)) return null;
            var records = session.TurnRecords;
            for (int i = records.Count - 1; i >= 0; i--)
                if (string.Equals(records[i].SeatId, seatId, StringComparison.Ordinal)) return records[i];
            return null;
        }

        static string SpectatorCommandField(string json, string field)
        {
            if (string.IsNullOrEmpty(json)) return null;
            if (!StrictJson.TryParse(json, out JsonNode root, out _) || !(root is JsonObject obj)) return null;
            if (!obj.TryGet(field, out JsonNode node) || node == null) return null;
            if (node is JsonString text) return text.Value;
            if (node is JsonInt number) return number.Value.ToString(CultureInfo.InvariantCulture);
            return null;
        }

        static string SpectatorResultLabel(string result)
        {
            if (string.IsNullOrEmpty(result)) return "-";
            if (result == "ok" || result == "end_turn") return "ok";
            if (result.StartsWith("invalid response", StringComparison.Ordinal)
                || result.StartsWith("provider", StringComparison.Ordinal)
                || result.StartsWith("failed", StringComparison.Ordinal))
                return AgentShort(result, 46);
            return "rejected: " + AgentShort(result, 40);
        }

        /// <summary>Short, non-clipped token totals that fit the dock at every demo resolution.</summary>
        static string CompactCount(long value)
        {
            if (value < 1000) return value.ToString(CultureInfo.InvariantCulture);
            if (value < 1000000)
                return (value / 1000.0).ToString("0.#", CultureInfo.InvariantCulture) + "k";
            return (value / 1000000.0).ToString("0.#", CultureInfo.InvariantCulture) + "M";
        }
    }
}
