using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using LuanShi.Engine;

namespace LuanShi.CombatScenarioCheck
{
    /// <summary>
    /// Loads the demo scene's real TBTK hex grid (exported once from
    /// <c>Assets/GameUI/Scenes/LuanShi_Demo.unity</c>: GridManager.gridT) into a <see cref="MapData"/>.
    /// The scenario is positioned on the map the game actually ships, not on a synthetic corridor, so a
    /// walkable-hex or adjacency mistake shows up here instead of in Play Mode.
    /// </summary>
    internal static class MapFixture
    {
        public static MapData LoadDemoMap(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("demo map fixture missing", path);
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            int width = root.GetProperty("width").GetInt32();
            int height = root.GetProperty("height").GetInt32();

            var map = new MapData
            {
                Width = width,
                Height = height,
                Walkable = new bool[width * height],
                Cost = new int[width * height],
                Neighbors = new int[width * height][],
            };
            for (int i = 0; i < map.Walkable.Length; i++) map.Cost[i] = 1;

            foreach (var node in root.GetProperty("nodes").EnumerateArray())
            {
                int x = node.GetProperty("x").GetInt32();
                int z = node.GetProperty("z").GetInt32();
                if (!map.InBounds(x, z)) throw new InvalidOperationException($"fixture node {x},{z} out of bounds");
                int i = map.Index(x, z);
                map.Walkable[i] = node.GetProperty("walkable").GetBoolean();
                map.Cost[i] = node.GetProperty("cost").GetInt32();
            }

            foreach (var pair in root.GetProperty("neighbors").EnumerateObject())
            {
                string[] parts = pair.Name.Split(',');
                int x = int.Parse(parts[0]), z = int.Parse(parts[1]);
                var list = new List<int>();
                foreach (var n in pair.Value.EnumerateArray())
                {
                    int nx = n[0].GetInt32(), nz = n[1].GetInt32();   // [x,z] pair
                    list.Add(map.Index(nx, nz));
                }
                map.Neighbors[map.Index(x, z)] = list.ToArray();
            }
            return map;
        }

        /// <summary>Engine-shaped two-seat world on the demo map, before the scenario writes its content.</summary>
        public static GameEngine NewEngine(int seed)
        {
            var engine = new GameEngine(seed);
            engine.State.Map = LoadFixture();
            return engine;
        }

        public static MapData LoadFixture()
            => LoadDemoMap(Path.Combine(AppContext.BaseDirectory, "demo-map.json"));

        /// <summary>Hex description used in evidence lines: "x,z walkable/cost".</summary>
        public static string Describe(MapData map, int x, int z)
            => map == null || !map.InBounds(x, z)
                ? x + "," + z + " (out of bounds)"
                : x + "," + z + (map.IsWalkable(x, z) ? "" : " (unwalkable)") + " cost " + map.Cost[map.Index(x, z)];
    }
}
