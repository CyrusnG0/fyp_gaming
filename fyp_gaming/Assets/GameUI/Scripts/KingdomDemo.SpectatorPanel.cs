// KingdomDemo (partial) — the researcher panel for the optional LLM-vs-LLM spectator pilot.
//
// It replaces the single-agent panel while spectatorMode is on. Everything here is display only:
// the scoreboard reads canonical state for the human researcher and is NEVER handed to the runner,
// a prompt or a model. The map above/below shows only the active seat's filtered observation.

using System;
using System.Globalization;
using UnityEngine;
using LuanShi.Engine;

namespace LuanShi
{
    public partial class KingdomDemo : MonoBehaviour
    {
        static Rect SpectatorPanelArea() => new Rect(460f, 70f, 700f, 430f);

        void DrawSpectatorPanel()
        {
            Rect area = SpectatorPanelArea();
            uiRects.Add(area);
            DrawBackdrop(area, CurrentSeatAccent());

            SpectatorSession session = spectatorSession;
            float x = area.x + 12f;
            float width = area.width - 24f;

            GUI.Label(new Rect(x, area.y + 4f, width, 24f),
                "LLM vs LLM pilot - domestic management", uiPanelTitle);

            string provider = agentTransport != null
                ? agentTransport.Provider + "/" + agentTransport.Model
                : agentConfig != null ? agentConfig.Provider + "/" + agentConfig.Model
                : "not configured";
            GUI.Label(new Rect(x, area.y + 27f, width, 16f),
                (agentFakeMode ? "FAKE  " : "LIVE  ") + AgentShort(provider, 64), uiSmall);

            if (session == null)
            {
                GUI.Label(new Rect(x, area.y + 44f, width, 18f),
                    "Unavailable: " + (spectatorSetupError ?? "spectator has not started"), uiReason);
                GUI.Label(new Rect(x, area.y + 66f, width, 18f),
                    "Viewing " + ViewedFactionName() + "'s filtered observation", uiSmall);
                DrawSpectatorControls(area, null);
                return;
            }

            string state = SpectatorStateName(session.State);
            if (session.State == SpectatorState.Running) state += " " + AgentSpinner();
            else if ((session.State == SpectatorState.Paused || session.State == SpectatorState.Failed)
                     && !string.IsNullOrEmpty(session.PauseReason))
                state += ": " + AgentShort(session.PauseReason, 72);
            state += "  (" + SpectatorElapsed(session) + ")";
            GUI.Label(new Rect(x, area.y + 44f, width, 18f), state, uiBody);

            string active = string.IsNullOrEmpty(session.ActiveFactionName)
                ? "-" : session.ActiveFactionName;
            string activeSeat = session.ActiveSeatId;
            string activeLabel = string.IsNullOrEmpty(activeSeat)
                ? "" : " (" + SpectatorControllerLabel(activeSeat) + ")";
            GUI.Label(new Rect(x, area.y + 62f, width, 18f),
                "Season " + session.CurrentSeason + " of " + session.SeasonsPlanned
                + "  -  Active: " + active + activeLabel, uiBody);
            GUI.Label(new Rect(x, area.y + 80f, width, 16f),
                "Viewing " + ViewedFactionName() + "'s filtered observation", uiSmall);

            DrawSeasonPips(area, session);

            int factions = Mathf.Min(2, engine.State.Factions.Count);
            float columnWidth = (width - 12f) * 0.5f;
            for (int i = 0; i < factions; i++)
            {
                FactionState faction = engine.State.Factions[i];
                float columnX = x + i * (columnWidth + 12f);
                bool isActive = string.Equals(session.ActiveSeatId, faction.SeatId, StringComparison.Ordinal);
                LlmTurnRunner runner = isActive ? session.Runner : null;
                SpectatorTurnRecord record = LastRecordFor(session, faction.SeatId);

                GUI.Label(new Rect(columnX, area.y + 104f, columnWidth, 20f),
                    faction.Name + " (" + SpectatorControllerLabel(faction.SeatId) + ")", uiPanelTitle);

                string commandJson = runner != null && !string.IsNullOrEmpty(runner.LastCommandJson)
                    ? runner.LastCommandJson : record?.LastCommandJson;
                string result = runner != null && !string.IsNullOrEmpty(runner.LastResult)
                    ? runner.LastResult : record?.LastResult;
                string summary = runner != null && !string.IsNullOrEmpty(runner.LastSummary)
                    ? runner.LastSummary : record?.LastSummary;

                string type = SpectatorCommandField(commandJson, "type");
                string target = SpectatorCommandField(commandJson, "target_id");
                string action = string.IsNullOrEmpty(type) ? "-" : type
                    + (string.IsNullOrEmpty(target) ? "" : " " + target);
                GUI.Label(new Rect(columnX, area.y + 128f, columnWidth, 15f),
                    "Last: " + AgentShort(action, 46), uiSmall);
                GUI.Label(new Rect(columnX, area.y + 144f, columnWidth, 15f),
                    "Result: " + SpectatorResultLabel(result), uiSmall);
                GUI.Label(new Rect(columnX, area.y + 160f, columnWidth, 15f),
                    "Decision: " + AgentShort(summary, 70), uiSmall);
            }

            GUI.Label(new Rect(x, area.y + 182f, width, 15f), "Recent actions", uiSmall);
            var recent = session.RecentActions;
            int firstRecent = Mathf.Max(0, recent.Count - 8);
            for (int i = firstRecent; i < recent.Count; i++)
                GUI.Label(new Rect(x, area.y + 198f + (i - firstRecent) * 13f, width, 13f),
                    (agentFakeMode ? "FAKE  " : "") + AgentShort(recent[i], 104), uiMono);

            GUI.Label(new Rect(x, area.y + 304f, width, 15f),
                "Spectator scoreboard (researcher view, not shown to the models)", uiSmall);
            var scores = session.Scoreboard();
            for (int i = 0; i < scores.Count && i < 2; i++)
            {
                SpectatorScore score = scores[i];
                GUI.Label(new Rect(x, area.y + 320f + i * 15f, width, 15f),
                    score.FactionName + ": Gold " + score.Gold + " | Food " + score.Food
                    + " | Population " + score.Population + " | Garrison " + score.Garrison, uiSmall);
            }

            int cap = agentConfig != null ? agentConfig.MaxTotalRequests : 0;
            bool tokensKnown = session.TotalInputTokens.HasValue && session.TotalOutputTokens.HasValue;
            string tokens = tokensKnown
                ? session.TotalInputTokens.Value.ToString("N0", CultureInfo.InvariantCulture) + " in / "
                  + session.TotalOutputTokens.Value.ToString("N0", CultureInfo.InvariantCulture) + " out"
                : "unknown";
            string cost = session.CostUsd.HasValue
                ? "$" + session.CostUsd.Value.ToString("0.######", CultureInfo.InvariantCulture)
                : "unknown";
            GUI.Label(new Rect(x, area.y + 354f, width, 16f),
                "Requests " + session.TotalRequests + "/" + cap + "  -  tokens " + tokens
                + "  -  cost " + cost, uiSmall);

            DrawSpectatorControls(area, session);
        }

        void DrawSpectatorControls(Rect area, SpectatorSession session)
        {
            float x = area.x + 12f;
            float y = area.y + 374f;
            int cap = agentConfig != null ? agentConfig.MaxTotalRequests : 0;
            int requests = session != null ? session.TotalRequests : 0;
            bool running = session != null && session.State == SpectatorState.Running;
            bool paused = session != null && session.State == SpectatorState.Paused;
            bool canExtend = session != null && session.State != SpectatorState.Failed
                && cap - requests >= 12 && engine.State.WinnerSeatId == null;

            SpectatorButton(new Rect(x, y, 150f, 28f), "Stop", running, StopSpectator);
            SpectatorButton(new Rect(x + 162f, y, 150f, 28f), "Resume", paused, ResumeSpectator);
            SpectatorButton(new Rect(x + 324f, y, 150f, 28f), "+3 seasons", canExtend,
                () => ExtendSpectator(3));

            GUI.Label(new Rect(x, y + 30f, area.width - 24f, 14f),
                "summary: " + (string.IsNullOrEmpty(spectatorSummaryPath) ? "-" : spectatorSummaryPath), uiMono);
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
                if (GUI.Button(rect, "Resume pilot", uiButton)) ResumeSpectator();
            }
            else
            {
                var previous = GUI.enabled;
                GUI.enabled = false;
                GUI.Button(rect, session == null ? "Spectator" : "Pilot " + session.State, uiButtonDisabled);
                GUI.enabled = previous;
            }
        }

        void DrawSeasonPips(Rect area, SpectatorSession session)
        {
            int planned = Mathf.Min(session.SeasonsPlanned, 12);
            for (int i = 0; i < planned; i++)
            {
                var pip = new Rect(area.x + 12f + i * 22f, area.y + 99f, 18f, 6f);
                bool played = i < session.SeasonsPlayed;
                bool current = !played && i == session.SeasonsPlayed
                    && session.State != SpectatorState.Completed;
                FillRect(pip, played ? AccentGold : current ? TabSelected : ButtonDisabled);
            }
            if (session.SeasonsPlanned > 12)
                GUI.Label(new Rect(area.x + 12f + 12 * 22f, area.y + 94f, 80f, 14f),
                    "+" + (session.SeasonsPlanned - 12), uiSmall);
        }

        void SpectatorButton(Rect rect, string label, bool enabled, Action action)
        {
            var previous = GUI.enabled;
            GUI.enabled = enabled;
            if (GUI.Button(rect, label, enabled ? uiButton : uiButtonDisabledSmall)) action?.Invoke();
            GUI.enabled = previous;
        }

        string ViewedFactionName()
        {
            if (engine == null || string.IsNullOrEmpty(currentSeat)) return "-";
            FactionState faction = engine.State.FindFaction(currentSeat);
            return faction != null && !string.IsNullOrEmpty(faction.Name) ? faction.Name : currentSeat;
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
            return seconds.ToString("0.0", CultureInfo.InvariantCulture) + "s";
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
    }
}
