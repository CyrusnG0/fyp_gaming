using System;
using System.Collections.Generic;
using System.Globalization;

namespace LuanShi.Engine
{
    public static partial class SpatialContextBuilder
    {
        private static SpatialFrontierSummary AnalyzeFrontier(GameEngine engine, string seatId,
            MapData map, int season, List<SpatialEntitySummary> own,
            List<SpatialArmyFrontier> armyFrontiers)
        {
            const string limitation =
                "Frontier is derived from the current sight boundary only (city=3, army=2, tower=5 hexes); " +
                "the engine does not track whether a hex has ever been explored. Cells outside live sight " +
                "are currently unseen, never assumed empty or passable.";

            if (map == null || map.Width <= 0 || map.Height <= 0)
            {
                return new SpatialFrontierSummary
                {
                    FrontierDirectionsFromOwn = "(no map)",
                    HistoryLimitationNote = limitation,
                };
            }

            int w = map.Width, h = map.Height;
            var visible = new bool[w, h];
            int visibleCount = 0, unseenCount = 0;
            int minX = w, maxX = -1, minZ = h, maxZ = -1;
            int visMinX = w, visMaxX = -1, visMinZ = h, visMaxZ = -1;
            var rowRanges = new List<string>(h);
            var terrainNotes = new List<string>();

            for (int z = 0; z < h; z++)
            {
                int runStart = -1;
                var segs = new List<string>(2);
                for (int x = 0; x < w; x++)
                {
                    bool vis = engine.InSight(seatId, x, z);
                    visible[x, z] = vis;
                    if (vis)
                    {
                        visibleCount++;
                        if (x < visMinX) visMinX = x;
                        if (x > visMaxX) visMaxX = x;
                        if (z < visMinZ) visMinZ = z;
                        if (z > visMaxZ) visMaxZ = z;
                        if (runStart < 0) runStart = x;
                        // Sight verified above: safe to record visible obstacles or step > 1.
                        if (!map.IsWalkable(x, z))
                            terrainNotes.Add("(" + x + "," + z + "):blocked");
                        else if (map.Cost != null && map.Cost[map.Index(x, z)] > 1)
                            terrainNotes.Add("(" + x + "," + z + "):step="
                                + map.Cost[map.Index(x, z)].ToString(CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        if (runStart >= 0)
                        {
                            segs.Add(runStart == x - 1
                                ? runStart.ToString(CultureInfo.InvariantCulture)
                                : runStart.ToString(CultureInfo.InvariantCulture) + ".."
                                    + (x - 1).ToString(CultureInfo.InvariantCulture));
                            runStart = -1;
                        }
                        unseenCount++;
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (z < minZ) minZ = z;
                        if (z > maxZ) maxZ = z;
                    }
                }
                if (runStart >= 0)
                {
                    segs.Add(runStart == w - 1
                        ? runStart.ToString(CultureInfo.InvariantCulture)
                        : runStart.ToString(CultureInfo.InvariantCulture) + ".."
                            + (w - 1).ToString(CultureInfo.InvariantCulture));
                }
                if (segs.Count > 0)
                    rowRanges.Add("z=" + z.ToString(CultureInfo.InvariantCulture) + ":x=" + string.Join(",", segs));
            }

            int cx = w / 2, cz = h / 2;
            if (own.Count > 0)
            {
                int sx = 0, sz = 0;
                foreach (var e in own) { sx += e.X; sz += e.Z; }
                cx = sx / own.Count;
                cz = sz / own.Count;
            }

            var dirCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            int boundaryCount = 0;
            for (int z = 0; z < h; z++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (!visible[x, z]) continue;
                    int unseenAdj = SpatialGeometry.CountUnseenNeighbors(engine, seatId, map, x, z);
                    if (unseenAdj <= 0) continue;
                    boundaryCount++;
                    if (x != cx || z != cz)
                    {
                        string dir = SpatialGeometry.CompassDirection(x - cx, z - cz);
                        if (dir != "here")
                            dirCounts[dir] = dirCounts.TryGetValue(dir, out int c) ? c + 1 : 1;
                    }
                    else
                    {
                        int[] nbs = map.Neighbors != null ? map.Neighbors[map.Index(x, z)] : null;
                        if (nbs == null) continue;
                        for (int i = 0; i < nbs.Length; i++)
                        {
                            int nb = nbs[i];
                            if (nb < 0 || nb >= w * h) continue;
                            int nx = nb % w, nz = nb / w;
                            if (visible[nx, nz]) continue;
                            string dir = SpatialGeometry.CompassDirection(nx - x, nz - z);
                            if (dir != "here")
                                dirCounts[dir] = dirCounts.TryGetValue(dir, out int c) ? c + 1 : 1;
                        }
                    }
                }
            }

            var globalDirs = new List<string>(dirCounts.Keys);
            globalDirs.Sort((a, b) =>
            {
                int c = dirCounts[b].CompareTo(dirCounts[a]);
                return c != 0
                    ? c
                    : SpatialGeometry.CompassRank(a).CompareTo(SpatialGeometry.CompassRank(b));
            });

            foreach (var e in own)
            {
                if (e.Kind != "army") continue;
                string ut = string.IsNullOrEmpty(e.UnitType) ? UnitTypes.Infantry : e.UnitType;
                int budget = BalanceConfig.MarchBudget(season, ut);
                armyFrontiers.Add(AnalyzeArmyFrontier(engine, seatId, map, visible, e, budget));
            }

            return new SpatialFrontierSummary
            {
                VisibleCellCount = visibleCount,
                TotalCellCount = w * h,
                CurrentlyUnseenCellCount = unseenCount,
                SightBoundaryCellCount = boundaryCount,
                HasCurrentlyUnseen = unseenCount > 0,
                UnseenMinX = unseenCount > 0 ? minX : -1,
                UnseenMaxX = unseenCount > 0 ? maxX : -1,
                UnseenMinZ = unseenCount > 0 ? minZ : -1,
                UnseenMaxZ = unseenCount > 0 ? maxZ : -1,
                VisibleMinX = visibleCount > 0 ? visMinX : -1,
                VisibleMaxX = visibleCount > 0 ? visMaxX : -1,
                VisibleMinZ = visibleCount > 0 ? visMinZ : -1,
                VisibleMaxZ = visibleCount > 0 ? visMaxZ : -1,
                WestUnseenCols = visibleCount > 0 ? visMinX : 0,
                EastUnseenCols = visibleCount > 0 ? Math.Max(0, w - 1 - visMaxX) : 0,
                SouthUnseenCols = visibleCount > 0 ? visMinZ : 0,
                NorthUnseenCols = visibleCount > 0 ? Math.Max(0, h - 1 - visMaxZ) : 0,
                VisibleRowRanges = rowRanges,
                VisibleTerrainNotes = terrainNotes,
                FrontierDirectionsFromOwn = unseenCount == 0
                    ? "(full map in current sight)"
                    : (globalDirs.Count == 0 ? "(none)" : string.Join(", ", globalDirs)),
                HistoryLimitationNote = limitation,
            };
        }

        private static SpatialArmyFrontier AnalyzeArmyFrontier(GameEngine engine, string seatId,
            MapData map, bool[,] visible, SpatialEntitySummary army, int budget)
        {
            var dirSet = new HashSet<string>(StringComparer.Ordinal);
            int frontierCells = 0;
            int range = Math.Max(1, budget);
            int minX = Math.Max(0, army.X - range), maxX = Math.Min(map.Width - 1, army.X + range);
            int minZ = Math.Max(0, army.Z - range), maxZ = Math.Min(map.Height - 1, army.Z + range);

            for (int z = minZ; z <= maxZ; z++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    if (!visible[x, z] || !map.IsWalkable(x, z)) continue;
                    bool reachableOnVisible = (x == army.X && z == army.Z)
                        || SpatialGeometry.VisiblePathCost(engine, seatId, map, army.X, army.Z, x, z, range) >= 0;
                    if (!reachableOnVisible) continue;

                    int unseenNeighbors = SpatialGeometry.CountUnseenNeighbors(engine, seatId, map, x, z);
                    if (unseenNeighbors <= 0) continue;
                    frontierCells++;

                    if (x != army.X || z != army.Z)
                    {
                        dirSet.Add(SpatialGeometry.CompassDirection(x - army.X, z - army.Z));
                    }
                    else
                    {
                        int idx = map.Index(x, z);
                        int[] nbs = map.Neighbors[idx];
                        if (nbs != null)
                        {
                            for (int i = 0; i < nbs.Length; i++)
                            {
                                int nx = nbs[i] % map.Width, nz = nbs[i] / map.Width;
                                if (!visible[nx, nz])
                                    dirSet.Add(SpatialGeometry.CompassDirection(nx - army.X, nz - army.Z));
                            }
                        }
                    }
                }
            }

            var dirs = new List<string>(dirSet);
            dirs.Sort((a, b) => SpatialGeometry.CompassRank(a).CompareTo(SpatialGeometry.CompassRank(b)));
            return new SpatialArmyFrontier
            {
                ArmyId = army.Id,
                X = army.X,
                Z = army.Z,
                FrontierDirections = dirs.Count == 0
                    ? "(none within reach; bordered by own sight or map edges)"
                    : string.Join(", ", dirs),
                BorderDirections = army.NearEdges ?? "",
                FrontierCellCount = frontierCells,
            };
        }

        private static bool TryParsePair(string raw, out int x, out int z)
        {
            x = 0; z = 0;
            if (string.IsNullOrWhiteSpace(raw)) return false;
            string s = raw.Trim().TrimStart('[').TrimEnd(']');
            int comma = s.IndexOf(',');
            if (comma <= 0 || comma >= s.Length - 1) return false;
            return int.TryParse(s.Substring(0, comma).Trim(), NumberStyles.Integer,
                       CultureInfo.InvariantCulture, out x)
                && int.TryParse(s.Substring(comma + 1).Trim(), NumberStyles.Integer,
                       CultureInfo.InvariantCulture, out z);
        }
    }
}
