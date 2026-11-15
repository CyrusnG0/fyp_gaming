using System;
using System.Collections.Generic;
using System.Globalization;

namespace LuanShi.Engine
{
    /// <summary>Target summary extracted strictly from seat-observable intel for movement diversity.</summary>
    internal sealed class SpatialObjectiveTarget
    {
        public string Kind;
        public string Id;
        public string Name;
        public string Knowledge; // "visible" | "stale"
        public int X, Z;
    }

    /// <summary>
    /// Builds a read-only, fog-filtered <see cref="SpatialContextView"/> for one acting seat.
    /// Reads only public map dimensions/grid adjacency, <see cref="GameEngine.InSight"/>, and the
    /// seat's own detached <see cref="Observation"/>. Never reads hidden entities or unseen terrain.
    /// </summary>
    public static partial class SpatialContextBuilder
    {
        public static SpatialContextView Build(GameEngine engine, string seatId,
            Observation observation = null)
        {
            var empty = new SpatialContextView
            {
                SeatId = null,
                Season = engine != null && engine.State != null ? engine.State.Season : 0,
                OwnEntities = new List<SpatialEntitySummary>(),
                VisibleOthers = new List<SpatialEntitySummary>(),
                StaleIntel = new List<SpatialStaleRecord>(),
                ObjectiveBearings = new List<SpatialObjectiveBearing>(),
                ArmyFrontiers = new List<SpatialArmyFrontier>(),
            };
            if (engine == null || engine.State == null || string.IsNullOrEmpty(seatId)) return empty;
            var fac = engine.State.FindFaction(seatId);
            if (fac == null) return empty;

            Observation obs = observation ?? engine.Observe(seatId);
            if (obs == null || !string.Equals(obs.SeatId, fac.SeatId, StringComparison.Ordinal))
                return empty;

            MapData map = engine.State.Map;
            int width = map != null ? map.Width : 0;
            int height = map != null ? map.Height : 0;
            int centerX = width > 0 ? width / 2 : 0;
            int centerZ = height > 0 ? height / 2 : 0;

            var own = new List<SpatialEntitySummary>();
            var visibleOthers = new List<SpatialEntitySummary>();
            CollectEntities(map, obs, fac.SeatId, width, height, centerX, centerZ, own, visibleOthers);
            PopulateReferenceBearings(map, own, visibleOthers);

            var stale = CollectStaleIntel(engine, obs, fac.SeatId, visibleOthers);
            var targets = CollectObjectives(visibleOthers, stale);
            var bearings = BuildBearings(map, own, targets);

            var armyFrontiers = new List<SpatialArmyFrontier>();
            SpatialFrontierSummary frontier = AnalyzeFrontier(engine, fac.SeatId, map, obs.Season, own, armyFrontiers);

            return new SpatialContextView
            {
                SeatId = fac.SeatId,
                Season = obs.Season,
                Map = new SpatialMapBounds
                {
                    Width = width,
                    Height = height,
                    FogCityRange = BalanceConfig.FogCityRange,
                    FogArmyRange = BalanceConfig.FogArmyRange,
                    FogWatchTowerRange = BalanceConfig.FogWatchTowerRange,
                    CenterX = centerX,
                    CenterZ = centerZ,
                },
                OwnEntities = own,
                VisibleOthers = visibleOthers,
                StaleIntel = stale,
                ObjectiveBearings = bearings,
                ArmyFrontiers = armyFrontiers,
                Frontier = frontier,
            };
        }

        internal static List<SpatialObjectiveTarget> CollectMovementTargets(GameEngine engine,
            string seatId, SpatialContextView spatial)
        {
            if (spatial != null)
                return TargetsFromView(spatial.VisibleOthers, spatial.StaleIntel);

            var list = new List<SpatialObjectiveTarget>();
            if (engine == null || string.IsNullOrEmpty(seatId)) return list;
            Observation obs = engine.Observe(seatId);
            if (obs == null) return list;

            MapData map = engine.State != null ? engine.State.Map : null;
            int w = map != null ? map.Width : 0;
            int h = map != null ? map.Height : 0;
            var own = new List<SpatialEntitySummary>();
            var visibleOthers = new List<SpatialEntitySummary>();
            CollectEntities(map, obs, seatId, w, h, w > 0 ? w / 2 : 0, h > 0 ? h / 2 : 0, own, visibleOthers);
            var stale = CollectStaleIntel(engine, obs, seatId, visibleOthers);
            return TargetsFromView(visibleOthers, stale);
        }

        private static List<SpatialObjectiveTarget> TargetsFromView(
            IReadOnlyList<SpatialEntitySummary> visibleOthers,
            IReadOnlyList<SpatialStaleRecord> stale)
        {
            var targets = new List<SpatialObjectiveTarget>();
            if (visibleOthers != null)
            {
                foreach (var e in visibleOthers)
                {
                    if (e == null) continue;
                    string targetKind = e.Kind == "city"
                        ? (e.Allegiance == "neutral" ? "neutral_city" : "foreign_city")
                        : e.Kind == "watch_tower"
                            ? (e.Allegiance == "neutral" ? "neutral_tower" : "foreign_tower")
                            : "foreign_army";
                    targets.Add(new SpatialObjectiveTarget
                    {
                        Kind = targetKind, Id = e.Id, Name = e.Name,
                        Knowledge = "visible", X = e.X, Z = e.Z,
                    });
                }
            }
            if (stale != null)
            {
                foreach (var s in stale)
                {
                    if (s == null) continue;
                    if (s.Kind == "army_last_seen" && s.Summary != null
                        && s.Summary.Contains("no longer visible"))
                        continue;
                    targets.Add(new SpatialObjectiveTarget
                    {
                        Kind = s.Kind, Id = s.Id, Name = s.Name,
                        Knowledge = "stale", X = s.X, Z = s.Z,
                    });
                }
            }
            return targets;
        }

        private static void CollectEntities(MapData map, Observation obs, string seatId,
            int width, int height, int centerX, int centerZ,
            List<SpatialEntitySummary> own, List<SpatialEntitySummary> visibleOthers)
        {
            foreach (var c in obs.Cities)
            {
                if (c == null) continue;
                var summary = MakeEntity(map, "city", c.Id, c.Name, c.OwnerSeatId, seatId,
                    c.X, c.Z, c.Garrison, c.GarrisonType, width, height, centerX, centerZ);
                if (summary.Allegiance == "own") own.Add(summary);
                else visibleOthers.Add(summary);
            }
            foreach (var a in obs.Armies)
            {
                if (a == null) continue;
                var summary = MakeEntity(map, "army", a.Id, a.Name, a.OwnerSeatId, seatId,
                    a.X, a.Z, a.Troops, a.UnitType, width, height, centerX, centerZ);
                if (summary.Allegiance == "own") own.Add(summary);
                else visibleOthers.Add(summary);
            }
            foreach (var t in obs.WatchTowers)
            {
                if (t == null) continue;
                var summary = MakeEntity(map, "watch_tower", t.Id, t.Name, t.OwnerSeatId, seatId,
                    t.X, t.Z, 0, null, width, height, centerX, centerZ);
                if (summary.Allegiance == "own") own.Add(summary);
                else visibleOthers.Add(summary);
            }
            own.Sort(CompareEntities);
            visibleOthers.Sort(CompareEntities);
        }

        private static SpatialEntitySummary MakeEntity(MapData map, string kind, string id,
            string name, string ownerSeatId, string viewerSeatId, int x, int z, int strength,
            string unitType, int width, int height, int centerX, int centerZ)
        {
            string allegiance = string.IsNullOrEmpty(ownerSeatId) ? "neutral"
                : string.Equals(ownerSeatId, viewerSeatId, StringComparison.Ordinal) ? "own" : "foreign";
            int cdx = centerX - x, cdz = centerZ - z;
            return new SpatialEntitySummary
            {
                Kind = kind,
                Id = id ?? "",
                Name = name ?? "",
                OwnerSeatId = ownerSeatId ?? "",
                Allegiance = allegiance,
                Knowledge = "visible",
                X = x,
                Z = z,
                Strength = strength,
                UnitType = unitType ?? "",
                WestEdgeDist = width > 0 ? x : 0,
                EastEdgeDist = width > 0 ? Math.Max(0, width - 1 - x) : 0,
                SouthEdgeDist = height > 0 ? z : 0,
                NorthEdgeDist = height > 0 ? Math.Max(0, height - 1 - z) : 0,
                NearEdges = SpatialGeometry.NearEdgesText(x, z, width, height),
                CenterDeltaX = cdx,
                CenterDeltaZ = cdz,
                CenterDistance = width > 0 && height > 0
                    ? SpatialGeometry.HexDistanceOrChebyshev(map, x, z, centerX, centerZ) : 0,
                CenterDirection = width > 0 && height > 0
                    ? SpatialGeometry.CompassDirection(cdx, cdz) : "here",
            };
        }

        private static void PopulateReferenceBearings(MapData map,
            List<SpatialEntitySummary> own, List<SpatialEntitySummary> visibleOthers)
        {
            if (own == null || own.Count == 0) return;
            SpatialEntitySummary primaryRef = null;
            foreach (var e in own)
                if (e.Kind == "city") { primaryRef = e; break; }
            if (primaryRef == null) primaryRef = own[0];

            foreach (var e in own)
            {
                if (ReferenceEquals(e, primaryRef)) continue;
                e.RefId = primaryRef.Id;
                e.RefDistance = SpatialGeometry.HexDistanceOrChebyshev(map, primaryRef.X, primaryRef.Z, e.X, e.Z);
                e.RefDirection = SpatialGeometry.CompassDirection(e.X - primaryRef.X, e.Z - primaryRef.Z);
            }

            if (visibleOthers == null) return;
            foreach (var v in visibleOthers)
            {
                SpatialEntitySummary bestOwn = null;
                int bestDist = int.MaxValue;
                foreach (var o in own)
                {
                    int d = SpatialGeometry.HexDistanceOrChebyshev(map, o.X, o.Z, v.X, v.Z);
                    if (bestOwn == null || d < bestDist
                        || (d == bestDist && bestOwn.Kind != "army" && o.Kind == "army"))
                    {
                        bestOwn = o;
                        bestDist = d;
                    }
                }
                if (bestOwn != null)
                {
                    v.RefId = bestOwn.Id;
                    v.RefDistance = bestDist;
                    v.RefDirection = SpatialGeometry.CompassDirection(v.X - bestOwn.X, v.Z - bestOwn.Z);
                }
            }
        }

        private static int CompareEntities(SpatialEntitySummary a, SpatialEntitySummary b)
        {
            int k = string.CompareOrdinal(a.Kind ?? "", b.Kind ?? "");
            if (k != 0) return k;
            return string.CompareOrdinal(a.Id ?? "", b.Id ?? "");
        }

        private static List<SpatialStaleRecord> CollectStaleIntel(GameEngine engine, Observation obs,
            string seatId, List<SpatialEntitySummary> visibleOthers)
        {
            var stale = new List<SpatialStaleRecord>();
            var visibleArmyIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var v in visibleOthers)
                if (v.Kind == "army" && !string.IsNullOrEmpty(v.Id)) visibleArmyIds.Add(v.Id);

            foreach (var s in obs.ScoutedCities)
            {
                if (s == null) continue;
                string detail = s.Accuracy == BalanceConfig.ScoutAccuracyExact
                    ? $"exact pop={s.Population} gar={s.Garrison} food={s.Food}"
                    : $"band gar={s.GarrisonBand ?? "?"} food={s.FoodBand ?? "?"}";
                stale.Add(new SpatialStaleRecord
                {
                    Kind = "scouted_city",
                    Id = s.CityId ?? "",
                    Name = s.Name ?? "",
                    OwnerSeatId = s.OwnerSeatId ?? "",
                    X = s.X,
                    Z = s.Z,
                    RecordedSeason = Math.Max(1, s.UntilSeason - BalanceConfig.ScoutReportExtraSeasons),
                    ValidUntilSeason = s.UntilSeason,
                    Knowledge = "stale",
                    Summary = $"scouted (outside live sight; valid through S{s.UntilSeason}; {detail})",
                });
            }

            var lastSeen = new Dictionary<string, (int x, int z, int season, string actor)>(StringComparer.Ordinal);
            foreach (var ev in obs.Events)
            {
                if (ev == null || ev.Payload == null) continue;
                if (ev.EventType == "army_destroyed")
                {
                    if (ev.Payload.TryGetValue("army_id", out string deadId) && !string.IsNullOrEmpty(deadId))
                        lastSeen.Remove(deadId);
                    continue;
                }
                if (ev.EventType != "army_moved" && ev.EventType != "army_routed") continue;
                if (string.Equals(ev.ActorSeatId, seatId, StringComparison.Ordinal)) continue;
                if (!ev.Payload.TryGetValue("army_id", out string armyId) || string.IsNullOrEmpty(armyId))
                    continue;
                if (!ev.Payload.TryGetValue("to", out string toRaw) || !TryParsePair(toRaw, out int tx, out int tz))
                    continue;
                lastSeen[armyId] = (tx, tz, ev.Season, ev.ActorSeatId ?? "");
            }

            foreach (var kv in lastSeen)
            {
                if (visibleArmyIds.Contains(kv.Key)) continue;
                var info = kv.Value;
                bool inSightNow = engine.InSight(seatId, info.x, info.z);
                stale.Add(new SpatialStaleRecord
                {
                    Kind = "army_last_seen",
                    Id = kv.Key,
                    Name = kv.Key,
                    OwnerSeatId = info.actor,
                    X = info.x,
                    Z = info.z,
                    RecordedSeason = info.season,
                    ValidUntilSeason = -1,
                    Knowledge = "stale",
                    Summary = inSightNow
                        ? $"last seen S{info.season} at ({info.x},{info.z}); cell now in sight and army no longer visible there"
                        : $"last seen S{info.season} at ({info.x},{info.z}); outside current sight (may have moved or perished)",
                });
            }

            stale.Sort((a, b) =>
            {
                int s = b.RecordedSeason.CompareTo(a.RecordedSeason);
                if (s != 0) return s;
                int k = string.CompareOrdinal(a.Kind ?? "", b.Kind ?? "");
                if (k != 0) return k;
                return string.CompareOrdinal(a.Id ?? "", b.Id ?? "");
            });
            return stale;
        }

        private static List<SpatialObjectiveTarget> CollectObjectives(
            List<SpatialEntitySummary> visibleOthers, List<SpatialStaleRecord> stale)
            => TargetsFromView(visibleOthers, stale);

        private static List<SpatialObjectiveBearing> BuildBearings(MapData map,
            List<SpatialEntitySummary> own, List<SpatialObjectiveTarget> targets)
        {
            var bearings = new List<SpatialObjectiveBearing>();
            foreach (var entity in own)
            {
                if (entity.Kind != "army") continue;
                foreach (var t in targets)
                {
                    if (t.X == entity.X && t.Z == entity.Z) continue;
                    int dist = SpatialGeometry.HexDistanceOrChebyshev(
                        map, entity.X, entity.Z, t.X, t.Z);
                    int dx = t.X - entity.X;
                    int dz = t.Z - entity.Z;
                    bearings.Add(new SpatialObjectiveBearing
                    {
                        ArmyId = entity.Id,
                        TargetKind = t.Kind,
                        TargetId = t.Id,
                        TargetName = t.Name,
                        Knowledge = t.Knowledge,
                        TargetX = t.X,
                        TargetZ = t.Z,
                        GridDistance = dist,
                        DeltaX = dx,
                        DeltaZ = dz,
                        Direction = SpatialGeometry.CompassDirection(dx, dz),
                    });
                }
            }
            bearings.Sort((a, b) =>
            {
                int aid = string.CompareOrdinal(a.ArmyId ?? "", b.ArmyId ?? "");
                if (aid != 0) return aid;
                bool av = a.Knowledge == "visible", bv = b.Knowledge == "visible";
                if (av != bv) return av ? -1 : 1;
                int d = a.GridDistance.CompareTo(b.GridDistance);
                if (d != 0) return d;
                return string.CompareOrdinal(a.TargetId ?? "", b.TargetId ?? "");
            });
            return bearings;
        }
    }
}
