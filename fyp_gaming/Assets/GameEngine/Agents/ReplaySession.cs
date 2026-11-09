using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace LuanShi.Engine
{
    public enum ReplayState
    {
        NotStarted,
        Playing,
        Paused,
        Completed
    }

    public sealed partial class ReplaySession
    {
        private readonly ReplayBundle _bundle;
        private readonly MapData _hostMap;
        private GameEngine _engine;
        private ReplayState _state = ReplayState.NotStarted;
        private float _speed = 1.0f;
        private float _stepTimer;
        private const float BaseStepInterval = 0.8f;

        private int _currentRequestIndex;
        private int _currentSeason;
        private int _currentTurnToken;
        private string _currentSeatId;
        private bool _turnOpen;
        private bool _isCompleted;

        private int _observationHashesMatched;
        private int _normalizedObservationsMatched;
        private int _acceptedActionsMatched;
        private int _voluntaryEndTurnsMatched;
        private readonly List<ReplaySeasonScoreboard> _replayedScores = new List<ReplaySeasonScoreboard>();
        private ReplayComparisonReport _finalReport;

        public ReplayBundle Bundle => _bundle;
        public GameEngine Engine => _engine;
        public ReplayState State => _state;
        public float Speed => _speed;
        public bool IsPlaying => _state == ReplayState.Playing;
        public bool IsCompleted => _isCompleted;
        public int CurrentRequestIndex => _currentRequestIndex;
        public int CurrentSeason => _currentSeason;
        public int CurrentTurnToken => _currentTurnToken;
        public string CurrentSeatId => _currentSeatId;
        public int ObservationHashesMatched => _observationHashesMatched;
        public int NormalizedObservationsMatched => _normalizedObservationsMatched;
        public int AcceptedActionsMatched => _acceptedActionsMatched;
        public int VoluntaryEndTurnsMatched => _voluntaryEndTurnsMatched;
        public ReplayComparisonReport FinalReport => _finalReport;
        public IReadOnlyList<ReplaySeasonScoreboard> ReplayedScores => _replayedScores;

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
                    {
                        if (t.Season == _currentSeason && string.Equals(t.SeatId, _currentSeatId, StringComparison.Ordinal))
                            return t.PersonaId;
                    }
                }
                return string.Equals(_currentSeatId, "seat-0", StringComparison.Ordinal) ? "wei-mild" : "shu-aggressive";
            }
        }

        public ReplaySession(ReplayBundle bundle, MapData hostMap = null)
        {
            _bundle = bundle ?? throw new ArgumentNullException(nameof(bundle));
            _hostMap = hostMap;
            Reset();
        }

        public void Reset()
        {
            _state = ReplayState.NotStarted;
            _stepTimer = 0f;
            _currentRequestIndex = 0;
            _currentTurnToken = 0;
            _currentSeatId = null;
            _turnOpen = false;
            _isCompleted = false;
            _observationHashesMatched = 0;
            _normalizedObservationsMatched = 0;
            _acceptedActionsMatched = 0;
            _voluntaryEndTurnsMatched = 0;
            _replayedScores.Clear();
            _finalReport = null;

            int seed = _bundle.Summary != null && _bundle.Summary.Seed != 0 ? _bundle.Summary.Seed : 12345;
            string profileKey = _bundle.Summary?.BalanceProfileKey ?? "easy-research";
            if (!BalanceProfile.TryGet(profileKey, out BalanceProfile profile))
                profile = BalanceProfile.EasyResearch;

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

            _engine.State.Armies.Add(new ArmyState { Id = "army-wei-1", Name = "Wei Army", OwnerSeatId = "seat-0", Troops = BalanceConfig.StartFieldArmyTroops, X = 2, Z = 6 });
            _engine.State.Armies.Add(new ArmyState { Id = "army-wei-2", Name = "Wei Army", OwnerSeatId = "seat-0", Troops = BalanceConfig.StartFieldArmyTroops, X = 2, Z = 8 });
            _engine.State.Armies.Add(new ArmyState { Id = "army-wei-3", Name = "Wei Army", OwnerSeatId = "seat-0", Troops = BalanceConfig.StartFieldArmyTroops, X = 2, Z = 10 });
            _engine.State.Armies.Add(new ArmyState { Id = "army-wei-4", Name = "Wei Army", OwnerSeatId = "seat-0", Troops = BalanceConfig.StartFieldArmyTroops, X = 4, Z = 12 });

            _engine.State.Armies.Add(new ArmyState { Id = "army-shu-1", Name = "Shu Army", OwnerSeatId = "seat-1", Troops = BalanceConfig.StartFieldArmyTroops, X = 23, Z = 6 });
            _engine.State.Armies.Add(new ArmyState { Id = "army-shu-2", Name = "Shu Army", OwnerSeatId = "seat-1", Troops = BalanceConfig.StartFieldArmyTroops, X = 23, Z = 8 });
            _engine.State.Armies.Add(new ArmyState { Id = "army-shu-3", Name = "Shu Army", OwnerSeatId = "seat-1", Troops = BalanceConfig.StartFieldArmyTroops, X = 23, Z = 10 });
            _engine.State.Armies.Add(new ArmyState { Id = "army-shu-4", Name = "Shu Army", OwnerSeatId = "seat-1", Troops = BalanceConfig.StartFieldArmyTroops, X = 21, Z = 4 });

            _engine.BeginSeason();
            _currentSeason = _engine.State.Season;
        }

        public void Play()
        {
            if (_isCompleted) return;
            _state = ReplayState.Playing;
        }

        public void Pause()
        {
            if (_state == ReplayState.Playing)
                _state = ReplayState.Paused;
        }

        public void SetSpeed(float multiplier)
        {
            if (multiplier > 0.05f) _speed = multiplier;
        }

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
            if (string.Equals(obsHash, req.ObservationHash, StringComparison.Ordinal))
            {
                _observationHashesMatched++;
                _normalizedObservationsMatched++;
            }
            else if (MatchesMonoNormalizedObservation(obsJson, req.ObservationHash))
            {
                _normalizedObservationsMatched++;
            }

            ParseResult parsed = LlmActionParser.Parse(req.RawModelText);
            bool isEndTurn = parsed.Ok && parsed.Action != null && parsed.Action.EndTurn;

            if (isEndTurn)
            {
                if (_turnOpen && _currentTurnToken != 0) _engine.EndSeatTurn(_currentTurnToken);
                _turnOpen = false;
                _voluntaryEndTurnsMatched++;
            }
            else if (parsed.Ok && parsed.Action != null)
            {
                ActionCommand cmd = LlmActionParser.ToCommand(parsed.Action);
                cmd.ControllerType = ControllerType.Llm;
                ValidationResult res = _engine.SubmitAs(req.SeatId, _currentTurnToken, cmd);
                if (res.Ok == req.ResultOk) _acceptedActionsMatched++;
            }

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
            while (!_isCompleted && _currentSeason == targetSeason)
            {
                StepRequest();
            }
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
                {
                    if (string.Equals(c.OwnerSeatId, f.SeatId, StringComparison.Ordinal))
                    {
                        s.Cities++; s.Gold += c.Gold; s.Food += c.Food; s.Population += c.Population; s.Garrison += c.Garrison;
                    }
                }
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

        private void RecordScoreboard(int season)
        {
            _replayedScores.Add(new ReplaySeasonScoreboard { Season = season, Scores = CurrentScores() });
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
