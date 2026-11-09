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

        void InitializeReplay()
        {
            if (!replayMode) return;

            uiInputLocked = true;
            uiLockReason = ReplayLockReason;
            selCity = null;
            selArmy = null;
            locationChoiceOpen = false;
            statusMsg = "Offline match replay — zero model calls; controls are docked on the right";

            string runKey = !string.IsNullOrEmpty(replayRunId) ? replayRunId : ReplayLoader.VerifiedDemoRunId;
            if (!ReplayLoader.TryResolveRunFiles(runKey, out string sPath, out string jPath, out string ePath, out string err))
            {
                _replayInitError = "Run files not found: " + err;
                Debug.LogWarning("[Replay] " + _replayInitError);
                return;
            }

            try
            {
                _replayBundle = ReplayLoader.Load(sPath, jPath, ePath);
                if (_replayBundle.Summary != null)
                {
                    randomSeed = _replayBundle.Summary.Seed;
                    if (!string.IsNullOrEmpty(_replayBundle.Summary.BalanceProfileKey))
                        balanceProfile = _replayBundle.Summary.BalanceProfileKey;
                }
                _replaySession = new ReplaySession(_replayBundle, engine != null ? engine.State.Map : null);
                engine = _replaySession.Engine;
                _replayLastViewedSeat = "seat-0";
                RefreshReplayView();
                SyncSpectatorArmyVisuals();
                Debug.Log("[Replay] initialized with " + _replayBundle.Requests.Count + " requests from " + runKey);
            }
            catch (Exception ex)
            {
                _replayInitError = "Failed to load replay: " + ex.Message;
                Debug.LogWarning("[Replay] " + _replayInitError);
            }
        }

        void TickReplay()
        {
            if (!replayMode || _replaySession == null) return;

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
            return "OFFLINE REPLAY  |  Season " + season + "/" + total + "  |  " + state + " (viewing " + viewed + ")";
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
            _replaySession.Reset();
            engine = _replaySession.Engine;
            _replaySyncedRequestIndex = -1;
            _replayFinalReportLogged = false;
            RefreshReplayView();
            SyncSpectatorArmyVisuals();
        }

        void ReplaySetSpeed(float multiplier)
        {
            if (_replaySession != null) _replaySession.SetSpeed(multiplier);
        }
    }
}
