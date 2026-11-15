using System;
using System.Collections.Generic;

namespace LuanShi.Engine
{
    /// <summary>
    /// Selects a directionally varied, objective- and frontier-aware subset of legal march
    /// candidates strictly from destinations already verified to be in sight and reachable.
    /// </summary>
    internal static class SpatialMovementDiversifier
    {
        public static List<MoveOption> SelectDiversified(GameEngine engine, string seatId,
            MapData map, ArmyState army, List<MoveOption> all, int cap,
            List<SpatialObjectiveTarget> targets, bool spatialContextEnabled = false)
        {
            var unseenByOption = new Dictionary<MoveOption, int>();
            for (int i = 0; i < all.Count; i++)
            {
                MoveOption opt = all[i];
                if (spatialContextEnabled)
                    opt.GridDistance = SpatialGeometry.HexDistanceOrChebyshev(map, army.X, army.Z, opt.X, opt.Z);
                opt.Direction = SpatialGeometry.CompassDirection(opt.X - army.X, opt.Z - army.Z);
                int unseen = SpatialGeometry.CountUnseenNeighbors(engine, seatId, map, opt.X, opt.Z);
                opt.ExploresFrontier = unseen > 0;
                unseenByOption[opt] = unseen;
            }

            int take = Math.Min(cap, all.Count);
            var picked = new List<MoveOption>(take);
            var used = new HashSet<MoveOption>();

            // 1. Direct captures always stay first.
            for (int i = 0; i < all.Count && picked.Count < take; i++)
            {
                if (all[i].Kind == "move") break;
                used.Add(all[i]);
                picked.Add(all[i]);
            }

            // 2. Best legal step toward each known objective (visible before stale, nearest first).
            if (targets != null && targets.Count > 0 && picked.Count < take)
            {
                int maxSteps = map != null ? map.Width + map.Height : 64;
                var ordered = new List<SpatialObjectiveTarget>(targets);
                ordered.Sort((a, b) =>
                {
                    bool av = a.Knowledge == "visible", bv = b.Knowledge == "visible";
                    if (av != bv) return av ? -1 : 1;
                    int da = HexOrStepDist(map, army.X, army.Z, a.X, a.Z, maxSteps);
                    int db = HexOrStepDist(map, army.X, army.Z, b.X, b.Z, maxSteps);
                    return da != db ? da.CompareTo(db) : string.CompareOrdinal(a.Id ?? "", b.Id ?? "");
                });
                foreach (var t in ordered)
                {
                    if (picked.Count >= take) break;
                    int curDist = HexOrStepDist(map, army.X, army.Z, t.X, t.Z, maxSteps);
                    if (curDist <= 0) continue;
                    MoveOption best = null;
                    int bestDist = curDist;
                    foreach (var opt in all)
                    {
                        if (used.Contains(opt)) continue;
                        int d = HexOrStepDist(map, opt.X, opt.Z, t.X, t.Z, maxSteps);
                        if (d < bestDist || (d == bestDist && best != null
                            && MovementPlanner.CompareOptions(opt, best) < 0))
                        {
                            best = opt; bestDist = d;
                        }
                    }
                    if (best != null)
                    {
                        best.ObjectiveHint = "toward " + (!string.IsNullOrEmpty(t.Id) ? t.Id : t.Kind);
                        used.Add(best);
                        picked.Add(best);
                    }
                }
            }

            // 3. Round-robin across the 8 compass sectors: prefer frontier steps & outward reach.
            for (int round = 0; round < 2 && picked.Count < take; round++)
            {
                for (int s = 0; s < SpatialGeometry.CompassOrder.Length && picked.Count < take; s++)
                {
                    string dir = SpatialGeometry.CompassOrder[s];
                    MoveOption best = null;
                    foreach (var opt in all)
                    {
                        if (used.Contains(opt) || opt.Direction != dir) continue;
                        if (best == null || BetterSectorOption(army, opt, best, unseenByOption))
                            best = opt;
                    }
                    if (best != null)
                    {
                        if (best.ExploresFrontier && string.IsNullOrEmpty(best.ObjectiveHint))
                        {
                            bool towardCenter = false;
                            if (spatialContextEnabled && map != null && map.Width > 0 && map.Height > 0)
                            {
                                int cx = map.Width / 2, cz = map.Height / 2;
                                int maxSteps = map.Width + map.Height;
                                int curCenter = HexOrStepDist(map, army.X, army.Z, cx, cz, maxSteps);
                                int nextCenter = HexOrStepDist(map, best.X, best.Z, cx, cz, maxSteps);
                                towardCenter = nextCenter < curCenter;
                            }
                            best.ObjectiveHint = towardCenter
                                ? "frontier " + dir + " (toward map_center)"
                                : "frontier " + dir;
                        }
                        used.Add(best);
                        picked.Add(best);
                    }
                }
            }

            // 4. Fill any remaining cap slots in deterministic standard order.
            for (int i = 0; i < all.Count && picked.Count < take; i++)
                if (used.Add(all[i])) picked.Add(all[i]);
            return picked;
        }

        private static bool BetterSectorOption(ArmyState army, MoveOption cand, MoveOption current,
            Dictionary<MoveOption, int> unseenByOption)
        {
            if (cand.ExploresFrontier != current.ExploresFrontier) return cand.ExploresFrontier;
            int cu = unseenByOption[cand], bu = unseenByOption[current];
            if (cu != bu) return cu > bu;
            int cd = StepDist(army.X, army.Z, cand.X, cand.Z);
            int bd = StepDist(army.X, army.Z, current.X, current.Z);
            if (cd != bd) return cd > bd;
            return MovementPlanner.CompareOptions(cand, current) < 0;
        }

        private static int HexOrStepDist(MapData map, int x1, int z1, int x2, int z2, int maxSteps)
        {
            int hex = map != null ? map.HexDistance(x1, z1, x2, z2, maxSteps) : -1;
            int step = StepDist(x1, z1, x2, z2);
            return hex >= 0 ? hex * 100 + step : step;
        }

        private static int StepDist(int x1, int z1, int x2, int z2)
            => Math.Max(Math.Abs(x2 - x1), Math.Abs(z2 - z1)) + Math.Abs(x2 - x1) + Math.Abs(z2 - z1);
    }
}
