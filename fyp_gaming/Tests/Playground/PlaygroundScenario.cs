using System;
using System.Collections.Generic;
using LuanShi.Engine;

namespace LuanShi.Playground
{
    /// <summary>
    /// Deterministic two-faction corridor used by the playground. Both seats are LLM-controlled and
    /// share the standard rules. The fog gap is deliberate: seat-a sees the neutral city at x=3,
    /// seat-b sees only x=9..12, and neither ever sees the other's capital or army. Every run with the
    /// same seed therefore starts from byte-identical state and observations.
    /// </summary>
    internal static class PlaygroundScenario
    {
        public const string SeatA = "seat-a";
        public const string SeatB = "seat-b";
        public const int Width = 13;

        public static GameEngine Create(PlaygroundConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            return Create(config.Seed, config.GameId);
        }

        public static GameEngine Create(int seed, string gameId)
        {
            var engine = new GameEngine(seed, gameId, BalanceProfile.Standard);
            engine.State.Map = Corridor(Width);

            engine.State.Factions.Add(new FactionState
            {
                SeatId = SeatA, Name = "Wei", Controller = ControllerType.Llm,
            });
            engine.State.Factions.Add(new FactionState
            {
                SeatId = SeatB, Name = "Shu", Controller = ControllerType.Llm,
            });

            engine.State.Cities.Add(City("city-a", "Wei Capital", 0, SeatA, 10000, 800, 500, 500));
            engine.State.Cities.Add(City("city-neutral", "Crossroads", 3, null, 3000, 300, 100, 0));
            engine.State.Cities.Add(City("city-b", "Shu Capital", 12, SeatB, 10000, 800, 500, 500));

            engine.State.Armies.Add(Army("army-a-1", "Wei Vanguard", SeatA, 1));
            engine.State.Armies.Add(Army("army-b-1", "Shu Vanguard", SeatB, 11));
            return engine;
        }

        /// <summary>
        /// A faithful fresh copy of the scenario for isolated validation. It intentionally does not
        /// call BeginSeason: the journal replays season boundaries in the same order as the live run.
        /// </summary>
        public static GameEngine CreateForReplay(PlaygroundConfig config)
            => Create(config.Seed, config.GameId);

        public static IReadOnlyList<string> SeatOrder(GameEngine engine)
            => SeatRotation.PlayOrder(engine.State).AsReadOnly();

        private static CityState City(string id, string name, int x, string owner,
            int population, int food, int gold, int garrison)
        {
            return new CityState
            {
                Id = id,
                Name = name,
                X = x,
                Z = 0,
                OwnerSeatId = owner,
                Population = population,
                Food = food,
                Gold = gold,
                Garrison = garrison,
                Morale = BalanceConfig.MoraleStart,
            };
        }

        private static ArmyState Army(string id, string name, string owner, int x)
        {
            return new ArmyState
            {
                Id = id,
                Name = name,
                OwnerSeatId = owner,
                Troops = 2000,
                UnitType = UnitTypes.Infantry,
                X = x,
                Z = 0,
                Morale = BalanceConfig.ArmyMoraleStart,
            };
        }

        private static MapData Corridor(int width)
        {
            var map = new MapData
            {
                Width = width,
                Height = 1,
                Walkable = new bool[width],
                Cost = new int[width],
                Neighbors = new int[width][],
            };
            for (int x = 0; x < width; x++)
            {
                map.Walkable[x] = true;
                map.Cost[x] = 1;
                var neighbors = new List<int>();
                if (x > 0) neighbors.Add(x - 1);
                if (x < width - 1) neighbors.Add(x + 1);
                map.Neighbors[x] = neighbors.ToArray();
            }
            return map;
        }
    }
}
