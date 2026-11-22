using System;
using System.Collections.Generic;
using System.Text;

namespace LuanShi.Engine
{
    /// <summary>Public map dimensions, coordinate axes and engine sight radii.</summary>
    public sealed class SpatialMapBounds
    {
        public int Width;
        public int Height;
        public string HorizontalAxis = "x (0=West .. Width-1=East)";
        public string VerticalAxis = "z (0=South .. Height-1=North)";
        public int FogCityRange;
        public int FogArmyRange;
        public int FogWatchTowerRange;
        public int CenterX;
        public int CenterZ;

        public string ToJson()
        {
            var sb = new StringBuilder(224);
            sb.Append('{');
            sb.Append("\"width\":").Append(MiniJson.Num(Width));
            sb.Append(",\"height\":").Append(MiniJson.Num(Height));
            sb.Append(",\"horizontal_axis\":").Append(MiniJson.Str(HorizontalAxis));
            sb.Append(",\"vertical_axis\":").Append(MiniJson.Str(VerticalAxis));
            sb.Append(",\"fog_city_range\":").Append(MiniJson.Num(FogCityRange));
            sb.Append(",\"fog_army_range\":").Append(MiniJson.Num(FogArmyRange));
            sb.Append(",\"fog_watch_tower_range\":").Append(MiniJson.Num(FogWatchTowerRange));
            sb.Append(",\"center_x\":").Append(MiniJson.Num(CenterX));
            sb.Append(",\"center_z\":").Append(MiniJson.Num(CenterZ));
            sb.Append('}');
            return sb.ToString();
        }
    }

    /// <summary>Currently visible entity copied from the seat-filtered Observation.</summary>
    public sealed class SpatialEntitySummary
    {
        public string Kind;          // "city" | "army" | "watch_tower"
        public string Id;
        public string Name;
        public string OwnerSeatId;
        public string Allegiance;    // "own" | "neutral" | "foreign"
        public string Knowledge = "visible";
        public int X, Z;
        public int Strength;
        public string UnitType;
        public int WestEdgeDist, EastEdgeDist, SouthEdgeDist, NorthEdgeDist;
        public string NearEdges;
        public int CenterDistance;
        public string CenterDirection = "";
        public int CenterDeltaX, CenterDeltaZ;
        public string RefId = "";
        public int RefDistance;
        public string RefDirection = "";

        public string ToJson()
        {
            var sb = new StringBuilder(320);
            sb.Append('{');
            sb.Append("\"kind\":").Append(MiniJson.Str(Kind));
            sb.Append(",\"id\":").Append(MiniJson.Str(Id));
            sb.Append(",\"name\":").Append(MiniJson.Str(Name));
            sb.Append(",\"owner_seat_id\":").Append(MiniJson.Str(OwnerSeatId));
            sb.Append(",\"allegiance\":").Append(MiniJson.Str(Allegiance));
            sb.Append(",\"knowledge\":").Append(MiniJson.Str(Knowledge));
            sb.Append(",\"x\":").Append(MiniJson.Num(X));
            sb.Append(",\"z\":").Append(MiniJson.Num(Z));
            sb.Append(",\"strength\":").Append(MiniJson.Num(Strength));
            sb.Append(",\"unit_type\":").Append(MiniJson.Str(UnitType));
            sb.Append(",\"west_edge_dist\":").Append(MiniJson.Num(WestEdgeDist));
            sb.Append(",\"east_edge_dist\":").Append(MiniJson.Num(EastEdgeDist));
            sb.Append(",\"south_edge_dist\":").Append(MiniJson.Num(SouthEdgeDist));
            sb.Append(",\"north_edge_dist\":").Append(MiniJson.Num(NorthEdgeDist));
            sb.Append(",\"near_edges\":").Append(MiniJson.Str(NearEdges));
            sb.Append(",\"center_distance\":").Append(MiniJson.Num(CenterDistance));
            sb.Append(",\"center_direction\":").Append(MiniJson.Str(CenterDirection));
            sb.Append(",\"ref_id\":").Append(MiniJson.Str(RefId));
            sb.Append(",\"ref_distance\":").Append(MiniJson.Num(RefDistance));
            sb.Append(",\"ref_direction\":").Append(MiniJson.Str(RefDirection));
            sb.Append('}');
            return sb.ToString();
        }
    }

    /// <summary>Previously discovered or scouted intel outside current live sight.</summary>
    public sealed class SpatialStaleRecord
    {
        public string Kind;          // "scouted_city" | "army_last_seen"
        public string Id;
        public string Name;
        public string OwnerSeatId;
        public int X, Z;
        public int RecordedSeason;
        public int ValidUntilSeason = -1;
        public string Knowledge = "stale";
        public string Summary;

        public string ToJson()
        {
            var sb = new StringBuilder(224);
            sb.Append('{');
            sb.Append("\"kind\":").Append(MiniJson.Str(Kind));
            sb.Append(",\"id\":").Append(MiniJson.Str(Id));
            sb.Append(",\"name\":").Append(MiniJson.Str(Name));
            sb.Append(",\"owner_seat_id\":").Append(MiniJson.Str(OwnerSeatId));
            sb.Append(",\"x\":").Append(MiniJson.Num(X));
            sb.Append(",\"z\":").Append(MiniJson.Num(Z));
            sb.Append(",\"recorded_season\":").Append(MiniJson.Num(RecordedSeason));
            sb.Append(",\"valid_until_season\":").Append(MiniJson.Num(ValidUntilSeason));
            sb.Append(",\"knowledge\":").Append(MiniJson.Str(Knowledge));
            sb.Append(",\"summary\":").Append(MiniJson.Str(Summary));
            sb.Append('}');
            return sb.ToString();
        }
    }

    /// <summary>Distance and compass bearing from one own army to a known objective.</summary>
    public sealed class SpatialObjectiveBearing
    {
        public string ArmyId;
        public string TargetKind;
        public string TargetId;
        public string TargetName;
        public string Knowledge;     // "visible" | "stale"
        public int TargetX, TargetZ;
        public int GridDistance;
        public int DeltaX, DeltaZ;
        public string Direction;

        public string ToJson()
        {
            var sb = new StringBuilder(224);
            sb.Append('{');
            sb.Append("\"army_id\":").Append(MiniJson.Str(ArmyId));
            sb.Append(",\"target_kind\":").Append(MiniJson.Str(TargetKind));
            sb.Append(",\"target_id\":").Append(MiniJson.Str(TargetId));
            sb.Append(",\"target_name\":").Append(MiniJson.Str(TargetName));
            sb.Append(",\"knowledge\":").Append(MiniJson.Str(Knowledge));
            sb.Append(",\"target_x\":").Append(MiniJson.Num(TargetX));
            sb.Append(",\"target_z\":").Append(MiniJson.Num(TargetZ));
            sb.Append(",\"grid_distance\":").Append(MiniJson.Num(GridDistance));
            sb.Append(",\"dx\":").Append(MiniJson.Num(DeltaX));
            sb.Append(",\"dz\":").Append(MiniJson.Num(DeltaZ));
            sb.Append(",\"direction\":").Append(MiniJson.Str(Direction));
            sb.Append('}');
            return sb.ToString();
        }
    }

    /// <summary>Per-army boundary directions toward unseen space vs nearby map borders.</summary>
    public sealed class SpatialArmyFrontier
    {
        public string ArmyId;
        public int X, Z;
        public string FrontierDirections;
        public string BorderDirections;
        public int FrontierCellCount;

        public string ToJson()
        {
            var sb = new StringBuilder(192);
            sb.Append('{');
            sb.Append("\"army_id\":").Append(MiniJson.Str(ArmyId));
            sb.Append(",\"x\":").Append(MiniJson.Num(X));
            sb.Append(",\"z\":").Append(MiniJson.Num(Z));
            sb.Append(",\"frontier_directions\":").Append(MiniJson.Str(FrontierDirections));
            sb.Append(",\"border_directions\":").Append(MiniJson.Str(BorderDirections));
            sb.Append(",\"frontier_cell_count\":").Append(MiniJson.Num(FrontierCellCount));
            sb.Append('}');
            return sb.ToString();
        }
    }

    /// <summary>Current-sight coverage vs currently unseen space, plus explicit history limitations.</summary>
    public sealed class SpatialFrontierSummary
    {
        public int VisibleCellCount;
        public int TotalCellCount;
        public int CurrentlyUnseenCellCount;
        public int SightBoundaryCellCount;
        public bool HasCurrentlyUnseen;
        public int UnseenMinX = -1, UnseenMaxX = -1, UnseenMinZ = -1, UnseenMaxZ = -1;
        public int VisibleMinX = -1, VisibleMaxX = -1, VisibleMinZ = -1, VisibleMaxZ = -1;
        public int WestUnseenCols, EastUnseenCols, SouthUnseenCols, NorthUnseenCols;
        public IReadOnlyList<string> VisibleRowRanges;
        public IReadOnlyList<string> VisibleTerrainNotes;
        public string FrontierDirectionsFromOwn;
        public string HistoryLimitationNote;

        public string ToJson()
        {
            var sb = new StringBuilder(384);
            sb.Append('{');
            sb.Append("\"visible_cells\":").Append(MiniJson.Num(VisibleCellCount));
            sb.Append(",\"total_cells\":").Append(MiniJson.Num(TotalCellCount));
            sb.Append(",\"currently_unseen_cells\":").Append(MiniJson.Num(CurrentlyUnseenCellCount));
            sb.Append(",\"sight_boundary_cells\":").Append(MiniJson.Num(SightBoundaryCellCount));
            sb.Append(",\"has_currently_unseen\":").Append(HasCurrentlyUnseen ? "true" : "false");
            sb.Append(",\"unseen_min_x\":").Append(MiniJson.Num(UnseenMinX));
            sb.Append(",\"unseen_max_x\":").Append(MiniJson.Num(UnseenMaxX));
            sb.Append(",\"unseen_min_z\":").Append(MiniJson.Num(UnseenMinZ));
            sb.Append(",\"unseen_max_z\":").Append(MiniJson.Num(UnseenMaxZ));
            sb.Append(",\"visible_min_x\":").Append(MiniJson.Num(VisibleMinX));
            sb.Append(",\"visible_max_x\":").Append(MiniJson.Num(VisibleMaxX));
            sb.Append(",\"visible_min_z\":").Append(MiniJson.Num(VisibleMinZ));
            sb.Append(",\"visible_max_z\":").Append(MiniJson.Num(VisibleMaxZ));
            sb.Append(",\"west_unseen_cols\":").Append(MiniJson.Num(WestUnseenCols));
            sb.Append(",\"east_unseen_cols\":").Append(MiniJson.Num(EastUnseenCols));
            sb.Append(",\"south_unseen_cols\":").Append(MiniJson.Num(SouthUnseenCols));
            sb.Append(",\"north_unseen_cols\":").Append(MiniJson.Num(NorthUnseenCols));
            sb.Append(",\"visible_row_ranges\":").Append(JsonStrList(VisibleRowRanges));
            sb.Append(",\"visible_terrain_notes\":").Append(JsonStrList(VisibleTerrainNotes));
            sb.Append(",\"frontier_directions_from_own\":").Append(MiniJson.Str(FrontierDirectionsFromOwn));
            sb.Append(",\"history_limitation_note\":").Append(MiniJson.Str(HistoryLimitationNote));
            sb.Append('}');
            return sb.ToString();
        }

        private static string JsonStrList(IReadOnlyList<string> items)
        {
            var list = new List<string>();
            if (items != null)
                foreach (var item in items)
                    if (item != null) list.Add(MiniJson.Str(item));
            return MiniJson.Array(list);
        }
    }

    /// <summary>Bounded, fog-filtered spatial overview for one acting seat.</summary>
    public sealed class SpatialContextView
    {
        public string SeatId;
        public int Season;
        public SpatialMapBounds Map;
        public IReadOnlyList<SpatialEntitySummary> OwnEntities;
        public IReadOnlyList<SpatialEntitySummary> VisibleOthers;
        public IReadOnlyList<SpatialStaleRecord> StaleIntel;
        public IReadOnlyList<SpatialObjectiveBearing> ObjectiveBearings;
        public IReadOnlyList<SpatialArmyFrontier> ArmyFrontiers;
        public SpatialFrontierSummary Frontier;

        /// <summary>Deterministic JSON serialization for hidden-world equivalence checks.</summary>
        public string ToJson()
        {
            var own = SerializeList(OwnEntities, e => e.ToJson());
            var vis = SerializeList(VisibleOthers, e => e.ToJson());
            var stale = SerializeList(StaleIntel, e => e.ToJson());
            var bearings = SerializeList(ObjectiveBearings, e => e.ToJson());
            var armyFr = SerializeList(ArmyFrontiers, e => e.ToJson());

            var sb = new StringBuilder(1024);
            sb.Append('{');
            sb.Append("\"seat_id\":").Append(MiniJson.Str(SeatId));
            sb.Append(",\"season\":").Append(MiniJson.Num(Season));
            sb.Append(",\"map\":").Append(Map != null ? Map.ToJson() : "null");
            sb.Append(",\"own_entities\":").Append(MiniJson.Array(own));
            sb.Append(",\"visible_others\":").Append(MiniJson.Array(vis));
            sb.Append(",\"stale_intel\":").Append(MiniJson.Array(stale));
            sb.Append(",\"objective_bearings\":").Append(MiniJson.Array(bearings));
            sb.Append(",\"army_frontiers\":").Append(MiniJson.Array(armyFr));
            sb.Append(",\"frontier\":").Append(Frontier != null ? Frontier.ToJson() : "null");
            sb.Append('}');
            return sb.ToString();
        }

        private static List<string> SerializeList<T>(IReadOnlyList<T> items, Func<T, string> toJson)
            where T : class
        {
            var list = new List<string>();
            if (items != null)
                foreach (var item in items)
                    if (item != null) list.Add(toJson(item));
            return list;
        }
    }

    /// <summary>Shared fog-safe compass, distance and edge helpers.</summary>
    internal static class SpatialGeometry
    {
        public static readonly string[] CompassOrder = { "W", "NW", "N", "NE", "E", "SE", "S", "SW" };

        /// <summary>Exact integer 8-sector octant classification without floating-point variance.</summary>
        public static int HexDistanceOrChebyshev(MapData map, int x1, int z1, int x2, int z2)
        {
            if (x1 == x2 && z1 == z2) return 0;
            int maxSteps = map != null ? map.Width + map.Height : 64;
            int hex = map != null ? map.HexDistance(x1, z1, x2, z2, maxSteps) : -1;
            return hex >= 0 ? hex : Math.Max(Math.Abs(x2 - x1), Math.Abs(z2 - z1));
        }

        public static string CompassDirection(int dx, int dz)
        {
            if (dx == 0 && dz == 0) return "here";
            int ax = Math.Abs(dx);
            int az = Math.Abs(dz);
            if (12 * az <= 5 * ax) return dx > 0 ? "E" : "W";
            if (12 * ax <= 5 * az) return dz > 0 ? "N" : "S";
            return (dz > 0 ? "N" : "S") + (dx > 0 ? "E" : "W");
        }

        public static int CompassRank(string dir)
        {
            for (int i = 0; i < CompassOrder.Length; i++)
                if (CompassOrder[i] == dir) return i;
            return CompassOrder.Length;
        }

        public static string NearEdgesText(int x, int z, int width, int height, int threshold = 2)
        {
            if (width <= 0 || height <= 0) return "";
            var parts = new List<string>(4);
            int w = x, e = width - 1 - x, s = z, n = height - 1 - z;
            if (w <= threshold) parts.Add("west=" + w);
            if (e <= threshold) parts.Add("east=" + e);
            if (s <= threshold) parts.Add("south=" + s);
            if (n <= threshold) parts.Add("north=" + n);
            return parts.Count == 0 ? "" : string.Join(", ", parts);
        }

        /// <summary>
        /// Counts in-bounds neighbors of an ALREADY IN-SIGHT cell (x, z) that are outside sight.
        /// Never reads Walkable or Cost of any neighbor cell.
        /// </summary>
        public static int CountUnseenNeighbors(GameEngine engine, string seatId, MapData map, int x, int z)
        {
            if (engine == null || map == null || !map.InBounds(x, z)) return 0;
            int idx = map.Index(x, z);
            int[] neighbors = map.Neighbors != null && idx >= 0 && idx < map.Neighbors.Length
                ? map.Neighbors[idx] : null;
            if (neighbors == null || neighbors.Length == 0) return 0;
            int unseen = 0;
            for (int i = 0; i < neighbors.Length; i++)
            {
                int nb = neighbors[i];
                if (nb < 0 || nb >= map.Width * map.Height) continue;
                int nx = nb % map.Width;
                int nz = nb / map.Width;
                if (!engine.InSight(seatId, nx, nz)) unseen++;
            }
            return unseen;
        }

        /// <summary>
        /// Shortest path cost traversing only currently in-sight walkable cells within
        /// <paramref name="maxCost"/>, or -1 if unreachable along visible terrain.
        /// Checks sight before reading Walkable or Cost so unseen terrain never leaks.
        /// </summary>
        public static int VisiblePathCost(GameEngine engine, string seatId, MapData map,
            int fromX, int fromZ, int toX, int toZ, int maxCost)
            => VisiblePathCost(engine?.State, seatId, map, fromX, fromZ, toX, toZ, maxCost);

        /// <summary>
        /// State-level <see cref="VisiblePathCost"/> shared by engine validation, movement
        /// planning, scripted AI, and UI highlights so every controller follows the exact same rule.
        /// </summary>
        public static int VisiblePathCost(GameState state, string seatId, MapData map,
            int fromX, int fromZ, int toX, int toZ, int maxCost)
        {
            if (state == null || map == null || !map.InBounds(fromX, fromZ) || !map.InBounds(toX, toZ))
                return -1;
            if (!GameEngine.InSight(state, seatId, fromX, fromZ) || !GameEngine.InSight(state, seatId, toX, toZ))
                return -1;
            if (!map.IsWalkable(toX, toZ)) return -1;
            if (fromX == toX && fromZ == toZ) return 0;

            int n = map.Width * map.Height;
            int start = map.Index(fromX, fromZ);
            int goal = map.Index(toX, toZ);
            var dist = new int[n];
            var done = new bool[n];
            for (int i = 0; i < n; i++) dist[i] = int.MaxValue;
            dist[start] = 0;

            for (;;)
            {
                int cur = -1, best = int.MaxValue;
                for (int i = 0; i < n; i++)
                    if (!done[i] && dist[i] < best) { best = dist[i]; cur = i; }
                if (cur < 0 || best > maxCost) return -1;
                if (cur == goal) return best;
                done[cur] = true;

                int[] nbs = map.Neighbors != null ? map.Neighbors[cur] : null;
                if (nbs == null) continue;
                for (int i = 0; i < nbs.Length; i++)
                {
                    int nb = nbs[i];
                    if (nb < 0 || nb >= n || done[nb]) continue;
                    int nx = nb % map.Width;
                    int nz = nb / map.Width;
                    if (!GameEngine.InSight(state, seatId, nx, nz)) continue;
                    if (!map.IsWalkable(nx, nz)) continue;
                    int stepCost = map.Cost != null ? map.Cost[nb] : 1;
                    int nd = best + stepCost;
                    if (nd <= maxCost && nd < dist[nb]) dist[nb] = nd;
                }
            }
        }
    }
}
