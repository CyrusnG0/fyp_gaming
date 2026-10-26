// KingdomDemo (partial) — command and turn flow: SubmitHuman, seat rotation, season rollover.

using System.Collections.Generic;
using UnityEngine;
using LuanShi.Engine;

namespace LuanShi
{
    public partial class KingdomDemo : MonoBehaviour
    {
        // ------------------------------------------------------------------ orders

        bool SubmitHuman(ActionCommand cmd)
        {
            cmd.ActorSeatId = currentSeat;
            cmd.ControllerType = ControllerType.Human;
            var r = engine.Submit(cmd);
            PushJson(cmd, r);
            if (r.Ok && cmd.Type == ActionType.March)
            {
                SafeClearArmyMoveTargets();
                SyncArmyVisual(cmd.TargetId);
                selArmy = null;
            }
            if (r.Ok && cmd.Type == ActionType.ConvertGarrison)
            {
                var convertedArmy = engine.State.ArmyAt(engine.State.FindCity(cmd.TargetId).X,
                                                        engine.State.FindCity(cmd.TargetId).Z);
                if (convertedArmy != null) EnsureArmyVisual(convertedArmy);
                garrisonConversionOpen = false;
                garrisonConversionCityId = null;
            }
            if (r.Ok && cmd.Type == ActionType.GarrisonArmy)
            {
                garrisonArmyId = null;
                garrisonArmyConversionOpen = false;
                var remainingArmy = engine.State.FindArmy(cmd.TargetId);
                if (remainingArmy == null)
                {
                    if (armyObjs.TryGetValue(cmd.TargetId, out var armyObject) && armyObject != null)
                        armyObject.SetActive(false);
                    selArmy = null;
                    SafeClearArmyMoveTargets();
                }
            }
            if (r.Ok && IsBattle(cmd.Type))
            {
                SafeClearArmyMoveTargets();
                SyncBattleVisuals();
            }
            RefreshObservation();            // the world just moved: the fog radius moves with it

            if (!r.Ok)
                statusMsg = r.Error == "no command points left"
                    ? "Not enough command points: no command points remain this season"
                    : "Order rejected: " + r.Error;
            else if (IsBattle(cmd.Type))
                statusMsg = LastBattleSummary() ?? "Order executed";
            else
                statusMsg = "Order executed";

            return r.Ok;
        }

        static bool IsBattle(string type)
            => type == ActionType.AttackArmy || type == ActionType.AttackCity;

        // ------------------------------------------------------------- hotseat seat flow

        private int activeTurnToken;   // engine turn token held by the human seat currently in the chair

        /// <summary>
        /// Ends the human seat's turn: closes its engine gate, plays any scripted seats, starts an LLM
        /// seat's bounded runner, or resolves the season. An LLM step returns immediately; the runner's
        /// completion callback resumes this same rotation from <see cref="ContinueAfterSeat"/>.
        /// </summary>
        void EndTurn()
        {
            if (spectatorMode) return;   // SpectatorSession owns every turn
            if (!ready || engine == null || string.IsNullOrEmpty(currentSeat)) return;
            if (reportShowing || handoverShowing) return;
            if (agentTurnActive || agentResumePending) return;   // repeat clicks while Shu is playing
            if (!SeatRotation.IsHuman(engine.State, currentSeat)) return;
            if (engine.State.WinnerSeatId != null) return;

            selCity = null; selArmy = null;
            SafeClearArmyMoveTargets();

            int token = activeTurnToken;
            activeTurnToken = 0;
            if (token != 0) engine.EndSeatTurn(token);
            ContinueAfterSeat(currentSeat);
        }

        /// <summary>
        /// Opens the first chair of the campaign (after InitializeAgent/InitializeSpectator so an LLM
        /// step has a runner). Never opens a human gate for a model/scripted seat.
        /// </summary>
        void StartFirstPlannedTurn()
        {
            if (spectatorMode || engine == null) return;
            StartPlannedTurn(TurnFlowPlanner.First(engine.State));
        }

        /// <summary>
        /// Dispatches one planner decision. Only a HumanSeat may call StartSeatTurn; LLM and scripted
        /// chairs are handed to their own controllers, so a roster with no human cannot leave an
        /// unowned engine gate open.
        /// </summary>
        void StartPlannedTurn(TurnFlowStep step)
        {
            switch (step.Action)
            {
                case TurnFlowAction.GameOver:
                    ReleaseControlLock();
                    return;

                case TurnFlowAction.ResolveSeason:
                    ResolveSeason();
                    return;

                case TurnFlowAction.HumanSeat:
                    currentSeat = step.SeatId;
                    StartSeatTurn();
                    return;

                case TurnFlowAction.LlmSeat:
                    BeginLlmTurn(step.SeatId);
                    return;

                default:   // ScriptedSeat
                    RunScriptedSeat(step.SeatId);
                    if (engine.State.WinnerSeatId != null)
                    {
                        ReleaseControlLock();
                        return;
                    }
                    ContinueAfterSeat(step.SeatId);
                    return;
            }
        }

        /// <summary>
        /// The single rotation walk shared by EndTurn and the LLM completion path. Scripted seats run
        /// synchronously (through the same gate), the next human/LLM chair starts, or the season resolves.
        /// </summary>
        void ContinueAfterSeat(string endedSeat)
        {
            if (engine == null) return;

            while (true)
            {
                TurnFlowStep step = TurnFlowPlanner.NextAfter(engine.State, endedSeat);
                switch (step.Action)
                {
                    case TurnFlowAction.GameOver:
                        ReleaseControlLock();
                        return;

                    case TurnFlowAction.ResolveSeason:
                        ResolveSeason();
                        return;

                    case TurnFlowAction.HumanSeat:
                        currentSeat = step.SeatId;
                        StartSeatTurn();
                        return;

                    case TurnFlowAction.LlmSeat:
                        // In single-LLM mode currentSeat/obs stay on the human display seat; the
                        // agent reaches its own seat only through AgentTurnHost.Observe.
                        BeginLlmTurn(step.SeatId);
                        return;

                    default:   // ScriptedSeat
                        RunScriptedSeat(step.SeatId);
                        if (engine.State.WinnerSeatId != null)
                        {
                            ReleaseControlLock();
                            return;
                        }
                        endedSeat = step.SeatId;
                        break;
                }
            }
        }

        void RunScriptedSeat(string seatId)
        {
            if (engine.State.WinnerSeatId != null) return;

            int token = engine.BeginSeatTurn(seatId);
            try
            {
                foreach (var cmd in ScriptedController.PlanTurn(engine.State, seatId))
                {
                    var r = engine.Submit(cmd);
                    PushJson(cmd, r);
                    if (r.Ok && cmd.Type == ActionType.March) SyncArmyVisual(cmd.TargetId);
                    if (r.Ok && IsBattle(cmd.Type)) SyncBattleVisuals();
                    if (engine.State.WinnerSeatId != null) break;
                }
            }
            finally
            {
                engine.EndSeatTurn(token);   // the gate must not stay open after a scripted seat
            }
        }

        /// <summary>
        /// Hands the chair to a human seat and opens its engine turn. With more than one human seat a
        /// full-screen blocker covers the map and every panel — the previous player's screen is not the
        /// next player's briefing (hotseat rule). With a single human seat: no blocker, play on.
        /// </summary>
        void StartSeatTurn()
        {
            if (engine == null) return;
            if (!SeatRotation.IsHuman(engine.State, currentSeat))
            {
                // Defensive: a model/scripted chair must never hold the human gate. Re-enter the
                // planner instead of stalling on a seat nobody owns.
                StartPlannedTurn(TurnFlowPlanner.First(engine.State));
                return;
            }

            selCity = null; selArmy = null;
            locationChoiceOpen = false;           // the previous seat's popup is not the next one's
            envoyText = "";                       // nor is a half-written envoy message
            envoyConfirmationUntil = 0f;
            activeTab = "map";                    // a tab left open by the previous player is not a briefing
            garrisonConversionOpen = false;       // nor is the previous player's confirm/slider state
            garrisonConversionCityId = null;
            garrisonConversionAmount = 0;
            garrisonArmyId = null;
            garrisonArmyAmount = 0;
            garrisonArmyConversionOpen = false;
            SafeClearArmyMoveTargets();
            jsonFeed.Clear();                     // one player's command feed is not the next one's

            activeTurnToken = engine.BeginSeatTurn(currentSeat);
            ReleaseControlLock();
            RefreshObservation();

            handoverShowing = SeatRotation.HumanSeats(engine.State).Count > 1;
            if (handoverShowing) return;

            if (pendingReport) { pendingReport = false; reportShowing = true; }
            else statusMsg = SeatTurnPrompt();
        }

        private bool warnedGridIndicatorClear;

        /// <summary>
        /// GridIndicator.select is a scene reference and may still be unwired when early turn-flow
        /// calls run. Clearing move highlights is cosmetic, so skip it rather than aborting the turn.
        /// </summary>
        void SafeClearArmyMoveTargets()
        {
            try { ClearArmyMoveTargets(); }
            catch (System.Exception e)
            {
                if (warnedGridIndicatorClear) return;
                warnedGridIndicatorClear = true;
                Debug.LogWarning("[KingdomDemo] grid indicator unavailable; skipping move-target clear: " + e.Message);
            }
        }

        /// <summary>The agent panel's lock must never outlive a controller turn.</summary>
        void ReleaseControlLock()
        {
            uiInputLocked = false;
            uiLockReason = null;
        }

        string SeatTurnPrompt()
        {
            var fac = engine.State.FindFaction(currentSeat);
            return $"{fac?.Name}'s turn (season {engine.State.Season}) — select a city or army to issue orders";
        }

        /// <summary>The seat-filtered world handed to the agent's ITurnHost and to the panel setup.</summary>
        Observation FilteredObservation(string seatId)
            => engine != null && !string.IsNullOrEmpty(seatId) ? engine.Observe(seatId) : null;

        /// <summary>The only world the UI draws: the current seat's filtered observation.</summary>
        void RefreshObservation()
        {
            obs = engine.Observe(currentSeat);
            // Selections are detached views (LS-003), so a refresh would otherwise leave the panels showing
            // the previous snapshot's numbers. Re-point them at the new snapshot; ApplyFog then drops any
            // selection the fog has taken away.
            if (selCity != null) selCity = obs != null ? obs.FindCity(selCity.Id) : null;
            if (selArmy != null) selArmy = obs != null ? obs.FindArmy(selArmy.Id) : null;
            ApplyFog();
            RefreshFogOverlays();
        }

        void PushJson(ActionCommand cmd, ValidationResult r)
        {
            jsonFeed.Add((r.Ok ? "✓ " : "✗ ") + cmd.ToJson());
            if (jsonFeed.Count > 4) jsonFeed.RemoveAt(0);
        }

        /// <summary>Season rollover: resolve the world, then open the next seat's court report (design §2 ①).</summary>
        void ResolveSeason()
        {
            var report = engine.EndSeason();
            engine.BeginSeason();
            currentSeat = SeatRotation.FirstSeat(engine.State);
            RefreshObservation();

            BuildReport(report);
            pendingReport = true;
            handoverShowing = false;
            if (spectatorMode) return;   // SpectatorSession owns the next season too

            // No human and no runnable LLM seat means every planned LlmSeat would fall straight
            // through to ResolveSeason again. Stop at the boundary instead of spinning seasons
            // synchronously forever.
            if (agentHasSeat && agentRunner == null
                && SeatRotation.HumanSeats(engine.State).Count == 0)
            {
                statusMsg = "No human seat and no usable LLM runner: turn flow stopped";
                ReleaseControlLock();
                return;
            }
            StartPlannedTurn(TurnFlowPlanner.First(engine.State));
        }

        // ---- test hooks -------------------------------------------------------

        /// <summary>
        /// Test hook for the coordinator's MCP rehearsal: exactly the End-turn button's path, including
        /// the modal/lock/winner guards. It does not bypass the human turn or the engine gate.
        /// </summary>
        public void DebugEndTurn()
        {
            if (spectatorMode) return;   // a spectator may never advance a model seat
            if (!ready || engine == null || uiInputLocked || reportShowing || handoverShowing) return;
            if (engine.State.WinnerSeatId != null) return;
            locationChoiceOpen = false;
            EndTurn();
        }
    }
}
