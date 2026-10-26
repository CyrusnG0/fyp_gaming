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
        private bool agentResumeAfterInterrupt;   // disabled mid-turn; first tick after enable resumes once
        private string agentInterruptedSeat;
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
            bool tick = spectatorMode
                ? spectatorSession != null
                : agentHasSeat || agentResumeAfterInterrupt;
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
            if (agentResumeAfterInterrupt)
            {
                // OnDisable closed the interrupted seat's gate; finish that one rotation walk now,
                // from the ticker rather than from the Unity lifecycle callback. Clear first so a
                // throwing continuation cannot be retried forever.
                agentResumeAfterInterrupt = false;
                string interruptedSeat = agentInterruptedSeat;
                agentInterruptedSeat = null;
                if (!string.IsNullOrEmpty(interruptedSeat) && engine != null)
                    ContinueAfterSeat(interruptedSeat);
                return;
            }

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
            // A model march must move the same banner and refresh the same fog/tint view a human
            // march does. Presentation only: the engine already executed the order above.
            if (result.Ok && cmd != null && cmd.Type == ActionType.March)
            {
                SafeClearArmyMoveTargets();
                SyncArmyVisual(cmd.TargetId);
                selArmy = null;
                RefreshObservation();
            }
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
        /// <summary>
        /// Offline rehearsal only (FAKE, provider=fake): a capture march when the seat's movement view
        /// advertises one, else its first option; then one affordable order and end_turn. The plan is
        /// built by the pure-C# FakeTurnPlan helper so the harness can assert its choices.
        /// </summary>
        void EnqueueFakeTurn(string seatId, Observation seatObs)
        {
            if (agentFake == null) return;
            agentFake.Reset();
            var plan = FakeTurnPlan.Build(MovementPlanner.Build(engine, seatId), seatObs, seatId);
            foreach (string command in plan)
                agentFake.EnqueueSuccess(command, 300, 120, 20);
        }
        private sealed class AgentTurnHost : IPlanningHost
        {
            private readonly KingdomDemo _owner;
            private readonly string _seatId;
            public AgentTurnHost(KingdomDemo owner, string seatId) { _owner = owner; _seatId = seatId; }
            public Observation Observe() => _owner.ObserveAgentSeat(_seatId);
            public ValidationResult Submit(ActionCommand cmd) => _owner.SubmitAgentCommand(_seatId, cmd);
            public void EndTurn() => _owner.AgentEndTurnRequested();
            /// <summary>Read-only, seat-filtered candidates for this agent seat.</summary>
            public MovementView PlanMovement() => MovementPlanner.Build(_owner.engine, _seatId);
            /// <summary>Authoritative terminal signal from the engine, not a cached observation.</summary>
            public bool IsGameOver => _owner.engine != null && _owner.engine.State.WinnerSeatId != null;
        }
    }
}
