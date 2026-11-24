// KingdomDemo (partial) — offline match replay controller.
//
// Replays recorded matches with ZERO live LLM or network calls.
// ReplaySession owns the authoritative replay stepping; this file connects it to
// KingdomDemo's lifecycle, updates visuals and fog-of-war, and handles UI input locks.

using System;
using UnityEngine;
using LuanShi.Engine;

namespace LuanShi
{
    public partial class KingdomDemo : MonoBehaviour
    {
        private const string ReplayLockReason = "Offline match replay (zero model calls)";

        private ReplaySession _replaySession;
        private ReplayBundle _replayBundle;
        private string _replayInitError;
        private string _replayLastViewedSeat;
        private int _replaySyncedRequestIndex = -1;
        private bool _replayFinalReportLogged;
        private ReplayPresetSet _replayPresets;

        void InitializeReplay()
        {
            if (!replayMode) return;

            uiInputLocked = true;
            uiLockReason = ReplayLockReason;
            selCity = null;
            selArmy = null;
            locationChoiceOpen = false;
            statusMsg = "Offline match replay — zero model calls; controls are docked on the right";

            _replayPresets = ReplayCatalog.LoadPresets();
            string runKey = !string.IsNullOrEmpty(replayRunId) ? replayRunId : ReplayCatalog.TreatyAttackRunId;
            _replayRunInput = runKey;
            OpenReplayRun(runKey);
        }

        /// <summary>
        /// Opens a recorded run through <see cref="ReplayCatalog"/>: discovery, validation and the LLM-only
        /// check. A failure keeps any run that is already loaded and says why; it never loads a guess.
        /// </summary>
        void OpenReplayRun(string runKey)
        {
            ReplayOpenResult open = ReplayCatalog.Open(runKey, _replayPresets != null ? _replayPresets.SearchRoots : null,
                engine != null ? engine.State.Map : null);
            _replayOpenMessage = open.Message;
            _replayOpenOk = open.Ok;
            if (!open.Ok)
            {
                if (_replaySession == null)
                {
                    _replayInitError = open.Message;
                    _replayChooserOpen = true;
                }
                Debug.LogWarning("[Replay] " + open.Status + ": " + open.Message);
                return;
            }

            if (_replaySession != null) _replaySession.OnEventReplayed -= HandleReplayEvent;
            _replayInitError = null;
            _replayBundle = open.Bundle;
            _replayOpenNotes = open.Notes;
            if (_replayBundle.Summary != null)
            {
                randomSeed = _replayBundle.Summary.Seed;
                if (!string.IsNullOrEmpty(_replayBundle.Summary.BalanceProfileKey))
                    balanceProfile = _replayBundle.Summary.BalanceProfileKey;
            }
            _replaySession = open.Session;
            _replaySession.OnEventReplayed += HandleReplayEvent;
            engine = _replaySession.Engine;
            jsonFeed.Clear();
            _replayLastViewedSeat = "seat-0";
            _replaySyncedRequestIndex = -1;
            _replayFinalReportLogged = false;
            _replayShowProviderReasoning = false;
            _replayChooserOpen = false;
            RefreshReplayView();
            SyncSpectatorArmyVisuals();
            Debug.Log("[Replay] initialized with " + _replayBundle.Requests.Count + " requests from " + runKey);
        }

        void TickReplay()
        {
            if (!replayMode || _replaySession == null) return;

            HandleReplayKeyboardControls();

            if (_replaySession.IsPlaying)
            {
                _replaySession.Tick(Time.deltaTime);
            }

            if (_replaySession.CurrentRequestIndex != _replaySyncedRequestIndex)
            {
                _replaySyncedRequestIndex = _replaySession.CurrentRequestIndex;
                RefreshReplayView();
                SyncSpectatorArmyVisuals();
            }

            if (_replaySession.IsCompleted && !_replayFinalReportLogged)
            {
                _replayFinalReportLogged = true;
                if (_replaySession.FinalReport != null)
                {
                    Debug.Log(_replaySession.FinalReport.FormatReport());
                }
            }
        }

        void HandleReplayKeyboardControls()
        {
            if (pauseMenuOpen || reportShowing || battleResultShowing || handoverShowing) return;
            if (_replayChooserOpen) return;   // typing a run id must not trigger playback keys

            if (Input.GetKeyDown(KeyCode.Space))
            {
                if (_replaySession.IsPlaying) ReplayPause();
                else ReplayPlay();
            }
            else if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.Period))
            {
                ReplayStepRequest();
            }
            else if (Input.GetKeyDown(KeyCode.S))
            {
                ReplayStepSeason();
            }
            else if (Input.GetKeyDown(KeyCode.R))
            {
                ReplayReset();
            }
            else if (Input.GetKeyDown(KeyCode.Alpha1)) ReplaySetSpeed(0.5f);
            else if (Input.GetKeyDown(KeyCode.Alpha2)) ReplaySetSpeed(1.0f);
            else if (Input.GetKeyDown(KeyCode.Alpha3)) ReplaySetSpeed(2.0f);
            else if (Input.GetKeyDown(KeyCode.Alpha4)) ReplaySetSpeed(4.0f);
        }

        void RefreshReplayView()
        {
            if (engine == null) return;
            string seat = _replaySession != null ? _replaySession.CurrentSeatId : null;
            if (string.IsNullOrEmpty(seat)) seat = _replayLastViewedSeat;
            if (string.IsNullOrEmpty(seat)) seat = currentSeat;
            if (string.IsNullOrEmpty(seat)) seat = "seat-0";
            _replayLastViewedSeat = seat;
            currentSeat = seat;
            RefreshObservation();
            ApplyFog();
        }

        void DismissReplayModals()
        {
            if (!replayMode) return;
            handoverShowing = false;
            pendingReport = false;
            if (reportShowing)
            {
                reportShowing = false;
                reportScroll = Vector2.zero;
            }
        }

        string ReplayHeaderLine()
        {
            if (_replaySession == null)
            {
                return "OFFLINE REPLAY — " + (_replayInitError ?? "loading");
            }
            string viewed = ViewedFactionNameFromObservation();
            int season = _replaySession.CurrentSeason;
            int total = _replaySession.TotalSeasons;
            string state = _replaySession.IsCompleted ? "Completed" : _replaySession.IsPlaying ? "Playing" : "Paused";
            return "OFFLINE REPLAY (LLM-only)  |  Season " + season + "/" + total + "  |  " + state + " (viewing " + viewed + ")";
        }

        string ReplayDetailLine()
        {
            if (_replaySession == null) return "Zero API calls (offline verification)";
            int req = _replaySession.CurrentRequestIndex;
            int totalReq = _replaySession.TotalRequests;
            int matched = _replaySession.ObservationHashesMatched;
            return "Zero API calls  |  Req " + req + "/" + totalReq + "  |  Obs matched " + matched + "/" + req;
        }

        public string GetDisplayedSummarySnapshot()
            => _replaySession != null ? _replaySession.GetDisplayedSummarySnapshot() : ("OFFLINE REPLAY — " + (_replayInitError ?? "loading"));

        void ReplayPlay()
        {
            if (_replaySession != null) _replaySession.Play();
        }

        void ReplayPause()
        {
            if (_replaySession != null) _replaySession.Pause();
        }

        void ReplayStepSeason()
        {
            if (_replaySession == null) return;
            _replaySession.StepSeason();
            _replaySyncedRequestIndex = _replaySession.CurrentRequestIndex;
            RefreshReplayView();
            SyncSpectatorArmyVisuals();
        }

        void ReplayStepRequest()
        {
            if (_replaySession == null) return;
            _replaySession.StepRequest();
            _replaySyncedRequestIndex = _replaySession.CurrentRequestIndex;
            RefreshReplayView();
            SyncSpectatorArmyVisuals();
        }

        void ReplayReset()
        {
            if (_replaySession == null) return;
            jsonFeed.Clear();
            _replaySession.Reset();
            engine = _replaySession.Engine;
            _replaySyncedRequestIndex = -1;
            _replayFinalReportLogged = false;
            RefreshReplayView();
            SyncSpectatorArmyVisuals();
        }

        void HandleReplayEvent(ReplayFeedEvent evt)
        {
            if (evt == null) return;
            string okMark = evt.ResultOk ? "✓ " : "✗ ";
            string line = $"{okMark}[Replay S{evt.Season} {evt.Faction}] {evt.ActionSummary}";
            // The explanation is what the model wrote, shown as its statement; the token count is the
            // provider's own figure. Neither is verified private reasoning.
            line += " — " + evt.StatementText();
            jsonFeed.Add(line);
            if (jsonFeed.Count > 4) jsonFeed.RemoveAt(0);
        }

        void ReplaySetSpeed(float multiplier)
        {
            if (_replaySession != null) _replaySession.SetSpeed(multiplier);
        }
    }
}
