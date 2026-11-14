using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LuanShi.Engine
{
    /// <summary>
    /// Session reset, host cancellation and the single terminal/teardown path of the turn runner.
    /// Split from the state machine in <c>LlmTurnRunner.cs</c>; behaviour is unchanged.
    /// </summary>
    public sealed partial class LlmTurnRunner
    {
        /// <summary>
        /// Clears the in-process session context used by the next <see cref="Begin"/>: action history,
        /// last-action display fields and per-turn counters. It deliberately does NOT reset
        /// <see cref="TotalRequests"/> or the cumulative token/cost totals: those are spend guards for
        /// the whole game session, so a reset cannot be used to evade the budget. Start a new runner
        /// (and therefore a new session budget) when a genuinely new session begins.
        /// </summary>
        public void ResetSession()
        {
            if (IsRunning)
                throw new InvalidOperationException("cancel the active turn before resetting the session");
            _histories.Clear();
            _activity.Clear();
            _history = new List<string>();
            _historySeed = null;
            _gameId = null;
            _memorySeason = -1;
            _memories.Clear();
            _pendingInquiryResult = null;
            ResetDeliberationTurnState();
            RequestsThisTurn = 0;
            ActionsThisTurn = 0;
            InvalidThisTurn = 0;
            LastSummary = null;
            LastCommandJson = null;
            LastResult = null;
            FailureReason = null;
            EndReason = EndReason.None;
            _season = 0;
            _host = null;
            State = RunnerState.Idle;
            Status = "idle";
        }

        /// <summary>Host cancellation. Does not call EndTurn; a late response is discarded.</summary>
        public void Cancel(string why)
        {
            if (!IsRunning) return;
            string reason = Limit(Redact(why), 200);
            InFlight flight = _inflight;
            _generation++;
            DropInflight();
            _clock?.Stop();
            State = RunnerState.Cancelled;
            FailureReason = string.IsNullOrEmpty(reason) ? "cancelled by host" : reason;
            EndReason = EndReason.Cancelled;
            Status = "cancelled: " + FailureReason;
            AddActivity("finished", EndReasonText);
            if (flight != null)
            {
                // Record the abandoned request with explicitly unknown provider fields. A late
                // response is inert and must not be mistaken for this request's result.
                AbandonDeliberationProvider(flight, DeliberationCancelReason.Cancelled);
                var cancelled = LlmResponse.Failure(LlmErrorKind.Cancelled, FailureReason,
                    ElapsedMs(flight));
                WriteRecord(flight, cancelled, false, null, null, FailureReason);
            }
        }

        private LlmResponse ReadResponse(Task<LlmResponse> task)
        {
            try { return task.GetAwaiter().GetResult(); }
            catch (OperationCanceledException)
            {
                return LlmResponse.Failure(LlmErrorKind.Timeout, "request timed out");
            }
            catch (Exception)
            {
                return LlmResponse.Failure(LlmErrorKind.Network, "transport failure");
            }
        }

        private void FinishLimit(string reason, EndReason endReason)
        {
            FailureReason = reason;
            EndReason = endReason;
            Status = "limit: " + reason;
            FinishTerminal(RunnerState.Finished, Status);
        }

        private void FinishFailed(string reason, EndReason endReason)
        {
            FailureReason = reason;
            EndReason = endReason;
            Status = "failed: " + reason;
            FinishTerminal(RunnerState.Failed, Status);
        }

        private void FinishTerminal(RunnerState state, string status)
        {
            _generation++;
            DropInflight();
            _clock?.Stop();
            State = state;
            Status = status;
            AddActivity("finished", EndReasonText);
            if (_endTurnCalled) return;
            _endTurnCalled = true;
            try { _host?.EndTurn(); }
            catch (Exception) { /* the host owns its turn; never let host bookkeeping crash the runner */ }
        }

        private void DropInflight()
        {
            CancellationTokenSource cts = _cts;
            _cts = null;
            if (cts != null)
            {
                try { cts.Cancel(); } catch (Exception) { }
                cts.Dispose();
            }
            InFlight abandoned = _inflight;
            Task<LlmResponse> task = abandoned?.Task;
            _inflight = null;
            // A flight dropped by Finish/Begin must not leave a ledger reservation holding budget.
            if (abandoned != null) AbandonDeliberationProvider(abandoned, DeliberationCancelReason.Aborted);
            if (task != null)
            {
                if (task.IsFaulted)
                {
                    // A task can finish faulted between dispatch and Cancel/Finish. Observe its
                    // exception now; observing only pending tasks would leak UnobservedTaskException.
                    var ignored = task.Exception;
                }
                else
                {
                    // Attach even to a completed non-faulted task: if it faults in the tiny window
                    // after the IsFaulted check, this continuation still observes the exception.
                    task.ContinueWith(t => { var alsoIgnored = t.Exception; },
                        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
                }
            }
        }
    }
}
