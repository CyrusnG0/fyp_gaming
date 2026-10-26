// KingdomDemo (partial) — optional LLM-vs-LLM spectator pilot ("domestic-management").
//
// Both seats are driven by the SAME shared LlmTurnRunner built by KingdomDemo.Agent.cs. The headless
// SpectatorSession owns the authoritative turn/season flow; this file only ticks it, keeps the map on
// the active seat's filtered observation and exposes rehearsal hooks. The researcher panel lives in
// KingdomDemo.SpectatorPanel.cs. Scoreboard data stays in the UI and never enters a prompt.

using System;
using System.Globalization;
using System.IO;
using UnityEngine;
using LuanShi.Engine;

namespace LuanShi
{
    public partial class KingdomDemo : MonoBehaviour
    {
        private const string SpectatorLockReason = "Spectating LLM-vs-LLM pilot";

        private SpectatorSession spectatorSession;
        private string spectatorLastActiveSeat;
        private string spectatorSummaryPath;
        private string spectatorSetupError;
        private SpectatorState spectatorSummaryNotified = SpectatorState.NotStarted;

        // ------------------------------------------------------------------ setup & tick

        /// <summary>Builds the conductor after InitGame/InitializeAgent. Never starts a human seat turn.</summary>
        void InitializeSpectator()
        {
            if (!spectatorMode) return;

            uiInputLocked = true;
            uiLockReason = SpectatorLockReason;
            selCity = null;
            selArmy = null;
            locationChoiceOpen = false;
            statusMsg = "Spectator pilot - both seats are LLM-controlled; orders are locked";
            spectatorSummaryNotified = SpectatorState.NotStarted;

            if (engine == null || agentRunner == null)
            {
                spectatorSetupError = string.IsNullOrEmpty(agentUnavailableReason)
                    ? "Agent setup failed" : agentUnavailableReason;
                RefreshObservation();
                return;
            }

            try
            {
                spectatorSession = new SpectatorSession(engine, agentRunner, Mathf.Max(0, spectatorSeasons),
                    randomSeed, agentRecorder);
                spectatorLastActiveSeat = SeatRotation.FirstSeat(engine.State);
                if (useFakeLlm && agentFake != null)
                    ResetFakeSpectatorQueue(FirstSpectatorSeat(), Mathf.Max(0, spectatorSeasons));
                RefreshSpectatorView();
                Debug.Log("[KingdomDemo] spectator pilot ready: " + spectatorSeasons + " seasons, run "
                          + agentRunner.RunId);
            }
            catch (Exception e)
            {
                spectatorSession = null;
                spectatorSetupError = "Spectator setup failed";
                Debug.LogWarning("[KingdomDemo] spectator setup failed: " + e.Message);
            }
        }

        /// <summary>Called once per frame from the existing agent tick coroutine.</summary>
        void TickSpectator()
        {
            DismissSpectatorModals();
            SpectatorSession session = spectatorSession;
            if (session == null) return;

            session.Tick();
            RefreshSpectatorView();
            UpdateSpectatorSummary(session);
        }

        /// <summary>Keeps the map on the active (or most recently active) seat's filtered observation.</summary>
        void RefreshSpectatorView()
        {
            if (engine == null) return;
            string seat = spectatorSession != null ? spectatorSession.ActiveSeatId : null;
            if (string.IsNullOrEmpty(seat)) seat = spectatorLastActiveSeat;
            if (string.IsNullOrEmpty(seat)) seat = currentSeat;
            if (string.IsNullOrEmpty(seat)) return;
            spectatorLastActiveSeat = seat;
            currentSeat = seat;
            RefreshObservation();
        }

        /// <summary>The court report/handover modals must never block a spectator loop.</summary>
        void DismissSpectatorModals()
        {
            if (!spectatorMode) return;
            handoverShowing = false;
            pendingReport = false;
            if (reportShowing)
            {
                reportShowing = false;
                reportScroll = Vector2.zero;
            }
        }

        // ------------------------------------------------------------------ fake rehearsal

        string UpcomingSpectatorSeat(SpectatorSession session)
        {
            var order = SeatRotation.PlayOrder(engine.State);
            if (order.Count == 0) return null;
            if (session == null || session.State == SpectatorState.NotStarted) return order[0];

            // After Stop/failure the last completed record is the seat that was closed; Resume
            // continues at the next boundary. With no record yet, the plan starts at the first seat.
            var records = session.TurnRecords;
            if (records.Count == 0) return order[0];
            string last = records[records.Count - 1].SeatId;
            int index = order.IndexOf(last);
            return index >= 0 && index + 1 < order.Count ? order[index + 1] : order[0];
        }

        string FirstSpectatorSeat()
        {
            var order = SeatRotation.PlayOrder(engine.State);
            return order.Count > 0 ? order[0] : null;
        }

        /// <summary>
        /// FAKE rehearsal script. Responses are queued for the whole run in turn order because the
        /// conductor opens the next seat inside Tick(); a per-frame queue would arrive too late.
        /// Every turn is levy, reclaim, end_turn and every summary is labelled FAKE.
        /// </summary>
        void EnqueueFakeSeatTurn(string seatId)
        {
            if (agentFake == null || engine == null || string.IsNullOrEmpty(seatId)) return;
            Observation seatObs = engine.Observe(seatId);
            string cityId = null;
            if (seatObs != null)
            {
                var cities = seatObs.CitiesOf(seatId);
                if (cities.Count > 0) cityId = cities[0].Id;
            }

            if (!string.IsNullOrEmpty(cityId))
            {
                agentFake.EnqueueSuccess("{\"type\":\"levy_tax\",\"target_id\":\"" + JsonText(cityId)
                    + "\",\"summary\":\"FAKE rehearsal: levy tax\"}", 200, 120, 20);
                agentFake.EnqueueSuccess("{\"type\":\"reclaim\",\"target_id\":\"" + JsonText(cityId)
                    + "\",\"summary\":\"FAKE rehearsal: reclaim\"}", 200, 120, 20);
            }
            agentFake.EnqueueSuccess("{\"type\":\"end_turn\",\"summary\":\"FAKE rehearsal: end turn\"}",
                150, 60, 10);
        }

        void ResetFakeSpectatorQueue(string firstSeatId, int seasons)
        {
            if (agentFake == null) return;
            agentFake.Reset();
            AppendFakeSpectatorPlan(firstSeatId, seasons);
        }

        void AppendFakeSpectatorPlan(string firstSeatId, int seasons)
        {
            if (agentFake == null || engine == null || seasons <= 0) return;
            var order = SeatRotation.PlayOrder(engine.State);
            if (order.Count == 0) return;
            int start = Mathf.Max(0, order.IndexOf(firstSeatId));
            for (int season = 0; season < seasons; season++)
                for (int i = 0; i < order.Count; i++)
                {
                    if (season == 0 && i < start) continue;   // resuming mid-season
                    EnqueueFakeSeatTurn(order[i]);
                }
        }

        // ------------------------------------------------------------------ summary & controls

        /// <summary>Writes the pilot summary once per transition into Paused/Completed.</summary>
        void UpdateSpectatorSummary(SpectatorSession session)
        {
            if (session == null) return;
            if (session.State == SpectatorState.Running || session.State == SpectatorState.NotStarted)
            {
                spectatorSummaryNotified = session.State;
                return;
            }
            if (session.State != SpectatorState.Paused && session.State != SpectatorState.Completed) return;
            if (spectatorSummaryNotified == session.State) return;
            spectatorSummaryNotified = session.State;

            try
            {
                string directory = agentRecorder != null ? agentRecorder.DirectoryPath
                    : Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "runs"));
                string path = new SpectatorSummary(directory)
                    .Write(session, agentConfig, agentTransport, session.Runner);
                spectatorSummaryPath = ShortSpectatorPath(path);
                Debug.Log("[KingdomDemo] spectator summary written: " + spectatorSummaryPath);
            }
            catch (Exception)
            {
                spectatorSummaryPath = "summary write failed";
                Debug.LogWarning("[KingdomDemo] spectator summary could not be written");
            }
        }

        static string ShortSpectatorPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            string file = Path.GetFileName(path);
            return string.IsNullOrEmpty(file) ? path : "runs/" + file;
        }

        void StopSpectator()
        {
            SpectatorSession session = spectatorSession;
            if (session != null
                && (session.State == SpectatorState.Running || session.State == SpectatorState.NotStarted))
            {
                session.Stop();
                UpdateSpectatorSummary(session);
            }
            RefreshSpectatorView();
        }

        void ResumeSpectator()
        {
            SpectatorSession session = spectatorSession;
            if (session == null || session.State != SpectatorState.Paused) return;
            if (useFakeLlm && agentFake != null)
                ResetFakeSpectatorQueue(UpcomingSpectatorSeat(session),
                    Mathf.Max(0, session.SeasonsPlanned - session.SeasonsPlayed));
            session.Resume();
            RefreshSpectatorView();
            UpdateSpectatorSummary(session);
        }

        void ExtendSpectator(int seasons)
        {
            SpectatorSession session = spectatorSession;
            if (session == null || seasons <= 0) return;
            int cap = agentConfig != null ? agentConfig.MaxTotalRequests : 0;
            if (cap - session.TotalRequests < 12) return;
            if (useFakeLlm && agentFake != null
                && (session.State != SpectatorState.Completed || engine.State.WinnerSeatId == null))
                AppendFakeSpectatorPlan(FirstSpectatorSeat(), seasons);
            session.ExtendBy(seasons);
            RefreshSpectatorView();
        }

        /// <summary>Play-mode exit: cancel the in-flight request (late answers are inert) and close the gate.</summary>
        void StopSpectatorSession()
        {
            SpectatorSession session = spectatorSession;
            if (session != null
                && (session.State == SpectatorState.Running || session.State == SpectatorState.NotStarted))
            {
                try { session.Stop(); } catch (Exception) { }
            }
            if (spectatorMode && agentRunner != null
                && (agentRunner.State == RunnerState.Requesting || agentRunner.State == RunnerState.Executing))
                agentRunner.Cancel("play mode ended");
            spectatorSession = null;
        }

        // ------------------------------------------------------------------ debug hooks

        /// <summary>One-line rehearsal status: state|season/seasons|activeSeat|requests|in|out|pause|summary.</summary>
        public string DebugSpectatorStatus()
        {
            SpectatorSession session = spectatorSession;
            string state = !spectatorMode ? "off" : session != null ? session.State.ToString() : "unavailable";
            string season = session != null
                ? session.CurrentSeason + "/" + session.SeasonsPlanned
                : "0/" + Mathf.Max(0, spectatorSeasons);
            string active = session != null && !string.IsNullOrEmpty(session.ActiveSeatId)
                ? session.ActiveSeatId : "-";
            int requests = session != null ? session.TotalRequests : 0;
            string input = session != null && session.TotalInputTokens.HasValue
                ? session.TotalInputTokens.Value.ToString(CultureInfo.InvariantCulture) : "unknown";
            string output = session != null && session.TotalOutputTokens.HasValue
                ? session.TotalOutputTokens.Value.ToString(CultureInfo.InvariantCulture) : "unknown";
            string reason = session != null ? session.PauseReason : spectatorSetupError;
            return SpectatorField(state) + "|" + season + "|" + SpectatorField(active) + "|" + requests
                 + "|" + input + "|" + output + "|" + SpectatorField(reason) + "|"
                 + SpectatorField(spectatorSummaryPath);
        }

        public void DebugStopSpectator() => StopSpectator();

        public void DebugResumeSpectator() => ResumeSpectator();

        public void DebugExtendSpectator(int seasons) => ExtendSpectator(seasons);

        static string SpectatorField(string value)
        {
            if (string.IsNullOrEmpty(value)) return "-";
            return value.Replace('|', '/').Replace('\r', ' ').Replace('\n', ' ').Trim();
        }
    }
}
