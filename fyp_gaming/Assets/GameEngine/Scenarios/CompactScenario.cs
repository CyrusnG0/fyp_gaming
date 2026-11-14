using System;
using System.Collections.Generic;
using System.Text;

namespace LuanShi.Engine
{
    /// <summary>Starting faction of a scenario. The controller is a host decision, not scenario data:
    /// the same scenario must be playable by two models, two humans or a mixed roster.</summary>
    public sealed class ScenarioFaction
    {
        public string SeatId;
        public string Name;
        public string DefaultController = ControllerType.Llm;
    }

    public sealed class ScenarioCity
    {
        public string Id, Name;
        public int X, Z;
        public string OwnerSeatId;                       // null/"" = neutral objective
        public int Population = BalanceConfig.StartPopulation;
        public int Food = BalanceConfig.StartFood;
        public int Gold = BalanceConfig.StartGold;
        public int Garrison;
        public string GarrisonType = UnitTypes.Infantry;
        public int Morale = BalanceConfig.MoraleStart;
        public int Training = BalanceConfig.TrainingStart;
        public int Defense = BalanceConfig.DefenseStart;
        public int ReclaimPct = BalanceConfig.ReclaimPctStart;
    }

    public sealed class ScenarioArmy
    {
        public string Id, Name, OwnerSeatId;
        public int X, Z;
        public int Troops = BalanceConfig.StartFieldArmyTroops;
        public string UnitType = UnitTypes.Infantry;
        public int Morale = BalanceConfig.ArmyMoraleStart;
    }

    public sealed class ScenarioTower
    {
        public string Id, Name;
        public int X, Z;
        public string OwnerSeatId;                       // null/"" = neutral objective
    }

    /// <summary>
    /// A match setup as data: which factions exist, where their cities and armies stand, and which
    /// neutral objectives sit between them. It never contains decisions — no marches, attacks or
    /// treaties — so a scenario cannot play the game for either seat.
    ///
    /// <see cref="CompactContact"/> is the opt-in short match: two seats, two cities each, one neutral
    /// city and one neutral watchtower on the demo map, positioned so the engine's own movement and
    /// sight rules allow legal contact within a few seasons. The host still chooses whether to use it;
    /// nothing here runs unless a host calls <see cref="Apply"/>.
    ///
    /// Numbers below are scenario content (city strength, stock, distances) pending architect review;
    /// they are deliberately not added to <see cref="BalanceConfig"/> because they describe one
    /// match, not a rule that every match shares.
    /// </summary>
    public sealed class CompactScenario
    {
        public const string CompactContactId = "compact-contact-v1";

        /// <summary>
        /// Two seats eleven hexes apart, each with a 2-hex-per-season legal advance toward a neutral
        /// city on the middle hex row. Hexes are the demo map's (26×17 TBTK grid, 429 nodes), so the
        /// scenario needs no new terrain: every position below is a walkable, cost-1 hex.
        /// </summary>
        public static CompactScenario CompactContact { get; } = new CompactScenario(
            CompactContactId,
            "Compact Contact",
            new List<ScenarioFaction>
            {
                new ScenarioFaction { SeatId = "seat-0", Name = "Wei" },
                new ScenarioFaction { SeatId = "seat-1", Name = "Shu" },
            },
            new List<ScenarioCity>
            {
                // Capitals hold a real garrison, so storming one needs the 3:1 rule or 器械.
                // 500 troops => an assault needs 1,500; a fresh 2,000-troop host qualifies, a vanguard
                // that has already fought a field battle usually does not. (TBD for architect review.)
                new ScenarioCity
                {
                    Id = "city-wei", Name = "Luoyang", X = 5, Z = 8, OwnerSeatId = "seat-0",
                    Population = 12000, Food = 1200, Gold = 900, Garrison = 500,
                    Morale = 70, Training = 1, Defense = 1,
                },
                // Second city is poor and restless: 民心 22 < 25 is 民變 risk, so relief competes with war.
                new ScenarioCity
                {
                    Id = "city-wei-2", Name = "Xuchang", X = 4, Z = 10, OwnerSeatId = "seat-0",
                    Population = 8000, Food = 400, Gold = 300, Garrison = 200,
                    Morale = 22, Training = 0, Defense = 0,
                },
                new ScenarioCity
                {
                    Id = "city-shu", Name = "Chengdu", X = 20, Z = 8, OwnerSeatId = "seat-1",
                    Population = 12000, Food = 1200, Gold = 900, Garrison = 500,
                    Morale = 70, Training = 1, Defense = 1,
                },
                new ScenarioCity
                {
                    Id = "city-shu-2", Name = "Jiangzhou", X = 21, Z = 10, OwnerSeatId = "seat-1",
                    Population = 8000, Food = 400, Gold = 300, Garrison = 200,
                    Morale = 22, Training = 0, Defense = 0,
                },
                // The contested prize: six steps from Wei's army, five from Shu's, one march order from
                // neither. Whoever marches gets it; the other arrives next to a foreign-owned city.
                new ScenarioCity
                {
                    Id = "city-neutral", Name = "Wancheng", X = 13, Z = 8, OwnerSeatId = null,
                    Population = 6000, Food = 200, Gold = 100, Garrison = 300,
                },
            },
            new List<ScenarioArmy>
            {
                // Vanguards meet at the middle; main hosts stay beside their capital so an assault is
                // available without first walking the whole map.
                new ScenarioArmy
                {
                    Id = "army-wei-1", Name = "Wei Vanguard", OwnerSeatId = "seat-0",
                    X = 7, Z = 8, Troops = 2000, UnitType = UnitTypes.Infantry,
                },
                new ScenarioArmy
                {
                    Id = "army-wei-2", Name = "Wei Main Host", OwnerSeatId = "seat-0",
                    X = 5, Z = 9, Troops = 2000, UnitType = UnitTypes.Infantry,
                },
                new ScenarioArmy
                {
                    Id = "army-shu-1", Name = "Shu Vanguard", OwnerSeatId = "seat-1",
                    X = 18, Z = 8, Troops = 2000, UnitType = UnitTypes.Infantry,
                },
                new ScenarioArmy
                {
                    Id = "army-shu-2", Name = "Shu Main Host", OwnerSeatId = "seat-1",
                    X = 20, Z = 9, Troops = 2000, UnitType = UnitTypes.Infantry,
                },
            },
            new List<ScenarioTower>
            {
                // Secondary objective beside the neutral city: a march onto the hex takes it.
                new ScenarioTower { Id = "tower-mid", Name = "Central Watchtower", X = 12, Z = 8, OwnerSeatId = null },
            });

        public string ScenarioId { get; }
        public string DisplayName { get; }
        public IReadOnlyList<ScenarioFaction> Factions { get; }
        public IReadOnlyList<ScenarioCity> Cities { get; }
        public IReadOnlyList<ScenarioArmy> Armies { get; }
        public IReadOnlyList<ScenarioTower> WatchTowers { get; }

        public CompactScenario(string scenarioId, string displayName,
            IReadOnlyList<ScenarioFaction> factions, IReadOnlyList<ScenarioCity> cities,
            IReadOnlyList<ScenarioArmy> armies, IReadOnlyList<ScenarioTower> watchTowers)
        {
            ScenarioId = scenarioId;
            DisplayName = displayName;
            Factions = factions ?? new List<ScenarioFaction>();
            Cities = cities ?? new List<ScenarioCity>();
            Armies = armies ?? new List<ScenarioArmy>();
            WatchTowers = watchTowers ?? new List<ScenarioTower>();
        }

        /// <summary>
        /// Writes the scenario onto an engine whose <see cref="GameState.Map"/> is already set. Fails
        /// closed: an invalid setup throws instead of producing a half-built match. It does not call
        /// <c>BeginSeason</c>; the host decides when the first season opens.
        /// </summary>
        public void Apply(GameEngine engine, Func<ScenarioFaction, string> controllerOf = null)
        {
            if (engine == null) throw new ArgumentNullException(nameof(engine));
            if (engine.State == null) throw new InvalidOperationException("engine has no state");
            if (engine.State.Map == null)
                throw new InvalidOperationException("set State.Map before applying a scenario");

            var problems = Validate(engine.State.Map);
            if (problems.Count > 0)
                throw new InvalidOperationException("invalid scenario " + ScenarioId + ": "
                    + string.Join("; ", problems));

            engine.State.Factions.Clear();
            engine.State.Cities.Clear();
            engine.State.Armies.Clear();
            engine.State.WatchTowers.Clear();

            foreach (var fac in Factions)
                engine.State.Factions.Add(new FactionState
                {
                    SeatId = fac.SeatId,
                    Name = fac.Name,
                    Controller = controllerOf != null ? controllerOf(fac) : fac.DefaultController,
                });

            foreach (var c in Cities)
                engine.State.Cities.Add(new CityState
                {
                    Id = c.Id, Name = c.Name, X = c.X, Z = c.Z, OwnerSeatId = c.OwnerSeatId,
                    Population = c.Population, Food = c.Food, Gold = c.Gold,
                    Garrison = c.Garrison, GarrisonType = UnitTypes.Normalize(c.GarrisonType),
                    Morale = c.Morale, Training = c.Training, Defense = c.Defense,
                    ReclaimPct = c.ReclaimPct,
                });

            foreach (var a in Armies)
                engine.State.Armies.Add(new ArmyState
                {
                    Id = a.Id, Name = a.Name, OwnerSeatId = a.OwnerSeatId,
                    X = a.X, Z = a.Z, Troops = a.Troops,
                    UnitType = UnitTypes.Normalize(a.UnitType), Morale = a.Morale,
                });

            foreach (var t in WatchTowers)
                engine.State.WatchTowers.Add(new WatchTowerState
                {
                    Id = t.Id, Name = t.Name, X = t.X, Z = t.Z, OwnerSeatId = t.OwnerSeatId,
                });
        }

        /// <summary>Every reason this scenario cannot be placed on <paramref name="map"/>; empty means valid.</summary>
        public IReadOnlyList<string> Validate(MapData map)
        {
            var problems = new List<string>();
            if (map == null) { problems.Add("no map"); return problems; }

            var seatIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var fac in Factions)
            {
                if (fac == null || string.IsNullOrEmpty(fac.SeatId)) { problems.Add("faction without seat id"); continue; }
                if (!seatIds.Add(fac.SeatId)) problems.Add("duplicate seat " + fac.SeatId);
            }

            var cityIds = new HashSet<string>(StringComparer.Ordinal);
            var cityHexes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var c in Cities)
            {
                if (c == null || string.IsNullOrEmpty(c.Id)) { problems.Add("city without id"); continue; }
                if (!cityIds.Add(c.Id)) problems.Add("duplicate city " + c.Id);
                if (!map.IsWalkable(c.X, c.Z)) problems.Add("city " + c.Id + " is not on a walkable hex");
                if (!cityHexes.Add(c.X + "," + c.Z)) problems.Add("two cities share hex " + c.X + "," + c.Z);
                if (!c.IsNeutral() && !seatIds.Contains(c.OwnerSeatId))
                    problems.Add("city " + c.Id + " has unknown owner " + c.OwnerSeatId);
                if (!UnitTypes.IsValid(c.GarrisonType))
                    problems.Add("city " + c.Id + " has unknown garrison type " + c.GarrisonType);
                if (c.Garrison < 0 || c.Population <= 0) problems.Add("city " + c.Id + " has impossible resources");
            }

            var armyIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var a in Armies)
            {
                if (a == null || string.IsNullOrEmpty(a.Id)) { problems.Add("army without id"); continue; }
                if (!armyIds.Add(a.Id)) problems.Add("duplicate army " + a.Id);
                if (!map.IsWalkable(a.X, a.Z)) problems.Add("army " + a.Id + " is not on a walkable hex");
                if (string.IsNullOrEmpty(a.OwnerSeatId) || !seatIds.Contains(a.OwnerSeatId))
                    problems.Add("army " + a.Id + " has unknown owner " + a.OwnerSeatId);
                if (!UnitTypes.IsValid(a.UnitType)) problems.Add("army " + a.Id + " has unknown unit type " + a.UnitType);
                if (a.Troops <= 0) problems.Add("army " + a.Id + " has no troops");
            }

            var towerIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var t in WatchTowers)
            {
                if (t == null || string.IsNullOrEmpty(t.Id)) { problems.Add("watchtower without id"); continue; }
                if (!towerIds.Add(t.Id)) problems.Add("duplicate watchtower " + t.Id);
                if (!map.IsWalkable(t.X, t.Z)) problems.Add("watchtower " + t.Id + " is not on a walkable hex");
                if (!t.IsNeutral() && !seatIds.Contains(t.OwnerSeatId))
                    problems.Add("watchtower " + t.Id + " has unknown owner " + t.OwnerSeatId);
            }
            return problems;
        }

        /// <summary>Deterministic description for evidence records.</summary>
        public string ToJson()
        {
            var sb = new StringBuilder(512);
            sb.Append("{\"scenario_id\":").Append(MiniJson.Str(ScenarioId));
            sb.Append(",\"display_name\":").Append(MiniJson.Str(DisplayName));

            var factions = new List<string>();
            foreach (var f in Factions)
                factions.Add("{\"seat_id\":" + MiniJson.Str(f.SeatId) + ",\"name\":" + MiniJson.Str(f.Name)
                    + ",\"default_controller\":" + MiniJson.Str(f.DefaultController) + "}");
            sb.Append(",\"factions\":").Append(MiniJson.Array(factions));

            var cities = new List<string>();
            foreach (var c in Cities)
                cities.Add("{\"id\":" + MiniJson.Str(c.Id) + ",\"name\":" + MiniJson.Str(c.Name)
                    + ",\"x\":" + MiniJson.Num(c.X) + ",\"z\":" + MiniJson.Num(c.Z)
                    + ",\"owner\":" + MiniJson.Str(c.OwnerSeatId) + ",\"garrison\":" + MiniJson.Num(c.Garrison)
                    + ",\"food\":" + MiniJson.Num(c.Food) + ",\"gold\":" + MiniJson.Num(c.Gold)
                    + ",\"morale\":" + MiniJson.Num(c.Morale) + "}");
            sb.Append(",\"cities\":").Append(MiniJson.Array(cities));

            var armies = new List<string>();
            foreach (var a in Armies)
                armies.Add("{\"id\":" + MiniJson.Str(a.Id) + ",\"name\":" + MiniJson.Str(a.Name)
                    + ",\"owner\":" + MiniJson.Str(a.OwnerSeatId) + ",\"x\":" + MiniJson.Num(a.X)
                    + ",\"z\":" + MiniJson.Num(a.Z) + ",\"troops\":" + MiniJson.Num(a.Troops)
                    + ",\"unit_type\":" + MiniJson.Str(UnitTypes.Normalize(a.UnitType)) + "}");
            sb.Append(",\"armies\":").Append(MiniJson.Array(armies));

            var towers = new List<string>();
            foreach (var t in WatchTowers)
                towers.Add("{\"id\":" + MiniJson.Str(t.Id) + ",\"x\":" + MiniJson.Num(t.X)
                    + ",\"z\":" + MiniJson.Num(t.Z) + ",\"owner\":" + MiniJson.Str(t.OwnerSeatId) + "}");
            sb.Append(",\"watchtowers\":").Append(MiniJson.Array(towers));
            sb.Append('}');
            return sb.ToString();
        }
    }

    internal static class ScenarioCityExtensions
    {
        /// <summary>Null or empty owner means neutral, mirroring <see cref="CityState.IsNeutral"/>.</summary>
        public static bool IsNeutral(this ScenarioCity city)
            => city != null && string.IsNullOrEmpty(city.OwnerSeatId);
    }

    internal static class ScenarioTowerExtensions
    {
        public static bool IsNeutral(this ScenarioTower tower)
            => tower != null && string.IsNullOrEmpty(tower.OwnerSeatId);
    }
}
