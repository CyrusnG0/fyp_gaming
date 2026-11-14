using System;
using System.Collections.Generic;
using LuanShi.Engine;

internal static partial class Harness
{
    private const string ShortcutArmyId = "army-shu-1";

    private static void SpatialCrowdedMapAndPromptBound()
    {
        var e = SpatialDemoWorld(706, withFrontierSample: true);
        int added = 0;
        for (int z = 1; z < 16 && added < 44; z += 2)
        {
            for (int x = 18; x < 26 && added < 44; x++)
            {
                if (e.State.ArmyAt(x, z) != null || !e.State.Map.IsWalkable(x, z)) continue;
                added++;
                e.State.Armies.Add(new ArmyState
                {
                    Id = "army-shu-crowd-" + added.ToString("D2"),
                    Name = "Crowd " + added,
                    OwnerSeatId = SeatShu,
                    UnitType = UnitTypes.Cavalry,
                    Troops = 1000,
                    X = x,
                    Z = z,
                });
            }
        }

        Observation obs = e.Observe(SeatShu);
        SpatialContextView sp = SpatialContextBuilder.Build(e, SeatShu, obs);
        MovementView mv = MovementPlanner.BuildDiversified(e, SeatShu, 10, sp);
        List<ActionOption> opts = ActionCatalogue.Build(obs);

        string sys = PromptBuilder.SystemPromptForSpatial(true, inquiry: true, persona: null, attack: true, spatial: true);
        string user1 = PromptBuilder.UserPromptWithSpatial(obs, opts, null, 4, 3, mv, sp, true,
            inquiryEnabled: true, attackEnabled: true);
        string user2 = PromptBuilder.UserPromptWithSpatial(obs, opts, null, 4, 3, mv, sp, true,
            inquiryEnabled: true, attackEnabled: true);
        int totalLen = sys.Length + user1.Length;

        Check(totalLen <= PromptBuilder.MaxPromptChars
            && user1 == user2
            && user1.Contains("[truncated:")
            && user1.Contains("SPATIAL CONTEXT")
            && !user1.Contains("unexplored")
            && !sys.Contains("unexplored")
            && user1.Contains("map: width=26 height=17")
            && PromptBuilder.VersionForSpatial(true, true, true, true, true)
                == PromptBuilder.SpatialContextPromptVersion,
            $"crowded map bounded prompt: total={totalLen} <= {PromptBuilder.MaxPromptChars}, truncated and deterministic");
    }

    private static MapData SpatialOrtho4Map(int width, int height)
    {
        var map = new MapData
        {
            Width = width,
            Height = height,
            Walkable = new bool[width * height],
            Cost = new int[width * height],
            Neighbors = new int[width * height][],
        };
        for (int z = 0; z < height; z++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = map.Index(x, z);
                map.Walkable[i] = true;
                map.Cost[i] = 1;
                var nbs = new List<int>(4);
                TryAddHexNeighbor(map, nbs, x - 1, z);
                TryAddHexNeighbor(map, nbs, x + 1, z);
                TryAddHexNeighbor(map, nbs, x, z - 1);
                TryAddHexNeighbor(map, nbs, x, z + 1);
                map.Neighbors[i] = nbs.ToArray();
            }
        }
        return map;
    }

    private static GameEngine BuildUnseenShortcutWorld(int seed, bool shortcutOpen,
        string unitType = null, bool extraSlope52 = false)
    {
        var e = new GameEngine(seed);
        var map = SpatialOrtho4Map(8, 8);
        // Visible obstacles: river at (3,4) (dist 2 from city) and forest at (4,3) (Cost=2, dist 2 from city).
        map.Walkable[map.Index(3, 4)] = false;
        map.Cost[map.Index(4, 3)] = 2;
        if (extraSlope52) map.Cost[map.Index(5, 2)] = 2;
        // Currently unseen cell (2,5): dist 3 from army (2,2), dist 4 from city (5,4).
        if (!shortcutOpen)
        {
            map.Walkable[map.Index(2, 5)] = false;
            map.Cost[map.Index(2, 5)] = 5;
        }
        e.State.Map = map;
        e.State.Factions.Add(new FactionState { SeatId = SeatShu, Name = "Shu", Controller = ControllerType.Llm });
        e.State.Factions.Add(new FactionState { SeatId = "seat-wei", Name = "Wei", Controller = ControllerType.Llm });
        e.State.Cities.Add(new CityState
        {
            Id = "city-shu", Name = "Chengdu", OwnerSeatId = SeatShu,
            X = 5, Z = 4, Population = 10000, Food = 4000, Gold = 1000, Garrison = 500, Morale = 80,
        });
        e.State.Cities.Add(new CityState
        {
            Id = "city-wei", Name = "Xuchang", OwnerSeatId = "seat-wei",
            X = 7, Z = 0, Population = 10000, Food = 4000, Gold = 1000, Garrison = 500, Morale = 80,
        });
        e.State.Armies.Add(new ArmyState
        {
            Id = ShortcutArmyId, Name = "Shu 1", OwnerSeatId = SeatShu,
            UnitType = unitType ?? UnitTypes.Infantry, Troops = 2000, X = 2, Z = 2,
        });
        e.BeginSeason();
        return e;
    }

    private static bool TryFindOption(MovementView mv, string armyId, int x, int z, out int cost)
    {
        cost = -1;
        if (mv == null) return false;
        foreach (var plan in mv.Armies)
        {
            if (plan.ArmyId != armyId) continue;
            foreach (var opt in plan.Options)
            {
                if (opt.X == x && opt.Z == z) { cost = opt.PathCost; return true; }
            }
        }
        return false;
    }

    private static bool OptionsSequenceEqual(MovementView a, MovementView b)
    {
        if (a == null || b == null || a.Armies.Count != b.Armies.Count) return false;
        for (int i = 0; i < a.Armies.Count; i++)
        {
            var pa = a.Armies[i];
            var pb = b.Armies[i];
            if (pa.ArmyId != pb.ArmyId || pa.Options.Count != pb.Options.Count) return false;
            for (int k = 0; k < pa.Options.Count; k++)
            {
                var oa = pa.Options[k];
                var ob = pb.Options[k];
                if (oa.X != ob.X || oa.Z != ob.Z || oa.PathCost != ob.PathCost
                    || oa.Kind != ob.Kind || oa.Direction != ob.Direction)
                    return false;
            }
        }
        return true;
    }

    private static bool AllOptionsValidate(MovementView mv, bool shortcutOpen)
    {
        if (mv == null || mv.Armies.Count == 0) return false;
        foreach (var plan in mv.Armies)
        {
            if (plan.Options.Count == 0) return false;
            foreach (var opt in plan.Options)
            {
                var clone = BuildUnseenShortcutWorld(707, shortcutOpen);
                if (!clone.Submit(SpatialMarchCmd(SeatShu, plan.ArmyId, opt.X, opt.Z)).Ok) return false;
            }
        }
        return true;
    }

    private static void SpatialReachableUnseenShortcutInvariance()
    {
        GameEngine eOpen = BuildUnseenShortcutWorld(707, shortcutOpen: true);
        GameEngine eBlocked = BuildUnseenShortcutWorld(707, shortcutOpen: false);

        bool unseenCheck = !eOpen.InSight(SeatShu, 2, 5) && !eBlocked.InSight(SeatShu, 2, 5)
            && eOpen.InSight(SeatShu, 3, 4) && eOpen.InSight(SeatShu, 4, 3)
            && eOpen.InSight(SeatShu, 3, 5) && eOpen.InSight(SeatShu, 4, 5);

        // Default planner uses full map Walkable/Cost, so the unseen shortcut changes advertised destinations/costs.
        MovementView defOpen = MovementPlanner.Build(eOpen, SeatShu, 40);
        MovementView defBlocked = MovementPlanner.Build(eBlocked, SeatShu, 40);
        bool defShowsLeak = TryFindOption(defOpen, ShortcutArmyId, 3, 5, out int defOpen35) && defOpen35 == 4
            && TryFindOption(defOpen, ShortcutArmyId, 4, 5, out int defOpen45) && defOpen45 == 5
            && !TryFindOption(defBlocked, ShortcutArmyId, 3, 5, out _)
            && TryFindOption(defBlocked, ShortcutArmyId, 4, 5, out int defBlk45) && defBlk45 == 6;

        // Opt-in planner and spatial context must use visible terrain only and match completely across both worlds.
        Observation obsOpen = eOpen.Observe(SeatShu);
        Observation obsBlk = eBlocked.Observe(SeatShu);
        SpatialContextView spOpen = SpatialContextBuilder.Build(eOpen, SeatShu, obsOpen);
        SpatialContextView spBlk = SpatialContextBuilder.Build(eBlocked, SeatShu, obsBlk);
        MovementView divOpen40 = MovementPlanner.BuildDiversified(eOpen, SeatShu, 40, spOpen);
        MovementView divBlk40 = MovementPlanner.BuildDiversified(eBlocked, SeatShu, 40, spBlk);
        MovementView divOpen8 = MovementPlanner.BuildDiversified(eOpen, SeatShu, 8, spOpen);
        MovementView divBlk8 = MovementPlanner.BuildDiversified(eBlocked, SeatShu, 8, spBlk);

        bool noLeak = !TryFindOption(divOpen40, ShortcutArmyId, 3, 5, out _)
            && !TryFindOption(divBlk40, ShortcutArmyId, 3, 5, out _)
            && TryFindOption(divOpen40, ShortcutArmyId, 4, 5, out int divOpen45) && divOpen45 == 6
            && TryFindOption(divBlk40, ShortcutArmyId, 4, 5, out int divBlk45) && divBlk45 == 6
            && OptionsSequenceEqual(divOpen40, divBlk40)
            && OptionsSequenceEqual(divOpen8, divBlk8)
            && divOpen40.ToJson() == divBlk40.ToJson()
            && divOpen8.ToJson() == divBlk8.ToJson()
            && spOpen.ToJson() == spBlk.ToJson();

        string pOpen8 = PromptBuilder.UserPromptWithSpatial(obsOpen, ActionCatalogue.Build(obsOpen), null, 4, 3,
            divOpen8, spOpen, spatialContextEnabled: true, inquiryEnabled: true, attackEnabled: true);
        string pBlk8 = PromptBuilder.UserPromptWithSpatial(obsBlk, ActionCatalogue.Build(obsBlk), null, 4, 3,
            divBlk8, spBlk, spatialContextEnabled: true, inquiryEnabled: true, attackEnabled: true);
        string pOpen40 = PromptBuilder.UserPromptWithSpatial(obsOpen, ActionCatalogue.Build(obsOpen), null, 4, 3,
            divOpen40, spOpen, spatialContextEnabled: true, inquiryEnabled: true, attackEnabled: true);
        string pBlk40 = PromptBuilder.UserPromptWithSpatial(obsBlk, ActionCatalogue.Build(obsBlk), null, 4, 3,
            divBlk40, spBlk, spatialContextEnabled: true, inquiryEnabled: true, attackEnabled: true);
        string mvText40 = PromptBuilder.MovementOptionsText(divOpen40, 40, 10, includeSpatialTags: true);
        bool promptMatch = pOpen8 == pBlk8 && pOpen40 == pBlk40
            && mvText40.Contains("4,5 cost=6") && !mvText40.Contains("3,5 cost=")
            && !pOpen8.Contains("unexplored") && !pOpen40.Contains("unexplored");

        bool legalOpen = AllOptionsValidate(divOpen40, shortcutOpen: true);
        bool legalBlk = AllOptionsValidate(divBlk40, shortcutOpen: false);

        Check(unseenCheck && defShowsLeak && noLeak && promptMatch && legalOpen && legalBlk,
            $"reachable unseen shortcut invariance: unseen={unseenCheck} def={defShowsLeak} noLeak={noLeak} prompt={promptMatch} legal={legalOpen}/{legalBlk}");

        VerifyPostMarchShortcutLeak(eOpen, eBlocked);
        VerifyPostMarchStillUnseenShortcutLeak();
    }

    private static string DiffWindow(string a, string b, out int index)
    {
        int len = Math.Min(a.Length, b.Length);
        index = len;
        for (int i = 0; i < len; i++)
        {
            if (a[i] != b[i]) { index = i; break; }
        }
        int start = Math.Max(0, index - 24);
        int take = Math.Min(64, a.Length - start);
        return a.Substring(start, take);
    }

    private static void VerifyPostMarchShortcutLeak(GameEngine eOpen, GameEngine eBlocked)
    {
        bool marched = eOpen.Submit(SpatialMarchCmd(SeatShu, ShortcutArmyId, 4, 5)).Ok
            && eBlocked.Submit(SpatialMarchCmd(SeatShu, ShortcutArmyId, 4, 5)).Ok;

        Observation obsOpen = eOpen.Observe(SeatShu);
        Observation obsBlk = eBlocked.Observe(SeatShu);
        var histOpen = new InvestigationService(obsOpen.GameId, SeatShu, obsOpen, null).SearchHistory("", 10, 0);
        var histBlk = new InvestigationService(obsBlk.GameId, SeatShu, obsBlk, null).SearchHistory("", 10, 0);

        string hJsonOpen = histOpen.ToJson(), hJsonBlk = histBlk.ToJson();
        string hSnippet = DiffWindow(hJsonOpen, hJsonBlk, out int hIdx);
        bool searchLeak = histOpen.Ok && histBlk.Ok && histOpen.Hits.Count == 3
            && histOpen.Hits[0].EventType == "action" && histOpen.Hits[0].ToJson() == histBlk.Hits[0].ToJson()
            && histOpen.Hits[1].EventType == "army_moved"
            && histOpen.Hits[1].Payload["cost"] == "5" && histBlk.Hits[1].Payload["cost"] == "6"
            && hSnippet.Contains("\"cost\":\"5\"")
            && hJsonOpen.Replace("\"cost\":\"5\"", "\"cost\":\"6\"") == hJsonBlk;

        string uOpen = PromptBuilder.UserPrompt(obsOpen, ActionCatalogue.Build(obsOpen), null, 4, 2,
            MovementPlanner.Build(eOpen, SeatShu, 8), null, null, inquiryEnabled: true, attackEnabled: true);
        string uBlk = PromptBuilder.UserPrompt(obsBlk, ActionCatalogue.Build(obsBlk), null, 4, 2,
            MovementPlanner.Build(eBlocked, SeatShu, 8), null, null, inquiryEnabled: true, attackEnabled: true);
        string uSnippet = DiffWindow(uOpen, uBlk, out int uIdx);
        bool obsLeak = obsOpen.ToJson().Replace("\"cost\":\"5\"", "\"cost\":\"6\"") == obsBlk.ToJson()
            && uSnippet.Contains("\"cost\":\"5\"")
            && uOpen.Replace("\"cost\":\"5\"", "\"cost\":\"6\"") == uBlk;

        // Arriving at (4,5) puts (2,5) at dist 2 <= FogArmyRange, so spatial frontier also sees the new cell.
        SpatialContextView spOpen = SpatialContextBuilder.Build(eOpen, SeatShu, obsOpen);
        SpatialContextView spBlk = SpatialContextBuilder.Build(eBlocked, SeatShu, obsBlk);
        bool sightRevealed = eOpen.InSight(SeatShu, 2, 5) && eBlocked.InSight(SeatShu, 2, 5)
            && spOpen.ArmyFrontiers[0].FrontierCellCount != spBlk.ArmyFrontiers[0].FrontierCellCount;
        string pOpen = PromptBuilder.UserPromptWithSpatial(obsOpen, ActionCatalogue.Build(obsOpen), null, 4, 2,
            MovementPlanner.BuildDiversified(eOpen, SeatShu, 8, spOpen), spOpen,
            spatialContextEnabled: true, inquiryEnabled: true, attackEnabled: true);
        string pBlk = PromptBuilder.UserPromptWithSpatial(obsBlk, ActionCatalogue.Build(obsBlk), null, 4, 2,
            MovementPlanner.BuildDiversified(eBlocked, SeatShu, 8, spBlk), spBlk,
            spatialContextEnabled: true, inquiryEnabled: true, attackEnabled: true);
        string pSnippet = DiffWindow(pOpen, pBlk, out int pIdx);
        bool spatialDiffsFirstAtFrontier = sightRevealed && pIdx < uIdx && pSnippet.Contains("adj_unseen=")
            && pOpen.Contains("\"cost\":\"5\"") && pBlk.Contains("\"cost\":\"6\"");

        Check(marched && searchLeak && obsLeak && spatialDiffsFirstAtFrontier,
            $"post-march shortcut leak (4,5): hIdx={hIdx} uIdx={uIdx} pIdx={pIdx} adj={spOpen.ArmyFrontiers[0].FrontierCellCount}/{spBlk.ArmyFrontiers[0].FrontierCellCount}");
    }

    private static void VerifyPostMarchStillUnseenShortcutLeak()
    {
        GameEngine cOpen = BuildUnseenShortcutWorld(707, true, UnitTypes.Cavalry, extraSlope52: true);
        GameEngine cBlk = BuildUnseenShortcutWorld(707, false, UnitTypes.Cavalry, extraSlope52: true);

        bool preUnseen = !cOpen.InSight(SeatShu, 2, 5) && !cBlk.InSight(SeatShu, 2, 5);
        MovementView preDivOpen = MovementPlanner.BuildDiversified(cOpen, SeatShu, 40);
        MovementView preDivBlk = MovementPlanner.BuildDiversified(cBlk, SeatShu, 40);
        bool advSame = TryFindOption(preDivOpen, ShortcutArmyId, 5, 5, out int co) && co == 7
            && TryFindOption(preDivBlk, ShortcutArmyId, 5, 5, out int cb) && cb == 7;

        bool marched = cOpen.Submit(SpatialMarchCmd(SeatShu, ShortcutArmyId, 5, 5)).Ok
            && cBlk.Submit(SpatialMarchCmd(SeatShu, ShortcutArmyId, 5, 5)).Ok;
        bool stillUnseen = !cOpen.InSight(SeatShu, 2, 5) && !cBlk.InSight(SeatShu, 2, 5);

        Observation obsOpen = cOpen.Observe(SeatShu);
        Observation obsBlk = cBlk.Observe(SeatShu);
        SpatialContextView spOpen = SpatialContextBuilder.Build(cOpen, SeatShu, obsOpen);
        SpatialContextView spBlk = SpatialContextBuilder.Build(cBlk, SeatShu, obsBlk);
        MovementView divOpen = MovementPlanner.BuildDiversified(cOpen, SeatShu, 40, spOpen);
        MovementView divBlk = MovementPlanner.BuildDiversified(cBlk, SeatShu, 40, spBlk);
        bool spatialAndMovesIdentical = spOpen.ToJson() == spBlk.ToJson() && divOpen.ToJson() == divBlk.ToJson();

        var histOpen = new InvestigationService(obsOpen.GameId, SeatShu, obsOpen, null).SearchHistory("", 10, 0);
        var histBlk = new InvestigationService(obsBlk.GameId, SeatShu, obsBlk, null).SearchHistory("", 10, 0);
        bool histCostOnly = histOpen.Hits[1].Payload["cost"] == "6" && histBlk.Hits[1].Payload["cost"] == "7"
            && histOpen.ToJson().Replace("\"cost\":\"6\"", "\"cost\":\"7\"") == histBlk.ToJson();

        string pOpen = PromptBuilder.UserPromptWithSpatial(obsOpen, ActionCatalogue.Build(obsOpen), null, 4, 2,
            divOpen, spOpen, spatialContextEnabled: true, inquiryEnabled: true, attackEnabled: true);
        string pBlk = PromptBuilder.UserPromptWithSpatial(obsBlk, ActionCatalogue.Build(obsBlk), null, 4, 2,
            divBlk, spBlk, spatialContextEnabled: true, inquiryEnabled: true, attackEnabled: true);
        string pSnippet = DiffWindow(pOpen, pBlk, out _);
        bool promptCostOnly = pOpen != pBlk && pSnippet.Contains("\"cost\":\"6\"")
            && pOpen.Replace("\"cost\":\"6\"", "\"cost\":\"7\"") == pBlk;

        Check(preUnseen && advSame && marched && stillUnseen && spatialAndMovesIdentical && histCostOnly && promptCostOnly,
            "post-march still-unseen shortcut leak (5,5): next spatial prompt and search_history differ solely at payload.cost (6 vs 7)");
    }

    private static HashSet<string> SplitCsvTokens(string raw)
    {
        var set = new HashSet<string>();
        if (string.IsNullOrEmpty(raw)) return set;
        foreach (string part in raw.Split(','))
        {
            string trimmed = part.Trim();
            if (trimmed.Length > 0) set.Add(trimmed);
        }
        return set;
    }

    private static void SpatialSightBoundaryCornerCheck()
    {
        // Full visibility on 3x3: border cells have no unseen neighbors, so no false border frontier is emitted.
        var full = new GameEngine(708);
        full.State.Map = SpatialOrtho4Map(3, 3);
        full.State.Factions.Add(new FactionState { SeatId = SeatShu, Name = "Shu", Controller = ControllerType.Llm });
        full.State.Cities.Add(new CityState { Id = "c-full", Name = "Core", OwnerSeatId = SeatShu, X = 1, Z = 1, Population = 5000, Food = 2000, Gold = 500, Garrison = 300 });
        full.BeginSeason();
        SpatialContextView spFull = SpatialContextBuilder.Build(full, SeatShu);

        // Corner army at (0,0) on 7x7: sight covers radius 2; boundary touches unseen only to N/NE/E, never S/W/SW.
        var corner = new GameEngine(709);
        corner.State.Map = SpatialOrtho4Map(7, 7);
        corner.State.Factions.Add(new FactionState { SeatId = SeatShu, Name = "Shu", Controller = ControllerType.Llm });
        corner.State.Armies.Add(new ArmyState { Id = "a-corner", Name = "Corner", OwnerSeatId = SeatShu, UnitType = UnitTypes.Infantry, Troops = 1000, X = 0, Z = 0 });
        corner.BeginSeason();
        SpatialContextView spCorner = SpatialContextBuilder.Build(corner, SeatShu);
        var dirs = SplitCsvTokens(spCorner.Frontier.FrontierDirectionsFromOwn);
        var armyDirs = spCorner.ArmyFrontiers.Count > 0
            ? SplitCsvTokens(spCorner.ArmyFrontiers[0].FrontierDirections)
            : new HashSet<string>();
        string borders = spCorner.ArmyFrontiers.Count > 0 ? spCorner.ArmyFrontiers[0].BorderDirections ?? "" : "";

        Check(!spFull.Frontier.HasCurrentlyUnseen
            && spFull.Frontier.CurrentlyUnseenCellCount == 0
            && spFull.Frontier.SightBoundaryCellCount == 0
            && spFull.Frontier.FrontierDirectionsFromOwn == "(full map in current sight)"
            && spCorner.Frontier.HasCurrentlyUnseen
            && spCorner.Frontier.SightBoundaryCellCount > 0
            && !dirs.Contains("W") && !dirs.Contains("S") && !dirs.Contains("SW")
            && !armyDirs.Contains("W") && !armyDirs.Contains("S") && !armyDirs.Contains("SW")
            && borders.Contains("west=0") && borders.Contains("south=0")
            && dirs.Contains("E") && dirs.Contains("N"),
            "sight boundary corner check: map edges never produce false frontier directions");
    }
}
