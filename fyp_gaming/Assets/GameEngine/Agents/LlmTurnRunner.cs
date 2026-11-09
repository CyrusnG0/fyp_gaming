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
    /// the turn). Terminal paths, Cancel and Begin detach the in-flight task (DropInflight) and Tick
    /// reads an attached task only while Requesting, so a late response can never execute. A
    /// duplicate delivery is refused by its completion source, and a repeated order is re-validated
    /// by the engine. The generation checks are defense in depth, not independently tested.
    /// </summary>
    public sealed partial class LlmTurnRunner
    {
        public const int MaxPromptChars = PromptBuilder.MaxPromptChars;
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
            public Observation Observation;
            public string PromptVersion;
            public string SchemaHash;
            public int MaxOutputTokens;
        }

        private readonly ILlmTransport _transport;
        private readonly LlmConfig _config;
        private readonly ILlmRunRecorder _recorder;
        private readonly ILlmMemoryStore _memoryStore;
        private readonly string _runId;

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
        private string _factionName;
        private int _season;
        private int? _historySeed;
        private bool _endTurnCalled;
        private bool _costKnown;
        private bool _inputUnknown;
        private bool _outputUnknown;
        private double _cost;
        private long? _totalInput;
        private long? _totalOutput;
        private string _gameId;
        private int _memorySeason = -1;
        private readonly List<LlmMemory> _memories = new List<LlmMemory>();

        public RunnerState State { get; private set; } = RunnerState.Idle;
        public string Status { get; private set; } = "idle";
        public string LastSummary { get; private set; }
        public string CurrentPersonaId { get; private set; }
        public string LastCommandJson { get; private set; }
        public string LastResult { get; private set; }
        public string FailureReason { get; private set; }
        public EndReason EndReason { get; private set; }
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
        public IReadOnlyList<LlmMemory> Memories => _memories.AsReadOnly();

        public LlmTurnRunner(ILlmTransport transport, LlmConfig config, ILlmRunRecorder recorder,
            ILlmMemoryStore memoryStore = null)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _config = config ?? new LlmConfig();
            _recorder = recorder;
            _memoryStore = memoryStore;
            _runId = "run-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + "-"
                   + Guid.NewGuid().ToString("N").Substring(0, 8);
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
            _season = 0;
            _factionName = null;
            _gameId = null;
            _memorySeason = -1;
            _memories.Clear();
            _pendingInquiryResult = null;
            // History is scoped to the seat and the game seed. Reusing one runner for both LLM
            // seats must not leak either seat's action feedback into the other's prompt. A seed
            // change starts a genuinely new session and clears every seat.
            if (_historySeed != seed)
            {
                _histories.Clear();
                _activity.Clear();
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
            CurrentPersonaId = null;
            LastCommandJson = null;
            LastResult = null;
            FailureReason = null;
            EndReason = EndReason.None;
            _endTurnCalled = false;
            _generation++;
            _clock = Stopwatch.StartNew();
            AddActivity("turn", "Turn started.");
            State = RunnerState.Requesting;
            Status = "requesting";
            // Terminal stop (a): never dispatch for a game that is already decided.
            if (HasPlanningHost && PlanningHost.IsGameOver) { FinishGameOver(); return; }
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
            _activity.Clear();
            _history = new List<string>();
            _historySeed = null;
            _gameId = null;
            _memorySeason = -1;
            _memories.Clear();
            _pendingInquiryResult = null;
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

        /// <summary>Advance the state machine. Never blocks and never touches provider I/O on this thread.</summary>
        public void Tick()
        {
            if (State != RunnerState.Requesting || _inflight == null) return;
            InFlight flight = _inflight;
            if (flight.Generation != _generation) return;

            if (TurnTimeExpired())
            {
                var expired = LlmResponse.Failure(LlmErrorKind.Timeout, "turn time limit reached",
                    ElapsedMs(flight));
                FinishLimit("turn time limit reached", EndReason.TurnTimeLimit);
                // No provider response arrived: finish reason and usage are recorded as unknown.
                WriteRecord(flight, expired, false, null, null, "turn time limit reached");
                return;
            }
            if (RequestTimedOut(flight))
            {
                var timeout = LlmResponse.Failure(LlmErrorKind.Timeout, "request timed out",
                    ElapsedMs(flight));
                FinishFailed("request timed out", EndReason.RequestTimeout);
                WriteRecord(flight, timeout, false, null, null, "request timed out");
                return;
            }
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
                var cancelled = LlmResponse.Failure(LlmErrorKind.Cancelled, FailureReason,
                    ElapsedMs(flight));
                WriteRecord(flight, cancelled, false, null, null, FailureReason);
            }
        }

        private bool IsRunning => State == RunnerState.Requesting || State == RunnerState.Executing;

        private void StartRequest()
        {
            if (!IsRunning) return;
            if (TurnTimeExpired()) { FinishLimit("turn time limit reached", EndReason.TurnTimeLimit); return; }
            if (_config.MaxRequestsPerTurn <= 0 || RequestsThisTurn >= _config.MaxRequestsPerTurn)
            { FinishLimit("request limit reached", EndReason.RequestLimit); return; }
            if (_config.MaxActionsPerTurn <= 0 || ActionsThisTurn >= _config.MaxActionsPerTurn)
            { FinishLimit("action limit reached", EndReason.ActionLimit); return; }
            if (_config.MaxTotalRequests <= 0 || TotalRequests >= _config.MaxTotalRequests)
            { FinishLimit("session request limit reached", EndReason.SessionRequestLimit); return; }
            if (_config.MaxUsd.HasValue)
            {
                // Fail closed: a configured dollar cap that cannot be measured must not be skipped.
                // This is checked before dispatch, so no-prices configs send nothing at all.
                if (!_costKnown) { FinishFailed("usd budget unknown", EndReason.UsdBudgetUnknown); return; }
                if (_cost >= _config.MaxUsd.Value)
                { FinishLimit("usd budget reached", EndReason.UsdBudgetLimit); return; }
            }

            // Terminal stop (b): re-check immediately before any observation or dispatch. A winner
            // declared since Begin must cost no provider request.
            bool planning = HasPlanningHost;
            if (planning && PlanningHost.IsGameOver) { FinishGameOver(); return; }

            Observation observation;
            try { observation = _host.Observe(); }
            catch (Exception) { FinishFailed("host observation failed", EndReason.HostError); return; }
            if (observation == null) { FinishFailed("host observation unavailable", EndReason.HostError); return; }
            // A host must never hand the runner another seat's snapshot. Reject before building or
            // recording a prompt so a foreign observation cannot enter the model context or run logs.
            if (!string.Equals(observation.SeatId, _seatId, StringComparison.Ordinal))
            { FinishFailed("observation seat mismatch", EndReason.HostError); return; }
            _season = observation.Season;
            bool newGame = !string.Equals(_gameId, observation.GameId, StringComparison.Ordinal);
            bool seasonBoundary = !newGame && observation.Season > _memorySeason;
            if (newGame || seasonBoundary)
            {
                _gameId = observation.GameId;
                _memories.Clear();
                if (_memoryStore != null && !string.IsNullOrEmpty(_gameId))
                {
                    _memoryStore.Consolidate(_gameId, _seatId, observation.Season,
                        LlmMemoryLifecycle.DefaultRetentionSeasons,
                        LlmMemoryLifecycle.DefaultMaxItems);
                    _memories.AddRange(_memoryStore.Load(_gameId, _seatId));
                }
                _memorySeason = observation.Season;
            }
            // Chat attribution comes from the seat-filtered observation, never canonical state.
            _factionName = observation.Faction(_seatId)?.Name;

            MovementView movement = null;
            if (planning && !TryPlanMovement(out movement, out string movementFailure))
            { FinishFailed(movementFailure, EndReason.HostError); return; }

            // Opt-in attack/capture candidates; null (and a byte-identical prompt) unless enabled.
            bool attackOn = AttackActive;
            AttackView attacks = null;
            if (attackOn && !TryPlanAttacks(out attacks, out string attackFailure))
            { FinishFailed(attackFailure, EndReason.HostError); return; }

            // The prompt carries a bounded event history; record exactly that same JSON, not the full one.
            string observationJson = PromptBuilder.BoundedObservationJson(observation);
            // The private result is consumed by exactly this prompt; it never survives into a
            // later seat's request or a recreated turn.
            string inquiryResult = TakePendingInquiryResult();
            bool inquiry = _config.EnableManualLookup;
            LlmPersona persona = LlmPersona.ResolveForTurn(_config, _host, observation, _seatId);
            CurrentPersonaId = persona?.Id;
            string system = PromptBuilder.SystemPromptFor(planning, inquiry, persona, attackOn);
            List<ActionOption> options = ActionCatalogue.Build(observation, attacks);
            IReadOnlyList<LlmMemory> relevantMemories = LlmMemoryRetriever.Select(
                _memories, observation, options);
            string user = planning
                ? PromptBuilder.UserPrompt(observation, options, _history,
                    RemainingRequests(), RemainingActions(), movement, relevantMemories,
                    inquiryResult, inquiry, persona, attackOn)
                : PromptBuilder.UserPrompt(observation, options, _history,
                    RemainingRequests(), RemainingActions(), relevantMemories, inquiryResult,
                    inquiry, persona);
            if (system.Length + user.Length > MaxPromptChars)
            { FinishFailed("prompt too large", EndReason.PromptTooLarge); return; }

            int index = RequestsThisTurn + 1;
            string promptVersion = PromptBuilder.VersionFor(planning, inquiry, persona, attackOn);
            string schemaHash = PromptBuilder.SchemaHashFor(planning, inquiry, persona, attackOn);
            var req = new LlmRequest(system, user, _config.MaxOutputTokens, _config.RequestTimeoutMs);
            // The exact prompt is captured before I/O, so a transport failure still leaves evidence.
            CaptureExchangeRequest(index, observation.Season, req, promptVersion, schemaHash);
            var cts = new CancellationTokenSource();
            if (_config.RequestTimeoutMs > 0) cts.CancelAfter(_config.RequestTimeoutMs);

            Task<LlmResponse> task;
            try { task = _transport.SendAsync(req, cts.Token); }
            catch (Exception)
            {
                cts.Dispose();
                CaptureExchangeDispatchFailure(index, observation.Season, promptVersion, schemaHash,
                    "transport failed to start");
                FinishFailed("transport failed to start", EndReason.TransportError);
                return;
            }
            if (task == null)
            {
                cts.Dispose();
                CaptureExchangeDispatchFailure(index, observation.Season, promptVersion, schemaHash,
                    "transport returned no task");
                FinishFailed("transport returned no task", EndReason.TransportError);
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
                Observation = observation,
                PromptVersion = promptVersion,
                SchemaHash = schemaHash,
                // Match the transport's effective fallback when the configured cap is zero.
                MaxOutputTokens = _config.MaxOutputTokens > 0 ? _config.MaxOutputTokens : 400,
            };
            State = RunnerState.Requesting;
            Status = "requesting response " + index;
            AddActivity("request", "Request " + index + " of "
                + Math.Max(0, _config.MaxRequestsPerTurn)
                + " - waiting for provider...");
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
    }
}
