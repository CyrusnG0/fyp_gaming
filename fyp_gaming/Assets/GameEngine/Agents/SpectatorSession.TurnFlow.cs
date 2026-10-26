using System;

namespace LuanShi.Engine
{
    public sealed partial class SpectatorSession
    {
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
                // A half-started runner must not keep a live request (or a bound host) alive while the
                // session is paused, or Resume() could never start this seat again. Cancel is a no-op
                // when Begin failed before reaching the requesting state.
                _runner.Cancel("runner turn could not begin");
                _host = null;
                _engine.EndSeatTurn(token);
                _turnOpen = false;
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
                Invalid = _runner.InvalidThisTurn,
                LastSummary = _runner.LastSummary,
                LastCommandJson = _runner.LastCommandJson,
                LastResult = _runner.LastResult,
                EndedBy = endedBy,
                EndReason = _runner.EndReason,
                EndReasonText = _runner.EndReasonText,
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
                _seasonScores.RemoveAll(s => s.Season == _currentSeason);
                _seasonScores.Add(new SpectatorSeasonScore
                {
                    Season = _currentSeason,
                    Scores = Scoreboard(),
                });

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
