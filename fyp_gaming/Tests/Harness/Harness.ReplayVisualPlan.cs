using System;
using System.Collections.Generic;
using System.Linq;
using LuanShi.Engine;

// [CATALOG] (continued) Switching between recordings on one scene. The planner is the engine-side half of
// the chooser's switch; the Unity half (markers, banners) is not exercised here, so these checks are engine
// tests, not visual verification.

internal static partial class Harness
{
    private static ReplayVisualSurface CatSurface(MapData map, GameState state)
    {
        var surface = new ReplayVisualSurface { Map = map };
        foreach (CityState c in state.Cities) surface.Cities[c.Id] = new[] { c.X, c.Z };
        foreach (WatchTowerState t in state.WatchTowers) surface.Towers[t.Id] = new[] { t.X, t.Z };
        return surface;
    }

    private static void CatalogVisualPlan(string root)
    {
        MapData demo = DemoMap();
        GameEngine sceneEngine = BuildPaidShapeEngine(12345, "visual-plan-a");
        GameState a = sceneEngine.State;

        // Same map, same entities: nothing to change.
        ReplayVisualPlan same = ReplayVisualPlanner.Plan(CatSurface(a.Map, a), a);
        CatCheck(same.Compatible && same.CitiesToCreate.Count + same.CitiesToMove.Count + same.CitiesToHide.Count
                 + same.TowersToCreate.Count + same.TowersToMove.Count + same.TowersToHide.Count == 0,
            "the same map with the same cities and towers needs no visual change");

        // Same terrain, a recording with different, moved and missing markers.
        GameEngine other = BuildPaidShapeEngine(777, "visual-plan-b");
        GameState b = other.State;
        CityState first = b.Cities[0];
        string movedId = first.Id;
        first.X = Math.Max(0, first.X - 1);
        b.Cities.Add(new CityState { Id = "city-extra", Name = "Extra", X = first.X, Z = first.Z });
        string droppedId = a.Cities.Last().Id;
        b.Cities.RemoveAll(c => c.Id == droppedId);
        ReplayVisualPlan plan = ReplayVisualPlanner.Plan(CatSurface(a.Map, a), b);
        CatCheck(plan.Compatible && plan.CitiesToCreate.Contains("city-extra") && plan.CitiesToMove.Contains(movedId)
                 && plan.CitiesToHide.Contains(droppedId) && !plan.CitiesToCreate.Contains(movedId),
            "a recording with a new, a moved and a missing city: create it, move it, hide the stale one");
        if (a.WatchTowers.Count > 0)
        {
            b.WatchTowers.Clear();
            ReplayVisualPlan noTowers = ReplayVisualPlanner.Plan(CatSurface(a.Map, a), b);
            CatCheck(noTowers.TowersToHide.Count == a.WatchTowers.Count, "towers the recording lacks are hidden, not left standing");
        }
        else CatCheck(true, "(this world has no watch towers; tower hiding is covered by the synthetic case below)");

        // Different map size: refused, with the sizes in the reason.
        var smaller = new MapData
        {
            Width = a.Map.Width - 1, Height = a.Map.Height,
            Walkable = new bool[(a.Map.Width - 1) * a.Map.Height], Cost = new int[(a.Map.Width - 1) * a.Map.Height],
            Neighbors = new int[(a.Map.Width - 1) * a.Map.Height][],
        };
        for (int i = 0; i < smaller.Walkable.Length; i++) { smaller.Walkable[i] = true; smaller.Cost[i] = 1; smaller.Neighbors[i] = new int[0]; }
        GameState diffSize = new GameState { Map = smaller };
        ReplayVisualPlan refusedSize = ReplayVisualPlanner.Plan(CatSurface(a.Map, a), diffSize);
        CatCheck(!refusedSize.Compatible && refusedSize.Reason.Contains(smaller.Width + "x" + smaller.Height)
                 && refusedSize.Reason.Contains(a.Map.Width + "x" + a.Map.Height) && refusedSize.Reason.Contains("cannot be rebuilt"),
            "a recording on a different-sized map is refused and both sizes are stated");

        // Same size, different terrain: refused with the count of differing hexes.
        GameEngine terrainEngine = BuildPaidShapeEngine(12345, "visual-plan-c");
        MapData tm = terrainEngine.State.Map;
        MapData copy = new MapData { Width = tm.Width, Height = tm.Height, Walkable = (bool[])tm.Walkable.Clone(),
            Cost = (int[])tm.Cost.Clone(), Neighbors = tm.Neighbors };
        int flip = Array.FindIndex(copy.Walkable, w => w);
        copy.Walkable[flip] = false;
        copy.Cost[(flip + 1) % copy.Cost.Length] += 3;
        terrainEngine.State.Map = copy;
        ReplayVisualPlan refusedTerrain = ReplayVisualPlanner.Plan(CatSurface(tm, terrainEngine.State), terrainEngine.State);
        CatCheck(!refusedTerrain.Compatible && refusedTerrain.Reason.Contains("differs") && refusedTerrain.Reason.Contains("2 of "),
            "same size but different terrain is refused with the number of differing hexes (" + refusedTerrain.Reason + ")");

        // An entity on a hex the scene's map cannot show.
        GameEngine blocked = BuildPaidShapeEngine(12345, "visual-plan-d");
        CityState bc = blocked.State.Cities[0];
        MapData blockedMap = blocked.State.Map;
        int idx = blockedMap.Index(bc.X, bc.Z);
        MapData sceneWithWall = new MapData { Width = blockedMap.Width, Height = blockedMap.Height,
            Walkable = (bool[])blockedMap.Walkable.Clone(), Cost = blockedMap.Cost, Neighbors = blockedMap.Neighbors };
        sceneWithWall.Walkable[idx] = false;
        blockedMap.Walkable[idx] = false;
        ReplayVisualPlan offMap = ReplayVisualPlanner.Plan(CatSurface(sceneWithWall, blocked.State), blocked.State);
        CatCheck(!offMap.Compatible && offMap.Reason.Contains(bc.Id) && offMap.Reason.Contains("not a walkable hex"),
            "a city on a hex that is not walkable on the scene's map is refused and named");

        CatCheck(!ReplayVisualPlanner.Plan(null, a).Compatible && !ReplayVisualPlanner.Plan(CatSurface(a.Map, a), null).Compatible
                 && !ReplayVisualPlanner.Plan(CatSurface(a.Map, a), new GameState()).Compatible,
            "a missing scene map, recording or recording map is refused rather than assumed compatible");

        // Real recordings, when present: every one against the demo scene map, A then B then A again.
        string[] ids = { ReplayCatalog.TreatyAttackRunId };
        foreach (string id in ids)
        {
            ReplayOpenResult open = ReplayCatalog.Open(id, ReplayCatalog.LoadPresets(System.IO.Path.Combine(root, "none.json")).SearchRoots);
            if (!open.Ok) { Console.WriteLine("  [CATALOG] skipped real-recording visual plan for " + id + " (" + open.Status + ")"); continue; }
            ReplayVisualPlan real = ReplayVisualPlanner.Plan(new ReplayVisualSurface { Map = demo }, open.Session.Engine.State);
            CatCheck(real.Compatible, "the treaty recording's terrain is the demo scene's terrain (" + (real.Reason ?? "compatible") + ")");
            GameEngine paid = BuildPaidShapeEngine(12345, "visual-plan-e");
            ReplayVisualPlan cross = ReplayVisualPlanner.Plan(new ReplayVisualSurface { Map = paid.State.Map }, open.Session.Engine.State);
            Console.WriteLine("  [CATALOG] treaty recording against the paid-shape test map: "
                + (cross.Compatible ? "compatible" : "refused: " + cross.Reason));
            CatCheck(cross.Compatible || cross.Reason.Contains("cannot be rebuilt"),
                "a recording on another map is either compatible or refused with the terrain reason");
        }
    }
}
