using System;
using System.Collections.Generic;
using System.Text;

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
        public int Prestige = BalanceConfig.PrestigeStart;    // 威望, range PrestigeMin..PrestigeMax

        public bool IsHuman => Controller == ControllerType.Human;

        public string ToJson()
        {
            var sb = new StringBuilder(128);
            sb.Append('{');
            sb.Append("\"seat_id\":").Append(MiniJson.Str(SeatId));
            sb.Append(",\"name\":").Append(MiniJson.Str(Name));
            sb.Append(",\"controller_type\":").Append(MiniJson.Str(Controller));
            sb.Append(",\"command_points\":").Append(MiniJson.Num(CommandPoints));
            sb.Append(",\"prestige\":").Append(MiniJson.Num(Prestige));
            sb.Append('}');
            return sb.ToString();
        }
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
        public bool ReclaimedThisSeason;
        public int Morale = BalanceConfig.MoraleStart;          // 民心, range MoraleMin..MoraleMax
        public int Defense = BalanceConfig.DefenseStart;        // 城防, range 0..DefenseMax
        public int Training = BalanceConfig.TrainingStart;      // 練兵, range 0..TrainingMax
        public int ReclaimPct = BalanceConfig.ReclaimPctStart;  // 開墾, range 0..ReclaimPctCap, scales food yield

        public bool IsNeutral => string.IsNullOrEmpty(OwnerSeatId);

        public string ToJson()
        {
            var sb = new StringBuilder(256);
            sb.Append('{');
            sb.Append("\"id\":").Append(MiniJson.Str(Id));
            sb.Append(",\"name\":").Append(MiniJson.Str(Name));
            sb.Append(",\"x\":").Append(MiniJson.Num(X));
            sb.Append(",\"z\":").Append(MiniJson.Num(Z));
            sb.Append(",\"owner_seat_id\":").Append(MiniJson.Str(OwnerSeatId));
            sb.Append(",\"population\":").Append(MiniJson.Num(Population));
            sb.Append(",\"food\":").Append(MiniJson.Num(Food));
            sb.Append(",\"gold\":").Append(MiniJson.Num(Gold));
            sb.Append(",\"garrison\":").Append(MiniJson.Num(Garrison));
            sb.Append(",\"morale\":").Append(MiniJson.Num(Morale));
            sb.Append(",\"defense\":").Append(MiniJson.Num(Defense));
            sb.Append(",\"training\":").Append(MiniJson.Num(Training));
            sb.Append(",\"reclaim_pct\":").Append(MiniJson.Num(ReclaimPct));
            sb.Append('}');
            return sb.ToString();
        }
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

    /// <summary>One treaty record (ACTIONS.md §5). Type ∈ nap 互不侵犯 | alliance 同盟 | truce 停戰.</summary>
    public sealed class TreatyRecord
    {
        public string Type;
        public string FactionA;        // seat id
        public string FactionB;        // seat id
        public int ExpirySeason;       // last season the treaty is in force

        public string ToJson()
        {
            var sb = new StringBuilder(128);
            sb.Append('{');
            sb.Append("\"type\":").Append(MiniJson.Str(Type));
            sb.Append(",\"faction_a\":").Append(MiniJson.Str(FactionA));
            sb.Append(",\"faction_b\":").Append(MiniJson.Str(FactionB));
            sb.Append(",\"expiry_season\":").Append(MiniJson.Num(ExpirySeason));
            sb.Append('}');
            return sb.ToString();
        }
    }

    /// <summary>
    /// 外交 state: per ordered-pair 信任, active treaties, and wars (ACTIONS.md §5, §9).
    /// Everything is keyed by seat id, so nothing is sized by player count — seats enter
    /// and leave through faction config, never through a hardcoded 3 or 8.
    /// </summary>
    public sealed class DiplomacyState
    {
        public readonly Dictionary<string, int> Trust = new Dictionary<string, int>();  // key: PairKey(from, to)
        public readonly List<TreatyRecord> Treaties = new List<TreatyRecord>();
        public readonly List<string> WarWith = new List<string>();                      // key: WarKey(a, b), unordered

        /// <summary>Key of an ordered pair — 信任 is directional (A's view of B ≠ B's view of A).</summary>
        public static string PairKey(string fromSeatId, string toSeatId) => fromSeatId + "|" + toSeatId;

        /// <summary>Order-independent key, so a war between A and B is one fact rather than two.</summary>
        public static string WarKey(string seatA, string seatB)
            => string.CompareOrdinal(seatA, seatB) <= 0 ? seatA + "|" + seatB : seatB + "|" + seatA;

        public int GetTrust(string fromSeatId, string toSeatId)
            => Trust.TryGetValue(PairKey(fromSeatId, toSeatId), out int value) ? value : BalanceConfig.TrustStart;

        public void SetTrust(string fromSeatId, string toSeatId, int value)
            => Trust[PairKey(fromSeatId, toSeatId)] = Clamp(value, BalanceConfig.TrustMin, BalanceConfig.TrustMax);

        public void AddTrust(string fromSeatId, string toSeatId, int delta)
            => SetTrust(fromSeatId, toSeatId, GetTrust(fromSeatId, toSeatId) + delta);

        public bool IsAtWar(string seatA, string seatB) => WarWith.Contains(WarKey(seatA, seatB));

        public void DeclareWar(string seatA, string seatB)
        {
            string key = WarKey(seatA, seatB);
            if (!WarWith.Contains(key)) WarWith.Add(key);
        }

        public void MakePeace(string seatA, string seatB) => WarWith.Remove(WarKey(seatA, seatB));

        public string ToJson()
        {
            var treaties = new List<string>(Treaties.Count);
            foreach (var t in Treaties) treaties.Add(t.ToJson());

            var sb = new StringBuilder(256);
            sb.Append("{\"trust\":").Append(MiniJson.IntMap(Trust));
            sb.Append(",\"treaties\":").Append(MiniJson.Array(treaties));
            sb.Append(",\"war_with\":").Append(MiniJson.StrArray(WarWith));
            sb.Append('}');
            return sb.ToString();
        }

        private static int Clamp(int value, int min, int max)
            => value < min ? min : value > max ? max : value;
    }

    public sealed class GameState
    {
        public string GameId = "g-001";
        public int Season;                       // 1-based once the first season begins
        public string Phase = "setup";           // setup | player_orders | resolution
        public readonly List<FactionState> Factions = new List<FactionState>();
        public readonly DiplomacyState Diplomacy = new DiplomacyState();
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
