using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace LuanShi.Engine
{
    public enum ReplayState { NotStarted, Playing, Paused, Completed }

    public sealed partial class ReplaySession
    {
        private readonly ReplayBundle _bundle;
        private readonly MapData _hostMap;
        private GameEngine _engine;
        private ReplayState _state = ReplayState.NotStarted;
        private float _speed = 1.0f;
        private float _stepTimer;
        private const float BaseStepInterval = 0.8f;

        private int _totalSeasons, _totalRequests;
        private int _expectedAcceptedActions, _expectedRejectedActions, _expectedVoluntaryEndTurns;
        private int _currentRequestIndex, _currentSeason, _currentTurnToken;
        private string _currentSeatId;
        private bool _turnOpen, _isCompleted;
        private bool _allowAttack, _allowResearch, _allowDiplomacy, _allowGarrisonConversion;
        private bool _hasDivergence, _isIncompleteRun;
        private string _divergenceSummary, _stopReason;
        public bool PauseOnDivergence = true;

        private int _observationHashesMatched, _observationHashMismatches;
        private readonly List<string> _observationHashDiscrepancies = new List<string>();

        private int _acceptedActionsReplayed, _rejectedActionsReplayed, _unexpectedRejections, _unexpectedAcceptances;
        private int _expectedEngineRejections, _engineRejectionsReplayed;
        private int _expectedInvalidResponses, _invalidResponsesReplayed;
        private int _expectedProviderFailures, _providerFailuresReplayed;
        private int _rejectionReasonMismatches, _actionOutcomeMismatches;
        private readonly List<string> _actionDiscrepancies = new List<string>();
        private int _voluntaryEndTurnsReplayed;
        private bool _llmVisibleMarchRoutes, _visibleMarchRuleEstablished;
        private bool? _establishedSharedVisibleMarchRoutes;
        private VisibleMarchRuleProvenance _visibleMarchRuleProvenance;
        private string _visibleMarchRuleNote;
        private AssaultTroopRatioRuleProvenance _assaultTroopRatioProvenance;
        private bool _assaultTroopRatioRuleEstablished;
        private bool? _establishedAssaultTroopRatio;
        private string _assaultTroopRatioRuleNote;

        private readonly List<ReplaySeasonScoreboard> _replayedScores = new List<ReplaySeasonScoreboard>();
        private readonly List<ReplayFeedEvent> _eventFeed = new List<ReplayFeedEvent>();
        private List<ReplaySeasonSummaryItem> _seasonSummaries;
        private ReplayComparisonReport _finalReport;
        public event Action<ReplayFeedEvent> OnEventReplayed;

        public ReplayBundle Bundle => _bundle;
        public GameEngine Engine => _engine;
        public ReplayState State => _state;
        public float Speed => _speed;
        public bool IsPlaying => _state == ReplayState.Playing;
        public bool IsCompleted => _isCompleted;
        public bool HasDivergence => _hasDivergence;
        public string DivergenceSummary => _divergenceSummary;
        public bool IsIncompleteRun => _isIncompleteRun;
        public string StopReason => _stopReason;
        public bool ReplayedVisibleMarchRoutes { get; private set; }
        public bool ReplayedLlmVisibleMarchRoutes => _llmVisibleMarchRoutes;
        public bool? EstablishedSharedVisibleMarchRoutes => _establishedSharedVisibleMarchRoutes;
        public VisibleMarchRuleProvenance VisibleMarchRuleProvenance => _visibleMarchRuleProvenance;
        public bool VisibleMarchRuleEstablished => _visibleMarchRuleEstablished;
        public string VisibleMarchRuleNote => _visibleMarchRuleNote;
        public bool ReplayedAssaultTroopRatio { get; private set; }
        public AssaultTroopRatioRuleProvenance AssaultTroopRatioProvenance => _assaultTroopRatioProvenance;
        public bool AssaultTroopRatioRuleEstablished => _assaultTroopRatioRuleEstablished;
        public bool? EstablishedAssaultTroopRatio => _establishedAssaultTroopRatio;
        public string AssaultTroopRatioRuleNote => _assaultTroopRatioRuleNote;
        public int TotalSeasons => _totalSeasons;
        public int TotalRequests => _totalRequests;
        public int CurrentRequestIndex => _currentRequestIndex;
        public int CurrentSeason => _currentSeason;
        public int CurrentTurnToken => _currentTurnToken;
        public string CurrentSeatId => _currentSeatId;
        public int ExpectedAcceptedActions => _expectedAcceptedActions;
        public int ExpectedRejectedActions => _expectedRejectedActions;
        public int ExpectedVoluntaryEndTurns => _expectedVoluntaryEndTurns;
        public int AcceptedActionsReplayed => _acceptedActionsReplayed;
        public int RejectedActionsReplayed => _rejectedActionsReplayed;
        public int ExpectedEngineRejections => _expectedEngineRejections;
        public int EngineRejectionsReplayed => _engineRejectionsReplayed;
        public int ExpectedInvalidResponses => _expectedInvalidResponses;
        public int InvalidResponsesReplayed => _invalidResponsesReplayed;
        public int ExpectedProviderFailures => _expectedProviderFailures;
        public int ProviderFailuresReplayed => _providerFailuresReplayed;
        public int UnexpectedRejections => _unexpectedRejections;
        public int UnexpectedAcceptances => _unexpectedAcceptances;
        public int RejectionReasonMismatches => _rejectionReasonMismatches;
        public int ActionOutcomeMismatches => _actionOutcomeMismatches;
        public IReadOnlyList<string> ActionDiscrepancies => _actionDiscrepancies;
        public int ObservationHashesMatched => _observationHashesMatched;
        public int ObservationHashMismatches => _observationHashMismatches;
        public IReadOnlyList<string> ObservationHashDiscrepancies => _observationHashDiscrepancies;
        public int VoluntaryEndTurnsReplayed => _voluntaryEndTurnsReplayed;
        public ReplayComparisonReport FinalReport => _finalReport;
        public IReadOnlyList<ReplaySeasonScoreboard> ReplayedScores => _replayedScores;
        public IReadOnlyList<ReplayFeedEvent> EventFeed => _eventFeed;
        public IReadOnlyList<ReplaySeasonSummaryItem> SeasonSummaries => _seasonSummaries ?? (_seasonSummaries = ReplaySeasonSummaryBuilder.Build(_bundle));

        public ReplayRequestRecord CurrentRequestRecord
            => _currentRequestIndex >= 0 && _currentRequestIndex < _bundle.Requests.Count
                ? _bundle.Requests[_currentRequestIndex]
                : (_bundle.Requests.Count > 0 ? _bundle.Requests[_bundle.Requests.Count - 1] : null);

        public string CurrentPersonaId
        {
            get
            {
                if (_bundle.Summary?.Turns != null)
                {
                    foreach (var t in _bundle.Summary.Turns)
                        if (t.Season == _currentSeason && string.Equals(t.SeatId, _currentSeatId, StringComparison.Ordinal))
                            return t.PersonaId;
                    foreach (var t in _bundle.Summary.Turns)
                        if (string.Equals(t.SeatId, _currentSeatId, StringComparison.Ordinal) && !string.IsNullOrEmpty(t.PersonaId))
                            return t.PersonaId;
                }
                return "unrecorded";
            }
        }

        public ReplaySession(ReplayBundle bundle, MapData hostMap = null)
        {
            _bundle = bundle ?? throw new ArgumentNullException(nameof(bundle));
            bool isLegacyDemo = _bundle.Summary != null && string.Equals(_bundle.Summary.RunId, ReplayLoader.VerifiedDemoRunId, StringComparison.Ordinal);
            if (!isLegacyDemo && _bundle.Bootstrap == null)
                throw new InvalidOperationException($"ReplaySession requires a verified replay bootstrap for non-legacy run '{_bundle.Summary?.RunId ?? "unknown"}'.");
            _hostMap = hostMap;
            PauseOnDivergence = _bundle.Bootstrap != null;
            Reset();
        }

        public void Reset()
        {
            _state = ReplayState.NotStarted; _stepTimer = 0f; _currentRequestIndex = 0;
            _currentTurnToken = 0; _currentSeatId = null; _turnOpen = false; _isCompleted = false;
            _hasDivergence = false; _divergenceSummary = null; _isIncompleteRun = false; _stopReason = null;

            ReplayBootstrapFeatures bf = _bundle.Bootstrap?.Features;
            _allowAttack = (bf != null && bf.Attack)
                || (_bundle.Summary?.AttackEnabled == true)
                || (_bundle.Requests.Count > 0 && PromptBuilder.IsAttackPromptVersion(_bundle.Requests[0].PromptVersion));
            _allowResearch = (bf != null && bf.Research)
                || (_bundle.Summary?.ResearchEnabled == true)
                || (_bundle.Requests.Count > 0 && PromptBuilder.IsResearchPromptVersion(_bundle.Requests[0].PromptVersion));
            _allowDiplomacy = (bf != null && bf.Diplomacy)
                || (_bundle.Summary?.DiplomacyEnabled == true)
                || (_bundle.Requests.Count > 0 && PromptBuilder.IsDiplomacyPromptVersion(_bundle.Requests[0].PromptVersion));
            _allowGarrisonConversion = (bf != null && bf.GarrisonConversion)
                || (_bundle.Summary?.GarrisonConversionEnabled == true)
                || (_bundle.Requests.Count > 0 && PromptBuilder.IsGarrisonConversionPromptVersion(_bundle.Requests[0].PromptVersion));

            _totalSeasons = _bundle.Summary != null ? _bundle.Summary.SeasonsPlayed : 0;
            _totalRequests = _bundle.Requests.Count;
            _expectedAcceptedActions = 0; _expectedRejectedActions = 0; _expectedVoluntaryEndTurns = 0;
            _expectedEngineRejections = 0; _expectedInvalidResponses = 0; _expectedProviderFailures = 0;
            for (int i = 0; i < _bundle.Requests.Count; i++)
            {
                var req = _bundle.Requests[i];
                if (!string.IsNullOrEmpty(req.InquiryTool) || IsDeliberationNoteRecord(req)) continue;
                if (IsEndTurnRecord(req, _allowAttack, _allowResearch, _allowDiplomacy, _allowGarrisonConversion))
                {
                    if (req.ResultOk) _expectedVoluntaryEndTurns++;
                }
                else
                {
                    if (req.ResultOk) _expectedAcceptedActions++;
                    else
                    {
                        _expectedRejectedActions++;
                        if (req.IsProviderFailure) _expectedProviderFailures++;
                        else if (!req.ParseOk) _expectedInvalidResponses++;
                        else _expectedEngineRejections++;
                    }
                }
            }

            _observationHashesMatched = 0; _observationHashMismatches = 0; _observationHashDiscrepancies.Clear();
            _acceptedActionsReplayed = 0; _rejectedActionsReplayed = 0; _unexpectedRejections = 0;
            _engineRejectionsReplayed = 0; _invalidResponsesReplayed = 0; _providerFailuresReplayed = 0;
            _unexpectedAcceptances = 0; _rejectionReasonMismatches = 0; _actionOutcomeMismatches = 0; _actionDiscrepancies.Clear();
            _voluntaryEndTurnsReplayed = 0; _replayedScores.Clear(); _eventFeed.Clear(); _finalReport = null; _seasonSummaries = null;

            VisibleMarchRuleResolution rule = ResolveVisibleMarchRuleInfo(_bundle);
            _llmVisibleMarchRoutes = rule.LlmHostVisibleRoutes;
            _establishedSharedVisibleMarchRoutes = rule.EstablishedSharedRule;
            _visibleMarchRuleProvenance = rule.Provenance;
            _visibleMarchRuleEstablished = rule.RuleEstablished;
            _visibleMarchRuleNote = rule.Note;

            AssaultRatioRuleResolution assaultRule = ResolveAssaultRatioRuleInfo(_bundle);
            _assaultTroopRatioProvenance = assaultRule.Provenance;
            _assaultTroopRatioRuleEstablished = assaultRule.RuleEstablished;
            _establishedAssaultTroopRatio = assaultRule.EstablishedRule;
            _assaultTroopRatioRuleNote = assaultRule.Note;

            if (_bundle.Bootstrap != null)
            {
                _engine = ReplayBootstrapLoader.CreateEngine(_bundle.Bootstrap);
                _engine.State.RequireVisibleMarchRoutes = rule.SharedEngineRule;
                _engine.State.RequireAssaultTroopRatio = assaultRule.SharedEngineRule;
                ReplayedVisibleMarchRoutes = _engine.State.RequireVisibleMarchRoutes;
                ReplayedAssaultTroopRatio = _engine.State.RequireAssaultTroopRatio;
                _currentSeason = _engine.State.Season;
                return;
            }

            int seed = _bundle.Summary != null && _bundle.Summary.Seed != 0 ? _bundle.Summary.Seed : 12345;
            string profileKey = _bundle.Summary?.BalanceProfileKey ?? "easy-research";
            if (!BalanceProfile.TryGet(profileKey, out BalanceProfile profile)) profile = BalanceProfile.EasyResearch;

            string gameId = "match-20261109T082434772-7e66ac10ae8c46fcbc94eea45d66cb53";
            _engine = new GameEngine(seed, gameId, profile);
            _engine.State.RequireVisibleMarchRoutes = rule.SharedEngineRule;
            _engine.State.RequireAssaultTroopRatio = assaultRule.SharedEngineRule;
            ReplayedVisibleMarchRoutes = _engine.State.RequireVisibleMarchRoutes;
            ReplayedAssaultTroopRatio = _engine.State.RequireAssaultTroopRatio;
            _engine.State.Map = _hostMap ?? BuildDefaultStaggeredHexMap(26, 17);

            _engine.State.Factions.Add(new FactionState { SeatId = "seat-0", Name = "Wei", Controller = ControllerType.Llm, Prestige = 50 });
            _engine.State.Factions.Add(new FactionState { SeatId = "seat-1", Name = "Shu", Controller = ControllerType.Llm, Prestige = 50 });

            var cityWei = new CityState { Id = "city-wei", Name = "Wei Capital", X = 3, Z = 8, OwnerSeatId = "seat-0" };
            _engine.Balance.ApplyStartingResources(cityWei);
            _engine.State.Cities.Add(cityWei);

            var cityShu = new CityState { Id = "city-shu", Name = "Chengdu", X = 22, Z = 8, OwnerSeatId = "seat-1" };
            _engine.Balance.ApplyStartingResources(cityShu);
            _engine.State.Cities.Add(cityShu);

            _engine.State.Cities.Add(new CityState { Id = "city-south", Name = "Southern City", X = 12, Z = 2, OwnerSeatId = null, Population = 3000, Food = 200, Gold = 100 });
            _engine.State.WatchTowers.Add(new WatchTowerState { Id = "watch-tower-1", Name = "Watch Tower", X = 6, Z = 2, OwnerSeatId = null });

            void AddArmy(string id, string seat, int x, int z) => _engine.State.Armies.Add(new ArmyState {
                Id = id, Name = seat == "seat-0" ? "Wei Army" : "Shu Army", OwnerSeatId = seat, Troops = BalanceConfig.StartFieldArmyTroops, X = x, Z = z
            });
            AddArmy("army-wei-1", "seat-0", 2, 6); AddArmy("army-wei-2", "seat-0", 2, 8);
            AddArmy("army-wei-3", "seat-0", 2, 10); AddArmy("army-wei-4", "seat-0", 4, 12);
            AddArmy("army-shu-1", "seat-1", 23, 6); AddArmy("army-shu-2", "seat-1", 23, 8);
            AddArmy("army-shu-3", "seat-1", 23, 10); AddArmy("army-shu-4", "seat-1", 21, 4);

            _engine.BeginSeason();
            _currentSeason = _engine.State.Season;
        }

        public void Play() { if (!_isCompleted) _state = ReplayState.Playing; }
        public void Pause() { if (_state == ReplayState.Playing) _state = ReplayState.Paused; }
        public void SetSpeed(float multiplier) { if (multiplier > 0.05f) _speed = multiplier; }

        public void Tick(float deltaTime)
        {
            if (_state != ReplayState.Playing || _isCompleted) return;
            float interval = BaseStepInterval / Math.Max(0.1f, _speed);
            _stepTimer += deltaTime;
            while (_stepTimer >= interval && _state == ReplayState.Playing && !_isCompleted)
            {
                _stepTimer -= interval;
                StepRequest();
            }
        }

        public bool StepRequest()
        {
            if (_isCompleted || _currentRequestIndex >= _bundle.Requests.Count)
            {
                if (!_isCompleted) FinishReplay();
                return false;
            }

            ReplayRequestRecord req = _bundle.Requests[_currentRequestIndex];
            if (!_turnOpen)
            {
                _currentTurnToken = _engine.BeginSeatTurn(req.SeatId);
                _currentSeatId = req.SeatId;
                _turnOpen = true;
            }

            Observation obs = _engine.Observe(req.SeatId);
            // A recorded diplomacy run has its player-written text projected out of the dispatched
            // observation JSON, so the same projection is applied here before hashing and parsing.
            bool diplomacy = _allowDiplomacy || PromptBuilder.IsDiplomacyPromptVersion(req.PromptVersion);
            if (diplomacy) obs = PromptBuilder.ProjectDiplomacyObservation(obs);

            bool observationDiverged = false;
            bool isRich = PromptBuilder.IsRichPromptVersion(req.PromptVersion);
            if (isRich && !string.IsNullOrEmpty(req.ObservationHash) && !req.RecordedObservationEventsIncluded.HasValue)
            {
                _observationHashMismatches++;
                string msg = $"Req {req.TotalRequests} (S{req.Season} T{req.TurnToken} R{req.RequestIndex} {req.SeatId}): missing recorded observation event truncation decision (context_report) for rich prompt '{req.PromptVersion}'.";
                if (_observationHashDiscrepancies.Count < 20) _observationHashDiscrepancies.Add(msg);
                RecordDivergence(msg);
                observationDiverged = true;
            }
            else
            {
                int eventCap = req.RecordedObservationEventsIncluded ?? PromptBuilder.MaxPromptObservationEvents;
                string obsJson = PromptBuilder.BoundedObservationJson(obs, eventCap);
                string obsHash = Hash(obsJson);
                if (string.Equals(obsHash, req.ObservationHash, StringComparison.Ordinal)) _observationHashesMatched++;
                else
                {
                    _observationHashMismatches++;
                    string msg;
                    if (_observationHashDiscrepancies.Count < 20)
                    {
                        string expShort = string.IsNullOrEmpty(req.ObservationHash) ? "none" : (req.ObservationHash.Length >= 8 ? req.ObservationHash.Substring(0, 8) : req.ObservationHash);
                        msg = $"Req {req.TotalRequests} (S{req.Season} T{req.TurnToken} R{req.RequestIndex} {req.SeatId}): hash mismatch actual={obsHash.Substring(0, 8)}... exp={expShort}...";
                        _observationHashDiscrepancies.Add(msg);
                    }
                    else
                    {
                        msg = $"Req {req.TotalRequests} (S{req.Season} T{req.TurnToken} R{req.RequestIndex} {req.SeatId}): observation hash mismatch";
                    }
                    if (!string.IsNullOrEmpty(req.ObservationHash))
                    {
                        RecordDivergence(msg);
                        observationDiverged = true;
                    }
                }
            }

            if (observationDiverged && PauseOnDivergence)
            {
                _state = ReplayState.Paused;
                return false;
            }

            bool isInquiry = !string.IsNullOrEmpty(req.InquiryTool);
            bool isNote = !isInquiry && IsDeliberationNoteRecord(req);
            string reqController = EffectiveControllerType(req);
            bool isLlmController = string.Equals(reqController, ControllerType.Llm, StringComparison.Ordinal);

            ActionCommand parsedCmd = null;
            bool isEndTurn = false;
            bool replayedParseOk = false;
            string parseError = null;
            string actType;
            string actSummary;
            bool replayedOk = false;

            if (isInquiry)
            {
                actType = req.InquiryTool;
                actSummary = $"Inquiry: {req.InquiryTool}";
                replayedOk = true;
            }
            else if (isNote)
            {
                actType = "note";
                actSummary = "Deliberation note";
                replayedOk = true;
            }
            else
            {
                ParseReplayRecordAction(req,
                    _allowAttack || PromptBuilder.IsAttackPromptVersion(req.PromptVersion),
                    _allowResearch || PromptBuilder.IsResearchPromptVersion(req.PromptVersion),
                    diplomacy,
                    _allowGarrisonConversion || PromptBuilder.IsGarrisonConversionPromptVersion(req.PromptVersion),
                    isLlmController, out parsedCmd, out isEndTurn, out replayedParseOk,
                    out parseError, out actType, out actSummary);

                if (isEndTurn)
                {
                    bool endResOk = false;
                    if (_turnOpen && _currentTurnToken != 0)
                    {
                        _engine.EndSeatTurn(_currentTurnToken);
                        _turnOpen = false;
                        endResOk = true;
                    }

                    if (req.ParseOk && req.ResultOk && endResOk)
                    {
                        replayedOk = true;
                        _voluntaryEndTurnsReplayed++;
                    }
                    else
                    {
                        _actionOutcomeMismatches++;
                        _actionDiscrepancies.Add($"Req {req.TotalRequests} (Season {req.Season}): voluntary end_turn outcome mismatch (req.ParseOk={req.ParseOk}, req.ResultOk={req.ResultOk}, endResOk={endResOk})");
                    }
                }
                else
                {
                    string replayedError = null;
                    if (!replayedParseOk || parsedCmd == null)
                    {
                        replayedOk = false;
                        replayedError = parseError ?? "parse_failure";
                    }
                    else
                    {
                        FactionState fac = _engine.State.FindFaction(req.SeatId);
                        if (fac != null) fac.Controller = reqController;
                        parsedCmd.ControllerType = reqController;
                        if (isLlmController && _llmVisibleMarchRoutes && !_engine.State.RequireVisibleMarchRoutes
                            && !MovementPlanner.AllowsVisibleRoute(_engine, req.SeatId, parsedCmd))
                        {
                            replayedOk = false;
                            replayedError = FeedbackPolicy.DestinationNotReachable;
                        }
                        else
                        {
                            ValidationResult res = _engine.SubmitAs(req.SeatId, _currentTurnToken, parsedCmd);
                            replayedOk = res.Ok;
                            if (!res.Ok) replayedError = res.Error ?? "validation_failure";
                        }
                    }

                    if (req.ParseOk && req.ResultOk && replayedOk) _acceptedActionsReplayed++;
                    else if ((!req.ParseOk || !req.ResultOk) && !replayedOk)
                    {
                        bool categoryMatch = req.ParseOk == replayedParseOk;
                        bool reasonMatch = true;
                        if (req.ParseOk && replayedParseOk && !string.IsNullOrEmpty(req.ResultError))
                        {
                            reasonMatch = string.Equals(replayedError, req.ResultError, StringComparison.Ordinal)
                                || (isLlmController && string.Equals(
                                    FeedbackPolicy.ForModel(replayedError, parsedCmd, obs, _engine.State.RequireAssaultTroopRatio),
                                    req.ResultError, StringComparison.Ordinal));
                        }

                        if (categoryMatch && reasonMatch)
                        {
                            _rejectedActionsReplayed++;
                            if (req.IsProviderFailure) _providerFailuresReplayed++;
                            else if (!req.ParseOk) _invalidResponsesReplayed++;
                            else _engineRejectionsReplayed++;
                        }
                        else
                        {
                            _rejectionReasonMismatches++; _actionOutcomeMismatches++;
                            _actionDiscrepancies.Add($"Req {req.TotalRequests} (Season {req.Season}): rejection category/reason mismatch (recParse={req.ParseOk}, actParse={replayedParseOk}, recErr='{req.ResultError}', actErr='{replayedError}')");
                        }
                    }
                    else if (req.ParseOk && req.ResultOk && !replayedOk)
                    {
                        _unexpectedRejections++; _actionOutcomeMismatches++;
                        _actionDiscrepancies.Add($"Req {req.TotalRequests} (Season {req.Season}): unexpected rejection ({replayedError})");
                    }
                    else if ((!req.ParseOk || !req.ResultOk) && replayedOk)
                    {
                        _unexpectedAcceptances++; _actionOutcomeMismatches++;
                        _actionDiscrepancies.Add($"Req {req.TotalRequests} (Season {req.Season}): unexpected acceptance");
                    }
                }
            }

            string resText = isInquiry ? "inquiry" : (isNote ? "note" : (isEndTurn ? "end_turn"
                : (replayedOk ? "accepted"
                    : (req.IsProviderFailure ? "provider failure"
                        : (req.IsEmptyResponse ? "empty response"
                            : (!replayedParseOk ? "invalid response" : "rejected"))))));
            string facName = "unknown";
            if (_engine?.State?.Factions != null)
            {
                foreach (var f in _engine.State.Factions)
                    if (string.Equals(f.SeatId, req.SeatId, StringComparison.Ordinal)) { facName = f.Name; break; }
            }

            var feedEvt = new ReplayFeedEvent
            {
                Sequence = _eventFeed.Count + 1,
                RunId = req.RunId,
                SeatId = req.SeatId,
                Faction = facName,
                Season = req.Season,
                RequestIndex = req.RequestIndex,
                ActionType = actType,
                ActionSummary = actSummary,
                CommandJson = req.CommandJson,
                ResultOk = isEndTurn ? (req.ParseOk && req.ResultOk) : replayedOk,
                ResultText = resText,
                Explanation = req.RecordedExplanation,
                ObservationHash = req.ObservationHash
            };
            _eventFeed.Add(feedEvt);
            OnEventReplayed?.Invoke(feedEvt);

            _currentRequestIndex++;
            if (_currentRequestIndex >= _bundle.Requests.Count)
            {
                FinishReplay();
                if (_hasDivergence && PauseOnDivergence) _state = ReplayState.Paused;
                return true;
            }

            ReplayRequestRecord nextReq = _bundle.Requests[_currentRequestIndex];
            if (nextReq.Season != _currentSeason)
            {
                if (_turnOpen && _currentTurnToken != 0) { _engine.EndSeatTurn(_currentTurnToken); _turnOpen = false; }
                _engine.EndSeason();
                RecordScoreboard(_currentSeason);
                _engine.BeginSeason();
                _currentSeason = _engine.State.Season;
                _currentTurnToken = _engine.BeginSeatTurn(nextReq.SeatId);
                _currentSeatId = nextReq.SeatId;
                _turnOpen = true;
            }
            else if (nextReq.TurnToken != _currentTurnToken)
            {
                if (_turnOpen && _currentTurnToken != 0) { _engine.EndSeatTurn(_currentTurnToken); _turnOpen = false; }
                _currentTurnToken = _engine.BeginSeatTurn(nextReq.SeatId);
                _currentSeatId = nextReq.SeatId;
                _turnOpen = true;
            }

            if (_hasDivergence && PauseOnDivergence) _state = ReplayState.Paused;
            return true;
        }

        private void RecordDivergence(string msg)
        {
            _hasDivergence = true;
            if (string.IsNullOrEmpty(_divergenceSummary)) _divergenceSummary = msg;
        }

        public void StepSeason()
        {
            if (_isCompleted) return;
            int targetSeason = _currentSeason;
            while (!_isCompleted && _currentSeason == targetSeason && !(_hasDivergence && PauseOnDivergence)) StepRequest();
        }

        public void RunToCompletion()
        {
            while (!_isCompleted && !(_hasDivergence && PauseOnDivergence)) StepRequest();
        }

        public List<ReplaySeatScore> CurrentScores()
        {
            var list = new List<ReplaySeatScore>();
            if (_engine == null) return list;
            foreach (var f in _engine.State.Factions)
            {
                var s = new ReplaySeatScore { SeatId = f.SeatId, Faction = f.Name };
                foreach (var c in _engine.State.Cities)
                    if (string.Equals(c.OwnerSeatId, f.SeatId, StringComparison.Ordinal))
                    { s.Cities++; s.Gold += c.Gold; s.Food += c.Food; s.Population += c.Population; s.Garrison += c.Garrison; }
                list.Add(s);
            }
            return list;
        }

        private void FinishReplay()
        {
            if (_turnOpen && _currentTurnToken != 0) { _engine.EndSeatTurn(_currentTurnToken); _turnOpen = false; }
            bool finalSeasonCompleted = IsFinalSeasonCompleted(_currentSeason);
            if (finalSeasonCompleted)
            {
                _engine.EndSeason();
                RecordScoreboard(_currentSeason);
            }
            else
            {
                _isIncompleteRun = true;
                _stopReason = _bundle.Summary?.StopReason;
            }
            _isCompleted = true;
            _state = ReplayState.Completed;
            GenerateComparisonReport();
        }

        private bool IsFinalSeasonCompleted(int season)
        {
            if (_bundle.Summary == null) return true;
            if (_bundle.Summary.Scoreboards != null)
            {
                foreach (var sb in _bundle.Summary.Scoreboards)
                    if (sb.Season == season && sb.Scores != null && sb.Scores.Count > 0) return true;
            }
            string st = _bundle.Summary.State ?? "";
            bool stoppedEarly = string.Equals(st, "Paused", StringComparison.OrdinalIgnoreCase)
                || string.Equals(st, "Stopped", StringComparison.OrdinalIgnoreCase)
                || string.Equals(st, "Failed", StringComparison.OrdinalIgnoreCase);
            return !stoppedEarly && _bundle.Summary.SeasonsPlayed >= season;
        }

        private void RecordScoreboard(int season) => _replayedScores.Add(new ReplaySeasonScoreboard { Season = season, Scores = CurrentScores() });

        private static bool IsDeliberationNoteRecord(ReplayRequestRecord req)
        {
            if (req == null || !string.IsNullOrEmpty(req.InquiryTool) || !req.ParseOk || req.ResultRecorded)
                return false;
            if (string.IsNullOrWhiteSpace(req.RawModelText)) return false;
            if (!StrictJson.TryParse(req.RawModelText, out JsonNode root, out _)) return false;
            return root is JsonObject obj && obj.TryGet("note", out _);
        }

        private static bool IsEndTurnRecord(ReplayRequestRecord req, bool allowAttack, bool allowResearch,
            bool allowDiplomacy, bool allowGarrisonConversion)
        {
            if (req == null || !req.ParseOk || !string.IsNullOrEmpty(req.InquiryTool)) return false;
            bool isLlm = !IsNonLlmRecord(req);
            if (!isLlm && !string.IsNullOrEmpty(req.CommandJson))
                return TryParseCommandJson(req.CommandJson, out ActionCommand cmd, out _)
                    && string.Equals(cmd.Type, "end_turn", StringComparison.Ordinal);
            // Same feature flags as the replay step's own parse, derived from the bootstrap and record identity.
            ParseResult p = LlmActionParser.Parse(req.RawModelText,
                allowAttack || PromptBuilder.IsAttackPromptVersion(req.PromptVersion),
                allowResearch || PromptBuilder.IsResearchPromptVersion(req.PromptVersion),
                allowDiplomacy || PromptBuilder.IsDiplomacyPromptVersion(req.PromptVersion),
                allowGarrisonConversion || PromptBuilder.IsGarrisonConversionPromptVersion(req.PromptVersion),
                PromptBuilder.IsUnitTypesPromptVersion(req.PromptVersion));
            return p.Ok && p.Action != null && p.Action.EndTurn;
        }

        private static string Hash(string text)
        {
            byte[] hash;
            using (SHA256 sha = SHA256.Create())
                hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text ?? ""));
            var sb = new StringBuilder(64);
            foreach (byte b in hash) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }
}
