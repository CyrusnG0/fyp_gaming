using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace LuanShi.Engine
{
    public enum RunnerState { Idle, Requesting, Executing, Finished, Failed, Cancelled }

    /// <summary>
    /// The trusted host adapter. Observe/Submit/EndTurn are called only from the runner's main-thread
    /// Tick/Begin path. The host binds the acting seat and turn token before submitting to the engine.
    /// </summary>
    public interface ITurnHost
    {
        Observation Observe();
        ValidationResult Submit(ActionCommand cmd);
        void EndTurn();
    }

    /// <summary>
    /// Main-thread, bounded one-seat LLM turn state machine. Provider I/O is the only background
    /// work. Every terminal path calls EndTurn once, except a host cancellation (the host then owns
    /// the turn). A generation counter makes late or duplicate responses inert.
    /// </summary>
    public sealed partial class LlmTurnRunner
    {
        public const int MaxPromptChars = 40000;
        public const int MaxRecentHistory = 12;

        private sealed class InFlight
        {
            public Task<LlmResponse> Task;
            public int Generation;
            public long StartedMs;
            public int RequestIndex;
            public string ObservationJson;
            public string ObservationHash;
            public int Season;
        }

        private readonly ILlmTransport _transport;
        private readonly LlmConfig _config;
        private readonly ILlmRunRecorder _recorder;
        private readonly string _runId;
        private readonly string _schemaHash;

        // One bounded feedback list per seat. A shared runner drives both LLM seats in a spectator
        // pilot, so switching seats must never leak one seat's action history into the other's prompt.
        private readonly Dictionary<string, List<string>> _histories =
            new Dictionary<string, List<string>>(StringComparer.Ordinal);
        private List<string> _history = new List<string>();

        private ITurnHost _host;
        private CancellationTokenSource _cts;
        private InFlight _inflight;
        private Stopwatch _clock;
        private int _generation;
        private int _turnToken;
        private int _seed;
        private string _seatId;
        private int? _historySeed;
        private bool _endTurnCalled;
        private bool _costKnown;
        private bool _inputUnknown;
        private bool _outputUnknown;
        private double _cost;
        private long? _totalInput;
        private long? _totalOutput;

        public RunnerState State { get; private set; } = RunnerState.Idle;
        public string Status { get; private set; } = "idle";
        public string LastSummary { get; private set; }
        public string LastCommandJson { get; private set; }
        public string LastResult { get; private set; }
        public string FailureReason { get; private set; }
        public int RequestsThisTurn { get; private set; }
        public int ActionsThisTurn { get; private set; }
        public int InvalidThisTurn { get; private set; }
        public int TotalRequests { get; private set; }
        public string RunId => _runId;

        /// <summary>Null means cost is unknown (no prices configured or some usage was missing).</summary>
        public double? CostUsd => _costKnown ? _cost : (double?)null;
        public long? TotalInputTokens => _inputUnknown ? null : _totalInput;
        public long? TotalOutputTokens => _outputUnknown ? null : _totalOutput;

        /// <summary>The current seat's bounded feedback list; never another seat's history.</summary>
        public IReadOnlyList<string> RecentHistory => _history;

        public LlmTurnRunner(ILlmTransport transport, LlmConfig config, ILlmRunRecorder recorder)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _config = config ?? new LlmConfig();
            _recorder = recorder;
            _runId = "run-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + "-"
                   + Guid.NewGuid().ToString("N").Substring(0, 8);
            _schemaHash = PromptBuilder.SchemaHash();
            if (recorder is LlmRunRecorder fileRecorder) fileRecorder.UseRunId(_runId);
            _costKnown = _config.UsdPerMTokIn.HasValue && _config.UsdPerMTokOut.HasValue;
        }

        /// <summary>Start a turn. Throws if a turn is already requesting/executing.</summary>
        public void Begin(ITurnHost host, int turnToken, string seatId, int seed)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (string.IsNullOrEmpty(seatId)) throw new ArgumentException("seat id is required", nameof(seatId));
            if (IsRunning) throw new InvalidOperationException("an LLM turn is already running");

            DropInflight();
            _host = host;
            _turnToken = turnToken;
            _seatId = seatId;
            _seed = seed;
            // History is scoped to the seat and the game seed. Reusing one runner for both LLM
            // seats must not leak either seat's action feedback into the other's prompt. A seed
            // change starts a genuinely new session and clears every seat.
            if (_historySeed != seed)
            {
                _histories.Clear();
                _historySeed = seed;
            }
            if (!_histories.TryGetValue(seatId, out _history))
            {
                _history = new List<string>();
                _histories[seatId] = _history;
            }

            RequestsThisTurn = 0;
            ActionsThisTurn = 0;
            InvalidThisTurn = 0;
            LastSummary = null;
            LastCommandJson = null;
            LastResult = null;
            FailureReason = null;
            _endTurnCalled = false;
            _generation++;
            _clock = Stopwatch.StartNew();
            State = RunnerState.Requesting;
            Status = "requesting";
            StartRequest();
        }

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
            _history = new List<string>();
            _historySeed = null;
            RequestsThisTurn = 0;
            ActionsThisTurn = 0;
            InvalidThisTurn = 0;
            LastSummary = null;
            LastCommandJson = null;
            LastResult = null;
            FailureReason = null;
            _host = null;
            State = RunnerState.Idle;
            Status = "idle";
        }

        /// <summary>Advance the state machine. Never blocks and never touches provider I/O on this thread.</summary>
        public void Tick()
        {
            if (State != RunnerState.Requesting || _inflight == null) return;
            InFlight flight = _inflight;
            if (flight.Generation != _generation) return;

            if (TurnTimeExpired()) { FinishLimit("turn time limit reached"); return; }
            if (RequestTimedOut(flight)) { FinishFailed("request timed out"); return; }
            if (!flight.Task.IsCompleted) return;

            _inflight = null;
            int latency = ElapsedMs(flight);
            State = RunnerState.Executing;
            Status = "executing response";
            LlmResponse response = ReadResponse(flight.Task);
            if (flight.Generation != _generation) return;
            if (response == null)
                response = LlmResponse.Failure(LlmErrorKind.Network, "transport returned no response", latency);
            TrackUsage(response);
            HandleResponse(flight, response);
        }

        /// <summary>Host cancellation. Does not call EndTurn; a late response is discarded.</summary>
        public void Cancel(string why)
        {
            if (!IsRunning) return;
            string reason = Limit(Redact(why), 200);
            _generation++;
            DropInflight();
            _clock?.Stop();
            State = RunnerState.Cancelled;
            FailureReason = string.IsNullOrEmpty(reason) ? "cancelled by host" : reason;
            Status = "cancelled: " + FailureReason;
        }

        private bool IsRunning => State == RunnerState.Requesting || State == RunnerState.Executing;

        private void HandleResponse(InFlight flight, LlmResponse response)
        {
            string commandJson = null;
            bool? resultOk = null;
            string resultError = null;

            if (!response.Ok)
            {
                LastResult = Redact(ErrorText(response));
                Status = "provider error: " + response.Error;
                resultError = response.ErrorDetail;
                FinishFailed("provider " + response.Error.ToString().ToLowerInvariant());
                WriteRecord(flight, response, false, null, null, resultError);
                return;
            }

            // A successful response whose usage cannot be counted makes a configured MaxUsd
            // unenforceable. Stop here rather than continuing on an unknown budget.
            if (_config.MaxUsd.HasValue && !_costKnown)
            {
                LastResult = "usd budget unknown";
                FinishFailed("usd budget unknown");
                WriteRecord(flight, response, false, null, null, "usd budget unknown");
                return;
            }

            ParseResult parsed = LlmActionParser.Parse(response.Text);
            if (!parsed.Ok)
            {
                InvalidThisTurn++;
                LastResult = Redact("invalid response: " + parsed.Error);
                AddHistory("invalid model response -> " + parsed.Error);
                resultError = parsed.Error;
                Status = "invalid response; requesting repair";
                if (InvalidThisTurn >= _config.MaxInvalidPerTurn)
                {
                    FinishLimit("invalid-response limit reached");
                    WriteRecord(flight, response, false, null, null, resultError);
                }
                else
                {
                    // Record the counters/state of this response before the next request increments them.
                    WriteRecord(flight, response, false, null, null, resultError);
                    StartRequest();
                }
                return;
            }

            LlmAction action = parsed.Action;
            if (!string.IsNullOrEmpty(action.Summary) || !string.IsNullOrEmpty(action.Reason))
                LastSummary = Limit(Redact(action.Summary ?? action.Reason), 200);

            if (action.EndTurn)
            {
                LastResult = "end_turn";
                FinishTerminal(RunnerState.Finished, "end_turn");
                WriteRecord(flight, response, true, null, null, null);
                return;
            }

            ActionCommand command = LlmActionParser.ToCommand(action);
            ActionsThisTurn++;
            bool accepted = false;
            try
            {
                ValidationResult validation = _host.Submit(command);
                accepted = validation.Ok;
                // The engine's target errors can distinguish a hidden-but-existing id from a
                // nonexistent one. The model gets one indistinguishable answer in both cases.
                resultError = accepted ? null : SanitizeModelFeedback(validation.Error);
            }
            catch (Exception)
            {
                resultError = "host submit failed";
            }

            commandJson = Redact(command == null ? null : command.ToJson());
            LastCommandJson = commandJson;
            resultOk = accepted;
            if (accepted)
            {
                LastResult = "ok";
                AddHistory(HistoryLine(action, "ok"));
                Status = "order accepted";
            }
            else
            {
                InvalidThisTurn++;
                LastResult = Redact(resultError);
                AddHistory(HistoryLine(action, "rejected: " + resultError));
                Status = "order rejected; continuing";
            }

            if (InvalidThisTurn >= _config.MaxInvalidPerTurn)
            {
                FinishLimit("invalid-response limit reached");
                WriteRecord(flight, response, true, commandJson, resultOk, resultError);
            }
            else if (ActionsThisTurn >= _config.MaxActionsPerTurn)
            {
                FinishLimit("action limit reached");
                WriteRecord(flight, response, true, commandJson, resultOk, resultError);
            }
            else
            {
                // Snapshot before StartRequest increments RequestsThisTurn/dispatches the next request.
                WriteRecord(flight, response, true, commandJson, resultOk, resultError);
                StartRequest();
            }
        }

        private void StartRequest()
        {
            if (!IsRunning) return;
            if (TurnTimeExpired()) { FinishLimit("turn time limit reached"); return; }
            if (_config.MaxRequestsPerTurn <= 0 || RequestsThisTurn >= _config.MaxRequestsPerTurn)
            { FinishLimit("request limit reached"); return; }
            if (_config.MaxActionsPerTurn <= 0 || ActionsThisTurn >= _config.MaxActionsPerTurn)
            { FinishLimit("action limit reached"); return; }
            if (_config.MaxTotalRequests <= 0 || TotalRequests >= _config.MaxTotalRequests)
            { FinishLimit("session request limit reached"); return; }
            if (_config.MaxUsd.HasValue)
            {
                // Fail closed: a configured dollar cap that cannot be measured must not be skipped.
                // This is checked before dispatch, so no-prices configs send nothing at all.
                if (!_costKnown) { FinishFailed("usd budget unknown"); return; }
                if (_cost >= _config.MaxUsd.Value)
                { FinishLimit("usd budget reached"); return; }
            }

            Observation observation;
            try { observation = _host.Observe(); }
            catch (Exception) { FinishFailed("host observation failed"); return; }
            if (observation == null) { FinishFailed("host observation unavailable"); return; }
            // A host must never hand the runner another seat's snapshot. Reject before building or
            // recording a prompt so a foreign observation cannot enter the model context or run logs.
            if (!string.Equals(observation.SeatId, _seatId, StringComparison.Ordinal))
            { FinishFailed("observation seat mismatch"); return; }

            // The prompt carries a bounded event history; record exactly that same JSON, not the full one.
            string observationJson = PromptBuilder.BoundedObservationJson(observation);
            string system = PromptBuilder.SystemPrompt();
            string user = PromptBuilder.UserPrompt(observation, ActionCatalogue.Build(observation),
                _history, RemainingRequests(), RemainingActions());
            if (system.Length + user.Length > MaxPromptChars)
            { FinishFailed("prompt too large"); return; }

            int index = RequestsThisTurn + 1;
            var req = new LlmRequest(system, user, _config.MaxOutputTokens, _config.RequestTimeoutMs);
            var cts = new CancellationTokenSource();
            if (_config.RequestTimeoutMs > 0) cts.CancelAfter(_config.RequestTimeoutMs);

            Task<LlmResponse> task;
            try { task = _transport.SendAsync(req, cts.Token); }
            catch (Exception)
            {
                cts.Dispose();
                FinishFailed("transport failed to start");
                return;
            }
            if (task == null)
            {
                cts.Dispose();
                FinishFailed("transport returned no task");
                return;
            }

            DropInflight();
            RequestsThisTurn++;
            TotalRequests++;
            _generation++;
            _cts = cts;
            _inflight = new InFlight
            {
                Task = task,
                Generation = _generation,
                StartedMs = _clock.ElapsedMilliseconds,
                RequestIndex = index,
                ObservationJson = observationJson,
                ObservationHash = Hash(observationJson),
                Season = observation.Season,
            };
            State = RunnerState.Requesting;
            Status = "requesting response " + index;
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

        private void FinishLimit(string reason)
        {
            FailureReason = reason;
            Status = "limit: " + reason;
            FinishTerminal(RunnerState.Finished, Status);
        }

        private void FinishFailed(string reason)
        {
            FailureReason = reason;
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
            Task<LlmResponse> task = _inflight?.Task;
            _inflight = null;
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

        /// <summary>
        /// Engine target-existence errors ("unknown city 'x'", "not your city", ...) reveal whether a
        /// hidden id exists. All such errors become one fixed string for the model; resource/phase
        /// errors pass through unchanged because they contain no hidden identifiers.
        /// </summary>
        private static string SanitizeModelFeedback(string error)
        {
            if (string.IsNullOrEmpty(error)) return error;
            if (error.StartsWith("unknown city", StringComparison.Ordinal)
                || error.StartsWith("unknown army", StringComparison.Ordinal)
                || error.StartsWith("unknown seat", StringComparison.Ordinal)
                || error.IndexOf("not your city", StringComparison.Ordinal) >= 0
                || error.IndexOf("not your army", StringComparison.Ordinal) >= 0
                || error.IndexOf("army is not yours", StringComparison.Ordinal) >= 0)
            {
                return "invalid target";
            }
            return error;
        }
    }
}
