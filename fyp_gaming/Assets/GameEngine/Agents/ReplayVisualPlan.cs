using System;
using System.Collections.Generic;

namespace LuanShi.Engine
{
    /// <summary>
    /// What the running scene can already draw: its terrain (the fixed hex grid, as the engine sees it) and
    /// the cities and watch towers that currently have a marker, by id and hex.
    /// </summary>
    public sealed class ReplayVisualSurface
    {
        public MapData Map;
        public readonly Dictionary<string, int[]> Cities = new Dictionary<string, int[]>(StringComparer.Ordinal);
        public readonly Dictionary<string, int[]> Towers = new Dictionary<string, int[]>(StringComparer.Ordinal);
    }

    /// <summary>
    /// How to show a replay engine on a given scene. Terrain is the scene's own hex grid and cannot be rebuilt
    /// while playing, so a recording on a different map is refused. Everything else (city, tower and army
    /// markers) is reconciled: created where missing, moved when the hex differs, hidden when the replay has
    /// no such entity.
    /// </summary>
    public sealed class ReplayVisualPlan
    {
        public bool Compatible;
        /// <summary>Why the recording cannot be shown on this scene; null when it can.</summary>
        public string Reason;
        public readonly List<string> CitiesToCreate = new List<string>();
        public readonly List<string> CitiesToMove = new List<string>();
        public readonly List<string> CitiesToHide = new List<string>();
        public readonly List<string> TowersToCreate = new List<string>();
        public readonly List<string> TowersToMove = new List<string>();
        public readonly List<string> TowersToHide = new List<string>();
    }

    public static class ReplayVisualPlanner
    {
        public static ReplayVisualPlan Plan(ReplayVisualSurface scene, GameState replay)
        {
            var plan = new ReplayVisualPlan();
            if (scene == null || scene.Map == null) { plan.Reason = "the scene's map is not available"; return plan; }
            if (replay == null || replay.Map == null) { plan.Reason = "the recording has no map"; return plan; }

            string terrain = TerrainMismatch(scene.Map, replay.Map);
            if (terrain != null) { plan.Reason = terrain; return plan; }

            MapData map = scene.Map;
            foreach (CityState city in replay.Cities)
            {
                string bad = OffMap(map, "city " + city.Id, city.X, city.Z);
                if (bad != null) { plan.Reason = bad; return plan; }
            }
            foreach (WatchTowerState tower in replay.WatchTowers)
            {
                string bad = OffMap(map, "watch tower " + tower.Id, tower.X, tower.Z);
                if (bad != null) { plan.Reason = bad; return plan; }
            }
            foreach (ArmyState army in replay.Armies)
            {
                string bad = OffMap(map, "army " + army.Id, army.X, army.Z);
                if (bad != null) { plan.Reason = bad; return plan; }
            }

            foreach (CityState city in replay.Cities) Reconcile(scene.Cities, city.Id, city.X, city.Z, plan.CitiesToCreate, plan.CitiesToMove);
            foreach (string id in scene.Cities.Keys)
                if (replay.FindCity(id) == null) plan.CitiesToHide.Add(id);
            foreach (WatchTowerState tower in replay.WatchTowers) Reconcile(scene.Towers, tower.Id, tower.X, tower.Z, plan.TowersToCreate, plan.TowersToMove);
            foreach (string id in scene.Towers.Keys)
                if (replay.WatchTowers.Find(t => t.Id == id) == null) plan.TowersToHide.Add(id);
            plan.Compatible = true;
            return plan;
        }

        private static void Reconcile(Dictionary<string, int[]> existing, string id, int x, int z, List<string> create, List<string> move)
        {
            if (!existing.TryGetValue(id, out int[] at)) create.Add(id);
            else if (at[0] != x || at[1] != z) move.Add(id);
        }

        private static string OffMap(MapData map, string what, int x, int z)
            => x < 0 || z < 0 || x >= map.Width || z >= map.Height || !map.Walkable[map.Index(x, z)]
                ? what + " stands at [" + x + "," + z + "], which is not a walkable hex of this scene's map"
                : null;

        /// <summary>Null when the two maps have the same size, walkability and movement costs.</summary>
        public static string TerrainMismatch(MapData scene, MapData replay)
        {
            if (scene.Width != replay.Width || scene.Height != replay.Height)
                return "the recording's map is " + replay.Width + "x" + replay.Height + " but this scene's map is "
                    + scene.Width + "x" + scene.Height + "; the terrain cannot be rebuilt while playing";
            int cells = scene.Width * scene.Height;
            int differing = 0, firstX = -1, firstZ = -1;
            for (int i = 0; i < cells; i++)
            {
                bool same = scene.Walkable[i] == replay.Walkable[i] && scene.Cost[i] == replay.Cost[i];
                if (!same) { if (differing == 0) { firstX = i % scene.Width; firstZ = i / scene.Width; } differing++; }
            }
            return differing == 0 ? null
                : "the recording's terrain differs from this scene's map at " + differing + " of " + cells
                  + " hexes (first at [" + firstX + "," + firstZ + "]); the terrain cannot be rebuilt while playing";
        }
    }
}
