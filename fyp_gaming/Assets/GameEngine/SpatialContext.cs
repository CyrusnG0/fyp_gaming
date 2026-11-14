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

        public string ToJson()
        {
            var sb = new StringBuilder(192);
            sb.Append('{');
            sb.Append("\"width\":").Append(MiniJson.Num(Width));
            sb.Append(",\"height\":").Append(MiniJson.Num(Height));
            sb.Append(",\"horizontal_axis\":").Append(MiniJson.Str(HorizontalAxis));
            sb.Append(",\"vertical_axis\":").Append(MiniJson.Str(VerticalAxis));
            sb.Append(",\"fog_city_range\":").Append(MiniJson.Num(FogCityRange));
            sb.Append(",\"fog_army_range\":").Append(MiniJson.Num(FogArmyRange));
            sb.Append(",\"fog_watch_tower_range\":").Append(MiniJson.Num(FogWatchTowerRange));
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

        public string ToJson()
        {
            var sb = new StringBuilder(224);
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

    /// <summary>Current-sight coverage vs unseen space, plus explicit history limitations.</summary>
    public sealed class SpatialFrontierSummary
    {
        public int VisibleCellCount;
        public int TotalCellCount;
        public int UnexploredCellCount;
        public bool HasUnexplored;
        public int UnseenMinX = -1, UnseenMaxX = -1, UnseenMinZ = -1, UnseenMaxZ = -1;
        public string FrontierDirectionsFromOwn;
        public string HistoryLimitationNote;

        public string ToJson()
        {
            var sb = new StringBuilder(256);
            sb.Append('{');
            sb.Append("\"visible_cells\":").Append(MiniJson.Num(VisibleCellCount));
            sb.Append(",\"total_cells\":").Append(MiniJson.Num(TotalCellCount));
            sb.Append(",\"unexplored_cells\":").Append(MiniJson.Num(UnexploredCellCount));
            sb.Append(",\"has_unexplored\":").Append(HasUnexplored ? "true" : "false");
            sb.Append(",\"unseen_min_x\":").Append(MiniJson.Num(UnseenMinX));
            sb.Append(",\"unseen_max_x\":").Append(MiniJson.Num(UnseenMaxX));
            sb.Append(",\"unseen_min_z\":").Append(MiniJson.Num(UnseenMinZ));
            sb.Append(",\"unseen_max_z\":").Append(MiniJson.Num(UnseenMaxZ));
            sb.Append(",\"frontier_directions_from_own\":").Append(MiniJson.Str(FrontierDirectionsFromOwn));
            sb.Append(",\"history_limitation_note\":").Append(MiniJson.Str(HistoryLimitationNote));
            sb.Append('}');
            return sb.ToString();
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
    }
}
