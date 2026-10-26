using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using LuanShi.Engine;

// [LS015-W1] Contract A: the seat-safe movement query.
//
// The query must mirror GameEngine.DoMarch exactly (same predicates, same order) while staying
// read-only and hidden-world neutral. The strongest check is brute force: on a fresh fixture per
// probed cell, Submit either accepts a march to that cell or it does not, and that answer must equal
// membership in the advertised (untruncated) option set. Hidden-world pairs pin that canonical facts
// outside the seat's sight change neither the movement view nor the observation.

internal static partial class Harness
{
    private static void RunMovementOptions()
    {
        Section("[LS015-W1] seat-safe movement options (Contract A)");
        MovementBasicsAndEquivalence();
        MovementSightAndOwnership();
        MovementBlockedStates();
        MovementOptionKinds();
        MovementCavalrySecondMarch();
        MovementReadOnlyAndDeterminism();
        MovementTruncation();
        MovementHiddenWorlds();
    }

    /// <summary>Suite-tagged assertion so the new LS-015 count stays distinguishable.</summary>
    private static void MovementCheck(bool ok, string name) => Check(ok, "[LS015-W1] " + name);

    // ---- fixtures -----------------------------------------------------------

    /// <summary>Plain 2D grid with 4-neighbour adjacency (fog distance = Manhattan steps).</summary>
    private static MapData MovementGrid(int width, int height)
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
                int i = map.Index(x, z);
                map.Walkable[i] = true;
                map.Cost[i] = 1;
                var list = new List<int>();
                if (x > 0) list.Add(map.Index(x - 1, z));
                if (x < width - 1) list.Add(map.Index(x + 1, z));
                if (z > 0) list.Add(map.Index(x, z - 1));
                if (z < height - 1) list.Add(map.Index(x, z + 1));
                map.Neighbors[i] = list.ToArray();
            }
        }
        return map;
    }

    /// <summary>seat-a owns city-a (0,0) and army-a-1 at (1,1); seat-b sits far away.</summary>
    private static GameEngine MovementFixture(int seed, int season, string unitType,
        int width = 8, int height = 6, bool hiddenEnemyCity = true)
    {
        var e = new GameEngine(seed);
        e.State.Map = MovementGrid(width, height);
        e.State.Factions.Add(new FactionState
        {
            SeatId = "seat-a", Name = "Wei", Controller = ControllerType.Human,
        });
        e.State.Factions.Add(new FactionState
        {
            SeatId = "seat-b", Name = "Shu", Controller = ControllerType.ScriptedAi,
        });
        e.State.Cities.Add(new CityState
        {
            Id = "city-a", Name = "City A", X = 0, Z = 0, OwnerSeatId = "seat-a",
            Population = 10000, Food = 5000, Gold = 1000, Garrison = 0, Morale = 100,
        });
        e.State.Armies.Add(new ArmyState
        {
            Id = "army-a-1", Name = "Army A", OwnerSeatId = "seat-a", Troops = 2000,
            UnitType = unitType, X = 1, Z = 1,
        });
        if (hiddenEnemyCity)
            e.State.Cities.Add(new CityState
            {
                Id = "city-hidden", Name = "Hidden", X = width - 1, Z = height - 1,
                OwnerSeatId = "seat-b", Population = 10000, Food = 500, Gold = 0, Garrison = 0,
            });
        e.BeginSeason();
        e.State.Season = season;
        e.State.Phase = "player_orders";
        return e;
    }

    private static ArmyMovePlan MovementPlan(MovementView view, string armyId)
    {
        if (view == null || view.Armies == null) return null;
        foreach (var plan in view.Armies) if (plan != null && plan.ArmyId == armyId) return plan;
        return null;
    }

    private static MoveOption MovementOptionAt(ArmyMovePlan plan, int x, int z)
    {
        if (plan == null || plan.Options == null) return null;
        foreach (var option in plan.Options)
            if (option != null && option.X == x && option.Z == z) return option;
        return null;
    }

    private static int MovementListed(MovementView view, string armyId, int x, int z)
        => MovementOptionAt(MovementPlan(view, armyId), x, z) == null ? 0 : 1;

    /// <summary>
    /// Brute-force: for every cell, rebuild the same fixture and ask Submit. Accepted iff the cell is
    /// in the untruncated candidate set (cap 40 is above every fixture's reachable count).
    /// </summary>
    private static void CheckMarchEquivalence(string label, Func<GameEngine> make, string armyId)
    {
        var dims = make();
        int width = dims.State.Map.Width, height = dims.State.Map.Height;
        int accepted = 0, mismatches = 0;
        string firstMismatch = null;
        for (int z = 0; z < height; z++)
        {
            for (int x = 0; x < width; x++)
            {
                var e = make();
                var view = MovementPlanner.Build(e, "seat-a", 40);
                bool listed = MovementListed(view, armyId, x, z) != 0;
                bool truncated = MovementPlan(view, armyId).Truncated;
                var r = e.Submit(MarchOrder(e, "seat-a", armyId, x, z));
                if (truncated || r.Ok != listed)
                {
                    mismatches++;
                    if (firstMismatch == null)
                        firstMismatch = $"({x},{z}) submit={r.Ok} listed={listed} truncated={truncated}";
                }
                if (r.Ok) accepted++;
            }
        }
        MovementCheck(mismatches == 0 && accepted > 0,
            $"{label}: Submit accepts exactly the advertised cells ({accepted} accepted, {mismatches} mismatches"
            + (firstMismatch == null ? ")" : $", first {firstMismatch})"));
    }

    private static void ApplyMovementTerrain(GameEngine e)
    {
        var map = e.State.Map;
        map.Cost[map.Index(2, 0)] = 2;            // forest
        map.Cost[map.Index(2, 1)] = 2;            // forest
        map.Walkable[map.Index(3, 1)] = false;    // river
        map.Walkable[map.Index(0, 2)] = false;    // mountain
    }

    // ---- normal move set + brute-force equivalence -------------------------

    private static void MovementBasicsAndEquivalence()
    {
        var e = MovementFixture(8101, 2, UnitTypes.Infantry);
        var view = MovementPlanner.Build(e, "seat-a");
        MovementCheck(view.SeatId == "seat-a" && view.Season == 2 && view.CommandPoints == 5
           && view.MarchOrderCost == 1 && view.Armies.Count == 1,
            "movement view carries seat, season, orders and the single own army");
        var plan = MovementPlan(view, "army-a-1");
        MovementCheck(plan != null && plan.ArmyName == "Army A" && plan.UnitType == UnitTypes.Infantry
           && plan.X == 1 && plan.Z == 1 && plan.MarchesLeft == 1 && plan.Budget == 6
           && plan.CanMarch && plan.BlockedReason == null,
            "plan carries army identity, position, marches left and the type-aware budget");
        MovementCheck(MovementOptionAt(plan, 0, 0) != null
           && MovementOptionAt(plan, 0, 0).Kind == "move"
           && MovementOptionAt(plan, 0, 0).PathCost == 2,
            "the own city cell is a plain move at its path cost");
        MovementCheck(MovementOptionAt(plan, 2, 0) != null && MovementOptionAt(plan, 2, 0).PathCost == 2,
            "a reachable plains cell is advertised with its cost");
        MovementCheck(MovementOptionAt(plan, 1, 1) == null, "the army's own cell is never advertised");
        MovementCheck(!plan.Truncated && plan.TotalReachable == plan.Options.Count,
            $"a small fixture is untruncated ({plan.TotalReachable}/{plan.Options.Count})");

        bool allSane = true;
        foreach (var option in plan.Options)
        {
            int cost = e.State.Map.PathCost(1, 1, option.X, option.Z, 6);
            if (!e.InSight("seat-a", option.X, option.Z)
                || !e.State.Map.IsWalkable(option.X, option.Z)
                || cost < 0 || cost != option.PathCost)
                allSane = false;
        }
        MovementCheck(allSane, "every advertised cell is walkable, in sight and priced with Map.PathCost");

        // 2-3 fixtures: terrain, winter (x0.7), and the archer/siege/cavalry budgets.
        CheckMarchEquivalence("terrain summer infantry",
            () => { var w = MovementFixture(8102, 2, UnitTypes.Infantry); ApplyMovementTerrain(w); return w; },
            "army-a-1");
        CheckMarchEquivalence("winter infantry (budget x0.7)",
            () => MovementFixture(8103, 4, UnitTypes.Infantry), "army-a-1");
        CheckMarchEquivalence("winter archer",
            () => MovementFixture(8104, 4, UnitTypes.Archer), "army-a-1");
        CheckMarchEquivalence("winter siege",
            () => MovementFixture(8105, 4, UnitTypes.Siege), "army-a-1");
        CheckMarchEquivalence("winter cavalry",
            () => MovementFixture(8106, 4, UnitTypes.Cavalry), "army-a-1");

        MovementCheck(BalanceConfig.MarchBudget(4, UnitTypes.Infantry) == 4
           && BalanceConfig.MarchBudget(4, UnitTypes.Archer) == 4
           && BalanceConfig.MarchBudget(4, UnitTypes.Cavalry) == 5
           && BalanceConfig.MarchBudget(4, UnitTypes.Siege) == 2,
            "winter applies the x0.7 budget to each unit type");
        MovementCheck(MovementPlan(MovementPlanner.Build(MovementFixture(8107, 4, UnitTypes.Cavalry), "seat-a"),
              "army-a-1").Budget == 5
           && MovementPlan(MovementPlanner.Build(MovementFixture(8108, 4, UnitTypes.Siege), "seat-a"),
              "army-a-1").Budget == 2,
            "plans expose the cavalry and siege budgets");
    }

    // ---- sight, ownership and exclusions -----------------------------------

    private static void MovementSightAndOwnership()
    {
        {
            var e = MovementFixture(8201, 2, UnitTypes.Infantry);
            var view = MovementPlanner.Build(e, "seat-a", 40);
            var plan = MovementPlan(view, "army-a-1");
            bool outOfSightListed = false;
            for (int z = 0; z < e.State.Map.Height; z++)
                for (int x = 0; x < e.State.Map.Width; x++)
                    if (!e.InSight("seat-a", x, z) && MovementOptionAt(plan, x, z) != null)
                        outOfSightListed = true;
            MovementCheck(!outOfSightListed, "no cell outside the seat's sight is ever advertised");
            MovementCheck(MovementOptionAt(plan, 5, 0) == null,
                "(5,0) is reachable by terrain but outside sight, so it is not advertised");
            var r = e.Submit(MarchOrder(e, "seat-a", "army-a-1", 5, 0));
            MovementCheck(!r.Ok && r.Error.Contains("beyond your sight"),
                $"and Submit agrees the cell is out of sight ({r.Error})");
        }
        {
            var e = MovementFixture(8202, 2, UnitTypes.Infantry);
            e.State.Cities.Add(new CityState
            {
                Id = "city-n", Name = "Neutral N", X = 5, Z = 0, OwnerSeatId = null,
                Population = 1000, Food = 100, Gold = 0, Garrison = 0,
            });
            MovementCheck(MovementListed(MovementPlanner.Build(e, "seat-a"), "army-a-1", 5, 0) == 0,
                "a neutral city outside sight never influences the advertised options");
            var r = e.Submit(MarchOrder(e, "seat-a", "army-a-1", 5, 0));
            MovementCheck(!r.Ok && r.Error.Contains("beyond your sight"),
                "and its rejection says only that the destination is out of sight");
        }
        {
            var e = MovementFixture(8203, 2, UnitTypes.Infantry);
            AddEnemyArmy(e, "seat-b", 2, 500, UnitTypes.Infantry);   // visible enemy army at (2,0)? no: z=0
            // AddEnemyArmy places the army at (2,0); move it onto the in-sight (2,1) for clarity.
            var enemy = e.State.FindArmy("army-seat-b-1");
            enemy.X = 2; enemy.Z = 1;
            MovementCheck(MovementListed(MovementPlanner.Build(e, "seat-a"), "army-a-1", 2, 1) == 0,
                "a visible enemy army's cell is excluded");
            var r = e.Submit(MarchOrder(e, "seat-a", "army-a-1", 2, 1));
            MovementCheck(!r.Ok && r.Error.Contains("occupied by an army"),
                $"and Submit refuses the occupied destination ({r.Error})");
        }
        {
            var e = MovementFixture(8204, 2, UnitTypes.Infantry);
            e.State.Cities.Add(new CityState
            {
                Id = "city-enemy", Name = "Enemy", X = 2, Z = 1, OwnerSeatId = "seat-b",
                Population = 1000, Food = 100, Gold = 0, Garrison = 100,
            });
            MovementCheck(MovementListed(MovementPlanner.Build(e, "seat-a"), "army-a-1", 2, 1) == 0,
                "a visible enemy city's cell is excluded");
            var r = e.Submit(MarchOrder(e, "seat-a", "army-a-1", 2, 1));
            MovementCheck(!r.Ok && r.Error.Contains("enemy city"),
                $"and Submit refuses the enemy city ({r.Error})");
        }
        {
            var e = MovementFixture(8205, 2, UnitTypes.Infantry);
            e.State.Armies.Add(new ArmyState
            {
                Id = "army-b-1", Name = "B Army", OwnerSeatId = "seat-b", Troops = 500,
                UnitType = UnitTypes.Infantry, X = 6, Z = 5,
            });
            var aView = MovementPlanner.Build(e, "seat-a");
            MovementCheck(MovementPlan(aView, "army-b-1") == null && aView.Armies.Count == 1,
                "seat-a's view never lists a foreign army");
            var bView = MovementPlanner.Build(e, "seat-b");
            MovementCheck(bView.SeatId == "seat-b" && bView.Armies.Count == 1
               && bView.Armies[0].ArmyId == "army-b-1",
                "a query for the other seat returns only that seat's army");
        }
        {
            var e = MovementFixture(8206, 2, UnitTypes.Infantry);
            var unknown = MovementPlanner.Build(e, "seat-zzz");
            MovementCheck(unknown.SeatId == null && unknown.Armies.Count == 0,
                "an unknown seat yields an empty view with SeatId null");
            var nullSeat = MovementPlanner.Build(e, null);
            MovementCheck(nullSeat.SeatId == null && nullSeat.Armies.Count == 0,
                "a null seat yields an empty view without throwing");
            MovementCheck(MovementPlanner.Build(null, "seat-a").SeatId == null,
                "a null engine yields an empty view without throwing");
        }
    }

    // ---- blocked states ----------------------------------------------------

    private static void MovementBlockedStates()
    {
        {
            var e = MovementFixture(8301, 2, UnitTypes.Infantry);
            e.State.FindFaction("seat-a").CommandPoints = 0;
            var view = MovementPlanner.Build(e, "seat-a");
            var plan = MovementPlan(view, "army-a-1");
            MovementCheck(!plan.CanMarch && plan.BlockedReason == "no orders left"
               && plan.Options.Count == 0 && plan.TotalReachable == 0 && !plan.Truncated,
                "no command points blocks the army with a safe reason and no options");
            var r = e.Submit(MarchOrder(e, "seat-a", "army-a-1", 2, 0));
            MovementCheck(!r.Ok && r.Error.Contains("no command points left"),
                $"and Submit refuses for want of orders ({r.Error})");

            e.State.FindFaction("seat-a").CommandPoints = 1;
            var one = MovementPlan(MovementPlanner.Build(e, "seat-a"), "army-a-1");
            MovementCheck(one.CanMarch && one.BlockedReason == null && one.Options.Count > 0,
                "one order is enough to unblock the army");
        }
        {
            var e = MovementFixture(8302, 2, UnitTypes.Infantry);
            e.State.FindArmy("army-a-1").MarchCountThisSeason =
                UnitTypes.MarchesPerSeason(UnitTypes.Infantry);
            var plan = MovementPlan(MovementPlanner.Build(e, "seat-a"), "army-a-1");
            MovementCheck(!plan.CanMarch && plan.MarchesLeft == 0
               && plan.BlockedReason == "already marched this season"
               && plan.Options.Count == 0,
                "a spent army is blocked with 'already marched this season'");
            var r = e.Submit(MarchOrder(e, "seat-a", "army-a-1", 2, 0));
            MovementCheck(!r.Ok && r.Error.Contains("already marched this season"),
                $"and Submit refuses the second march ({r.Error})");
        }
    }

    // ---- option kinds and ordering -----------------------------------------

    private static void MovementOptionKinds()
    {
        {
            var e = MovementFixture(8401, 2, UnitTypes.Infantry);
            e.State.Cities.Add(new CityState
            {
                Id = "city-n", Name = "Neutral N", X = 2, Z = 0, OwnerSeatId = null,
                Population = 1000, Food = 100, Gold = 0, Garrison = 0,
            });
            var option = MovementOptionAt(MovementPlan(MovementPlanner.Build(e, "seat-a"), "army-a-1"), 2, 0);
            MovementCheck(option != null && option.Kind == "capture_neutral_city"
               && option.CityId == "city-n" && option.TowerId == null && option.PathCost == 2,
                "a neutral city cell is a capture_neutral_city carrying the observable city id");
            var r = e.Submit(MarchOrder(e, "seat-a", "army-a-1", 2, 0));
            MovementCheck(r.Ok && e.State.FindCity("city-n").OwnerSeatId == "seat-a"
               && Find(e.Log, "city_captured") != null,
                $"marching onto the advertised neutral city captures it ({r.Error})");
        }
        {
            var e = MovementFixture(8402, 2, UnitTypes.Infantry);
            e.State.WatchTowers.Add(new WatchTowerState
            {
                Id = "tower-b", Name = "Enemy Tower", X = 3, Z = 1, OwnerSeatId = "seat-b",
            });
            e.State.WatchTowers.Add(new WatchTowerState
            {
                Id = "tower-a", Name = "Own Tower", X = 0, Z = 3, OwnerSeatId = "seat-a",
            });
            // The own tower widens sight (range 5), so raise the cap to keep every candidate visible.
            var view = MovementPlanner.Build(e, "seat-a", 40);
            var plan = MovementPlan(view, "army-a-1");
            var hostile = MovementOptionAt(plan, 3, 1);
            var own = MovementOptionAt(plan, 0, 3);
            MovementCheck(hostile != null && hostile.Kind == "capture_tower"
               && hostile.TowerId == "tower-b" && hostile.CityId == null,
                "a non-own tower cell is a capture_tower carrying the observable tower id");
            MovementCheck(own != null && own.Kind == "move" && own.TowerId == null,
                "standing on an own tower is a plain move, not a capture");
            var r = e.Submit(MarchOrder(e, "seat-a", "army-a-1", 3, 1));
            MovementCheck(r.Ok && e.State.WatchTowerAt(3, 1)?.OwnerSeatId == "seat-a"
               && Find(e.Log, "watch_tower_captured") != null,
                $"marching onto the advertised tower captures it ({r.Error})");
        }
        {
            var e = MovementFixture(8403, 2, UnitTypes.Infantry);
            e.State.Cities.Add(new CityState
            {
                Id = "city-n", Name = "Neutral N", X = 0, Z = 3, OwnerSeatId = null,
                Population = 1000, Food = 100, Gold = 0, Garrison = 0,
            });
            var plan = MovementPlan(MovementPlanner.Build(e, "seat-a"), "army-a-1");
            MovementCheck(plan.Options.Count > 0 && plan.Options[0].X == 0 && plan.Options[0].Z == 3
               && plan.Options[0].Kind == "capture_neutral_city"
               && plan.Options[0].PathCost > 1,
                "captures sort before cheaper plain moves (contract order)");

            bool ordered = true;
            bool sawMove = false;
            MoveOption prev = null;
            foreach (var option in plan.Options)
            {
                if (option.Kind == "move") sawMove = true;
                else if (sawMove) ordered = false;   // a capture after a move breaks captures-first
                if (prev != null)
                {
                    bool prevCapture = prev.Kind != "move", capture = option.Kind != "move";
                    int cmp = !prevCapture && capture ? 1
                            : prevCapture && !capture ? -1
                            : prev.PathCost != option.PathCost ? prev.PathCost.CompareTo(option.PathCost)
                            : prev.Z != option.Z ? prev.Z.CompareTo(option.Z)
                            : prev.X.CompareTo(option.X);
                    if (cmp > 0) ordered = false;
                }
                prev = option;
            }
            MovementCheck(ordered, "options are captures first, then cost ascending, then Z, then X");
        }
    }

    private static void MovementCavalrySecondMarch()
    {
        var e = MovementFixture(8501, 2, UnitTypes.Cavalry);
        var fresh = MovementPlan(MovementPlanner.Build(e, "seat-a"), "army-a-1");
        MovementCheck(fresh.Budget == 8 && fresh.MarchesLeft == 2 && fresh.CanMarch,
            "cavalry starts with two marches and an 8-point budget");
        var first = e.Submit(MarchOrder(e, "seat-a", "army-a-1", 2, 1));
        MovementCheck(first.Ok, $"the first cavalry march is accepted ({first.Error})");
        var afterFirst = MovementPlan(MovementPlanner.Build(e, "seat-a"), "army-a-1");
        MovementCheck(afterFirst.MarchesLeft == 1 && afterFirst.CanMarch
           && afterFirst.Options.Count > 0 && afterFirst.X == 2 && afterFirst.Z == 1,
            "after one march the cavalry still advertises a second march from its new cell");
        var second = e.Submit(MarchOrder(e, "seat-a", "army-a-1", 2, 2));
        MovementCheck(second.Ok, $"the second cavalry march is accepted ({second.Error})");
        var afterSecond = MovementPlan(MovementPlanner.Build(e, "seat-a"), "army-a-1");
        MovementCheck(afterSecond.MarchesLeft == 0 && !afterSecond.CanMarch
           && afterSecond.BlockedReason == "already marched this season"
           && afterSecond.Options.Count == 0,
            "after two marches the cavalry is blocked with no options");
        var third = e.Submit(MarchOrder(e, "seat-a", "army-a-1", 2, 3));
        MovementCheck(!third.Ok && third.Error.Contains("already marched 2 times"),
            $"a third cavalry march is refused ({third.Error})");

        // The second-march query must still mirror Submit exactly.
        CheckMarchEquivalence("cavalry second march",
            () =>
            {
                var w = MovementFixture(8502, 2, UnitTypes.Cavalry);
                var army = w.State.FindArmy("army-a-1");
                army.MarchCountThisSeason = 1;
                army.MarchedThisSeason = true;
                army.X = 2; army.Z = 1;
                return w;
            }, "army-a-1");
    }

    // ---- read-only + determinism -------------------------------------------

    private static GameEngine ReadOnlyWorld()
    {
        var e = MovementFixture(8601, 2, UnitTypes.Cavalry);
        e.State.Cities.Add(new CityState
        {
            Id = "city-n", Name = "Neutral N", X = 2, Z = 0, OwnerSeatId = null,
            Population = 1000, Food = 100, Gold = 0, Garrison = 0,
        });
        e.State.WatchTowers.Add(new WatchTowerState
        {
            Id = "tower-b", Name = "Enemy Tower", X = 3, Z = 1, OwnerSeatId = "seat-b",
        });
        return e;
    }

    private static string MovementFingerprint(GameEngine e)
    {
        var sb = new StringBuilder();
        sb.Append(e.State.Season).Append('|').Append(e.State.Phase).Append('|')
          .Append(e.State.WinnerSeatId ?? "-").Append('\n');
        foreach (var c in e.State.Cities) sb.Append(c.ToJson()).Append('\n');
        foreach (var a in e.State.Armies) sb.Append(a.ToJson()).Append('\n');
        foreach (var t in e.State.WatchTowers) sb.Append(t.ToJson()).Append('\n');
        foreach (var f in e.State.Factions) sb.Append(f.ToJson()).Append('\n');
        sb.Append(e.State.Diplomacy.ToJson()).Append('\n');
        sb.Append(e.Log.ToJsonLines());
        return sb.ToString();
    }

    private static object MovementPrivateField(GameEngine e, string name)
    {
        var field = typeof(GameEngine).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
        return field == null ? null : field.GetValue(e);
    }

    private static void MovementReadOnlyAndDeterminism()
    {
        var e = ReadOnlyWorld();
        var twin = ReadOnlyWorld();   // identical seed + fixture, never queried

        string before = MovementFingerprint(e);
        int logBefore = e.Log.Records.Count;
        int cpBefore = e.State.FindFaction("seat-a").CommandPoints;
        object actionIdBefore = MovementPrivateField(e, "nextActionId");

        string first = MovementPlanner.Build(e, "seat-a").ToJson();
        string second = MovementPlanner.Build(e, "seat-a").ToJson();
        MovementPlanner.Build(e, "seat-a", 3);
        MovementPlanner.Build(e, "unknown-seat");

        MovementCheck(MovementFingerprint(e) == before && e.Log.Records.Count == logBefore,
            "Build mutates no canonical state and emits no events");
        MovementCheck(e.State.FindFaction("seat-a").CommandPoints == cpBefore,
            "Build spends no command points");
        MovementCheck(Equals(MovementPrivateField(e, "nextActionId"), actionIdBefore)
           && Convert.ToInt32(actionIdBefore) == 1,
            "Build does not advance the action id counter");

        var rngA = (Random)MovementPrivateField(e, "rng");
        var rngB = (Random)MovementPrivateField(twin, "rng");
        bool rngSame = rngA != null && rngB != null;
        for (int i = 0; rngSame && i < 8; i++) rngSame = rngA.NextDouble() == rngB.NextDouble();
        MovementCheck(rngSame, "Build consumes no RNG (the untouched twin yields the same sequence)");

        MovementCheck(first == second, "two Builds on the same state serialise identically");
        MovementCheck(MovementPlanner.Build(e, "seat-a").ToJson() == first,
            "a later Build is unchanged by earlier Builds");
    }

    // ---- truncation --------------------------------------------------------

    private static GameEngine TruncationWorld()
    {
        // 10x10 open grid fully covered by own towers; cavalry reaches ~90 cells at budget 8.
        var e = MovementFixture(8701, 2, UnitTypes.Cavalry, 10, 10, hiddenEnemyCity: false);
        var army = e.State.FindArmy("army-a-1");
        army.X = 4; army.Z = 4;
        e.State.WatchTowers.Add(new WatchTowerState { Id = "tw-1", Name = "T1", X = 9, Z = 0, OwnerSeatId = "seat-a" });
        e.State.WatchTowers.Add(new WatchTowerState { Id = "tw-2", Name = "T2", X = 0, Z = 9, OwnerSeatId = "seat-a" });
        e.State.WatchTowers.Add(new WatchTowerState { Id = "tw-3", Name = "T3", X = 9, Z = 9, OwnerSeatId = "seat-a" });
        e.State.WatchTowers.Add(new WatchTowerState { Id = "tw-4", Name = "T4", X = 5, Z = 5, OwnerSeatId = "seat-a" });
        return e;
    }

    private static void MovementTruncation()
    {
        var e = TruncationWorld();
        var defaultView = MovementPlanner.Build(e, "seat-a");
        var defaultPlan = MovementPlan(defaultView, "army-a-1");
        var capped = MovementPlan(MovementPlanner.Build(e, "seat-a", 40), "army-a-1");
        var small = MovementPlan(MovementPlanner.Build(e, "seat-a", 3), "army-a-1");
        var tiny = MovementPlan(MovementPlanner.Build(e, "seat-a", 0), "army-a-1");
        var huge = MovementPlan(MovementPlanner.Build(e, "seat-a", 100), "army-a-1");

        MovementCheck(capped.TotalReachable > 40,
            $"the fixture has more candidates than the hard cap ({capped.TotalReachable})");
        MovementCheck(defaultPlan.Options.Count == 12 && defaultPlan.Truncated,
            $"the default cap advertises 12 and flags truncation ({defaultPlan.Options.Count})");
        MovementCheck(capped.Options.Count == 40 && capped.Truncated
           && capped.TotalReachable == defaultPlan.TotalReachable,
            "the hard cap is 40 and TotalReachable is cap-independent");
        MovementCheck(small.Options.Count == 3 && small.Truncated
           && small.TotalReachable == capped.TotalReachable,
            "a smaller cap yields an explicit, counted truncation");
        MovementCheck(tiny.Options.Count == 1 && huge.Options.Count == 40,
            "caps below 1 clamp to 1 and above 40 clamp to 40");

        bool prefix = true;
        for (int i = 0; i < 3 && i < capped.Options.Count; i++)
        {
            var a = small.Options[i];
            var b = capped.Options[i];
            if (a.X != b.X || a.Z != b.Z || a.PathCost != b.PathCost || a.Kind != b.Kind
                || a.CityId != b.CityId || a.TowerId != b.TowerId)
                prefix = false;
        }
        MovementCheck(prefix, "truncation keeps a stable prefix of the full ordering");
    }

    // ---- paired hidden worlds ---------------------------------------------

    /// <summary>seat-a sees city-a (radius 3) and army-a-1 (radius 2); none of the hidden variants is in range.</summary>
    private static GameEngine HiddenWorld(int seed, string variant)
    {
        var e = MovementFixture(seed, 2, UnitTypes.Infantry, hiddenEnemyCity: false);
        switch (variant)
        {
            case "army":
                e.State.Armies.Add(new ArmyState
                {
                    Id = "army-hidden", Name = "Hidden", OwnerSeatId = "seat-b", Troops = 500,
                    UnitType = UnitTypes.Infantry, X = 6, Z = 5,
                });
                break;
            case "adjacent":
                // (2,3) is adjacent to the in-sight (2,2) but itself outside every own sight radius.
                e.State.Armies.Add(new ArmyState
                {
                    Id = "army-hidden", Name = "Hidden", OwnerSeatId = "seat-b", Troops = 500,
                    UnitType = UnitTypes.Infantry, X = 2, Z = 3,
                });
                break;
            case "city":
                e.State.Cities.Add(new CityState
                {
                    Id = "city-hidden", Name = "Hidden", X = 6, Z = 5, OwnerSeatId = "seat-b",
                    Population = 1000, Food = 100, Gold = 0, Garrison = 0,
                });
                break;
            case "tower":
                e.State.WatchTowers.Add(new WatchTowerState
                {
                    Id = "tower-hidden", Name = "Hidden", X = 0, Z = 5, OwnerSeatId = "seat-b",
                });
                break;
        }
        return e;
    }

    private static void MovementHiddenWorlds()
    {
        var baseline = HiddenWorld(8801, "baseline");
        string baseView = MovementPlanner.Build(baseline, "seat-a").ToJson();
        string baseObs = baseline.Observe("seat-a").ToJson();
        MovementCheck(MovementPlan(MovementPlanner.Build(baseline, "seat-a"), "army-a-1").Options.Count > 0,
            "the hidden-world baseline advertises options");

        string[] variants = { "army", "adjacent", "city", "tower" };
        foreach (string variant in variants)
        {
            var world = HiddenWorld(8801, variant);
            bool hiddenSeen = false;
            for (int z = 0; z < world.State.Map.Height; z++)
                for (int x = 0; x < world.State.Map.Width; x++)
                    if (world.InSight("seat-a", x, z)
                        && (world.State.ArmyAt(x, z)?.OwnerSeatId == "seat-b"
                            || world.State.CityAt(x, z)?.OwnerSeatId == "seat-b"
                            || world.State.WatchTowerAt(x, z)?.OwnerSeatId == "seat-b"))
                        hiddenSeen = true;
            MovementCheck(!hiddenSeen && world.Observe("seat-a").ToJson() == baseObs,
                $"hidden {variant}: the observation is byte-identical");
            MovementCheck(MovementPlanner.Build(world, "seat-a").ToJson() == baseView,
                $"hidden {variant}: the movement view is byte-identical");
        }

        var adjacent = HiddenWorld(8801, "adjacent");
        MovementCheck(!adjacent.InSight("seat-a", 2, 3) && adjacent.InSight("seat-a", 2, 2),
            "the adjacent hidden army really is just outside sight, next to a visible cell");
    }
}
