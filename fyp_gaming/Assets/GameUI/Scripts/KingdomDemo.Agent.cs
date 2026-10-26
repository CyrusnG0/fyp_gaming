// KingdomDemo (partial) — the live LLM seat.
//
// The only place the UI talks to a model. The model sees its own filtered Observation and acts only
// through engine.SubmitAs with the host-captured seat/token, exactly like a human click.
using System;
using System.Collections;
using System.Globalization;
using System.IO;
using UnityEngine;
using LuanShi.Engine;
namespace LuanShi
{
    public partial class KingdomDemo : MonoBehaviour
    {
        [Header("LLM agent (offline rehearsal)")]
        [Tooltip("Swap in the scripted fake provider. Every panel line and run record is labelled FAKE; " +
                 "leave this off for the real, API-backed demo.")]
        public bool useFakeLlm;

        [Header("LLM agent (demo transparency)")]
        [Tooltip("When true the agent panel and command feed show the LLM's full executed command JSON " +
                 "for the demo; set false for a fair human-vs-LLM game where only the public action " +
                 "type is shown.")]
        public bool showAgentDetails = true;

        private const string AgentNoKey = "No API key configured (llm.local.json)";
        private const string AgentCancelFeed = "[LLM] cancelled by user";
        private LlmConfig agentConfig;
        private ILlmTransport agentTransport;
        private LlmRunRecorder agentRecorder;
        private LlmTurnRunner agentRunner;
        private FakeLlmTransport agentFake;
        private string agentSeatId;
        private string agentFactionName = "Shu";
        private string agentUnavailableReason;
        private string agentRecorderShort;
        private bool agentHasSeat;
        private bool agentFakeMode;
        private bool agentTurnActive;
        private bool agentResumePending;
        private bool agentTickFaultLogged;
        private string agentLastActionType;
        private int agentSeatToken;
        private float agentTurnStartedAt;
        private float agentLastElapsed;
        private Coroutine agentTickLoop;
        // ------------------------------------------------------------------ setup
        /// <summary>Validates the LLM seat, loads llm.local.json (never logged raw) and builds the runner.</summary>
        void InitializeAgent()
        {
            // Spectator mode drives BOTH seats with one runner, so it does not depend on llmSeats.
            if (spectatorMode)
            {
                agentHasSeat = true;
                agentSeatId = null;
                agentFactionName = "LLM seats";
                LoadAgentConfig();
                RebuildAgentRunner();
                return;
            }

            agentHasSeat = false;
            agentSeatId = null;
            if (llmSeats == null || llmSeats.Length == 0) return;

            // Validate the whole roster entry list, then drive the first usable seat. A malformed
            // entry must be visible in the log rather than silently becoming a dead chair later.
            string usableSeat = null;
            foreach (string id in llmSeats)
            {
                if (string.IsNullOrEmpty(id))
                {
                    Debug.LogWarning("[KingdomDemo] llmSeats contains an empty entry; ignoring it");
                    continue;
                }
                if (ObserveAgentSeat(id) == null)
                {
                    Debug.LogWarning("[KingdomDemo] llmSeats entry '" + id +
                                     "' is not part of this game; ignoring it");
                    continue;
                }
                if (usableSeat == null) usableSeat = id;
            }
            if (string.IsNullOrEmpty(usableSeat))
            {
                agentUnavailableReason = "LLM seat is not part of this game";
                return;
            }

            agentSeatId = usableSeat;
            Observation seatObs = ObserveAgentSeat(agentSeatId);
            agentHasSeat = true;
            var faction = seatObs != null ? seatObs.Faction(agentSeatId) : null;
            if (faction != null && !string.IsNullOrEmpty(faction.Name)) agentFactionName = faction.Name;
            LoadAgentConfig();
            RebuildAgentRunner();
        }
        void LoadAgentConfig()
        {
            try
            {
                string path = Path.Combine(Application.dataPath, "..", "llm.local.json");
                agentConfig = LlmConfig.LoadFromFile(path);
                if (!string.IsNullOrEmpty(agentConfig.LoadWarning))
                    Debug.LogWarning("[KingdomDemo] " + agentConfig.LoadWarning);
                Debug.Log("[KingdomDemo] agent config: " + agentConfig.Redacted());
            }
            catch (Exception) { agentConfig = new LlmConfig(); }
        }
        void RebuildAgentRunner()
        {
            agentRunner = null;
            agentTransport = null;
            agentFake = null;
            agentRecorderShort = null;
            try
            {
                string runsDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "runs"));
                agentRecorder = new LlmRunRecorder(runsDir);
                if (useFakeLlm)
                {
                    agentFake = new FakeLlmTransport();
                    agentTransport = agentFake;
                    agentFakeMode = true;
                    agentUnavailableReason = agentConfig != null && agentConfig.HasApiKey ? null : AgentNoKey;
                }
                else
                {
                    agentFakeMode = false;
                    if (agentConfig == null || !agentConfig.HasApiKey)
                    {
                        agentUnavailableReason = AgentNoKey;
                        return;
                    }
                    agentTransport = new OpenAiChatTransport(agentConfig);
                    agentUnavailableReason = null;
                }
                agentRunner = new LlmTurnRunner(agentTransport, agentConfig, agentRecorder);
                agentRecorderShort = "runs/" + Path.GetFileName(agentRecorder.FilePath);
            }
            catch (Exception)
            {
                agentRunner = null; agentTransport = null; agentFake = null; agentFakeMode = false;
                agentUnavailableReason = "Agent setup failed";
            }
        }
        void StartAgentTicker()
        {
            bool tick = spectatorMode ? spectatorSession != null : agentHasSeat;
            if (tick && agentTickLoop == null) agentTickLoop = StartCoroutine(AgentTickLoop());
        }
        IEnumerator AgentTickLoop()
        {
            while (true)
            {
                try
                {
                    if (spectatorMode) TickSpectator();
                    else TickAgent();
                }
                catch (Exception e)
                {
                    HandleAgentTickException(e);
                }
                yield return null;   // Input.cs already owns the class's single Update
            }
        }
        /// <summary>
        /// A tick failure must never strand the shared turn gate. Log once (type only, never a request
        /// body or key), cancel the runner, close the engine gate, unlock the UI and walk on.
        /// </summary>
        void HandleAgentTickException(Exception e)
        {
            if (!agentTickFaultLogged)
            {
                agentTickFaultLogged = true;
                Debug.LogError("[KingdomDemo] agent tick failed (" + e.GetType().Name +
                               "); cancelling the current seat and continuing");
            }

            if (spectatorMode)
            {
                try { StopSpectatorSession(); } catch (Exception) { }
                return;
            }

            try { if (agentRunner != null) agentRunner.Cancel("agent tick failed"); }
            catch (Exception) { }
            string endedSeat = agentSeatId;
            int failedToken = agentSeatToken;
            agentSeatToken = 0;
            if (failedToken != 0 && engine != null)
            {
                try { engine.EndSeatTurn(failedToken); } catch (Exception) { }
            }
            agentTurnActive = false;
            agentResumePending = false;
            ReleaseControlLock();
            if (!string.IsNullOrEmpty(endedSeat) && engine != null)
            {
                try { ContinueAfterSeat(endedSeat); } catch (Exception) { }
            }
        }
        // ------------------------------------------------------------------ one LLM seat turn
        /// <summary>Opens the LLM gate and starts one bounded runner turn; returns without blocking.</summary>
        void BeginLlmTurn(string seatId)
        {
            if (!agentHasSeat || engine == null)
            {
                string reason = string.IsNullOrEmpty(agentUnavailableReason)
                    ? "no LLM seat is configured" : agentUnavailableReason;
                PushAgentFeed("[LLM] agent unavailable: " + reason);
                ReleaseControlLock();
                ContinueAfterSeat(seatId);
                return;
            }

            agentSeatId = seatId;
            agentLastActionType = null;
            agentTickFaultLogged = false;
            Observation seatObs = ObserveAgentSeat(seatId);
            if (seatObs == null)
            {
                agentUnavailableReason = "LLM seat is not part of this game";
                PushAgentFeed("[LLM] agent unavailable: " + agentUnavailableReason);
                ReleaseControlLock();
                ContinueAfterSeat(seatId);
                return;
            }
            var faction = seatObs.Faction(seatId);
            if (faction != null && !string.IsNullOrEmpty(faction.Name)) agentFactionName = faction.Name;

            // Single-LLM mode: currentSeat/obs/fog stay on the human display seat for the whole
            // agent turn. The runner gets the model's private world only through AgentTurnHost.Observe.
            // Spectator mode keeps its existing active-seat view.
            if (!spectatorMode) activeTab = "map";
            if (useFakeLlm != agentFakeMode) RebuildAgentRunner();
            int token;
            try { token = engine.BeginSeatTurn(seatId); }
            catch (Exception)
            {
                agentUnavailableReason = "LLM seat is not part of this game";
                PushAgentFeed("[LLM] agent unavailable: " + agentUnavailableReason);
                ReleaseControlLock(); ContinueAfterSeat(seatId); return;
            }
            agentSeatToken = token; agentTurnActive = true; agentResumePending = false;
            agentTurnStartedAt = Time.unscaledTime; agentLastElapsed = 0f;
            if (agentRunner == null || (!useFakeLlm && !string.IsNullOrEmpty(agentUnavailableReason)))
            {
                // No silent scripted fallback: close the gate, tell the player, pass the seat.
                agentUnavailableReason = string.IsNullOrEmpty(agentUnavailableReason)
                    ? "agent unavailable" : agentUnavailableReason;
                engine.EndSeatTurn(token);
                agentTurnActive = false; agentSeatToken = 0;
                agentLastElapsed = Time.unscaledTime - agentTurnStartedAt; agentTurnStartedAt = 0f;
                PushAgentFeed("[LLM] agent unavailable: " + agentUnavailableReason);
                ReleaseControlLock(); RefreshObservation(); ContinueAfterSeat(seatId);
                return;
            }
            uiInputLocked = true;
            uiLockReason = $"{agentFactionName} (LLM) is playing...";
            if (agentFake != null) EnqueueFakeTurn(seatId, seatObs);
            try { agentRunner.Begin(new AgentTurnHost(this, seatId), token, seatId, randomSeed); }
            catch (Exception)
            {
                agentRunner.Cancel("agent turn could not start");
                agentUnavailableReason = "Agent turn could not start"; CompleteAgentSeatTurn();
            }
        }
        void TickAgent()
        {
            if (!ready || !agentHasSeat || !agentTurnActive) return;
            if (engine != null && engine.State.WinnerSeatId != null)
            {
                // The last accepted order decided the game; do not pay for or wait on another
                // request. Close the gate through the normal completion path.
                if (agentRunner != null) agentRunner.Cancel("game over");
                agentResumePending = false;
                CompleteAgentSeatTurn();
                return;
            }
            RunnerState state = agentRunner != null ? agentRunner.State : RunnerState.Idle;
            if (state == RunnerState.Requesting || state == RunnerState.Executing) agentRunner.Tick();
            if (agentResumePending
                || (agentRunner != null && (agentRunner.State == RunnerState.Finished
                    || agentRunner.State == RunnerState.Failed
                    || agentRunner.State == RunnerState.Cancelled)))
                CompleteAgentSeatTurn();
        }
        /// <summary>Closes the LLM gate, refreshes the observation and resumes the rotation walk.</summary>
        void CompleteAgentSeatTurn()
        {
            if (!agentTurnActive) return;
            agentTurnActive = false;
            agentResumePending = false;
            agentLastElapsed = Time.unscaledTime - agentTurnStartedAt;
            agentTurnStartedAt = 0f;
            int token = agentSeatToken;
            agentSeatToken = 0;
            if (token != 0) engine.EndSeatTurn(token);
            ReleaseControlLock();
            RefreshObservation();
            ContinueAfterSeat(agentSeatId);
        }
        void CancelAgentTurn()
        {
            if (!agentTurnActive || agentRunner == null) return;
            if (agentRunner.State != RunnerState.Requesting && agentRunner.State != RunnerState.Executing) return;
            agentRunner.Cancel("user cancelled");
            agentResumePending = false;
            PushAgentFeed(AgentCancelFeed);
            CompleteAgentSeatTurn();
        }
        void AgentEndTurnRequested()
        {
            if (agentTurnActive) agentResumePending = true;
        }
        ValidationResult SubmitAgentCommand(string seatId, ActionCommand cmd)
        {
            ValidationResult result = engine.SubmitAs(seatId, agentSeatToken, cmd);
            agentLastActionType = cmd != null ? cmd.Type : null;
            if (showAgentDetails)
                jsonFeed.Add((result.Ok ? "✓ " : "✗ ") + "[LLM] " + (cmd != null ? cmd.ToJson() : "null"));
            else
                jsonFeed.Add("[LLM] " + (cmd != null ? cmd.Type : "?") + " -> " +
                             (result.Ok ? "ok" : "rejected"));
            if (jsonFeed.Count > 4) jsonFeed.RemoveAt(0);
            return result;
        }
        Observation ObserveAgentSeat(string seatId) => FilteredObservation(seatId);
        void PushAgentFeed(string line)
        {
            if (string.IsNullOrEmpty(line)) return;
            jsonFeed.Add(line);
            if (jsonFeed.Count > 4) jsonFeed.RemoveAt(0);
        }
        /// <summary>Offline rehearsal only: one affordable order then end_turn. Provider=fake.</summary>
        void EnqueueFakeTurn(string seatId, Observation seatObs)
        {
            if (agentFake == null) return;
            agentFake.Reset();
            string cityId = null;
            if (seatObs != null && seatObs.CommandPoints >= BalanceConfig.CommandPointCost(ActionType.LevyTax))
            {
                var cities = seatObs.CitiesOf(seatId); if (cities.Count > 0) cityId = cities[0].Id;
            }
            if (!string.IsNullOrEmpty(cityId))
                agentFake.EnqueueSuccess(
                    "{\"type\":\"levy_tax\",\"target_id\":\"" + JsonText(cityId) + "\"," +
                    "\"summary\":\"FAKE rehearsal: levy tax\"}", 350, 120, 20);
            agentFake.EnqueueSuccess(
                "{\"type\":\"end_turn\",\"summary\":\"FAKE rehearsal: end turn\"}", 250, 80, 10);
        }
        private sealed class AgentTurnHost : ITurnHost
        {
            private readonly KingdomDemo _owner;
            private readonly string _seatId;
            public AgentTurnHost(KingdomDemo owner, string seatId) { _owner = owner; _seatId = seatId; }
            public Observation Observe() => _owner.ObserveAgentSeat(_seatId);
            public ValidationResult Submit(ActionCommand cmd) => _owner.SubmitAgentCommand(_seatId, cmd);
            public void EndTurn() => _owner.AgentEndTurnRequested();
        }
        // ------------------------------------------------------------------ panel
        partial void DrawAgentPanel(Rect area)
        {
            if (!agentHasSeat) { uiRects.Remove(area); return; }   // nothing drawn = no swallowed click
            if (!uiRects.Contains(area)) uiRects.Add(area);
            Color accent = agentSeatId != null && factionColors.TryGetValue(agentSeatId, out var tint)
                ? tint : AccentGold;
            DrawBackdrop(area, accent);
            float x = area.x + 12f;
            float width = area.width - 24f;
            GUI.Label(new Rect(x, area.y + 6f, area.width - 174f, 24f),
                $"Agent: {agentFactionName} (LLM)", uiPanelTitle);
            bool running = IsAgentRunning();
            var previous = GUI.enabled;
            GUI.enabled = running;
            if (GUI.Button(new Rect(area.x + area.width - 162f, area.y + 8f, 150f, 26f),
                "Cancel LLM turn", running ? uiButton : uiButtonDisabledSmall))
                CancelAgentTurn();
            GUI.enabled = previous;
            string provider = agentTransport != null ? agentTransport.Provider + " / " + agentTransport.Model
                : (agentConfig != null ? agentConfig.Provider + " / " + agentConfig.Model : "not configured");
            GUI.Label(new Rect(x, area.y + 32f, width, 18f),
                AgentShort(provider, 58) + "  ·  " + (agentFakeMode ? "FAKE" : "LIVE"), uiSmall);
            string state = $"state: {AgentStateName()}";
            if (running) state += " " + AgentSpinner();
            string elapsed = AgentElapsedLabel();
            if (elapsed.Length > 0) state += "  (" + elapsed + ")";
            GUI.Label(new Rect(x, area.y + 50f, width, 19f), AgentShort(state, 70), uiBody);
            LlmTurnRunner runner = agentRunner;
            int req = runner != null ? runner.RequestsThisTurn : 0;
            int act = runner != null ? runner.ActionsThisTurn : 0;
            int total = runner != null ? runner.TotalRequests : 0;
            int maxReq = agentConfig != null ? agentConfig.MaxRequestsPerTurn : 0;
            int maxAct = agentConfig != null ? agentConfig.MaxActionsPerTurn : 0;
            int maxTotal = agentConfig != null ? agentConfig.MaxTotalRequests : 0;

            GUI.Label(new Rect(x, area.y + 69f, width, 18f),
                $"Requests {req}/{maxReq} - session {total}/{maxTotal} - actions {act}/{maxAct}", uiSmall);
            if (showAgentDetails)
            {
                GUI.Label(new Rect(x, area.y + 87f, width, 18f),
                    "Decision: " + AgentShort(runner != null ? runner.LastSummary : null, 64), uiSmall);
                GUI.Label(new Rect(x, area.y + 105f, width, 16f),
                    "Command: " + AgentShort(runner != null ? runner.LastCommandJson : null, 72), uiMono);
                GUI.Label(new Rect(x, area.y + 121f, width, 18f),
                    "Result: " + AgentShort(AgentResultLabel(), 66), uiSmall);
            }
            else
            {
                GUI.Label(new Rect(x, area.y + 87f, width, 18f),
                    "Action: " + AgentShort(agentLastActionType, 64), uiSmall);
                GUI.Label(new Rect(x, area.y + 121f, width, 18f),
                    "Result: " + AgentShort(AgentPublicResultLabel(), 66), uiSmall);
            }
            GUI.Label(new Rect(x, area.y + 139f, width, 18f), AgentTokenCostLabel(), uiSmall);
            string footer = AgentRecorderLabel();
            if (showAgentDetails) footer = "Observer detail (demo transparency)  ·  " + footer;
            GUI.Label(new Rect(x, area.y + 156f, width, 15f), footer, uiMono);
            string failure = AgentFailureReason();
            if (showAgentDetails && !string.IsNullOrEmpty(failure))
                GUI.Label(new Rect(x, area.y + 172f, width, 16f),
                    "failure: " + AgentShort(failure, 72), uiReason);
        }
        bool IsAgentRunning()
            => agentTurnActive && agentRunner != null
               && (agentRunner.State == RunnerState.Requesting
                   || agentRunner.State == RunnerState.Executing);
        string AgentStateName()
        {
            if (!agentHasSeat) return "unavailable";
            if (!agentFakeMode && !string.IsNullOrEmpty(agentUnavailableReason)) return "unavailable";
            if (agentRunner == null) return "idle";
            switch (agentRunner.State)
            {
                case RunnerState.Requesting: return "requesting";
                case RunnerState.Executing: return "executing";
                case RunnerState.Finished: return "finished";
                case RunnerState.Failed: return "failed";
                case RunnerState.Cancelled: return "cancelled";
                default: return "idle";
            }
        }
        string AgentResultLabel()
        {
            LlmTurnRunner runner = agentRunner;
            if (runner == null || string.IsNullOrEmpty(runner.LastResult)) return "-";
            if (runner.LastResult == "ok" || runner.LastResult == "end_turn") return runner.LastResult;
            if (runner.State == RunnerState.Failed)
                return "failed: " + (string.IsNullOrEmpty(runner.FailureReason)
                    ? runner.LastResult : runner.FailureReason);
            if (runner.LastResult.StartsWith("invalid response", StringComparison.Ordinal))
                return runner.LastResult;
            return "rejected: " + runner.LastResult;
        }
        /// <summary>The fair-play panel only reveals the public outcome, never the validator's detail.</summary>
        string AgentPublicResultLabel()
        {
            LlmTurnRunner runner = agentRunner;
            if (runner == null || string.IsNullOrEmpty(runner.LastResult)) return "-";
            if (runner.LastResult == "ok" || runner.LastResult == "end_turn") return "ok";
            if (runner.State == RunnerState.Failed) return "failed";
            return "rejected";
        }
        string AgentFailureReason()
        {
            if (!agentFakeMode && !string.IsNullOrEmpty(agentUnavailableReason))
                return agentUnavailableReason;
            LlmTurnRunner runner = agentRunner;
            if (runner == null) return null;
            return runner.State == RunnerState.Failed || runner.State == RunnerState.Cancelled
                ? runner.FailureReason : null;
        }
        string AgentTokenCostLabel()
        {
            LlmTurnRunner runner = agentRunner;
            bool known = runner != null && runner.TotalInputTokens.HasValue
                         && runner.TotalOutputTokens.HasValue;
            string tokens = known
                ? runner.TotalInputTokens.Value.ToString("N0", CultureInfo.InvariantCulture) + " in / "
                  + runner.TotalOutputTokens.Value.ToString("N0", CultureInfo.InvariantCulture) + " out"
                : "unknown";
            string cost = runner != null && runner.CostUsd.HasValue
                ? "$" + runner.CostUsd.Value.ToString("0.######", CultureInfo.InvariantCulture)
                : "unknown";
            return "tokens: " + tokens + "  -  cost: " + cost;
        }
        string AgentRecorderLabel()
            => agentRecorderShort == null ? "recorder: -" : "recorder: " + AgentShort(agentRecorderShort, 68);
        string AgentElapsedLabel()
        {
            float seconds = agentTurnStartedAt > 0f ? Time.unscaledTime - agentTurnStartedAt : agentLastElapsed;
            return seconds <= 0f ? "" : seconds.ToString("0.0", CultureInfo.InvariantCulture) + "s";
        }
        static string AgentSpinner()
        {
            const string frames = "|/-\\";
            return frames[(int)(Time.unscaledTime * 4f) % frames.Length].ToString();
        }
        static string AgentShort(string value, int max)
        {
            if (string.IsNullOrEmpty(value)) return "-";
            string one = value.Replace('\r', ' ').Replace('\n', ' ');
            if (one.Length <= max) return one;
            return max <= 3 ? one.Substring(0, max) : one.Substring(0, max - 3) + "...";
        }
        static string JsonText(string value) => (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
        // ------------------------------------------------------------------ debug hooks
        /// <summary>One-line rehearsal status: state|requests|actions|lastResult|failure.</summary>
        public string DebugAgentStatus()
        {
            if (!agentHasSeat) return "unavailable|0|0|-|no LLM seat";
            int req = agentRunner != null ? agentRunner.RequestsThisTurn : 0;
            int act = agentRunner != null ? agentRunner.ActionsThisTurn : 0;
            string last = agentRunner != null ? agentRunner.LastResult : null;
            return AgentField(AgentStateName()) + "|" + req + "|" + act + "|"
                 + AgentField(last) + "|" + AgentField(AgentFailureReason());
        }
        public void DebugCancelAgent() => CancelAgentTurn();
        static string AgentField(string value)
        {
            if (string.IsNullOrEmpty(value)) return "-";
            return value.Replace('|', '/').Replace('\r', ' ').Replace('\n', ' ').Trim();
        }
        // ------------------------------------------------------------------ play-mode exit
        /// <summary>
        /// Re-enabling the component (or a scripted enable/disable cycle) must resume the coroutine;
        /// Start() normally owns the first start, so this is a no-op until the game is ready.
        /// </summary>
        void OnEnable()
        {
            if (ready) StartAgentTicker();
        }

        void OnDisable()
        {
            if (spectatorMode) StopSpectatorSession();
            if (agentTickLoop != null) { StopCoroutine(agentTickLoop); agentTickLoop = null; }
            if (agentRunner != null && IsAgentRunning()) agentRunner.Cancel("play mode ended");
            int token = agentSeatToken; agentSeatToken = 0;
            if (token != 0 && engine != null) engine.EndSeatTurn(token);
            agentTurnActive = false; agentResumePending = false; ReleaseControlLock();
        }
    }
}
