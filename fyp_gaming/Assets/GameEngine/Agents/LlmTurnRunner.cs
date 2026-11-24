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
            public SpatialTruncationReport SpatialTruncation;
            /// <summary>Ledger reservation held for this provider round-trip (null when deliberation is off).</summary>
            public string DeliberationReservationId;
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
        public SpatialTruncationReport LastSpatialTruncation { get; private set; }
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
            ILlmMemoryStore memoryStore = null,
            ILlmStrategicNoteStore strategicNoteStore = null,
            DeliberationBudgetLedger deliberationLedger = null,
            IDeliberationClock deliberationClock = null)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _config = config ?? new LlmConfig();
            _recorder = recorder;
            _memoryStore = memoryStore;
            // Optional deliberation plumbing. All defaults preserve the legacy path exactly; the
            // note store is only created when the opt-in feature actually runs.
            _strategicNoteStore = strategicNoteStore;
            _injectedDeliberationLedger = deliberationLedger;
            _deliberationClock = deliberationClock;
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
            LastSpatialTruncation = null;
            ResetDeliberationTurnState();
            ResetExperimentTurnState();
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

        /// <summary>Advance the state machine. Never blocks and never touches provider I/O on this thread.</summary>
        public void Tick()
        {
            if (State != RunnerState.Requesting || _inflight == null) return;
            InFlight flight = _inflight;
            if (flight.Generation != _generation) return;

            string timeCeiling = TimeCeilingHit();
            if (timeCeiling != null)
            {
                _stopDetail = timeCeiling;
                var expired = LlmResponse.Failure(LlmErrorKind.Timeout, "turn time limit reached",
                    ElapsedMs(flight));
                SettleDeliberationProvider(flight, null);
                FinishLimit("turn time limit reached", EndReason.TurnTimeLimit);
                // No provider response arrived: finish reason and usage are recorded as unknown.
                WriteRecord(flight, expired, false, null, null, "turn time limit reached");
                return;
            }
            if (RequestTimedOut(flight))
            {
                var timeout = LlmResponse.Failure(LlmErrorKind.Timeout, "request timed out",
                    ElapsedMs(flight));
                SettleDeliberationProvider(flight, null);
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
            // One completed provider round-trip settles its ledger reservation exactly once, before
            // any parse, action or inquiry work. Feature-off flights carry no reservation id.
            SettleDeliberationProvider(flight, response);
            HandleResponse(flight, response);
        }

        private bool IsRunning => State == RunnerState.Requesting || State == RunnerState.Executing;

        private void StartRequest()
        {
            if (!IsRunning) return;
            if (TurnTimeExpired()) { FinishLimit("turn time limit reached", EndReason.TurnTimeLimit); return; }
            if (!ExperimentPreflight()) return;
            // Deliberation mode delegates request/action/session admission to the shared per-match
            // ledger. The legacy counters remain display evidence only in that mode. Feature-off
            // keeps the exact historical checks below.
            if (!DeliberationActive)
            {
                if (_config.MaxRequestsPerTurn <= 0 || RequestsThisTurn >= _config.MaxRequestsPerTurn)
                { FinishLimit("request limit reached", EndReason.RequestLimit); return; }
                if (_config.MaxActionsPerTurn <= 0 || ActionsThisTurn >= _config.MaxActionsPerTurn)
                { FinishLimit("action limit reached", EndReason.ActionLimit); return; }
                if (_config.MaxTotalRequests <= 0 || TotalRequests >= _config.MaxTotalRequests)
                { FinishLimit("session request limit reached", EndReason.SessionRequestLimit); return; }
            }
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

            // Opt-in deliberation: bind one shared ledger per match and load this seat's private
            // note before any prompt or provider work. Failure here is a bounded stop, never a
            // silent fallback to a different accounting scheme.
            if (DeliberationActive && !PrepareDeliberation(observation, out string deliberationFailure))
            { FinishFailed(deliberationFailure, EndReason.DeliberationLimit); return; }
            if (!ExperimentTokenGate()) return;

            MovementView movement = null;
            if (planning && !TryPlanMovement(out movement, out string movementFailure))
            { FinishFailed(movementFailure, EndReason.HostError); return; }

            // Opt-in attack/capture candidates; null (and a byte-identical prompt) unless enabled.
            bool attackOn = AttackActive;
            AttackView attacks = null;
            if (attackOn && !TryPlanAttacks(out attacks, out string attackFailure))
            { FinishFailed(attackFailure, EndReason.HostError); return; }

            // Opt-in diplomacy: the player-written messages and notes are rendered once, bounded and
            // neutralised, and the same text is stripped from the observation this prompt carries.
            // Building the section first means it reads the seat's own unfiltered copies; the
            // projection is a new observation, so the host's snapshot is never mutated.
            bool diplomacy = _config.EnableDiplomacy;
            string diplomacySection = diplomacy
                ? PromptBuilder.DiplomacyMessagesSection(observation) : null;
            if (diplomacy) observation = PromptBuilder.ProjectDiplomacyObservation(observation);

            // The prompt carries a bounded event history; record exactly that same JSON, not the full one.
            string observationJson = PromptBuilder.BoundedObservationJson(observation);
            // The private result is consumed by exactly this prompt; it never survives into a
            // later seat's request or a recreated turn.
            string inquiryResult = TakePendingInquiryResult();
            bool deliberation = DeliberationActive;
            // In deliberation mode the full tool addendum replaces the legacy manual-only addendum.
            bool inquiry = _config.EnableManualLookup && !deliberation;
            string strategicNote = deliberation ? _strategicNote : null;
            IReadOnlyList<string> earlierResults = deliberation
                ? EarlierRetainedInquiryResults(inquiryResult) : null;
            LlmPersona persona = LlmPersona.ResolveForTurn(_config, _host, observation, _seatId);
            CurrentPersonaId = persona?.Id;
            bool rich = ExperimentActive;
            bool research = _config.EnableResearch;
            SpatialContextOptions spatialOpts = _config?.SpatialContext;
            bool spatialOn = planning && spatialOpts != null && spatialOpts.IsEnabled
                && movement != null && movement.SpatialContext != null;
            int promptLimit = rich ? _config.EffectiveMaxPromptChars : MaxPromptChars;
            if (spatialOn && spatialOpts.ProviderCharLimit > 0)
                promptLimit = Math.Min(promptLimit, spatialOpts.ProviderCharLimit);
            string system = PromptBuilder.CombinedSystemPromptFor(planning, inquiry, persona, attackOn,
                deliberation, rich, research, spatialOn, diplomacy);
            string promptVersion = PromptBuilder.CombinedVersionFor(planning, inquiry, persona, attackOn,
                deliberation, rich, research, spatialOn, diplomacy);
            string schemaHash = PromptBuilder.CombinedSchemaHashFor(planning, inquiry, persona, attackOn,
                deliberation, rich, research, spatialOn, diplomacy);
            List<ActionOption> options = ActionCatalogue.Build(observation, attacks, research, diplomacy);
            // Opt-in action reference: rules and costs for exactly the orders this config enables,
            // read from the match's own balance profile. It goes before the garrison-conversion
            // addendum and suffix, which stay last in the system text and the version.
            // The attack rule text follows the engine's own rule for this match (carried on the attack
            // view), never the retained historical constant. Historical-rule mode swaps the wording and
            // the version to the legacy identity; if that cannot be done exactly the turn fails closed.
            bool assaultRatioRule = attackOn && attacks != null && attacks.AssaultRatioEnforced;
            if (assaultRatioRule)
            {
                if (!PromptBuilder.TryApplyHistoricalAssaultRule(system, promptVersion,
                        out system, out promptVersion))
                { FinishFailed("assault rule prompt unavailable", EndReason.HostError); return; }
                schemaHash = PromptBuilder.ActionReferenceHash(system);
            }
            bool actionReference = planning && _config.EnableActionReference;
            if (actionReference)
            {
                system += PromptBuilder.ActionReferenceAddendum(observation?.Balance, attackOn, research,
                    diplomacy, _config.EnableGarrisonConversion, assaultRatioRule, UnitTypesActive);
                promptVersion += PromptBuilder.ActionReferenceVersionSuffix;
                schemaHash = PromptBuilder.ActionReferenceHash(system);
            }
            // The revised live attack wording carries its own identity; historical mode already
            // switched to the legacy attack version above.
            if (planning && attackOn && !assaultRatioRule)
                promptVersion += PromptBuilder.AssaultTextVersionSuffix;
            // Opt-in unit types: facts for the four recruitable types, one candidate entry per type.
            // After the assault-text suffix and before the garrison suffix, which stays last.
            bool unitTypes = UnitTypesActive;
            if (unitTypes)
            {
                system = PromptBuilder.ApplyUnitTypes(system, observation?.Balance, attackOn,
                    _config.EnableGarrisonConversion, assaultRatioRule);
                promptVersion += PromptBuilder.UnitTypesVersionSuffix;
                schemaHash = PromptBuilder.GarrisonConversionHash(system);
                options = ActionCatalogue.ExpandRecruitTypes(options, observation);
            }
            if (_config.EnableGarrisonConversion)
            {
                system += PromptBuilder.GarrisonConversionAddendum;
                promptVersion += PromptBuilder.GarrisonConversionVersionSuffix;
                schemaHash = PromptBuilder.GarrisonConversionHash(system);
                ActionCatalogue.AppendGarrisonConversions(options, observation);
            }
            IReadOnlyList<LlmMemory> relevantMemories = LlmMemoryRetriever.Select(
                _memories, observation, options);
            int requestsLeft = RemainingRequests();
            // In deliberation mode the action budget means accepted actions, not attempts.
            int actionsLeft = deliberation
                ? Math.Max(0, _config.MaxActionsPerTurn - _acceptedActionsThisTurn)
                : RemainingActions();
            // Deliberation uses the richer builder even for a non-planning host: with a null
            // movement view and no attacks it emits the same domestic body plus the private note,
            // retained results and the deliberation identity.
            string user;
            if (rich)
            {
                // Experiment: one seat-filtered, size-reported prompt. The recorded observation JSON
                // is exactly the JSON this prompt carries, including its configured event cap.
                if (!TryBuildRichPrompt(observation, options, movement, attacks, persona, inquiryResult,
                        promptVersion, system.Length, requestsLeft, actionsLeft,
                        diplomacySection == null ? 0 : diplomacySection.Length,
                        out user, out string richObservation))
                    return;
                observationJson = richObservation;
            }
            else
            {
                SpatialTruncationReport spatialReport = null;
                user = _config.EnableGarrisonConversion || actionReference || unitTypes
                    ? PromptBuilder.UserPromptWithFinalBudget(observation, options, _history,
                        requestsLeft, actionsLeft, movement, spatialOn ? movement.SpatialContext : null,
                        spatialOn ? spatialOpts : null, promptLimit, promptVersion, system.Length,
                        diplomacySection == null ? 0 : diplomacySection.Length,
                        relevantMemories, inquiryResult, persona, attackOn, attacks,
                        strategicNote, earlierResults, out spatialReport)
                    : planning || deliberation
                    ? (spatialOn
                        ? PromptBuilder.UserPromptWithSpatial(observation, options, _history,
                            requestsLeft, actionsLeft, movement, movement.SpatialContext, spatialOpts,
                            out spatialReport, promptLimit, relevantMemories, inquiryResult, inquiry,
                            persona, attackOn, attacks, strategicNote, earlierResults, deliberation,
                            research)
                        : PromptBuilder.UserPrompt(observation, options, _history,
                            requestsLeft, actionsLeft, movement, relevantMemories,
                            inquiryResult, inquiry, persona, attackOn, attacks, strategicNote,
                            earlierResults, deliberation, research))
                    : PromptBuilder.UserPrompt(observation, options, _history,
                        requestsLeft, actionsLeft, relevantMemories, inquiryResult,
                        inquiry, persona, research);
                LastSpatialTruncation = spatialReport;
            }
            // The bounded diplomacy section sits before the candidate list, so the model reads the
            // untrusted text immediately next to the legal orders. It is added after the builder (and
            // after the rich assembler's own truncation ladder): a section that cannot fit still
            // fails closed on the size check below. The version line is then aligned with the system
            // prompt and the run record, because the shared builders carry no diplomacy flag.
            if (diplomacy) user = PromptBuilder.InsertDiplomacySection(user, diplomacySection);
            if (diplomacy || _config.EnableGarrisonConversion || actionReference || unitTypes)
                user = PromptBuilder.ApplyDiplomacyVersion(user, promptVersion);
            if ((_config.EnableGarrisonConversion || actionReference || unitTypes) && LastSpatialTruncation != null)
            {
                LastSpatialTruncation.SystemChars = system.Length;
                LastSpatialTruncation.UserChars = user.Length;
                LastSpatialTruncation.TotalChars = system.Length + user.Length;
            }
            if (system.Length + user.Length > promptLimit)
            {
                RecordPromptSizeFinalCheck(system.Length, user.Length, promptLimit, diplomacySection);
                FinishFailed("prompt too large", EndReason.PromptTooLarge);
                return;
            }

            int index = RequestsThisTurn + 1;
            var req = new LlmRequest(system, user, _config.MaxOutputTokens, _config.RequestTimeoutMs);
            // Admit and mark dispatched before the provider is called. A denial sends nothing and
            // records no provider request.
            DeliberationAdmission requestAdmission = null;
            if (deliberation && !TryDispatchDeliberationRequest(out requestAdmission)) return;

            RecordExperimentSettings(promptVersion, schemaHash);
            SpatialTruncationReport requestSpatialTruncation = LastSpatialTruncation;
            // The exact prompt is captured before I/O, so a transport failure still leaves evidence.
            // It is recorded only once dispatch is approved: a denied request never reached a provider.
            CaptureExchangeRequest(index, observation.Season, req, promptVersion, schemaHash,
                requestSpatialTruncation);

            var cts = new CancellationTokenSource();
            if (_config.RequestTimeoutMs > 0) cts.CancelAfter(_config.RequestTimeoutMs);

            Task<LlmResponse> task;
            try { task = _transport.SendAsync(req, cts.Token); }
            catch (Exception)
            {
                cts.Dispose();
                AbandonDeliberationAdmission(requestAdmission);
                CaptureExchangeDispatchFailure(index, observation.Season, promptVersion, schemaHash,
                    "transport failed to start");
                FinishFailed("transport failed to start", EndReason.TransportError);
                return;
            }
            if (task == null)
            {
                cts.Dispose();
                AbandonDeliberationAdmission(requestAdmission);
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
                SpatialTruncation = requestSpatialTruncation,
                DeliberationReservationId = requestAdmission?.ReservationId,
            };
            State = RunnerState.Requesting;
            Status = "requesting response " + index;
            AddActivity("request", "Request " + index + " of "
                + Math.Max(0, _config.MaxRequestsPerTurn)
                + " - waiting for provider...");
        }
    }
}
