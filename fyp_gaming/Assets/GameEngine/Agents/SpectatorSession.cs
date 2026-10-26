using System;
using System.Collections.Generic;
using System.Globalization;

namespace LuanShi.Engine
{
    public enum SpectatorState
    {
        NotStarted,
        Running,
        Paused,
        Completed,
        Failed,
    }

    /// <summary>One completed (or stopped) seat turn in a spectator pilot.</summary>
    public sealed class SpectatorTurnRecord
    {
        public int Season { get; internal set; }
        public string SeatId { get; internal set; }
        public string FactionName { get; internal set; }
        public int Requests { get; internal set; }
        public int Actions { get; internal set; }
        public int Accepted { get; internal set; }
        public int Rejected { get; internal set; }
        public string LastSummary { get; internal set; }
        public string LastCommandJson { get; internal set; }
        public string LastResult { get; internal set; }
        public string EndedBy { get; internal set; }
    }

    /// <summary>
    /// Researcher/spectator-only aggregate read directly from canonical state.
    /// This value is NEVER passed into a model prompt; only the seat-filtered
    /// <see cref="Observation"/> built by <see cref="GameEngine.Observe"/> may enter a prompt.
    /// </summary>
    public sealed class SpectatorScore
    {
        public string SeatId { get; internal set; }
        public string FactionName { get; internal set; }
        public int Gold { get; internal set; }
        public int Food { get; internal set; }
        public int Population { get; internal set; }
        public int Garrison { get; internal set; }
        public int Cities { get; internal set; }
    }

    /// <summary>
    /// Headless conductor for an LLM-vs-LLM spectator game. It owns authoritative turn flow:
    /// for every season it starts each seat's turn with <see cref="GameEngine.BeginSeatTurn"/>,
    /// binds that seat and token into the host passed to <see cref="LlmTurnRunner.Begin"/>, and
    /// closes the turn with <see cref="GameEngine.EndSeatTurn"/> when the runner stops. Only one
    /// request is ever in flight because the next seat is not begun until the runner is terminal.
    ///
    /// <see cref="Tick"/> is non-blocking and is called once per frame by the Unity host or in a
    /// sleep loop by the harness/live pilot. The engine must already have had its first
    /// <see cref="GameEngine.BeginSeason"/> called; this class advances later seasons itself.
    /// </summary>
    public sealed class SpectatorSession
    {
        public const int MaxRecentActions = 12;

        private sealed class TurnHost : ITurnHost
        {
            private readonly SpectatorSession _session;
            private readonly GameEngine _engine;
            private readonly string _seatId;
            private readonly int _token;
            private readonly int _season;
            private readonly string _factionName;

            public int Accepted;
            public int Rejected;
            public bool EndTurnCalled;

            public string SeatId => _seatId;
            public string FactionName => _factionName;
            public int Season => _season;

            public TurnHost(SpectatorSession session, GameEngine engine, string seatId, int token,
                int season, string factionName)
            {
                _session = session;
                _engine = engine;
                _seatId = seatId;
                _token = token;
                _season = season;
                _factionName = factionName;
            }

            public Observation Observe() => _engine.Observe(_seatId);

            public ValidationResult Submit(ActionCommand cmd)
            {
                ValidationResult result;
                try
                {
                    result = _engine.SubmitAs(_seatId, _token, cmd);
                }
                catch (Exception)
                {
                    result = ValidationResult.Fail("host submit failed");
                }
                if (result.Ok) Accepted++;
                else Rejected++;
                _session.NoteAction(_season, _factionName, cmd, result.Ok);
                return result;
            }

            public void EndTurn() => EndTurnCalled = true;
        }

        private readonly GameEngine _engine;
        private readonly LlmTurnRunner _runner;
        private readonly ILlmRunRecorder _recorder;
        private readonly int _seed;
        private readonly List<string> _playOrder;
        private readonly List<SpectatorTurnRecord> _records = new List<SpectatorTurnRecord>();
        private readonly List<string> _recentActions = new List<string>();
        private readonly Dictionary<string, int> _failureStreak =
            new Dictionary<string, int>(StringComparer.Ordinal);

        private TurnHost _host;
        private int _turnToken;
        private int _seatIndex;
        private int _seasonsPlanned;
        private int _seasonsPlayed;
        private int _currentSeason;
        private bool _turnOpen;
        private bool _boundaryPending;
        private string _activeSeatId;
        private string _activeFactionName;
        private string _pauseReason;
        private string _stopReason;
        private DateTime? _startedUtc;
        private DateTime? _endedUtc;

        public SpectatorState State { get; private set; } = SpectatorState.NotStarted;
        public int Seed => _seed;
        public int CurrentSeason => _currentSeason;
        public int SeasonsPlanned => _seasonsPlanned;
        public int SeasonsPlayed => _seasonsPlayed;
        public string ActiveSeatId => _activeSeatId;
        public string ActiveFactionName => _activeFactionName;
        public string PauseReason => _pauseReason;
        public string StopReason => State == SpectatorState.Completed
            ? (_stopReason ?? "completed")
            : (State == SpectatorState.Paused || State == SpectatorState.Failed ? _pauseReason : null);
        public IReadOnlyList<SpectatorTurnRecord> TurnRecords => _records;
        public IReadOnlyList<string> RecentActions => _recentActions;
        public int TotalRequests => _runner.TotalRequests;
        public long? TotalInputTokens => _runner.TotalInputTokens;
        public long? TotalOutputTokens => _runner.TotalOutputTokens;
        public double? CostUsd => _runner.CostUsd;
        public DateTime StartedUtc => _startedUtc ?? DateTime.UtcNow;
        public DateTime? EndedUtc => _endedUtc;
        public LlmTurnRunner Runner => _runner;
        public ILlmRunRecorder Recorder => _recorder;

        public int TotalActions
        {
            get
            {
                int total = 0;
                for (int i = 0; i < _records.Count; i++) total += _records[i].Actions;
                return total;
            }
        }

        public int TotalAccepted
        {
            get
            {
                int total = 0;
                for (int i = 0; i < _records.Count; i++) total += _records[i].Accepted;
                return total;
            }
        }

        public int TotalRejected
        {
            get
            {
                int total = 0;
                for (int i = 0; i < _records.Count; i++) total += _records[i].Rejected;
                return total;
            }
        }

        public SpectatorSession(GameEngine engine, LlmTurnRunner runner, int seasonsToPlay, int seed,
            ILlmRunRecorder recorder = null)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _runner = runner ?? throw new ArgumentNullException(nameof(runner));
            if (seasonsToPlay < 0) throw new ArgumentOutOfRangeException(nameof(seasonsToPlay));
            _seasonsPlanned = seasonsToPlay;
            _seed = seed;
            _recorder = recorder;
            _playOrder = SeatRotation.PlayOrder(engine.State);
            _currentSeason = engine.State.Season;
            if (_playOrder.Count == 0)
            {
                State = SpectatorState.Failed;
                _pauseReason = "no seats in this game";
            }
        }

        /// <summary>Begins the first seat turn. Tick() also starts an unstarted session.</summary>
        public void Start()
        {
            if (State == SpectatorState.Running) return;
            if (State == SpectatorState.Completed) return;
            if (State == SpectatorState.Failed) return;
            if (_playOrder.Count == 0)
            {
                State = SpectatorState.Failed;
                _pauseReason = "no seats in this game";
                return;
            }
            if (_startedUtc == null) _startedUtc = DateTime.UtcNow;
            State = SpectatorState.Running;
            _pauseReason = null;
            _seatIndex = 0;
            _boundaryPending = false;
            _currentSeason = _engine.State.Season;
            if (_engine.State.WinnerSeatId != null) { Complete("game_over"); return; }
            if (_seasonsPlanned <= 0) { Complete("completed"); return; }
            BeginSeat(_playOrder[0]);
        }

        /// <summary>
        /// Advances one step: collects a finished request, closes that seat, and starts the next seat
        /// or season. It never blocks and never has two requests in flight.
        /// </summary>
        public void Tick()
        {
            if (State == SpectatorState.NotStarted) { Start(); return; }
            if (State != SpectatorState.Running) return;

            if (!_turnOpen)
            {
                if (_boundaryPending) ContinueFromBoundary();
                return;
            }

            if (_runner.State == RunnerState.Requesting || _runner.State == RunnerState.Executing)
            {
                _runner.Tick();
                if (_engine.State.WinnerSeatId != null
                    && (_runner.State == RunnerState.Requesting
                        || _runner.State == RunnerState.Executing))
                {
                    // The game was decided by an accepted order; stop paying for more model calls
                    // and close this turn before reporting completion.
                    _runner.Cancel("game over");
                }
                if (_runner.State == RunnerState.Requesting || _runner.State == RunnerState.Executing)
                    return;
            }

            FinishActiveSeat();
        }

        /// <summary>
        /// Cancels the in-flight request, safely closes the active seat, and pauses. The late task is
        /// dropped by the runner's generation counter, so its response can never execute. Resume
        /// continues at the next seat boundary.
        /// </summary>
        public void Stop()
        {
            if (State == SpectatorState.Completed || State == SpectatorState.Failed) return;
            if (State == SpectatorState.NotStarted)
            {
                State = SpectatorState.Paused;
                _pauseReason = "stopped by host";
                return;
            }
            if (State != SpectatorState.Running) return;

            if (_runner.State == RunnerState.Requesting || _runner.State == RunnerState.Executing)
                _runner.Cancel("stopped by host");

            if (_turnOpen)
            {
                SpectatorTurnRecord record = CloseSeatTurn("stopped");
                if (record != null) _records.Add(record);
                _boundaryPending = true;
            }

            State = SpectatorState.Paused;
            _pauseReason = "stopped by host";
        }

        /// <summary>Continues a paused session from the next seat (or season) boundary.</summary>
        public void Resume()
        {
            if (State != SpectatorState.Paused) return;
            if (_playOrder.Count == 0) return;
            if (_startedUtc == null) _startedUtc = DateTime.UtcNow;
            State = SpectatorState.Running;
            _pauseReason = null;

            if (_turnOpen) return;   // defensive: a paused session normally has no open seat
            if (_boundaryPending) ContinueFromBoundary();
            else BeginSeat(_playOrder[_seatIndex]);
        }

        /// <summary>
        /// Adds seasons to the plan. A session that completed its prior plan starts the next season
        /// immediately; a game-over completion is not extended.
        /// </summary>
        public void ExtendBy(int seasons)
        {
            if (seasons <= 0) return;
            _seasonsPlanned += seasons;

            if (State != SpectatorState.Completed || _stopReason == "game_over") return;
            if (_engine.State.WinnerSeatId != null) return;

            try { _engine.BeginSeason(); }
            catch (Exception)
            {
                State = SpectatorState.Failed;
                _pauseReason = "next season could not begin";
                return;
            }
            _currentSeason = _engine.State.Season;
            _seatIndex = 0;
            _boundaryPending = false;
            _stopReason = null;
            _endedUtc = null;
            State = SpectatorState.Running;
            BeginSeat(_playOrder[0]);
        }

        /// <summary>
        /// Researcher/spectator-only per-faction totals read from canonical state. This is never
        /// included in a prompt; prompts use only the seat-filtered Observation.
        /// </summary>
        public List<SpectatorScore> Scoreboard()
        {
            var scores = new List<SpectatorScore>();
            foreach (FactionState faction in _engine.State.Factions)
            {
                var score = new SpectatorScore
                {
                    SeatId = faction.SeatId,
                    FactionName = faction.Name,
                };
                foreach (CityState city in _engine.State.Cities)
                {
                    if (!string.Equals(city.OwnerSeatId, faction.SeatId, StringComparison.Ordinal)) continue;
                    score.Cities++;
                    score.Gold += city.Gold;
                    score.Food += city.Food;
                    score.Population += city.Population;
                    score.Garrison += city.Garrison;
                }
                scores.Add(score);
            }
            return scores;
        }

        internal void NoteAction(int season, string factionName, ActionCommand command, bool ok)
        {
            if (command == null) return;
            string target = string.IsNullOrEmpty(command.TargetId) ? "" : " " + command.TargetId;
            string line = "S" + season.ToString(CultureInfo.InvariantCulture) + " "
                        + (string.IsNullOrEmpty(factionName) ? "-" : factionName) + ": "
                        + (command.Type ?? "?") + target + " -> " + (ok ? "ok" : "rejected");
            if (line.Length > 160) line = line.Substring(0, 160);
            _recentActions.Add(line);
            while (_recentActions.Count > MaxRecentActions) _recentActions.RemoveAt(0);
        }

        private void BeginSeat(string seatId)
        {
            if (State != SpectatorState.Running) return;
            if (_runner.State == RunnerState.Requesting || _runner.State == RunnerState.Executing)
            {
                State = SpectatorState.Paused;
                _pauseReason = "runner is still active";
                return;
            }

            FactionState faction = _engine.State.FindFaction(seatId);
            if (faction == null)
            {
                State = SpectatorState.Paused;
                _pauseReason = "seat is not part of this game";
                return;
            }

            int token;
            try { token = _engine.BeginSeatTurn(seatId); }
            catch (Exception)
            {
                State = SpectatorState.Paused;
                _pauseReason = "seat turn could not begin";
                return;
            }

            _activeSeatId = seatId;
            _activeFactionName = faction.Name;
            _turnToken = token;
            _turnOpen = true;
            _host = new TurnHost(this, _engine, seatId, token, _currentSeason, faction.Name);

            try { _runner.Begin(_host, token, seatId, _seed); }
            catch (Exception)
            {
                _engine.EndSeatTurn(token);
                _turnOpen = false;
                _host = null;
                _activeSeatId = null;
                _turnToken = 0;
                State = SpectatorState.Paused;
                _pauseReason = "runner turn could not begin";
            }
        }

        private void FinishActiveSeat()
        {
            if (!_turnOpen) return;

            RunnerState runnerState = _runner.State;
            string failure = _runner.FailureReason;
            string endedBy;
            if (runnerState == RunnerState.Failed)
                endedBy = "failed: " + (failure ?? "runner failed");
            else if (runnerState == RunnerState.Finished && !string.IsNullOrEmpty(failure))
                endedBy = "limit: " + failure;
            else if (runnerState == RunnerState.Finished)
                endedBy = "end_turn";
            else if (runnerState == RunnerState.Cancelled)
                endedBy = "cancelled: " + (failure ?? "cancelled");
            else
                endedBy = "idle";

            SpectatorTurnRecord record = CloseSeatTurn(endedBy);
            if (record != null) _records.Add(record);

            bool failed = runnerState == RunnerState.Failed;
            if (failed)
            {
                _failureStreak.TryGetValue(record != null ? record.SeatId : "", out int streak);
                _failureStreak[record != null ? record.SeatId : ""] = streak + 1;
            }
            else if (record != null)
            {
                _failureStreak[record.SeatId] = 0;
            }

            if (_engine.State.WinnerSeatId != null) { Complete("game_over"); return; }

            if (failed)
            {
                if (IsProviderPause(failure))
                {
                    State = SpectatorState.Paused;
                    _pauseReason = failure;
                    _boundaryPending = true;
                    return;
                }
                if (record != null)
                {
                    _failureStreak.TryGetValue(record.SeatId, out int streak);
                    if (streak >= 2)
                    {
                        State = SpectatorState.Paused;
                        _pauseReason = record.SeatId + " failed twice in a row: "
                                     + (failure ?? "runner failed");
                        _boundaryPending = true;
                        return;
                    }
                }
            }

            if (runnerState == RunnerState.Finished && !string.IsNullOrEmpty(failure)
                && failure.IndexOf("session request limit", StringComparison.Ordinal) >= 0)
            {
                State = SpectatorState.Paused;
                _pauseReason = "session request limit reached";
                _boundaryPending = true;
                return;
            }

            if (runnerState == RunnerState.Cancelled)
            {
                State = SpectatorState.Paused;
                _pauseReason = failure ?? "runner cancelled";
                _boundaryPending = true;
                return;
            }

            if (runnerState == RunnerState.Idle)
            {
                State = SpectatorState.Paused;
                _pauseReason = "runner ended unexpectedly";
                _boundaryPending = true;
                return;
            }

            _boundaryPending = true;
            ContinueFromBoundary();
        }

        private SpectatorTurnRecord CloseSeatTurn(string endedBy)
        {
            TurnHost host = _host;
            int token = _turnToken;
            _host = null;
            _turnToken = 0;
            _turnOpen = false;
            _activeSeatId = null;
            _activeFactionName = null;
            if (token != 0) _engine.EndSeatTurn(token);

            if (host == null) return null;
            return new SpectatorTurnRecord
            {
                Season = host.Season,
                SeatId = host.SeatId,
                FactionName = host.FactionName,
                Requests = _runner.RequestsThisTurn,
                Actions = _runner.ActionsThisTurn,
                Accepted = host.Accepted,
                Rejected = host.Rejected,
                LastSummary = _runner.LastSummary,
                LastCommandJson = _runner.LastCommandJson,
                LastResult = _runner.LastResult,
                EndedBy = endedBy,
            };
        }

        private void ContinueFromBoundary()
        {
            if (State != SpectatorState.Running) return;
            _boundaryPending = false;

            _seatIndex++;
            if (_seatIndex >= _playOrder.Count)
            {
                _seatIndex = 0;
                try { _engine.EndSeason(); }
                catch (Exception)
                {
                    State = SpectatorState.Paused;
                    _pauseReason = "season resolution failed";
                    return;
                }
                _seasonsPlayed++;

                if (_engine.State.WinnerSeatId != null) { Complete("game_over"); return; }
                if (_seasonsPlayed >= _seasonsPlanned) { Complete("completed"); return; }

                try { _engine.BeginSeason(); }
                catch (Exception)
                {
                    State = SpectatorState.Paused;
                    _pauseReason = "next season could not begin";
                    return;
                }
                _currentSeason = _engine.State.Season;
            }

            BeginSeat(_playOrder[_seatIndex]);
        }

        private void Complete(string reason)
        {
            _stopReason = reason;
            _endedUtc = DateTime.UtcNow;
            _activeSeatId = null;
            _activeFactionName = null;
            _turnOpen = false;
            _host = null;
            _turnToken = 0;
            State = SpectatorState.Completed;
        }

        private static bool IsProviderPause(string failure)
        {
            if (string.IsNullOrEmpty(failure)) return false;
            return failure.IndexOf("provider auth", StringComparison.Ordinal) >= 0
                || failure.IndexOf("provider ratelimited", StringComparison.Ordinal) >= 0
                || failure.IndexOf("provider rate limited", StringComparison.Ordinal) >= 0;
        }

    }
}
