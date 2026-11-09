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

        private int _observationHashesMatched, _observationHashMismatches;
        private readonly List<string> _observationHashDiscrepancies = new List<string>();

        private int _acceptedActionsReplayed, _rejectedActionsReplayed, _unexpectedRejections, _unexpectedAcceptances;
        private int _rejectionReasonMismatches, _actionOutcomeMismatches;
        private readonly List<string> _actionDiscrepancies = new List<string>();
        private int _voluntaryEndTurnsReplayed;

        private readonly List<ReplaySeasonScoreboard> _replayedScores = new List<ReplaySeasonScoreboard>();
        private readonly List<ReplayFeedEvent> _eventFeed = new List<ReplayFeedEvent>();
        private ReplayComparisonReport _finalReport;
        public event Action<ReplayFeedEvent> OnEventReplayed;

        public ReplayBundle Bundle => _bundle;
        public GameEngine Engine => _engine;
        public ReplayState State => _state;
        public float Speed => _speed;
        public bool IsPlaying => _state == ReplayState.Playing;
        public bool IsCompleted => _isCompleted;
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
            if (_bundle.Summary == null || !string.Equals(_bundle.Summary.RunId, ReplayLoader.VerifiedDemoRunId, StringComparison.Ordinal))
                throw new InvalidOperationException($"ReplaySession requires verified demo run '{ReplayLoader.VerifiedDemoRunId}'; got '{_bundle.Summary?.RunId}'.");
            _hostMap = hostMap;
            Reset();
        }

        public void Reset()
        {
            _state = ReplayState.NotStarted; _stepTimer = 0f; _currentRequestIndex = 0;
            _currentTurnToken = 0; _currentSeatId = null; _turnOpen = false; _isCompleted = false;

            _totalSeasons = _bundle.Summary != null ? _bundle.Summary.SeasonsPlayed : 0;
            _totalRequests = _bundle.Requests.Count;
            _expectedAcceptedActions = 0; _expectedRejectedActions = 0; _expectedVoluntaryEndTurns = 0;
            for (int i = 0; i < _bundle.Requests.Count; i++)
            {
                var req = _bundle.Requests[i];
                if (IsEndTurnRecord(req)) { if (req.ResultOk) _expectedVoluntaryEndTurns++; }
                else { if (req.ResultOk) _expectedAcceptedActions++; else _expectedRejectedActions++; }
            }

            _observationHashesMatched = 0; _observationHashMismatches = 0; _observationHashDiscrepancies.Clear();
            _acceptedActionsReplayed = 0; _rejectedActionsReplayed = 0; _unexpectedRejections = 0;
            _unexpectedAcceptances = 0; _rejectionReasonMismatches = 0; _actionOutcomeMismatches = 0; _actionDiscrepancies.Clear();
            _voluntaryEndTurnsReplayed = 0; _replayedScores.Clear(); _eventFeed.Clear(); _finalReport = null;

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
            string obsJson = PromptBuilder.BoundedObservationJson(obs);
            string obsHash = Hash(obsJson);
            if (string.Equals(obsHash, req.ObservationHash, StringComparison.Ordinal)) _observationHashesMatched++;
            else
            {
                _observationHashMismatches++;
                if (_observationHashDiscrepancies.Count < 20)
                {
                    string expShort = string.IsNullOrEmpty(req.ObservationHash) ? "none" : (req.ObservationHash.Length >= 8 ? req.ObservationHash.Substring(0, 8) : req.ObservationHash);
                    _observationHashDiscrepancies.Add($"Req {req.TotalRequests} (S{req.Season} T{req.TurnToken} R{req.RequestIndex} {req.SeatId}): hash mismatch actual={obsHash.Substring(0, 8)}... exp={expShort}...");
                }
            }

            ParseResult parsed = LlmActionParser.Parse(req.RawModelText);
            bool isEndTurn = parsed.Ok && parsed.Action != null && parsed.Action.EndTurn;
            bool replayedOk = false;

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
                bool replayedParseOk = parsed.Ok && parsed.Action != null;
                string replayedError = null;
                if (!replayedParseOk)
                {
                    replayedOk = false;
                    replayedError = parsed.Error ?? "parse_failure";
                }
                else
                {
                    ActionCommand cmd = LlmActionParser.ToCommand(parsed.Action);
                    cmd.ControllerType = ControllerType.Llm;
                    ValidationResult res = _engine.SubmitAs(req.SeatId, _currentTurnToken, cmd);
                    replayedOk = res.Ok;
                    if (!res.Ok) replayedError = res.Error ?? "validation_failure";
                }

                if (req.ParseOk && req.ResultOk && replayedOk) _acceptedActionsReplayed++;
                else if ((!req.ParseOk || !req.ResultOk) && !replayedOk)
                {
                    bool categoryMatch = req.ParseOk == replayedParseOk;
                    bool reasonMatch = true;
                    if (req.ParseOk && replayedParseOk && !string.IsNullOrEmpty(req.ResultError))
                        reasonMatch = string.Equals(replayedError, req.ResultError, StringComparison.Ordinal);

                    if (categoryMatch && reasonMatch) _rejectedActionsReplayed++;
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

            string actType = isEndTurn ? "end_turn" : (parsed.Ok && parsed.Action != null ? parsed.Action.Type : "unknown");
            string actSummary = isEndTurn
                ? "Voluntary end of turn"
                : (parsed.Ok && parsed.Action != null
                    ? $"{parsed.Action.Type} {parsed.Action.TargetId ?? ""}".Trim()
                    : (!string.IsNullOrEmpty(req.RawModelText) ? req.RawModelText : "unknown"));
            string resText = isEndTurn ? "end_turn" : (replayedOk ? "accepted" : "rejected");
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

            return true;
        }

        public void StepSeason()
        {
            if (_isCompleted) return;
            int targetSeason = _currentSeason;
            while (!_isCompleted && _currentSeason == targetSeason) StepRequest();
        }

        public void RunToCompletion()
        {
            while (!_isCompleted) StepRequest();
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
            _engine.EndSeason();
            RecordScoreboard(_currentSeason);
            _isCompleted = true;
            _state = ReplayState.Completed;
            GenerateComparisonReport();
        }

        private void RecordScoreboard(int season) => _replayedScores.Add(new ReplaySeasonScoreboard { Season = season, Scores = CurrentScores() });

        private static bool IsEndTurnRecord(ReplayRequestRecord req)
        {
            if (req == null) return false;
            ParseResult p = LlmActionParser.Parse(req.RawModelText);
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
