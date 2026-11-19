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
        private int _expectedAcceptedActions, _expectedRejectedActions, _expectedInvalidReplies, _expectedVoluntaryEndTurns;
        private int _currentRequestIndex, _currentSeason, _currentTurnToken;
        private string _currentSeatId;
        private bool _turnOpen, _isCompleted;
        private bool _allowAttack, _allowResearch, _allowDiplomacy, _visibleMarchRoutes;
        private bool _hasDivergence, _isIncompleteRun;
        private string _divergenceSummary, _stopReason;
        public bool PauseOnDivergence = true;

        private int _observationHashesMatched, _observationHashMismatches;
        private readonly List<string> _observationHashDiscrepancies = new List<string>();

        private int _acceptedActionsReplayed, _rejectedActionsReplayed, _invalidRepliesReplayed, _unexpectedRejections, _unexpectedAcceptances;
        private int _rejectionReasonMismatches, _actionOutcomeMismatches;
        private readonly List<string> _actionDiscrepancies = new List<string>();
        private int _voluntaryEndTurnsReplayed;

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
        public int TotalSeasons => _totalSeasons;
        public int TotalRequests => _totalRequests;
        public int CurrentRequestIndex => _currentRequestIndex;
        public int CurrentSeason => _currentSeason;
        public int CurrentTurnToken => _currentTurnToken;
        public string CurrentSeatId => _currentSeatId;
        public int ExpectedAcceptedActions => _expectedAcceptedActions;
        public int ExpectedRejectedActions => _expectedRejectedActions;
        public int ExpectedInvalidReplies => _expectedInvalidReplies;
        public int ExpectedVoluntaryEndTurns => _expectedVoluntaryEndTurns;
        public int AcceptedActionsReplayed => _acceptedActionsReplayed;
        public int RejectedActionsReplayed => _rejectedActionsReplayed;
        public int InvalidRepliesReplayed => _invalidRepliesReplayed;
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

        public string CurrentPersonaId => GetPersonaIdForSeatAndSeason(_currentSeatId, _currentSeason);

        /// <summary>
        /// Returns the recorded persona for a specific <see cref="ReplayRequestRecord"/> using only
        /// direct recorded evidence for that exact seat and season.
        /// Returns "unknown" when that evidence is absent; never infers from faction name,
        /// prompt-version wording, or another season's/seat's turn.
        /// </summary>
        public string GetPersonaIdForRequest(ReplayRequestRecord rec)
        {
            if (rec == null || string.IsNullOrEmpty(rec.SeatId))
                return "unknown";
            return GetPersonaIdForSeatAndSeason(rec.SeatId, rec.Season);
        }

        private string GetPersonaIdForSeatAndSeason(string seatId, int season)
        {
            if (string.IsNullOrEmpty(seatId) || season <= 0)
                return "unknown";

            if (_bundle.Summary?.Turns != null)
            {
                foreach (var t in _bundle.Summary.Turns)
                {
                    if (t.Season == season && string.Equals(t.SeatId, seatId, StringComparison.Ordinal))
                        return !string.IsNullOrEmpty(t.PersonaId) ? t.PersonaId : "unknown";
                }
            }

            return "unknown";
        }

        /// <summary>
        /// Formats the recorded action label for UI/report display. Never labels an empty response
        /// as "end_turn"; only reports end_turn when the recorded response contains a valid end-turn command.
        /// </summary>
        public string FormatRecordedActionLabel(ReplayRequestRecord rec)
        {
            if (rec == null)
                return "unknown";

            if (!string.IsNullOrEmpty(rec.InquiryTool))
                return !string.IsNullOrWhiteSpace(rec.RawModelText) ? rec.RawModelText : $"inquiry:{rec.InquiryTool}";

            if (IsDeliberationNoteRecord(rec))
                return !string.IsNullOrWhiteSpace(rec.RawModelText) ? rec.RawModelText : "note (deliberation)";

            if (string.IsNullOrWhiteSpace(rec.RawModelText))
            {
                string finish = !string.IsNullOrWhiteSpace(rec.FinishReason) ? rec.FinishReason.Trim() : "unknown";
                return $"empty recorded response (outcome unknown, finish_reason={finish})";
            }

            if (IsEndTurnRecord(rec, _bundle.Bootstrap?.Features ?? _bundle.ScenarioSetup?.Features))
                return rec.RawModelText;

            return rec.RawModelText;
        }

        public bool IsFakeScriptedSession()
            => (_bundle.ScenarioSetup != null && _bundle.ScenarioSetup.IsFakeScripted)
               || string.Equals(_bundle.Summary?.Provider, "fake", StringComparison.OrdinalIgnoreCase);

        public ReplaySession(ReplayBundle bundle, MapData hostMap = null)
        {
            _bundle = bundle ?? throw new ArgumentNullException(nameof(bundle));
            bool isLegacyDemo = _bundle.Summary != null && string.Equals(_bundle.Summary.RunId, ReplayLoader.VerifiedDemoRunId, StringComparison.Ordinal);
            if (!isLegacyDemo && _bundle.Bootstrap == null && _bundle.ScenarioSetup == null)
                throw new InvalidOperationException($"ReplaySession requires a replay bootstrap or scenario.json fallback for run '{_bundle.Summary?.RunId ?? "unknown"}'.");
            _hostMap = hostMap;
            Reset();
        }

        public void Reset()
        {
            _state = ReplayState.NotStarted; _stepTimer = 0f; _currentRequestIndex = 0;
            _currentTurnToken = 0; _currentSeatId = null; _turnOpen = false; _isCompleted = false;
            _hasDivergence = false; _divergenceSummary = null; _isIncompleteRun = false; _stopReason = null;

            _totalSeasons = _bundle.Summary != null ? _bundle.Summary.SeasonsPlayed : 0;
            _totalRequests = _bundle.Requests.Count;
            _expectedAcceptedActions = 0; _expectedRejectedActions = 0; _expectedInvalidReplies = 0; _expectedVoluntaryEndTurns = 0;
            var activeFeatures = _bundle.Bootstrap?.Features ?? _bundle.ScenarioSetup?.Features;
            for (int i = 0; i < _bundle.Requests.Count; i++)
            {
                var req = _bundle.Requests[i];
                if (!string.IsNullOrEmpty(req.InquiryTool)) continue;
                if (IsDeliberationNoteRecord(req)) continue;
                if (IsEndTurnRecord(req, activeFeatures)) { if (req.ResultOk) _expectedVoluntaryEndTurns++; }
                else if (!req.ParseOk) _expectedInvalidReplies++;
                else if (req.ResultOk) _expectedAcceptedActions++;
                else _expectedRejectedActions++;
            }

            _observationHashesMatched = 0; _observationHashMismatches = 0; _observationHashDiscrepancies.Clear();
            _acceptedActionsReplayed = 0; _rejectedActionsReplayed = 0; _invalidRepliesReplayed = 0; _unexpectedRejections = 0;
            _unexpectedAcceptances = 0; _rejectionReasonMismatches = 0; _actionOutcomeMismatches = 0; _actionDiscrepancies.Clear();
            _voluntaryEndTurnsReplayed = 0; _replayedScores.Clear(); _eventFeed.Clear(); _finalReport = null; _seasonSummaries = null;

            if (_bundle.Bootstrap != null)
            {
                var f = _bundle.Bootstrap.Features;
                _allowAttack = f != null && f.Attack;
                _allowResearch = f != null && f.Research;
                _allowDiplomacy = f != null && f.Diplomacy;
                _visibleMarchRoutes = f != null && f.VisibleMarchRoutes;
                _engine = ReplayBootstrapLoader.CreateEngine(_bundle.Bootstrap);
                _currentSeason = _engine.State.Season;
                return;
            }

            if (_bundle.ScenarioSetup != null)
            {
                var f = _bundle.ScenarioSetup.Features;
                _allowAttack = f != null && f.Attack;
                _allowResearch = f != null && f.Research;
                _allowDiplomacy = f != null && f.Diplomacy;
                _visibleMarchRoutes = f != null && f.VisibleMarchRoutes;
                _engine = ReplayScenarioFallback.CreateEngine(_bundle.ScenarioSetup, _bundle.Summary, _hostMap);
                _currentSeason = _engine.State.Season;
                return;
            }

            _allowAttack = false;
            _allowResearch = false;
            _allowDiplomacy = _bundle.Requests.Count > 0 && PromptBuilder.IsDiplomacyPromptVersion(_bundle.Requests[0].PromptVersion);
            _visibleMarchRoutes = false;

            int seed = _bundle.Summary != null && _bundle.Summary.Seed != 0 ? _bundle.Summary.Seed : 12345;
            string profileKey = _bundle.Summary?.BalanceProfileKey ?? "easy-research";
            if (!BalanceProfile.TryGet(profileKey, out BalanceProfile profile)) profile = BalanceProfile.EasyResearch;

            string gameId = "match-20261109T082434772-7e66ac10ae8c46fcbc94eea45d66cb53";
            _engine = new GameEngine(seed, gameId, profile);
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
            if (isRich && !req.RecordedObservationEventsIncluded.HasValue)
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
                    string expShort = string.IsNullOrEmpty(req.ObservationHash) ? "none" : (req.ObservationHash.Length >= 8 ? req.ObservationHash.Substring(0, 8) : req.ObservationHash);
                    string msg = $"Req {req.TotalRequests} (S{req.Season} T{req.TurnToken} R{req.RequestIndex} {req.SeatId}): hash mismatch actual={obsHash.Substring(0, 8)}... exp={expShort}...";
                    if (_observationHashDiscrepancies.Count < 20) _observationHashDiscrepancies.Add(msg);
                    RecordDivergence(msg);
                    observationDiverged = true;
                }
            }

            if (observationDiverged && PauseOnDivergence)
            {
                _state = ReplayState.Paused;
                return false;
            }

            bool isInquiry = !string.IsNullOrEmpty(req.InquiryTool);
            bool isNote = !isInquiry && IsDeliberationNoteRecord(req);
            ParseResult parsed = (isInquiry || isNote) ? null : LlmActionParser.Parse(req.RawModelText, _allowAttack, _allowResearch, diplomacy);
            bool replayedParseOk = parsed != null && parsed.Ok && parsed.Action != null;
            bool isEndTurn = !isInquiry && !isNote && replayedParseOk && parsed.Action.EndTurn;
            bool replayedOk = false;
            string replayedError = null;
            int prevEventCount = _engine?.Log?.Records?.Count ?? 0;
            ActionCommand executedCmd = null;
            int marchFromX = -1, marchFromZ = -1;

            if (isInquiry || isNote)
            {
                replayedOk = true;
            }
            else if (isEndTurn)
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
                    string msg = $"Req {req.TotalRequests} (Season {req.Season}): voluntary end_turn outcome mismatch (req.ParseOk={req.ParseOk}, req.ResultOk={req.ResultOk}, endResOk={endResOk})";
                    _actionDiscrepancies.Add(msg);
                    RecordDivergence(msg);
                }
            }
            else
            {
                if (!replayedParseOk)
                {
                    replayedOk = false;
                    replayedError = parsed.Error ?? "parse_failure";
                }
                else
                {
                    ActionCommand cmd = LlmActionParser.ToCommand(parsed.Action);
                    cmd.ControllerType = ControllerType.Llm;
                    executedCmd = cmd;
                    if (cmd.Type == ActionType.March || cmd.Type == "army_march")
                    {
                        var ma = _engine.State.FindArmy(cmd.TargetId);
                        if (ma != null)
                        {
                            marchFromX = ma.X;
                            marchFromZ = ma.Z;
                        }
                    }
                    ValidationResult res = _visibleMarchRoutes && !MovementPlanner.AllowsVisibleRoute(_engine, req.SeatId, cmd)
                        ? ValidationResult.Fail(FeedbackPolicy.DestinationNotReachable)
                        : _engine.SubmitAs(req.SeatId, _currentTurnToken, cmd);
                    replayedOk = res.Ok;
                    if (!res.Ok) replayedError = res.Error ?? "validation_failure";
                }

                if (req.ParseOk && req.ResultOk && replayedOk) _acceptedActionsReplayed++;
                else if (!req.ParseOk && !replayedOk)
                {
                    if (!replayedParseOk) _invalidRepliesReplayed++;
                    else
                    {
                        _rejectionReasonMismatches++; _actionOutcomeMismatches++;
                        string msg = $"Req {req.TotalRequests} (Season {req.Season}): invalid-reply mismatch (recParse={req.ParseOk}, actParse={replayedParseOk}, recErr='{req.ResultError}', actErr='{replayedError}')";
                        _actionDiscrepancies.Add(msg);
                        RecordDivergence(msg);
                    }
                }
                else if (req.ParseOk && !req.ResultOk && !replayedOk)
                {
                    bool categoryMatch = replayedParseOk;
                    bool reasonMatch = true;
                    if (replayedParseOk && !string.IsNullOrEmpty(req.ResultError))
                        reasonMatch = string.Equals(replayedError, req.ResultError, StringComparison.Ordinal);

                    if (categoryMatch && reasonMatch) _rejectedActionsReplayed++;
                    else
                    {
                        _rejectionReasonMismatches++; _actionOutcomeMismatches++;
                        string msg = $"Req {req.TotalRequests} (Season {req.Season}): rejection category/reason mismatch (recParse={req.ParseOk}, actParse={replayedParseOk}, recErr='{req.ResultError}', actErr='{replayedError}')";
                        _actionDiscrepancies.Add(msg);
                        RecordDivergence(msg);
                    }
                }
                else if (req.ParseOk && req.ResultOk && !replayedOk)
                {
                    _unexpectedRejections++; _actionOutcomeMismatches++;
                    string msg = $"Req {req.TotalRequests} (Season {req.Season}): unexpected rejection ({replayedError})";
                    _actionDiscrepancies.Add(msg);
                    RecordDivergence(msg);
                }
                else if ((!req.ParseOk || !req.ResultOk) && replayedOk)
                {
                    _unexpectedAcceptances++; _actionOutcomeMismatches++;
                    string msg = $"Req {req.TotalRequests} (Season {req.Season}): unexpected acceptance";
                    _actionDiscrepancies.Add(msg);
                    RecordDivergence(msg);
                }
            }

            string actType = isInquiry ? req.InquiryTool : (isNote ? "note" : (isEndTurn ? "end_turn" : (replayedParseOk ? parsed.Action.Type : "invalid")));
            string actSummary = BuildFeedActionSummary(req, isInquiry, isNote, isEndTurn, parsed, replayedOk, executedCmd, marchFromX, marchFromZ, prevEventCount);
            string resText = isInquiry ? "inquiry" : (isNote ? "note" : (isEndTurn ? "end_turn" : (replayedOk ? "accepted" : (!replayedParseOk ? "invalid" : "rejected"))));
            string facName = FactionNameOrSeat(req.SeatId);
            BuildFeedHeadlineAndOutcomes(
                req, facName, isInquiry, isNote, isEndTurn, parsed, replayedParseOk, replayedOk, replayedError,
                executedCmd, marchFromX, marchFromZ, prevEventCount, out string headline, out string outcomeDetails);

            var feedEvt = new ReplayFeedEvent
            {
                Sequence = _eventFeed.Count + 1,
                RunId = req.RunId,
                SeatId = req.SeatId,
                Faction = facName,
                PersonaId = GetPersonaIdForRequest(req),
                Season = req.Season,
                TurnToken = req.TurnToken,
                RequestIndex = req.RequestIndex,
                ExchangeId = req.ExchangeId,
                ActionType = actType,
                ActionSummary = actSummary,
                Headline = headline,
                OutcomeDetails = outcomeDetails,
                CommandJson = req.CommandJson,
                ResultOk = isInquiry || isNote || (isEndTurn ? (req.ParseOk && req.ResultOk) : replayedOk),
                ResultText = resText,
                ResultError = replayedError ?? req.ResultError,
                InquiryTool = req.InquiryTool,
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
            while (!_isCompleted && _currentSeason == targetSeason && !(_hasDivergence && PauseOnDivergence))
            {
                if (!StepRequest()) break;
            }
        }

        public void RunToCompletion()
        {
            while (!_isCompleted && !(_hasDivergence && PauseOnDivergence))
            {
                if (!StepRequest()) break;
            }
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
                {
                    if (sb.Season == season && sb.Scores != null && sb.Scores.Count > 0)
                        return true;
                }
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

        private string BuildFeedActionSummary(
            ReplayRequestRecord req, bool isInquiry, bool isNote, bool isEndTurn,
            ParseResult parsed, bool replayedOk, ActionCommand cmd,
            int marchFromX, int marchFromZ, int prevEventCount)
        {
            if (isInquiry) return $"Inquiry: {req.InquiryTool}";
            if (isNote) return "Deliberation note";
            if (isEndTurn) return "Voluntary end of turn";
            if (parsed == null || !parsed.Ok || parsed.Action == null)
                return !string.IsNullOrEmpty(req.ResultError) ? $"invalid ({req.ResultError})" : "invalid";

            var sb = new StringBuilder();
            if (replayedOk && cmd != null && (cmd.Type == ActionType.March || cmd.Type == "army_march") && marchFromX >= 0)
                sb.Append($"march {cmd.TargetId} ({marchFromX},{marchFromZ})->({cmd.ParamA},{cmd.ParamB})");
            else
                sb.Append($"{parsed.Action.Type} {parsed.Action.TargetId ?? ""}".Trim());

            if (_engine?.Log?.Records != null)
            {
                for (int i = prevEventCount; i < _engine.Log.Records.Count; i++)
                {
                    var ev = _engine.Log.Records[i];
                    if (ev.EventType == "battle_field" && ev.Payload != null)
                    {
                        ev.Payload.TryGetValue("attacker_army_id", out string att);
                        ev.Payload.TryGetValue("defender_army_id", out string def);
                        ev.Payload.TryGetValue("outcome", out string outcome);
                        sb.Append($" | battle {att} vs {def}: {outcome}");
                    }
                    else if (ev.EventType == "battle_siege" && ev.Payload != null)
                    {
                        ev.Payload.TryGetValue("army_id", out string att);
                        ev.Payload.TryGetValue("city_id", out string cid);
                        ev.Payload.TryGetValue("outcome", out string outcome);
                        sb.Append($" | siege {att} vs {cid}: {outcome}");
                    }
                    else if (ev.EventType == "army_routed" && ev.Payload != null)
                    {
                        ev.Payload.TryGetValue("army_id", out string aid);
                        ev.Payload.TryGetValue("from", out string from);
                        ev.Payload.TryGetValue("to", out string to);
                        sb.Append($" | routed {aid} {from}->{to}");
                    }
                    else if (ev.EventType == "city_captured" && ev.Payload != null)
                    {
                        ev.Payload.TryGetValue("city_id", out string cid);
                        sb.Append($" | captured {cid} -> {ev.ActorSeatId}");
                    }
                }
            }
            return sb.ToString();
        }

        private string FactionNameOrSeat(string seatId)
        {
            if (string.IsNullOrEmpty(seatId)) return "unknown";
            if (_engine?.State?.Factions != null)
            {
                foreach (var f in _engine.State.Factions)
                    if (string.Equals(f.SeatId, seatId, StringComparison.Ordinal)) return f.Name;
            }
            return seatId;
        }

        private void BuildFeedHeadlineAndOutcomes(
            ReplayRequestRecord req, string facName, bool isInquiry, bool isNote, bool isEndTurn,
            ParseResult parsed, bool replayedParseOk, bool replayedOk, string replayedError,
            ActionCommand cmd, int marchFromX, int marchFromZ, int prevEventCount,
            out string headline, out string outcomeDetails)
        {
            var outcomes = new List<string>();
            if (_engine?.Log?.Records != null)
            {
                for (int i = prevEventCount; i < _engine.Log.Records.Count; i++)
                {
                    var ev = _engine.Log.Records[i];
                    if (ev.Payload == null) continue;
                    if (ev.EventType == "battle_field")
                    {
                        ev.Payload.TryGetValue("attacker_army_id", out string attId);
                        ev.Payload.TryGetValue("defender_army_id", out string defId);
                        ev.Payload.TryGetValue("attacker_seat_id", out string attSeat);
                        ev.Payload.TryGetValue("defender_seat_id", out string defSeat);
                        ev.Payload.TryGetValue("attacker_troops_before", out string attBefore);
                        ev.Payload.TryGetValue("attacker_troops_after", out string attAfter);
                        ev.Payload.TryGetValue("defender_troops_before", out string defBefore);
                        ev.Payload.TryGetValue("defender_troops_after", out string defAfter);
                        ev.Payload.TryGetValue("outcome", out string outcome);
                        ev.Payload.TryGetValue("winner_seat_id", out string winnerSeat);
                        string attFac = !string.IsNullOrEmpty(attSeat) ? FactionNameOrSeat(attSeat) : facName;
                        string defFac = !string.IsNullOrEmpty(defSeat) ? FactionNameOrSeat(defSeat) : "unknown";
                        string winFac = !string.IsNullOrEmpty(winnerSeat) ? FactionNameOrSeat(winnerSeat) : "unknown";
                        outcomes.Add($"Battle: {attFac} ({attId}, {attBefore}->{attAfter} troops) attacks {defFac} ({defId}, {defBefore}->{defAfter} troops) | Outcome: {outcome} | Winner: {winFac}");
                    }
                    else if (ev.EventType == "battle_siege")
                    {
                        ev.Payload.TryGetValue("army_id", out string attId);
                        ev.Payload.TryGetValue("city_id", out string cid);
                        ev.Payload.TryGetValue("attacker_troops_before", out string attBefore);
                        ev.Payload.TryGetValue("attacker_troops_after", out string attAfter);
                        ev.Payload.TryGetValue("garrison_before", out string garBefore);
                        ev.Payload.TryGetValue("garrison_after", out string garAfter);
                        ev.Payload.TryGetValue("outcome", out string outcome);
                        string cityName = _engine.State.FindCity(cid)?.Name ?? cid;
                        outcomes.Add($"Siege: {facName} ({attId}, {attBefore}->{attAfter}) vs {cityName} ({cid}, garrison {garBefore}->{garAfter}) | Outcome: {outcome}");
                    }
                    else if (ev.EventType == "army_routed")
                    {
                        ev.Payload.TryGetValue("army_id", out string aid);
                        ev.Payload.TryGetValue("city_id", out string cid);
                        ev.Payload.TryGetValue("from", out string from);
                        ev.Payload.TryGetValue("to", out string to);
                        ev.Payload.TryGetValue("troops", out string troops);
                        string cityName = !string.IsNullOrEmpty(cid) ? (_engine.State.FindCity(cid)?.Name ?? cid) : "unknown";
                        string ownerFac = FactionNameOrSeat(ev.ActorSeatId);
                        outcomes.Add($"Routed: {ownerFac} {aid} routed {from} -> {cityName} {to} with {troops} troops");
                    }
                    else if (ev.EventType == "city_captured")
                    {
                        ev.Payload.TryGetValue("city_id", out string cid);
                        string cityName = _engine.State.FindCity(cid)?.Name ?? cid;
                        string newOwner = FactionNameOrSeat(ev.ActorSeatId);
                        outcomes.Add($"Captured: {newOwner} ({ev.ActorSeatId}) captured {cityName} ({cid})");
                    }
                }
            }
            outcomeDetails = outcomes.Count > 0 ? string.Join("\n", outcomes) : "";

            string seatTag = !string.IsNullOrEmpty(req.SeatId) ? $"{facName} ({req.SeatId})" : facName;
            if (isInquiry) { headline = $"{seatTag} ran manual lookup: {req.InquiryTool}"; return; }
            if (isNote) { headline = $"{seatTag} recorded a deliberation note"; return; }
            if (isEndTurn) { headline = $"{seatTag} ended its turn"; return; }
            if (!replayedParseOk || parsed == null || !parsed.Ok || parsed.Action == null)
            {
                string err = !string.IsNullOrEmpty(replayedError) ? replayedError : (!string.IsNullOrEmpty(req.ResultError) ? req.ResultError : "parse_failure");
                headline = $"{seatTag} submitted an invalid reply ({err})";
                return;
            }
            if (!replayedOk)
            {
                string err = !string.IsNullOrEmpty(replayedError) ? replayedError : "rejected";
                headline = $"{seatTag} {parsed.Action.Type} rejected ({err})";
                return;
            }

            if (cmd != null && (cmd.Type == ActionType.March || cmd.Type == "army_march"))
            {
                var army = _engine?.State?.FindArmy(cmd.TargetId);
                string armyName = army != null && !string.IsNullOrEmpty(army.Name) ? $"{army.Name} ({cmd.TargetId})" : cmd.TargetId;
                var destCity = _engine?.State?.CityAt(cmd.ParamA, cmd.ParamB);
                string destText = destCity != null ? $"{destCity.Name} ({cmd.ParamA},{cmd.ParamB})" : $"({cmd.ParamA},{cmd.ParamB})";
                string fromText = marchFromX >= 0 ? $"from ({marchFromX},{marchFromZ}) " : "";
                headline = $"{seatTag} marched {armyName} {fromText}to {destText}";
                return;
            }

            string targetName = cmd?.TargetId ?? parsed.Action.TargetId ?? "";
            var targetCity = !string.IsNullOrEmpty(targetName) ? _engine?.State?.FindCity(targetName) : null;
            if (targetCity != null) targetName = $"{targetCity.Name} ({targetCity.Id})";
            headline = string.IsNullOrEmpty(targetName)
                ? $"{seatTag}: {parsed.Action.Type}"
                : $"{seatTag}: {parsed.Action.Type} -> {targetName}";
        }

        private static bool IsEndTurnRecord(ReplayRequestRecord req, ReplayBootstrapFeatures features)
        {
            if (req == null || !string.IsNullOrEmpty(req.InquiryTool)) return false;
            bool atk = features != null && features.Attack;
            bool res = features != null && features.Research;
            bool dip = (features != null && features.Diplomacy) || PromptBuilder.IsDiplomacyPromptVersion(req.PromptVersion);
            ParseResult p = LlmActionParser.Parse(req.RawModelText, atk, res, dip);
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
