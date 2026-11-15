using System;
using System.Collections.Generic;
using LuanShi.Engine;

namespace LuanShi.ExperimentAnalysis.Tests
{
    /// <summary>
    /// Minimal assertion log for the standalone experiment-analysis project. A failure is printed with
    /// its observed value and makes the process exit non-zero; nothing is skipped silently.
    /// </summary>
    internal sealed class TestLog
    {
        private readonly List<string> _failures = new List<string>();

        public int Passed { get; private set; }
        public int Failed => _failures.Count;

        public void Check(bool ok, string name, string detail = null)
        {
            if (ok)
            {
                Passed++;
                Console.WriteLine("  PASS " + name);
                return;
            }
            string line = detail == null ? name : name + " | observed: " + detail;
            _failures.Add(line);
            Console.WriteLine("  FAIL " + line);
        }

        public void Section(string title) => Console.WriteLine("\n== " + title + " ==");

        public int Summarise()
        {
            Console.WriteLine();
            Console.WriteLine(Passed + " passed, " + Failed + " failed");
            if (Failed == 0) return 0;
            foreach (string failure in _failures) Console.WriteLine("  FAILED: " + failure);
            return 1;
        }
    }

    /// <summary>
    /// Two-seat fixture on a plain walkable grid. It builds only public engine state, so the checks run
    /// through the same observation, validator and runner paths as a real host.
    /// </summary>
    internal static class Fixture
    {
        public const string GameId = "experiment-analysis-fixture";
        public const string SeatA = "seat-a";
        public const string SeatB = "seat-b";
        public const int Seed = 4242;

        public static MapData Grid(int width = 8, int height = 6)
        {
            var map = new MapData
            {
                Width = width, Height = height,
                Walkable = new bool[width * height],
                Cost = new int[width * height],
                Neighbors = new int[width * height][],
            };
            for (int z = 0; z < height; z++)
            {
                for (int x = 0; x < width; x++)
                {
                    int index = map.Index(x, z);
                    map.Walkable[index] = true;
                    map.Cost[index] = 1;
                    var links = new List<int>();
                    if (x > 0) links.Add(map.Index(x - 1, z));
                    if (x < width - 1) links.Add(map.Index(x + 1, z));
                    if (z > 0) links.Add(map.Index(x, z - 1));
                    if (z < height - 1) links.Add(map.Index(x, z + 1));
                    map.Neighbors[index] = links.ToArray();
                }
            }
            return map;
        }

        /// <summary>seat-a owns city-a and army-a-1; seat-b owns city-b further east.</summary>
        public static GameEngine World(int seed = Seed, int researchPointsA = 100,
            int researchPointsB = 0)
        {
            var engine = new GameEngine(seed, GameId);
            engine.State.Map = Grid();
            engine.State.Factions.Add(new FactionState
            {
                SeatId = SeatA, Name = "Wei", Controller = ControllerType.Llm,
            });
            engine.State.Factions.Add(new FactionState
            {
                SeatId = SeatB, Name = "Shu", Controller = ControllerType.Llm,
            });
            engine.State.Cities.Add(new CityState
            {
                Id = "city-a", Name = "City A", X = 0, Z = 0, OwnerSeatId = SeatA,
                Population = 10000, Food = 3000, Gold = 900, Garrison = 500, Morale = 70,
            });
            engine.State.Cities.Add(new CityState
            {
                Id = "city-b", Name = "City B", X = 6, Z = 4, OwnerSeatId = SeatB,
                Population = 10000, Food = 3000, Gold = 900, Garrison = 500, Morale = 70,
            });
            engine.State.Armies.Add(new ArmyState
            {
                Id = "army-a-1", Name = "Wei Vanguard", OwnerSeatId = SeatA, Troops = 2000,
                UnitType = UnitTypes.Infantry, X = 1, Z = 0, Morale = 60,
            });
            engine.State.Armies.Add(new ArmyState
            {
                Id = "army-b-1", Name = "Shu Vanguard", OwnerSeatId = SeatB, Troops = 2000,
                UnitType = UnitTypes.Infantry, X = 5, Z = 4, Morale = 60,
            });
            engine.BeginSeason();
            engine.State.FindFaction(SeatA).ResearchPoints = researchPointsA;
            engine.State.FindFaction(SeatB).ResearchPoints = researchPointsB;
            return engine;
        }

        /// <summary>
        /// Production-shaped config: deliberation on (inquiry + private note), research on, the legacy
        /// manual lookup off because the deliberation addendum replaces it. Every ceiling is finite.
        /// </summary>
        public static LlmConfig Config(bool research = true, bool deliberation = true)
        {
            return new LlmConfig
            {
                EnableDeliberation = deliberation,
                EnableManualLookup = false,
                EnableResearch = research,
                MaxRequestsPerTurn = 10,
                MaxActionsPerTurn = 4,
                MaxInvalidPerTurn = 3,
                MaxTotalRequests = 60,
                MaxInquiriesPerTurn = 4,
                MaxInquiriesPerSession = 20,
                MaxRetainedInquiryResults = 4,
                MaxTurnSeconds = 60,
                RequestTimeoutMs = 5000,
                MaxOutputTokens = 400,
            };
        }

        public static void Drain(LlmTurnRunner runner)
        {
            for (int i = 0; i < 500
                            && (runner.State == RunnerState.Requesting
                                || runner.State == RunnerState.Executing); i++)
                runner.Tick();
        }

        public static EventRecord FindEvent(EventLog log, string eventType)
        {
            foreach (EventRecord record in log.Records)
                if (record.EventType == eventType) return record;
            return null;
        }

        public static int CountEvents(EventLog log, string eventType)
        {
            int count = 0;
            foreach (EventRecord record in log.Records)
                if (record.EventType == eventType) count++;
            return count;
        }

        /// <summary>Minimal host matching the production/spectator binding shape.</summary>
        internal sealed class Host : IPlanningHost
        {
            private readonly GameEngine _engine;
            private readonly string _seat;

            public readonly int Token;
            public int SubmitCalls;
            public int EndTurnCalls;

            public Host(GameEngine engine, string seat)
            {
                _engine = engine;
                _seat = seat;
                Token = engine.BeginSeatTurn(seat);
            }

            public Observation Observe() => _engine.Observe(_seat);

            public ValidationResult Submit(ActionCommand cmd)
            {
                SubmitCalls++;
                return _engine.SubmitAs(_seat, Token, cmd);
            }

            public void EndTurn()
            {
                EndTurnCalls++;
                _engine.EndSeatTurn(Token);
            }

            public MovementView PlanMovement() => MovementPlanner.Build(_engine, _seat);

            public bool IsGameOver => _engine.State.WinnerSeatId != null;
        }
    }
}
