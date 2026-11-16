using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using LuanShi.Engine;

namespace LuanShi.ControlledScenarios
{
    /// <summary>
    /// Transport-independent evidence capture. It wraps ANY <see cref="ILlmTransport"/>: before
    /// dispatch it records the acting seat and the exact prompt, then links the response (or the
    /// reason there is none) and an after-state. It also enforces the scenario request cap, the
    /// shared whole-run request allowance and both wall deadlines. A request that outlives a
    /// deadline is ABANDONED, not killed: an inner task that ignores its CancellationToken keeps
    /// running, is observed for exceptions, and can never mutate a terminal capture or the sealed
    /// list. It stores nothing from LlmConfig, so no credential can reach evidence.
    /// </summary>
    public sealed class CsRecordingTransport : ILlmTransport
    {
        private readonly object _gate = new object();
        private readonly ILlmTransport _inner;
        private readonly Func<string> _activeSeat;
        private readonly CsRunBudget _budget;
        private readonly int _maxScenarioRequests;
        private readonly long _scenarioDeadlineMs;   // absolute on the budget's monotonic clock
        private readonly Func<string, CsWorldSnapshot> _before;
        private readonly Func<string, CsWorldSnapshot> _after;
        private readonly bool _requireOutputTokenLimit;
        private readonly List<CsDispatchCapture> _captures = new List<CsDispatchCapture>();
        private int _dispatched;
        private int _lateDiscarded;
        private bool _sealed;

        public CsRecordingTransport(ILlmTransport inner, Func<string> activeSeat, CsRunBudget budget,
            int maxScenarioRequests, long scenarioDeadlineMs,
            Func<string, CsWorldSnapshot> beforeSnapshot, Func<string, CsWorldSnapshot> afterSnapshot,
            bool requireOutputTokenLimit = false)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _budget = budget ?? throw new ArgumentNullException(nameof(budget));
            _activeSeat = activeSeat;
            _maxScenarioRequests = maxScenarioRequests < 0 ? 0 : maxScenarioRequests;
            _scenarioDeadlineMs = scenarioDeadlineMs;
            _before = beforeSnapshot;
            _after = afterSnapshot;
            _requireOutputTokenLimit = requireOutputTokenLimit;
        }

        public string Provider => _inner.Provider;
        public string Model => _inner.Model;

        /// <summary>Captures whose SendAsync actually called the inner transport.</summary>
        public int DispatchedCount => Volatile.Read(ref _dispatched);

        /// <summary>Inner responses that arrived after their dispatch was abandoned or sealed.</summary>
        public int LateResponsesDiscarded => Volatile.Read(ref _lateDiscarded);

        public List<CsDispatchCapture> Captures()
        {
            lock (_gate)
            {
                var copy = new List<CsDispatchCapture>(_captures.Count);
                foreach (CsDispatchCapture capture in _captures) copy.Add(Clone(capture));
                return copy;
            }
        }

        /// <summary>Freezes the list. Requests still in flight are retained without a response.</summary>
        public void Seal()
        {
            lock (_gate)
            {
                if (_sealed) return;
                _sealed = true;
                foreach (CsDispatchCapture capture in _captures)
                {
                    if (capture.State != "in_flight") continue;
                    capture.State = "no_response_before_seal";
                    capture.ReplyText = null;
                    capture.Ok = null;
                    capture.ErrorKind = null;
                    capture.ErrorDetail = null;
                    capture.FinishReason = null;
                    capture.InputTokens = null;
                    capture.OutputTokens = null;
                    capture.ReasoningTokens = null;
                    capture.TotalTokens = null;
                    capture.HttpStatus = null;
                    capture.ProviderRequestId = null;
                    capture.ElapsedMs = null;
                    capture.LatencyMs = null;
                    capture.After = null;
                }
            }
        }

        public async Task<LlmResponse> SendAsync(LlmRequest req, CancellationToken ct)
        {
            string seat = _activeSeat == null ? null : _activeSeat();
            if (string.IsNullOrEmpty(seat)) seat = "unknown";
            CsWorldSnapshot before = Snapshot(_before, seat);

            // Gates run before the inner transport is touched, in the specified order. The
            // output-token gate is first so a request that can never be bounded consumes no
            // request allowance and is never counted as dispatched.
            string refusal = null;
            if (_requireOutputTokenLimit && (req == null || req.MaxOutputTokens <= 0))
                refusal = "no_output_token_limit";
            else if (DispatchedCount >= _maxScenarioRequests) refusal = "max_scenario_requests";
            else if (!_budget.TryReserve()) refusal = "max_run_requests";
            else if (_budget.DeadlineReached) refusal = "max_run_wall_seconds";
            else if (ScenarioDeadlineReached()) refusal = "max_scenario_wall_seconds";
            if (refusal != null)
            {
                RecordRefusal(seat, req, before, refusal);
                return LlmResponse.Failure(LlmErrorKind.BudgetExhausted, refusal);
            }

            CsDispatchCapture capture = BeginCapture(seat, req, before);
            var clock = Stopwatch.StartNew();
            Task<LlmResponse> innerTask;
            try { innerTask = _inner.SendAsync(req, ct); }
            catch (OperationCanceledException)
            {
                // A synchronous throw is still a dispatched request; it has no response bytes.
                bool timedOut = LooksLikeRunnerTimeout(ct, clock, req);
                Finish(capture, timedOut ? "timed_out" : "cancelled", clock,
                    timedOut ? LlmErrorKind.Timeout : LlmErrorKind.Cancelled,
                    timedOut ? "request timed out; transport task abandoned" : "transport cancelled");
                return LlmResponse.Failure(
                    timedOut ? LlmErrorKind.Timeout : LlmErrorKind.Cancelled,
                    timedOut ? "request timed out; transport task abandoned" : "transport cancelled");
            }
            catch (Exception e)
            {
                Finish(capture, "transport_exception", clock,
                    LlmErrorKind.Network, "transport threw " + e.GetType().Name);
                return LlmResponse.Failure(LlmErrorKind.Network, "transport threw " + e.GetType().Name);
            }
            if (innerTask == null)
            {
                Finish(capture, "transport_exception", clock,
                    LlmErrorKind.Network, "transport returned no task");
                return LlmResponse.Failure(LlmErrorKind.Network, "transport returned no task");
            }

            long waitMs = Math.Min(_budget.RemainingMs, RemainingScenarioMs());
            if (waitMs < 1) waitMs = 1;
            // Task.Delay takes a finite TimeSpan; an unbounded budget still has a finite scenario
            // deadline in the runner, and this clamp keeps direct callers safe.
            if (waitMs > int.MaxValue) waitMs = int.MaxValue;
            var cancelSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (ct.Register(() => cancelSignal.TrySetResult(true)))
            {
                Task delay = Task.Delay(TimeSpan.FromMilliseconds(waitMs));
                await Task.WhenAny(innerTask, delay, cancelSignal.Task).ConfigureAwait(false);
                // Prefer an inner outcome that is already available, even if it raced the timer.
                if (!innerTask.IsCompleted && ct.IsCancellationRequested)
                {
                    // The runner cancels the same token for a request timeout and for a host stop,
                    // so use the elapsed time against req.TimeoutMs to name the real cause.
                    bool timedOut = LooksLikeRunnerTimeout(ct, clock, req);
                    string state = timedOut ? "timed_out" : "cancelled";
                    Finish(capture, state, clock,
                        timedOut ? LlmErrorKind.Timeout : LlmErrorKind.Cancelled,
                        timedOut ? "request timed out; transport task abandoned" : "cancelled by the host");
                    ObserveLate(innerTask);
                    return LlmResponse.Failure(
                        timedOut ? LlmErrorKind.Timeout : LlmErrorKind.Cancelled,
                        timedOut ? "request timed out; transport task abandoned" : "cancelled by the host");
                }
                if (!innerTask.IsCompleted && delay.IsCompleted)
                {
                    Finish(capture, "deadline_abandoned", clock, LlmErrorKind.Timeout,
                        "dispatch deadline reached; the transport task was abandoned");
                    ObserveLate(innerTask);
                    return LlmResponse.Failure(LlmErrorKind.Timeout,
                        "dispatch deadline reached; transport abandoned, not aborted");
                }
            }

            LlmResponse response;
            try { response = await innerTask.ConfigureAwait(false); }
            catch (OperationCanceledException)
            {
                bool timedOut = LooksLikeRunnerTimeout(ct, clock, req);
                string state = timedOut ? "timed_out" : "cancelled";
                Finish(capture, state, clock,
                    timedOut ? LlmErrorKind.Timeout : LlmErrorKind.Cancelled,
                    timedOut ? "request timed out; transport task abandoned" : "transport cancelled");
                return LlmResponse.Failure(
                    timedOut ? LlmErrorKind.Timeout : LlmErrorKind.Cancelled,
                    timedOut ? "request timed out; transport task abandoned" : "transport cancelled");
            }
            catch (Exception e)
            {
                Finish(capture, "transport_exception", clock,
                    LlmErrorKind.Network, "transport threw " + e.GetType().Name);
                return LlmResponse.Failure(LlmErrorKind.Network, "transport threw " + e.GetType().Name);
            }
            if (response == null)
            {
                Finish(capture, "transport_exception", clock,
                    LlmErrorKind.Network, "transport returned no response");
                return LlmResponse.Failure(LlmErrorKind.Network, "transport returned no response");
            }

            CsWorldSnapshot after = Snapshot(_after, seat);
            Complete(capture, response, clock, after);
            return response;
        }

        /// <summary>
        /// The runner cancels the same <see cref="CancellationToken"/> both when its per-request
        /// timeout expires and when the host stops the turn. The elapsed time decides which: a
        /// cancellation close to <see cref="LlmRequest.TimeoutMs"/> is a request timeout, an early
        /// one is a host stop. A task that ignores cancellation can only be abandoned, never killed.
        /// </summary>
        private static bool LooksLikeRunnerTimeout(CancellationToken ct, Stopwatch clock, LlmRequest req)
            => ct.IsCancellationRequested && req != null && req.TimeoutMs > 0
               && clock.ElapsedMilliseconds + 50 >= req.TimeoutMs;

        private bool ScenarioDeadlineReached()
            => _budget.ElapsedMs >= _scenarioDeadlineMs;

        private long RemainingScenarioMs()
        {
            long left = _scenarioDeadlineMs - _budget.ElapsedMs;
            return left < 0 ? 0 : left;
        }

        private CsDispatchCapture BeginCapture(string seat, LlmRequest req, CsWorldSnapshot before)
        {
            lock (_gate)
            {
                var capture = new CsDispatchCapture
                {
                    Index = _captures.Count + 1, Seat = seat,
                    System = req == null ? null : req.System,
                    User = req == null ? null : req.User,
                    Before = before, State = "in_flight",
                };
                _captures.Add(capture);
                _dispatched++;
                return capture;
            }
        }

        private void RecordRefusal(string seat, LlmRequest req, CsWorldSnapshot before, string reason)
        {
            lock (_gate)
            {
                _captures.Add(new CsDispatchCapture
                {
                    Index = _captures.Count + 1, Seat = seat,
                    System = req == null ? null : req.System,
                    User = req == null ? null : req.User,
                    Before = before, State = "refused_before_dispatch", RefusalReason = reason,
                });
            }
        }

        private void Finish(CsDispatchCapture capture, string state, Stopwatch clock,
            LlmErrorKind error, string detail)
        {
            lock (_gate)
            {
                if (_sealed || !IsLive(capture.State)) { if (_sealed) Interlocked.Increment(ref _lateDiscarded); return; }
                capture.State = state;
                capture.ErrorKind = error.ToString();
                capture.ErrorDetail = detail;
                capture.ElapsedMs = clock.ElapsedMilliseconds;
                capture.After = null;
            }
        }

        private void Complete(CsDispatchCapture capture, LlmResponse response, Stopwatch clock,
            CsWorldSnapshot after)
        {
            lock (_gate)
            {
                if (_sealed || !IsLive(capture.State)) { if (_sealed) Interlocked.Increment(ref _lateDiscarded); return; }
                capture.State = response.Ok ? "completed" : "failed";
                capture.ReplyText = response.Text;
                capture.Ok = response.Ok;
                capture.ErrorKind = response.Error.ToString();
                capture.ErrorDetail = response.ErrorDetail;
                capture.FinishReason = response.FinishReason;
                capture.InputTokens = response.InputTokens;
                capture.OutputTokens = response.OutputTokens;
                capture.ReasoningTokens = response.ReasoningTokens;
                capture.TotalTokens = response.TotalTokens;
                capture.HttpStatus = response.HttpStatus;
                capture.ProviderRequestId = response.ProviderRequestId;
                capture.ElapsedMs = clock.ElapsedMilliseconds;
                capture.LatencyMs = response.LatencyMs;
                capture.After = after;
            }
        }

        private static bool IsLive(string state)
            => string.Equals(state, "in_flight", StringComparison.Ordinal);

        private void ObserveLate(Task<LlmResponse> task)
        {
            // The abandoned task may never return; this continuation only observes and counts.
            task.ContinueWith(completed =>
            {
                if (completed.IsFaulted && completed.Exception != null) _ = completed.Exception;
                if (completed.Status == TaskStatus.RanToCompletion)
                    Interlocked.Increment(ref _lateDiscarded);
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        private static CsWorldSnapshot Snapshot(Func<string, CsWorldSnapshot> callback, string seat)
        {
            if (callback == null) return null;
            try { return callback(seat); }
            catch (Exception) { return null; }
        }

        private static CsDispatchCapture Clone(CsDispatchCapture source)
        {
            return new CsDispatchCapture
            {
                Index = source.Index, Seat = source.Seat, System = source.System, User = source.User,
                ReplyText = source.ReplyText, Ok = source.Ok, ErrorKind = source.ErrorKind,
                ErrorDetail = source.ErrorDetail, FinishReason = source.FinishReason,
                InputTokens = source.InputTokens, OutputTokens = source.OutputTokens,
                ReasoningTokens = source.ReasoningTokens, TotalTokens = source.TotalTokens,
                HttpStatus = source.HttpStatus, ProviderRequestId = source.ProviderRequestId,
                ElapsedMs = source.ElapsedMs, LatencyMs = source.LatencyMs,
                ScriptNote = source.ScriptNote, State = source.State,
                RefusalReason = source.RefusalReason,
                Before = Clone(source.Before), After = Clone(source.After),
            };
        }

        private static CsWorldSnapshot Clone(CsWorldSnapshot source)
        {
            if (source == null) return null;
            var copy = new CsWorldSnapshot
            {
                SeatId = source.SeatId, ObservationJson = source.ObservationJson,
                ObservationSha256 = source.ObservationSha256, WorldDigest = source.WorldDigest,
                CatalogueText = source.CatalogueText, MovementJson = source.MovementJson,
                AttackJson = source.AttackJson, MovementTruncated = source.MovementTruncated,
                AttackTruncated = source.AttackTruncated,
                UnknownFields = source.UnknownFields == null
                    ? new List<string>() : new List<string>(source.UnknownFields),
            };
            if (source.Catalogue != null)
            {
                copy.Catalogue = new List<CsCatalogueOption>(source.Catalogue.Count);
                foreach (CsCatalogueOption option in source.Catalogue)
                {
                    copy.Catalogue.Add(option == null ? null : new CsCatalogueOption
                    {
                        Type = option.Type, TargetId = option.TargetId, Label = option.Label,
                        Cost = option.Cost, LikelyAffordable = option.LikelyAffordable,
                        ParamA = option.ParamA, ParamB = option.ParamB,
                        Args = option.Args == null
                            ? null : new Dictionary<string, string>(option.Args),
                    });
                }
            }
            return copy;
        }
    }
}
