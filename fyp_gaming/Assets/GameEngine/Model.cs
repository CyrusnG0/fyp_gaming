using System;
using System.Collections.Generic;

namespace LuanShi.Engine
{
    /// <summary>Controller kinds from CONTROLLER_PROTOCOL.md §1.</summary>
    public static class ControllerType
    {
        public const string Human = "human";
        public const string Llm = "llm";
        public const string ScriptedAi = "scripted_ai";
    }

    public sealed class FactionState
    {
        public string SeatId;          // "seat-0", "seat-1", ...
        public string Name;            // 魏 / 蜀 / ...
        public string Controller;      // ControllerType.*
        public int CommandPoints;

        public bool IsHuman => Controller == ControllerType.Human;
    }

    public sealed class CityState
    {
        public string Id;              // "city-wei", ...
        public string Name;            // 魏都 ...
        public int X, Z;               // hex coords
        public string OwnerSeatId;     // null/"" = neutral
        public int Population;
        public int Food;
        public int Gold;
        public int Garrison;
        public bool FarmedThisSeason;

        public bool IsNeutral => string.IsNullOrEmpty(OwnerSeatId);
    }

    public sealed class ArmyState
    {
        public string Id;              // "army-wei-1", ...
        public string Name;
        public string OwnerSeatId;
        public int Troops;
        public int X, Z;
        public bool MarchedThisSeason;
    }

    /// <summary>
    /// Engine-owned copy of the strategic map. The Unity adapter builds this once from
    /// the TBTK grid; the engine never touches Unity types. Neighbors are hex-adjacency
    /// (precomputed by the adapter via cube-coordinate distance).
    /// </summary>
    public sealed class MapData
    {
        public int Width, Height;
        public bool[] Walkable;
        public int[] Cost;
        public int[][] Neighbors;

        public int Index(int x, int z) => z * Width + x;
        public bool InBounds(int x, int z) => x >= 0 && z >= 0 && x < Width && z < Height;
        public bool IsWalkable(int x, int z) => InBounds(x, z) && Walkable[Index(x, z)];

        /// <summary>Dijkstra over hex costs. Returns total cost, or -1 if unreachable within maxCost.</summary>
        public int PathCost(int fromX, int fromZ, int toX, int toZ, int maxCost)
        {
            var path = FindPath(fromX, fromZ, toX, toZ, maxCost);
            if (path == null || path.Count == 0) return -1;
            int total = 0;
            for (int i = 1; i < path.Count; i++) total += Cost[path[i]];
            return total;
        }

        /// <summary>Dijkstra; returns node indices from start to destination inclusive, or null.</summary>
        public List<int> FindPath(int fromX, int fromZ, int toX, int toZ, int maxCost)
        {
            if (!InBounds(fromX, fromZ) || !InBounds(toX, toZ) || !IsWalkable(toX, toZ)) return null;
            int start = Index(fromX, fromZ), goal = Index(toX, toZ);
            if (start == goal) return new List<int> { start };

            int n = Width * Height;
            var dist = new int[n];
            var prev = new int[n];
            var done = new bool[n];
            for (int i = 0; i < n; i++) { dist[i] = int.MaxValue; prev[i] = -1; }
            dist[start] = 0;

            for (;;)
            {
                int cur = -1, best = int.MaxValue;
                for (int i = 0; i < n; i++)
                    if (!done[i] && dist[i] < best) { best = dist[i]; cur = i; }
                if (cur < 0) return null;               // unreachable
                if (cur == goal) break;
                if (best > maxCost) return null;        // beyond this season's reach
                done[cur] = true;

                foreach (int nb in Neighbors[cur])
                {
                    if (done[nb] || !Walkable[nb]) continue;
                    int nd = dist[cur] + Cost[nb];
                    if (nd < dist[nb]) { dist[nb] = nd; prev[nb] = cur; }
                }
            }

            if (dist[goal] > maxCost) return null;
            var path = new List<int>();
            for (int at = goal; at >= 0; at = prev[at]) path.Add(at);
            path.Reverse();
            return path;
        }
    }

    public sealed class GameState
    {
        public string GameId = "g-001";
        public int Season;                       // 1-based once the first season begins
        public string Phase = "setup";           // setup | player_orders | resolution
        public readonly List<FactionState> Factions = new List<FactionState>();
        public readonly List<CityState> Cities = new List<CityState>();
        public readonly List<ArmyState> Armies = new List<ArmyState>();
        public MapData Map;

        public FactionState FindFaction(string seatId) => Factions.Find(f => f.SeatId == seatId);
        public CityState FindCity(string id) => Cities.Find(c => c.Id == id);
        public ArmyState FindArmy(string id) => Armies.Find(a => a.Id == id);
        public CityState CityAt(int x, int z) => Cities.Find(c => c.X == x && c.Z == z);
        public ArmyState ArmyAt(int x, int z) => Armies.Find(a => a.X == x && a.Z == z);

        public List<CityState> CitiesOf(string seatId) => Cities.FindAll(c => c.OwnerSeatId == seatId);
        public List<ArmyState> ArmiesOf(string seatId) => Armies.FindAll(a => a.OwnerSeatId == seatId);
    }
}
