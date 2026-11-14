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

    /// <summary>
    /// One starting city. Content values are split in two groups so a balance profile keeps working:
    ///
    /// <list type="bullet">
    /// <item><b>Profile-derived</b> (<c>Population</c>, <c>Food</c>, <c>Gold</c> of an owned city):
    /// a zero <see cref="Population"/> and a null <see cref="FoodOverride"/> / <see cref="GoldOverride"/>
    /// mean "take it from the selected profile", exactly as a host's own bootstrap does. Under
    /// <c>food-easy</c> an owned city therefore opens with 3,000 food, not the standard 500.</item>
    /// <item><b>Deliberate scenario overrides</b> (<see cref="Garrison"/>, <see cref="Morale"/>,
    /// <see cref="Training"/>, <see cref="Defense"/>, <see cref="ReclaimPct"/>, and any non-null
    /// food/gold/population): scenario content, identical under every profile. A neutral city always
    /// uses its fixed placeholder stock because a profile only seeds owned cities.</item>
    /// </list>
    /// </summary>
    public sealed class ScenarioCity
    {
        public string Id, Name;
        public int X, Z;
        public string OwnerSeatId;                       // null/"" = neutral objective
        public int Population;                           // 0 = profile default (BalanceConfig.StartPopulation)
        public int? FoodOverride;                        // null = profile default
        public int? GoldOverride;                        // null = profile default
        public int Garrison;
        public string GarrisonType = UnitTypes.Infantry;
        public int Morale = BalanceConfig.MoraleStart;
        public int Training = BalanceConfig.TrainingStart;
        public int Defense = BalanceConfig.DefenseStart;
        public int ReclaimPct = BalanceConfig.ReclaimPctStart;
        public int NeutralPopulation = 6000;             // used only when OwnerSeatId is null
        public int NeutralFood = 200;
        public int NeutralGold = 100;
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
    /// Numbers below are scenario content (positions, garrisons, 民心) pending architect review; they
    /// are deliberately not added to <see cref="BalanceConfig"/> because they describe one match, not a
    /// rule that every match shares. Economy values are profile-derived unless explicitly overridden.
    /// </summary>
    public sealed class CompactScenario
    {
        public const string CompactContactId = "compact-contact-v1";
        public const int MaxFactions = 8;                // AGENTS.md #6: never hardcode more than the engine allows

        /// <summary>
        /// Mirrored opening (2026-11-14 fairness review). Every distance that decides the opening race
        /// is equal: each vanguard is 5 hexes from the neutral city, each capital 7, each second city 2
        /// from its capital. Only the hex grid's own irregularity makes the two sides look different.
        /// The neutral watchtower sits one hex beside the contested city, equally reachable.
        ///
        /// Turn order is still an advantage: <c>seat-0</c> orders first every season, so with mirrored
        /// distances seat-0 takes the neutral city in season 3 and seat-1 arrives adjacent. That is a
        /// documented first-mover effect, not a hidden imbalance — hosts alternate it with
        /// <see cref="WithSeatsSwapped"/>, which keeps the geography and gives the first seat the other
        /// faction.
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
                    Id = "city-wei", Name = "Luoyang", X = 6, Z = 8, OwnerSeatId = "seat-0",
                    Garrison = 500, Morale = 70, Training = 1, Defense = 1,
                },
                // Restless secondary city: 民心 22 < 25 is 民變 risk, so relief competes with war under
                // every profile. Its stock stays profile-derived (it is not artificially starved).
                new ScenarioCity
                {
                    Id = "city-wei-2", Name = "Xuchang", X = 5, Z = 10, OwnerSeatId = "seat-0",
                    Garrison = 200, Morale = 22, Training = 0, Defense = 0,
                },
                new ScenarioCity
                {
                    Id = "city-shu", Name = "Chengdu", X = 20, Z = 8, OwnerSeatId = "seat-1",
                    Garrison = 500, Morale = 70, Training = 1, Defense = 1,
                },
                new ScenarioCity
                {
                    Id = "city-shu-2", Name = "Jiangzhou", X = 21, Z = 10, OwnerSeatId = "seat-1",
                    Garrison = 200, Morale = 22, Training = 0, Defense = 0,
                },
                // The contested prize: five steps from each vanguard, seven from each capital.
                new ScenarioCity
                {
                    Id = "city-neutral", Name = "Wancheng", X = 13, Z = 8, OwnerSeatId = null,
                    Garrison = 300, NeutralPopulation = 6000, NeutralFood = 200, NeutralGold = 100,
                },
            },
            new List<ScenarioArmy>
            {
                // Vanguards meet at the middle; main hosts stay beside their capital so an assault is
                // available without first walking the whole map.
                new ScenarioArmy
                {
                    Id = "army-wei-1", Name = "Wei Vanguard", OwnerSeatId = "seat-0",
                    X = 8, Z = 8, Troops = 2000, UnitType = UnitTypes.Infantry,
                },
                new ScenarioArmy
                {
                    Id = "army-wei-2", Name = "Wei Main Host", OwnerSeatId = "seat-0",
                    X = 6, Z = 9, Troops = 2000, UnitType = UnitTypes.Infantry,
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

        /// <summary>Resolves a scenario key. Unknown keys are rejected so a typo cannot silently
        /// start the wrong match.</summary>
        public static bool TryGet(string key, out CompactScenario scenario)
        {
            if (key == CompactContactId) { scenario = CompactContact; return true; }
            scenario = null;
            return false;
        }

        /// <summary>All selectable scenario keys, for a host's picker and for evidence.</summary>
        public static IReadOnlyList<string> Keys { get; } = new[] { CompactContactId };

        /// <summary>
        /// The same geography with the two seats exchanged (seat-0 plays what seat-1 played and vice
        /// versa). Hosts alternate it so the first-moving seat does not always hold the same faction.
        /// </summary>
        public CompactScenario WithSeatsSwapped()
        {
            var seatIds = new List<string>();
            foreach (var f in Factions) seatIds.Add(f.SeatId);
            if (seatIds.Count != 2)
                throw new InvalidOperationException("seat swapping needs exactly two factions");

            string Map(string seatId)
            {
                if (seatId == seatIds[0]) return seatIds[1];
                if (seatId == seatIds[1]) return seatIds[0];
                return seatId;
            }

            var factions = new List<ScenarioFaction>();
            foreach (var f in Factions)
                factions.Add(new ScenarioFaction
                {
                    SeatId = Map(f.SeatId), Name = f.Name, DefaultController = f.DefaultController,
                });

            var cities = new List<ScenarioCity>();
            foreach (var c in Cities)
                cities.Add(new ScenarioCity
                {
                    Id = c.Id, Name = c.Name, X = c.X, Z = c.Z,
                    OwnerSeatId = string.IsNullOrEmpty(c.OwnerSeatId) ? c.OwnerSeatId : Map(c.OwnerSeatId),
                    Population = c.Population, FoodOverride = c.FoodOverride, GoldOverride = c.GoldOverride,
                    Garrison = c.Garrison, GarrisonType = c.GarrisonType, Morale = c.Morale,
                    Training = c.Training, Defense = c.Defense, ReclaimPct = c.ReclaimPct,
                    NeutralPopulation = c.NeutralPopulation, NeutralFood = c.NeutralFood,
                    NeutralGold = c.NeutralGold,
                });

            var armies = new List<ScenarioArmy>();
            foreach (var a in Armies)
                armies.Add(new ScenarioArmy
                {
                    Id = a.Id, Name = a.Name, OwnerSeatId = Map(a.OwnerSeatId), X = a.X, Z = a.Z,
                    Troops = a.Troops, UnitType = a.UnitType, Morale = a.Morale,
                });

            var towers = new List<ScenarioTower>();
            foreach (var t in WatchTowers)
                towers.Add(new ScenarioTower
                {
                    Id = t.Id, Name = t.Name, X = t.X, Z = t.Z,
                    OwnerSeatId = string.IsNullOrEmpty(t.OwnerSeatId) ? t.OwnerSeatId : Map(t.OwnerSeatId),
                });

            return new CompactScenario(ScenarioId + "-swapped", DisplayName + " (seats swapped)",
                factions, cities, armies, towers);
        }

        /// <summary>
        /// Writes the scenario onto an engine whose <see cref="GameState.Map"/> is already set. Fails
        /// closed and atomically: every field is validated first and the entity objects are built before
        /// the existing match state is touched, so invalid data can never clear or half-replace a game.
        /// It does not call <c>BeginSeason</c>; the host decides when the first season opens.
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

            // Build everything first: a failure here leaves the caller's game untouched.
            var factions = new List<FactionState>();
            foreach (var fac in Factions)
                factions.Add(new FactionState
                {
                    SeatId = fac.SeatId,
                    Name = fac.Name,
                    Controller = controllerOf != null ? controllerOf(fac) : fac.DefaultController,
                });

            var cities = new List<CityState>();
            foreach (var c in Cities)
            {
                var city = new CityState
                {
                    Id = c.Id, Name = c.Name, X = c.X, Z = c.Z, OwnerSeatId = c.OwnerSeatId,
                    Garrison = c.Garrison, GarrisonType = UnitTypes.Normalize(c.GarrisonType),
                    Morale = c.Morale, Training = c.Training, Defense = c.Defense,
                    ReclaimPct = c.ReclaimPct,
                };
                if (city.IsNeutral)
                {
                    // A profile only seeds owned cities; neutral objectives keep their fixed placeholder
                    // stock so the objective is identical under every profile.
                    city.Population = c.NeutralPopulation;
                    city.Food = c.NeutralFood;
                    city.Gold = c.NeutralGold;
                }
                else
                {
                    // Profile first, scenario override second. With no override the owned city keeps
                    // exactly what the profile's own bootstrap would have given it.
                    engine.Balance.ApplyStartingResources(city);
                    if (c.Population > 0) city.Population = c.Population;
                    if (c.FoodOverride.HasValue) city.Food = c.FoodOverride.Value;
                    if (c.GoldOverride.HasValue) city.Gold = c.GoldOverride.Value;
                }
                cities.Add(city);
            }

            var armies = new List<ArmyState>();
            foreach (var a in Armies)
                armies.Add(new ArmyState
                {
                    Id = a.Id, Name = a.Name, OwnerSeatId = a.OwnerSeatId,
                    X = a.X, Z = a.Z, Troops = a.Troops,
                    UnitType = UnitTypes.Normalize(a.UnitType), Morale = a.Morale,
                });

            var towers = new List<WatchTowerState>();
            foreach (var t in WatchTowers)
                towers.Add(new WatchTowerState
                {
                    Id = t.Id, Name = t.Name, X = t.X, Z = t.Z, OwnerSeatId = t.OwnerSeatId,
                });

            engine.State.Factions.Clear();
            engine.State.Cities.Clear();
            engine.State.Armies.Clear();
            engine.State.WatchTowers.Clear();
            engine.State.Factions.AddRange(factions);
            engine.State.Cities.AddRange(cities);
            engine.State.Armies.AddRange(armies);
            engine.State.WatchTowers.AddRange(towers);
            engine.State.ScenarioId = ScenarioId;
        }

        /// <summary>Every reason this scenario cannot be placed on <paramref name="map"/>; empty means valid.</summary>
        public IReadOnlyList<string> Validate(MapData map)
        {
            var problems = new List<string>();
            if (map == null) { problems.Add("no map"); return problems; }

            if (Factions.Count > MaxFactions)
                problems.Add(Factions.Count + " factions exceeds the maximum of " + MaxFactions);
            if (Factions.Count == 0) problems.Add("a scenario needs at least one faction");

            var seatIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var fac in Factions)
            {
                if (fac == null || string.IsNullOrEmpty(fac.SeatId)) { problems.Add("faction without seat id"); continue; }
                if (!seatIds.Add(fac.SeatId)) problems.Add("duplicate seat " + fac.SeatId);
                if (string.IsNullOrEmpty(fac.Name)) problems.Add("seat " + fac.SeatId + " has no name");
                if (string.IsNullOrEmpty(fac.DefaultController)) problems.Add("seat " + fac.SeatId + " has no controller");
            }

            var cityIds = new HashSet<string>(StringComparer.Ordinal);
            var cityHexes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var c in Cities)
            {
                if (c == null || string.IsNullOrEmpty(c.Id)) { problems.Add("city without id"); continue; }
                if (!cityIds.Add(c.Id)) problems.Add("duplicate city " + c.Id);
                if (!map.IsWalkable(c.X, c.Z)) problems.Add("city " + c.Id + " is not on a walkable hex");
                if (!cityHexes.Add(c.X + "," + c.Z)) problems.Add("two cities share hex " + c.X + "," + c.Z);
                if (!IsNeutral(c.OwnerSeatId) && !seatIds.Contains(c.OwnerSeatId))
                    problems.Add("city " + c.Id + " has unknown owner " + c.OwnerSeatId);
                if (!UnitTypes.IsValid(c.GarrisonType))
                    problems.Add("city " + c.Id + " has unknown garrison type " + c.GarrisonType);
                if (c.Garrison < 0) problems.Add("city " + c.Id + " has a negative garrison");
                if (c.Morale < BalanceConfig.MoraleMin || c.Morale > BalanceConfig.MoraleMax)
                    problems.Add("city " + c.Id + " has morale outside "
                        + BalanceConfig.MoraleMin + ".." + BalanceConfig.MoraleMax + " (" + c.Morale + ")");
                if (c.Training < 0 || c.Training > BalanceConfig.TrainingMax)
                    problems.Add("city " + c.Id + " has training outside 0.." + BalanceConfig.TrainingMax);
                if (c.Defense < 0 || c.Defense > BalanceConfig.DefenseMax)
                    problems.Add("city " + c.Id + " has defense outside 0.." + BalanceConfig.DefenseMax);
                if (c.ReclaimPct < 0 || c.ReclaimPct > BalanceConfig.ReclaimPctCap)
                    problems.Add("city " + c.Id + " has reclaim outside 0.." + BalanceConfig.ReclaimPctCap);
                if (c.Population < 0) problems.Add("city " + c.Id + " has a negative population");
                if (c.FoodOverride.HasValue && c.FoodOverride.Value < 0)
                    problems.Add("city " + c.Id + " has negative food");
                if (c.GoldOverride.HasValue && c.GoldOverride.Value < 0)
                    problems.Add("city " + c.Id + " has negative gold");
                if (IsNeutral(c.OwnerSeatId))
                {
                    if (c.NeutralPopulation <= 0) problems.Add("neutral city " + c.Id + " has no population");
                    if (c.NeutralFood < 0) problems.Add("neutral city " + c.Id + " has negative food");
                    if (c.NeutralGold < 0) problems.Add("neutral city " + c.Id + " has negative gold");
                }
            }

            var armyIds = new HashSet<string>(StringComparer.Ordinal);
            var armyHexes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var a in Armies)
            {
                if (a == null || string.IsNullOrEmpty(a.Id)) { problems.Add("army without id"); continue; }
                if (!armyIds.Add(a.Id)) problems.Add("duplicate army " + a.Id);
                if (!map.IsWalkable(a.X, a.Z)) problems.Add("army " + a.Id + " is not on a walkable hex");
                if (!armyHexes.Add(a.X + "," + a.Z))
                    problems.Add("two armies share hex " + a.X + "," + a.Z);
                if (cityHexes.Contains(a.X + "," + a.Z))
                    problems.Add("army " + a.Id + " starts on a city hex");
                if (string.IsNullOrEmpty(a.OwnerSeatId) || !seatIds.Contains(a.OwnerSeatId))
                    problems.Add("army " + a.Id + " has unknown owner " + a.OwnerSeatId);
                if (!UnitTypes.IsValid(a.UnitType)) problems.Add("army " + a.Id + " has unknown unit type " + a.UnitType);
                if (a.Troops <= 0) problems.Add("army " + a.Id + " has no troops");
                if (a.Morale < BalanceConfig.MoraleMin || a.Morale > BalanceConfig.MoraleMax)
                    problems.Add("army " + a.Id + " has morale outside "
                        + BalanceConfig.MoraleMin + ".." + BalanceConfig.MoraleMax + " (" + a.Morale + ")");
            }

            var towerIds = new HashSet<string>(StringComparer.Ordinal);
            var towerHexes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var t in WatchTowers)
            {
                if (t == null || string.IsNullOrEmpty(t.Id)) { problems.Add("watchtower without id"); continue; }
                if (!towerIds.Add(t.Id)) problems.Add("duplicate watchtower " + t.Id);
                if (!map.IsWalkable(t.X, t.Z)) problems.Add("watchtower " + t.Id + " is not on a walkable hex");
                if (!towerHexes.Add(t.X + "," + t.Z))
                    problems.Add("two watchtowers share hex " + t.X + "," + t.Z);
                if (!IsNeutral(t.OwnerSeatId) && !seatIds.Contains(t.OwnerSeatId))
                    problems.Add("watchtower " + t.Id + " has unknown owner " + t.OwnerSeatId);
            }
            return problems;
        }

        private static bool IsNeutral(string owner) => string.IsNullOrEmpty(owner);

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
                    + ",\"food\":" + (c.FoodOverride.HasValue ? MiniJson.Num(c.FoodOverride.Value) : "null")
                    + ",\"gold\":" + (c.GoldOverride.HasValue ? MiniJson.Num(c.GoldOverride.Value) : "null")
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
}
