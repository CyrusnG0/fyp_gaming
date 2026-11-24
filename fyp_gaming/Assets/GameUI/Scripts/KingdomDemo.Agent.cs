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

        [Tooltip("Test-only, opt-in rehearsal flag for the live LLM seat. Forces fake provider and " +
                 "runs one scripted recruit -> convert_garrison -> march -> end_turn sequence through " +
                 "AgentTurnHost without ever calling a real provider or changing default matches.")]
        public bool fakeGarrisonRehearsal;

        [Header("LLM agent (demo transparency)")]
        [Tooltip("When true the agent panel and command feed show the LLM's full executed command JSON " +
                 "for the demo; set false for a fair human-vs-LLM game where only the public action " +
                 "type is shown.")]
        public bool showAgentDetails = true;

        private const string AgentNoKey = "No API key configured (llm.local.json)";
        private const string AgentCancelFeed = "[LLM] cancelled by user";
        private LlmConfig agentLoadedConfig;
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
        private bool agentGarrisonRehearsalActive;
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
            if (replayMode) return;
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
            if (llmSeats == null || llmSeats.Length == 0)
            {
                // A match starting with no preconfigured LLM seat may still delegate a human faction
                // later; prepare config and fake/keyed runner upfront without marking an LLM chair yet.
                LoadAgentConfig();
                if (FakeTransportRequested || (agentLoadedConfig != null && agentLoadedConfig.HasApiKey))
                    RebuildAgentRunner();
                return;
            }

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
        bool FakeTransportRequested => useFakeLlm || fakeGarrisonRehearsal;

        void LoadAgentConfig()
        {
            try
            {
                string path = Path.Combine(Application.dataPath, "..", "llm.local.json");
                agentLoadedConfig = LlmConfig.LoadFromFile(path);
                if (!string.IsNullOrEmpty(agentLoadedConfig.LoadWarning))
                    Debug.LogWarning("[KingdomDemo] " + agentLoadedConfig.LoadWarning);
                Debug.Log("[KingdomDemo] agent config: " + agentLoadedConfig.Redacted());
            }
            catch (Exception) { agentLoadedConfig = new LlmConfig(); }
            agentConfig = agentLoadedConfig;
        }
        void RebuildAgentRunner()
        {
            agentRunner = null;
            agentTransport = null;
            agentFake = null;
            agentRecorderShort = null;
            try
            {
                LlmConfig baseConfig = agentLoadedConfig ?? agentConfig ?? new LlmConfig();
                agentConfig = fakeGarrisonRehearsal
                    ? FakeTurnPlan.GarrisonRehearsalConfig(baseConfig)
                    : baseConfig;
                agentGarrisonRehearsalActive = fakeGarrisonRehearsal;
                string runsDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "runs"));
                agentRecorder = new LlmRunRecorder(runsDir);
                var agentMemoryStore = new LocalLlmMemoryStore(Path.Combine(runsDir, "memory"));
                if (FakeTransportRequested)
                {
                    agentFake = new FakeLlmTransport();
                    agentTransport = agentFake;
                    agentFakeMode = true;
                    agentUnavailableReason = baseConfig.HasApiKey ? null : AgentNoKey;
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
                agentRunner = new LlmTurnRunner(agentTransport, agentConfig, agentRecorder, agentMemoryStore);
                if (engine != null) CaptureReplayBootstrap();
                agentRecorderShort = "runs/" + Path.GetFileName(agentRecorder.FilePath);
            }
            catch (Exception)
            {
                agentRunner = null; agentTransport = null; agentFake = null;
                agentFakeMode = false; agentGarrisonRehearsalActive = false;
                agentUnavailableReason = "Agent setup failed";
            }
        }
        void StartAgentTicker()
        {
            bool tick = replayMode ? _replaySession != null : spectatorMode
                ? spectatorSession != null
                : agentHasSeat || agentResumeAfterInterrupt || handoverState.HasEligibleSeats;
            if (tick && agentTickLoop == null) agentTickLoop = StartCoroutine(AgentTickLoop());
        }
        IEnumerator AgentTickLoop()
        {
            while (true)
            {
                try
                {
                    if (!pauseMenuOpen)
                    {
                        if (replayMode) TickReplay();
                        else if (spectatorMode) TickSpectator();
                        else TickAgent();
                    }
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
            try
            {
                RecordCompletedAgentTurnForHandover(endedSeat);
                SyncHandoverBoundaryAfterSeatEnd(endedSeat, FilteredObservation(endedSeat));
            }
            catch (Exception) { }
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
            if (engine == null || (!agentHasSeat && !EnsureAgentRunnerReady(out _)))
            {
                string reason = string.IsNullOrEmpty(agentUnavailableReason)
                    ? "no LLM runner is available" : agentUnavailableReason;
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
            agentHasSeat = true;
            var faction = seatObs.Faction(seatId);
            if (faction != null && !string.IsNullOrEmpty(faction.Name)) agentFactionName = faction.Name;

            // Single-LLM mode: currentSeat/obs/fog stay on the human display seat for the whole
            // agent turn. The runner gets the model's private world only through AgentTurnHost.Observe.
            // Spectator mode keeps its existing active-seat view.
            if (!spectatorMode) activeTab = "map";
            if (FakeTransportRequested != agentFakeMode
                || fakeGarrisonRehearsal != agentGarrisonRehearsalActive)
                RebuildAgentRunner();
            if (!string.IsNullOrEmpty(engine.ActiveSeatId))
            {
                PushAgentFeed("[LLM] turn gate busy (" + engine.ActiveSeatId + "); skipping duplicate begin");
                ReleaseControlLock();
                return;
            }
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
            if (agentRunner == null
                || (!FakeTransportRequested && !string.IsNullOrEmpty(agentUnavailableReason))
                || (fakeGarrisonRehearsal && (agentFake == null || !agentFakeMode
                    || agentConfig == null || !agentConfig.EnableGarrisonConversion)))
            {
                // No silent scripted fallback: close the gate, tell the player, pass the seat.
                agentUnavailableReason = string.IsNullOrEmpty(agentUnavailableReason)
                    ? "agent unavailable" : agentUnavailableReason;
                engine.EndSeatTurn(token);
                agentTurnActive = false; agentSeatToken = 0;
                agentLastElapsed = Time.unscaledTime - agentTurnStartedAt; agentTurnStartedAt = 0f;
                PushAgentFeed("[LLM] agent unavailable: " + agentUnavailableReason);
                SyncHandoverBoundaryAfterSeatEnd(seatId, FilteredObservation(seatId));
                ReleaseControlLock(); RefreshObservation(); ContinueAfterSeat(seatId);
                return;
            }
            uiInputLocked = true;
            uiLockReason = $"{agentFactionName} (LLM) is playing...";
            var host = new AgentTurnHost(this, seatId, token);
            if (agentFake != null) EnqueueFakeTurn(seatId, seatObs, host);
            try { agentRunner.Begin(host, token, seatId, randomSeed); }
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
        /// <summary>Closes the LLM gate, captures seat-filtered turn telemetry, syncs authoritative
        /// between-turn reclaim/handover, refreshes the observation and resumes the rotation walk.</summary>
        void CompleteAgentSeatTurn()
        {
            if (!agentTurnActive) return;
            string endedSeat = agentSeatId;
            agentTurnActive = false;
            agentResumePending = false;
            agentLastElapsed = Time.unscaledTime - agentTurnStartedAt;
            agentTurnStartedAt = 0f;
            int token = agentSeatToken;
            agentSeatToken = 0;
            if (token != 0) engine.EndSeatTurn(token);
            RecordCompletedAgentTurnForHandover(endedSeat);
            SyncHandoverBoundaryAfterSeatEnd(endedSeat, FilteredObservation(endedSeat));
            ReleaseControlLock();
            RefreshObservation();
            ContinueAfterSeat(endedSeat);
        }
        void CancelAgentTurn()
        {
            if (!CanCancelAgentTurn()) return;
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
        ValidationResult SubmitAgentCommand(string seatId, int token, ActionCommand cmd)
        {
            ValidationResult result = engine.SubmitAs(seatId, token, ControllerType.Llm, cmd);
            if (result.Ok) agentLastActionType = cmd != null ? cmd.Type : null;
            // A model march must move the same banner and refresh the same fog/tint view a human
            // march does, and a model battle must leave the same map a human battle does: destroyed
            // armies lose their banner, routed survivors and the occupying winner are re-placed, and
            // the fog/tint view is re-read from the display seat's own observation. Presentation
            // only: the engine already executed the order above.
            if (result.Ok && cmd != null && IsBattle(cmd.Type))
            {
                SafeClearArmyMoveTargets();
                SyncBattleVisuals();
                RefreshObservation();
            }
            else if (result.Ok && cmd != null && cmd.Type == ActionType.ConvertGarrison)
            {
                var city = engine.State.FindCity(cmd.TargetId);
                var convertedArmy = city != null ? engine.State.ArmyAt(city.X, city.Z) : null;
                if (convertedArmy != null) EnsureArmyVisual(convertedArmy);
                RefreshObservation();
            }
            else if (result.Ok && cmd != null && cmd.Type == ActionType.March)
            {
                SafeClearArmyMoveTargets();
                SyncArmyVisual(cmd.TargetId);
                selArmy = null;
                RefreshObservation();
            }
            string feedLine = HandoverUiPrivacy.FormatLiveAgentFeedLine(
                CanExposeAgentDetailsForSeat(seatId), cmd, result);
            if (!string.IsNullOrEmpty(feedLine)) PushAgentFeed(feedLine);
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
        void EnqueueFakeTurn(string seatId, Observation seatObs, AgentTurnHost host)
        {
            if (agentFake == null) return;
            if (fakeGarrisonRehearsal)
            {
                FakeTurnPlan.EnqueueGarrisonRehearsal(agentFake, host, seatId,
                    agentConfig != null ? agentConfig.SpatialContext : null, delayMs: 200);
                return;
            }
            agentFake.Reset();
            bool spatialOn = agentConfig != null && agentConfig.SpatialContext != null
                && agentConfig.SpatialContext.IsEnabled;
            MovementView movement = (agentConfig != null && agentConfig.RequireVisibleMarchRoutes) || spatialOn
                ? MovementPlanner.BuildDiversified(engine, seatId,
                    MovementPlanner.DefaultMaxOptionsPerArmy, null, agentConfig.SpatialContext)
                : MovementPlanner.Build(engine, seatId);
            var plan = FakeTurnPlan.Build(movement, seatObs, seatId,
                agentConfig != null && agentConfig.EnableManualLookup);
            foreach (string command in plan)
                agentFake.EnqueueSuccess(command, 300, 120, 20);
        }
        private sealed class AgentTurnHost : IAttackPlanningHost, IPersonaHost, ISpatialPlanningHost
        {
            private readonly SingleLlmTurnHost _inner;
            public AgentTurnHost(KingdomDemo owner, string seatId, int token)
            {
                _inner = new SingleLlmTurnHost(
                    owner.engine,
                    owner.agentConfig,
                    seatId,
                    () => owner.ObserveAgentSeat(seatId),
                    cmd => owner.SubmitAgentCommand(seatId, token, cmd),
                    owner.AgentEndTurnRequested);
            }
            public string FactionKey => _inner.FactionKey;
            public Observation Observe() => _inner.Observe();
            public ValidationResult Submit(ActionCommand cmd) => _inner.Submit(cmd);
            public void EndTurn() => _inner.EndTurn();
            /// <summary>Read-only, seat-filtered candidates for this agent seat.</summary>
            public MovementView PlanMovement() => _inner.PlanMovement();
            /// <summary>Read-only, seat-filtered candidates honoring explicit per-run spatial options.</summary>
            public MovementView PlanMovement(SpatialContextOptions spatialOptions)
                => _inner.PlanMovement(spatialOptions);
            /// <summary>
            /// Read-only, seat-filtered attack/assault candidates. Only consulted when the config
            /// opts in (<see cref="LlmConfig.EnableAttack"/>), so the default prompt is unchanged.
            /// </summary>
            public AttackView PlanAttacks() => _inner.PlanAttacks();
            /// <summary>Authoritative terminal signal from the engine, not a cached observation.</summary>
            public bool IsGameOver => _inner.IsGameOver;
        }
    }
}
