using System;
using System.Collections.Generic;
using LuanShi.Engine;

internal static partial class Harness
{
    private static MapData Corridor(int width)
    {
        var map = new MapData { Width = width, Height = 1, Walkable = new bool[width], Cost = new int[width], Neighbors = new int[width][] };
        for (int x = 0; x < width; x++)
        {
            map.Walkable[x] = true;
            map.Cost[x] = 1;
            var list = new List<int>();
            if (x > 0) list.Add(x - 1);
            if (x < width - 1) list.Add(x + 1);
            map.Neighbors[x] = list.ToArray();
        }
        return map;
    }

    /// <summary>One human faction "seat-a" owning city-a at (0,0); state pinned to `season`.
    /// `sight` adds an owned watchtower at the far end so the corridor is fully visible — the fog-edge
    /// march rule (2026-10-20) means path-budget tests need vision to reach their destination.</summary>
    private static GameEngine Build(int seed, int season, int morale, int food, int garrison = 0, int population = 10000,
        bool sight = true)
    {
        var e = new GameEngine(seed);
        e.State.Map = Corridor(10);
        e.State.Factions.Add(new FactionState { SeatId = "seat-a", Name = "Wei", Controller = ControllerType.Human });
        e.State.Cities.Add(new CityState
        {
            Id = "city-a", Name = "City A", X = 0, Z = 0, OwnerSeatId = "seat-a",
            Population = population, Food = food, Gold = 1000, Garrison = garrison, Morale = morale,
        });
        if (sight)
            e.State.WatchTowers.Add(new WatchTowerState
            {
                Id = "tower-a", Name = "Tower", X = 9, Z = 0, OwnerSeatId = "seat-a",
            });
        e.BeginSeason();
        e.State.Season = season;
        e.State.Phase = "player_orders";
        return e;
    }

    private static ArmyState AddArmy(GameEngine e, string seat, int x, int troops)
    {
        var army = new ArmyState { Id = "army-" + seat + "-1", Name = "Army", OwnerSeatId = seat, Troops = troops, X = x, Z = 0 };
        e.State.Armies.Add(army);
        return army;
    }

    private static EventRecord Find(EventLog log, string type)
    {
        foreach (var r in Log(log)) if (r.EventType == type) return r;
        return null;
    }

    private static EventRecord Find(EventLog log, string type, int season)
    {
        foreach (var r in Log(log)) if (r.EventType == type && r.Season == season) return r;
        return null;
    }

    private static IEnumerable<EventRecord> Log(EventLog log) => log.Records;

    private static int CountEvents(EventLog log, string type)
    {
        int n = 0;
        foreach (var r in Log(log)) if (r.EventType == type) n++;
        return n;
    }

    private static bool HasPlan(GameEngine e, string type)
    {
        foreach (var cmd in ScriptedController.PlanTurn(e.State, "seat-a"))
            if (cmd.Type == type) return true;
        return false;
    }
}
