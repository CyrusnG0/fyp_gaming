// KingdomDemo (partial) — the compact, docked LLM agent details panel.
//
// Presentation-only: it reads the runner/config fields the agent owns and never adds an information
// path to the game. The idle panel is four short rows. Decision/command/result rows appear only while
// the agent is running or after a turn, wrapped and clipped inside the docked rect; the full text stays
// in the tooltip/feed. The run-record path is shown only in a tooltip, not as primary UI text.

using UnityEngine;
using LuanShi.Engine;

namespace LuanShi
{
    public partial class KingdomDemo : MonoBehaviour
    {
        Rect AgentPanelArea()
            => new Rect(AgentPanelX, AgentPanelY, AgentPanelWidth,
                        AgentTurnRowsVisible() ? AgentPanelDetailHeight : AgentPanelIdleHeight);

        /// <summary>True once the runner has produced something for this turn (running included).</summary>
        bool AgentTurnRowsVisible()
        {
            if (IsAgentRunning()) return true;
            LlmTurnRunner runner = agentRunner;
            if (runner == null) return false;
            return runner.State == RunnerState.Finished
                || runner.State == RunnerState.Failed
                || runner.State == RunnerState.Cancelled
                || !string.IsNullOrEmpty(runner.LastSummary)
                || !string.IsNullOrEmpty(runner.LastCommandJson)
                || !string.IsNullOrEmpty(runner.LastResult);
        }

        partial void DrawAgentPanel(Rect area)
        {
            if (!agentHasSeat) { uiRects.Remove(area); return; }   // nothing drawn = no swallowed click
            if (!uiRects.Contains(area)) uiRects.Add(area);
            Color accent = agentSeatId != null && factionColors.TryGetValue(agentSeatId, out var tint)
                ? tint : AccentGold;
            DrawBackdrop(area, accent);

            float x = area.x + PanelPadX;
            float width = area.width - PanelPadX * 2f;
            bool running = IsAgentRunning();
            bool turnRows = AgentTurnRowsVisible();

            float titleX = area.x + PanelHeaderPadX;
            float titleWidth = Mathf.Max(80f, area.width - AgentCancelWidth - PanelHeaderPadX * 2f - 8f);
            GUI.Label(new Rect(titleX, area.y + AgentTitleRowY, titleWidth, AgentTitleHeight),
                      EllipsizeToWidth(uiPanelTitle, $"Agent: {agentFactionName} (LLM)", titleWidth),
                      uiPanelTitle);
            var cancelRect = new Rect(area.x + area.width - PanelHeaderPadX - AgentCancelWidth,
                                      area.y + AgentTitleRowY, AgentCancelWidth, AgentCancelHeight);
            var previousEnabled = GUI.enabled;
            GUI.enabled = running;
            if (GUI.Button(cancelRect, "Cancel", running ? uiButton : uiButtonDisabledSmall)) CancelAgentTurn();
            GUI.enabled = previousEnabled;

            string provider = agentTransport != null
                ? agentTransport.Provider + " / " + agentTransport.Model
                : (agentConfig != null ? agentConfig.Provider + " / " + agentConfig.Model : "not configured");
            string mode = agentFakeMode ? "FAKE" : "LIVE";
            GUI.Label(new Rect(x, area.y + AgentProviderRowY, width, AgentRowHeight),
                      EllipsizeToWidth(uiSmall, mode + "  " + provider, width), uiSmall);

            GUI.Label(new Rect(x, area.y + AgentStateRowY, width, AgentRowHeight),
                      EllipsizeToWidth(uiBody, AgentStateLine(), width), uiBody);

            GUI.Label(new Rect(x, area.y + AgentRequestsRowY, width, AgentRowHeight),
                      EllipsizeToWidth(uiSmall, AgentRequestsLine(), width), uiSmall);

            if (!turnRows) return;

            if (showAgentDetails)
            {
                string summary = agentRunner != null ? agentRunner.LastSummary : null;
                float decisionHeight = Mathf.Max(AgentLineHeight, TextLineHeight(uiSmallWrap))
                                       * AgentDecisionLines;
                DrawWrappedClampedLabel(
                    new Rect(x, area.y + AgentDecisionRowY, width, decisionHeight),
                    "Decision: " + (string.IsNullOrEmpty(summary) ? "-" : summary),
                    uiSmallWrap, AgentDecisionLines, summary);

                string command = agentRunner != null ? agentRunner.LastCommandJson : null;
                float commandHeight = Mathf.Max(AgentMonoRowHeight, TextLineHeight(uiMonoWrap))
                                      * AgentCommandLines;
                DrawWrappedClampedLabel(
                    new Rect(x, area.y + AgentCommandRowY, width, commandHeight),
                    "Command: " + (string.IsNullOrEmpty(command) ? "-" : command),
                    uiMonoWrap, AgentCommandLines, command);

                GUI.Label(new Rect(x, area.y + AgentResultRowY, width, AgentRowHeight),
                          EllipsizeToWidth(uiSmall, "Result: " + AgentResultLabel(), width), uiSmall);
            }
            else
            {
                GUI.Label(new Rect(x, area.y + AgentDecisionRowY, width, AgentRowHeight),
                          EllipsizeToWidth(uiSmall, "Action: " + AgentShort(agentLastActionType, 64), width), uiSmall);
                GUI.Label(new Rect(x, area.y + AgentResultRowY, width, AgentRowHeight),
                          EllipsizeToWidth(uiSmall, "Result: " + AgentPublicResultLabel(), width), uiSmall);
            }

            GUI.Label(new Rect(x, area.y + AgentTokensRowY, width, AgentRowHeight),
                      EllipsizeToWidth(uiSmall, AgentTokensLabel(), width), uiSmall);
            GUI.Label(new Rect(x, area.y + AgentCostRowY, width, AgentRowHeight),
                      AgentCostLabel(), uiSmallWrap);

            string footer = AgentRecorderLabel();
            if (showAgentDetails) footer = "Observer detail (demo transparency)  ·  " + footer;
            DrawFittedLabel(new Rect(x, area.y + AgentRecorderRowY, width, AgentRowHeight),
                            EllipsizeToWidth(uiMono, footer, width), uiMono, agentRecorderShort);

            string failure = AgentFailureReason();
            if (showAgentDetails && !string.IsNullOrEmpty(failure))
                GUI.Label(new Rect(x, area.y + AgentFailureRowY, width, AgentRowHeight),
                          EllipsizeToWidth(uiReason, "failure: " + failure, width), uiReason);
        }

        string AgentStateLine()
        {
            string state = AgentStateDisplayName();
            string elapsed = AgentElapsedLabel();
            if (elapsed.Length > 0) state += "  " + elapsed;
            return state;
        }

        static string CapitalizeAgentState(string value)
            => string.IsNullOrEmpty(value)
                ? "-"
                : char.ToUpperInvariant(value[0]) + value.Substring(1);

        string AgentStateDisplayName() => CapitalizeAgentState(AgentStateName());

        string AgentRequestsLine()
        {
            LlmTurnRunner runner = agentRunner;
            int req = runner != null ? runner.RequestsThisTurn : 0;
            int act = runner != null ? runner.ActionsThisTurn : 0;
            int total = runner != null ? runner.TotalRequests : 0;
            int maxReq = agentConfig != null ? agentConfig.MaxRequestsPerTurn : 0;
            int maxAct = agentConfig != null ? agentConfig.MaxActionsPerTurn : 0;
            int maxTotal = agentConfig != null ? agentConfig.MaxTotalRequests : 0;
            return $"Requests {req}/{maxReq} | session {total}/{maxTotal} | actions {act}/{maxAct}";
        }

        string AgentTokensLabel()
        {
            LlmTurnRunner runner = agentRunner;
            bool known = runner != null && runner.TotalInputTokens.HasValue
                         && runner.TotalOutputTokens.HasValue;
            string tokens = known
                ? runner.TotalInputTokens.Value.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)
                  + " in / "
                  + runner.TotalOutputTokens.Value.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)
                  + " out"
                : "unknown";
            return "tokens: " + tokens;
        }

        string AgentCostLabel()
        {
            LlmTurnRunner runner = agentRunner;
            if (runner != null && runner.CostUsd.HasValue)
                return "cost: $" + runner.CostUsd.Value.ToString("0.######",
                    System.Globalization.CultureInfo.InvariantCulture);
            return "cost: unknown (not reported by provider)";
        }
    }
}
