using System;
using System.Collections.Generic;
using LuanShi.Engine;

internal static partial class Harness
{
    private const string SeatShu = "seat-shu";
    private const string ShuArmy4 = "army-shu-4";

    private static void RunSpatialContext()
    {
        Section("[map-context] opt-in spatial context and diversified movement");
        SpatialFeatureOffParity();
        SpatialHiddenWorldAndTerrainInvariance();
        SpatialMovementLegalityAndFreedom();
        SpatialDirectionalDiversityAndObjectives();
        SpatialLabelsAndFrontierHonesty();
        SpatialCrowdedMapAndPromptBound();
        SpatialReachableUnseenShortcutInvariance();
        SpatialSightBoundaryCornerCheck();
    }

    private static MapData SpatialStaggeredHexMap(int width, int height)
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
                var nbs = new List<int>(6);
                TryAddHexNeighbor(map, nbs, x - 1, z);
                TryAddHexNeighbor(map, nbs, x + 1, z);
                TryAddHexNeighbor(map, nbs, x, z - 1);
                TryAddHexNeighbor(map, nbs, x, z + 1);
                int dzDiag = (x % 2 == 0) ? -1 : 1;
                TryAddHexNeighbor(map, nbs, x - 1, z + dzDiag);
                TryAddHexNeighbor(map, nbs, x + 1, z + dzDiag);
                map.Neighbors[i] = nbs.ToArray();
            }
        }
        return map;
    }

    private static void TryAddHexNeighbor(MapData map, List<int> list, int nx, int nz)
    {
        if (map.InBounds(nx, nz)) list.Add(map.Index(nx, nz));
    }

    private static GameEngine SpatialDemoWorld(int seed = 701, bool withFrontierSample = false)
    {
        var e = new GameEngine(seed);
        var map = SpatialStaggeredHexMap(26, 17);
        for (int x = 0; x < 26; x += 2) map.Walkable[map.Index(x, 16)] = false;
        e.State.Map = map;

        e.State.Factions.Add(new FactionState { SeatId = "seat-wei", Name = "Wei", Controller = ControllerType.Llm });
        e.State.Factions.Add(new FactionState { SeatId = "seat-shu", Name = "Shu", Controller = ControllerType.Llm });
        e.State.Cities.Add(new CityState { Id = "city-wei", Name = "Xuchang", OwnerSeatId = "seat-wei", X = 3, Z = 8, Population = 15000, Food = 5000, Gold = 2000, Garrison = 1200, Morale = 80 });
        e.State.Cities.Add(new CityState { Id = "city-shu", Name = "Chengdu", OwnerSeatId = "seat-shu", X = 22, Z = 8, Population = 15000, Food = 5000, Gold = 2000, Garrison = 1200, Morale = 80 });
        e.State.Cities.Add(new CityState { Id = "city-south", Name = "Xiangyang", OwnerSeatId = "", X = 12, Z = 2, Population = 10000, Food = 2000, Gold = 800, Garrison = 500, Morale = 70 });
        e.State.WatchTowers.Add(new WatchTowerState { Id = "watch-tower-1", Name = "Southern Watch", OwnerSeatId = "", X = 6, Z = 2 });

        int[,] weiCoords = { { 2, 6 }, { 2, 8 }, { 2, 10 }, { 4, 12 } };
        for (int i = 0; i < 4; i++)
            e.State.Armies.Add(new ArmyState { Id = "army-wei-" + (i + 1), Name = "Wei " + (i + 1), OwnerSeatId = "seat-wei", UnitType = UnitTypes.Infantry, Troops = 2000, X = weiCoords[i, 0], Z = weiCoords[i, 1] });

        int[,] shuCoords = { { 23, 6 }, { 23, 8 }, { 23, 10 }, { 21, 4 } };
        for (int i = 0; i < 4; i++)
            e.State.Armies.Add(new ArmyState { Id = "army-shu-" + (i + 1), Name = "Shu " + (i + 1), OwnerSeatId = "seat-shu", UnitType = UnitTypes.Infantry, Troops = 2000, X = shuCoords[i, 0], Z = shuCoords[i, 1] });

        if (withFrontierSample)
        {
            e.State.Armies.Add(new ArmyState { Id = "army-wei-scout", Name = "Wei Scout", OwnerSeatId = "seat-wei", UnitType = UnitTypes.Cavalry, Troops = 900, X = 19, Z = 4 });
            e.State.FindFaction(SeatShu).ScoutedCities.Add(new ScoutReport { CityId = "city-south", UntilSeason = 2 });
        }

        e.BeginSeason();
        return e;
    }

    private static ActionCommand SpatialMarchCmd(string seatId, string armyId, int x, int z)
        => new ActionCommand
        {
            Type = ActionType.March, ActorSeatId = seatId, TargetId = armyId, ParamA = x, ParamB = z,
        };

    private static void SpatialFeatureOffParity()
    {
        GameEngine e = SpatialDemoWorld(701, withFrontierSample: true);
        Observation obs = e.Observe(SeatShu);
        List<ActionOption> opts = ActionCatalogue.Build(obs);
        MovementView legacyMove = MovementPlanner.Build(e, "seat-shu");
        SpatialContextView spatial = SpatialContextBuilder.Build(e, "seat-shu", obs);
        string[] hist = { "S1 recruited in city-shu" };

        bool noTags = legacyMove.Armies.Count > 0;
        bool cleanOptions = true;
        foreach (var plan in legacyMove.Armies)
        {
            if (plan.SelectionMode != null) noTags = false;
            foreach (var opt in plan.Options)
            {
                if (opt.Direction != null || opt.ExploresFrontier || opt.ObjectiveHint != null)
                    cleanOptions = false;
                if (opt.Note != null && opt.Note != "terrain cost counted"
                    && opt.Note != "neutral city in sight; march captures on arrival"
                    && opt.Note != "watch tower in sight; march captures on arrival")
                    cleanOptions = false;
            }
        }
        string moveJson = legacyMove.ToJson();
        Check(noTags && cleanOptions && !moveJson.Contains("\"direction\""),
            "feature off: MovementPlanner.Build and MoveOption.Note stay legacy/untagged");

        string off4 = PromptBuilder.MovementOptionsText(legacyMove, 4, int.MaxValue, false);
        string off3 = PromptBuilder.MovementOptionsText(legacyMove, 4, int.MaxValue);
        Check(off4 == off3,
            "feature off: MovementOptionsText(..., includeSpatialTags: false) is byte-identical");

        string vOff = PromptBuilder.VersionForSpatial(true, false, false, false, false);
        string sOff = PromptBuilder.SystemPromptForSpatial(true, false, null, false, false);
        string hOff = PromptBuilder.SchemaHashForSpatial(true, false, null, false, false);
        Check(vOff == PromptBuilder.VersionFor(true, false, false, false)
            && sOff == PromptBuilder.SystemPromptFor(true, false, null, false)
            && hOff == PromptBuilder.SchemaHashFor(true, false, null, false),
            "feature off: version, system prompt and schema hash stay identical");

        string uLegacy = PromptBuilder.UserPrompt(obs, opts, hist, 4, 3, legacyMove);
        string uOff = PromptBuilder.UserPromptWithSpatial(obs, opts, hist, 4, 3,
            legacyMove, spatial, spatialContextEnabled: false);
        Check(uLegacy == uOff && !uOff.Contains("SPATIAL CONTEXT"),
            "feature off: UserPromptWithSpatial is byte-identical to UserPrompt");
    }

    private static void SpatialHiddenWorldAndTerrainInvariance()
    {
        GameEngine eA = SpatialDemoWorld(702, withFrontierSample: true);
        GameEngine eB = SpatialDemoWorld(702, withFrontierSample: true);

        // Mutate ONLY cells outside Shu's sight AND beyond Shu's reach (x <= 15).
        Check(!eB.InSight("seat-shu", 14, 10) && !eB.InSight("seat-shu", 8, 12)
            && !eB.InSight("seat-shu", 15, 4),
            "hidden fixture: mutated enemy entities lie strictly outside Shu sight");

        eB.State.Cities.Add(new CityState
        {
            Id = "city-hidden-east", Name = "HiddenEast", OwnerSeatId = "seat-wei",
            X = 14, Z = 10, Population = 18000, Food = 9000, Gold = 5000, Garrison = 2500,
        });
        eB.State.Armies.Add(new ArmyState
        {
            Id = "army-hidden-1", Name = "Ambush", OwnerSeatId = "seat-wei",
            UnitType = UnitTypes.Cavalry, Troops = 3000, X = 15, Z = 4,
        });
        eB.State.WatchTowers.Add(new WatchTowerState
        {
            Id = "tower-hidden", Name = "Secret Tower", OwnerSeatId = "seat-wei", X = 8, Z = 12,
        });

        var mapB = eB.State.Map;
        for (int z = 0; z < mapB.Height; z++)
        {
            for (int x = 0; x <= 14; x++)
            {
                if (eB.InSight("seat-shu", x, z)) continue;
                int idx = mapB.Index(x, z);
                mapB.Cost[idx] = 3;
                if ((x + z) % 3 == 0 && eB.State.CityAt(x, z) == null && eB.State.WatchTowerAt(x, z) == null)
                    mapB.Walkable[idx] = false;
            }
        }

        Observation obsA = eA.Observe(SeatShu);
        Observation obsB = eB.Observe(SeatShu);
        SpatialContextView spA = SpatialContextBuilder.Build(eA, "seat-shu", obsA);
        SpatialContextView spB = SpatialContextBuilder.Build(eB, "seat-shu", obsB);
        Check(spA.ToJson() == spB.ToJson(),
            "hidden world + unexplored terrain: SpatialContextView JSON is byte-identical");

        MovementView mvA = MovementPlanner.BuildDiversified(eA, "seat-shu", 8, spA);
        MovementView mvB = MovementPlanner.BuildDiversified(eB, "seat-shu", 8, spB);
        Check(mvA.ToJson() == mvB.ToJson(),
            "hidden world + unexplored terrain: diversified MovementView JSON is byte-identical");

        string pA = PromptBuilder.UserPromptWithSpatial(obsA, ActionCatalogue.Build(obsA),
            null, 4, 3, mvA, spA, spatialContextEnabled: true);
        string pB = PromptBuilder.UserPromptWithSpatial(obsB, ActionCatalogue.Build(obsB),
            null, 4, 3, mvB, spB, spatialContextEnabled: true);
        Check(pA == pB && !pB.Contains("city-hidden-east") && !pB.Contains("army-hidden-1")
            && !pB.Contains("tower-hidden"),
            "hidden world + unexplored terrain: rendered prompt leaks zero hidden facts");
    }

    private static void SpatialMovementLegalityAndFreedom()
    {
        Func<GameEngine> make = () =>
        {
            var e = SpatialDemoWorld(703);
            e.State.FindArmy(ShuArmy4).UnitType = UnitTypes.Cavalry;
            return e;
        };

        GameEngine probe = make();
        SpatialContextView sp = SpatialContextBuilder.Build(probe, "seat-shu");
        MovementView div = MovementPlanner.BuildDiversified(probe, "seat-shu", 5, sp);
        MovementView all = MovementPlanner.Build(probe, "seat-shu", 100);

        ArmyMovePlan planDiv = null, planAll = null;
        foreach (var p in div.Armies) if (p.ArmyId == "army-shu-4") planDiv = p;
        foreach (var p in all.Armies) if (p.ArmyId == "army-shu-4") planAll = p;

        bool allAccepted = planDiv != null && planDiv.Options.Count == 5 && planDiv.Truncated;
        if (allAccepted)
        {
            foreach (var opt in planDiv.Options)
            {
                GameEngine c = make();
                ValidationResult r = c.Submit(SpatialMarchCmd("seat-shu", opt.ArmyId, opt.X, opt.Z));
                if (!r.Ok) allAccepted = false;
            }
        }
        Check(allAccepted,
            "authoritative legality: every advertised diversified option is accepted by Submit");

        // Find a legal destination outside the truncated diversified list and verify Submit still accepts it.
        MoveOption unlisted = null;
        if (planAll != null && planDiv != null)
        {
            var listed = new HashSet<string>();
            foreach (var o in planDiv.Options) listed.Add(o.X + "," + o.Z);
            foreach (var o in planAll.Options)
                if (!listed.Contains(o.X + "," + o.Z)) { unlisted = o; break; }
        }
        GameEngine freeEngine = make();
        bool unlistedOk = unlisted != null
            && freeEngine.Submit(SpatialMarchCmd("seat-shu", "army-shu-4", unlisted.X, unlisted.Z)).Ok;
        bool domesticOk = freeEngine.Submit(new ActionCommand
        {
            Type = ActionType.Reclaim, ActorSeatId = "seat-shu", TargetId = "city-shu",
        }).Ok;
        Check(unlistedOk && domesticOk,
            "model freedom: non-advertised legal march and domestic orders remain accepted");
    }

    private static void SpatialDirectionalDiversityAndObjectives()
    {
        GameEngine e = SpatialDemoWorld(704, withFrontierSample: true);
        SpatialContextView sp = SpatialContextBuilder.Build(e, "seat-shu");
        MovementView std6 = MovementPlanner.Build(e, "seat-shu", 6);
        MovementView div6 = MovementPlanner.BuildDiversified(e, "seat-shu", 6, sp);

        ArmyMovePlan stdPlan = null, divPlan = null;
        foreach (var p in std6.Armies) if (p.ArmyId == "army-shu-4") stdPlan = p;
        foreach (var p in div6.Armies) if (p.ArmyId == "army-shu-4") divPlan = p;

        var dirs = new HashSet<string>(StringComparer.Ordinal);
        bool hasWestFrontier = false, hasGoalHint = false;
        int minDivX = int.MaxValue, minStdX = int.MaxValue;
        if (stdPlan != null)
            foreach (var o in stdPlan.Options)
                if (o.X < minStdX) minStdX = o.X;
        if (divPlan != null)
        {
            foreach (var o in divPlan.Options)
            {
                if (!string.IsNullOrEmpty(o.Direction)) dirs.Add(o.Direction);
                if (o.ExploresFrontier && (o.Direction == "W" || o.Direction == "NW" || o.Direction == "SW"))
                    hasWestFrontier = true;
                if (!string.IsNullOrEmpty(o.ObjectiveHint)) hasGoalHint = true;
                if (o.X < minDivX) minDivX = o.X;
            }
        }
        bool hasEdgeTags = false;
        foreach (var own in sp.OwnEntities)
            if (own.Id == "army-shu-2" && own.EastEdgeDist == 2 && own.NearEdges.Contains("east=2"))
                hasEdgeTags = true;

        Check(divPlan != null && divPlan.SelectionMode == "diversified"
            && dirs.Count >= 4 && hasWestFrontier && hasGoalHint && minDivX <= minStdX && hasEdgeTags,
            $"diversity on 26x17 Shu: dirs={dirs.Count}, westFrontier={hasWestFrontier}, goalHint={hasGoalHint}, minDivX={minDivX}, minStdX={minStdX}");

        // Verify captures still come first when an unowned watch tower is in sight alongside enemy/frontier.
        e.State.WatchTowers.Add(new WatchTowerState { Id = "tower-near", Name = "Near Tower", OwnerSeatId = "", X = 21, Z = 6 });
        SpatialContextView spCap = SpatialContextBuilder.Build(e, "seat-shu");
        MovementView divCap = MovementPlanner.BuildDiversified(e, "seat-shu", 6, spCap);
        ArmyMovePlan capPlan = null;
        foreach (var p in divCap.Armies) if (p.ArmyId == "army-shu-4") capPlan = p;
        Check(capPlan != null && capPlan.Options.Count > 0 && capPlan.Options[0].Kind == "capture_tower",
            "captures first: in-sight neutral watch tower stays first in diversified options");
    }

    private static void SpatialLabelsAndFrontierHonesty()
    {
        var e = new GameEngine(705);
        e.State.Map = SpatialStaggeredHexMap(26, 17);
        e.State.Factions.Add(new FactionState { SeatId = "seat-wei", Name = "Wei", Controller = ControllerType.Llm });
        e.State.Factions.Add(new FactionState { SeatId = "seat-shu", Name = "Shu", Controller = ControllerType.Llm });
        e.State.Cities.Add(new CityState { Id = "city-wei", Name = "WeiCity", OwnerSeatId = "seat-wei", X = 3, Z = 8, Population = 10000, Food = 4000, Gold = 1000, Garrison = 500 });
        e.State.Cities.Add(new CityState { Id = "city-shu", Name = "ShuCity", OwnerSeatId = "seat-shu", X = 22, Z = 8, Population = 10000, Food = 4000, Gold = 1000, Garrison = 500 });
        e.State.Cities.Add(new CityState { Id = "city-mid", Name = "MidCity", OwnerSeatId = "", X = 12, Z = 2, Population = 8000, Food = 1500, Gold = 500, Garrison = 300 });
        e.State.Armies.Add(new ArmyState { Id = "army-wei-1", Name = "Wei Vanguard", OwnerSeatId = "seat-wei", UnitType = UnitTypes.Cavalry, Troops = 1500, X = 12, Z = 8 });
        e.State.Armies.Add(new ArmyState { Id = "army-shu-fwd", Name = "Shu Forward", OwnerSeatId = "seat-shu", UnitType = UnitTypes.Cavalry, Troops = 1800, X = 14, Z = 8 });
        e.BeginSeason();

        // Season 1: Wei steps to (13,8) while Shu sees (13,8); then Shu steps east so (13,8) leaves sight.
        bool weiMoved = e.Submit(SpatialMarchCmd("seat-wei", "army-wei-1", 13, 8)).Ok;
        SpatialContextView spVis = SpatialContextBuilder.Build(e, "seat-shu");
        bool sawVisible = spVis.VisibleOthers.Count == 1
            && spVis.VisibleOthers[0].Id == "army-wei-1"
            && spVis.VisibleOthers[0].Knowledge == "visible";

        e.State.FindFaction(SeatShu).ScoutedCities.Add(
            new ScoutReport { CityId = "city-mid", UntilSeason = 2 });
        bool shuSteppedAway = e.Submit(SpatialMarchCmd("seat-shu", "army-shu-fwd", 16, 8)).Ok;
        e.EndSeason();
        e.BeginSeason();

        SpatialContextView spStale = SpatialContextBuilder.Build(e, "seat-shu");
        string spText = PromptBuilder.SpatialContextText(spStale);
        bool hasScouted = false, hasLastSeenOut = false;
        foreach (var s in spStale.StaleIntel)
        {
            if (s.Kind == "scouted_city" && s.Id == "city-mid" && s.Knowledge == "stale")
                hasScouted = true;
            if (s.Kind == "army_last_seen" && s.Id == "army-wei-1" && s.Knowledge == "stale"
                && s.Summary.Contains("outside current sight"))
                hasLastSeenOut = true;
        }

        // Season 2: Wei moves away to (11,8) out of sight; Shu returns to (14,8) so (13,8) enters sight empty.
        bool weiLeft = e.Submit(SpatialMarchCmd("seat-wei", "army-wei-1", 11, 8)).Ok;
        e.Submit(SpatialMarchCmd("seat-shu", "army-shu-fwd", 14, 8));
        SpatialContextView spCleared = SpatialContextBuilder.Build(e, "seat-shu");
        MovementView divCleared = MovementPlanner.BuildDiversified(e, "seat-shu", 6, spCleared);
        bool marksNoLongerVisible = false;
        foreach (var s in spCleared.StaleIntel)
            if (s.Kind == "army_last_seen" && s.Id == "army-wei-1" && s.Summary.Contains("no longer visible"))
                marksNoLongerVisible = true;
        bool targetsGoneHex = false;
        foreach (var p in divCleared.Armies)
            foreach (var o in p.Options)
                if (o.ObjectiveHint != null && o.ObjectiveHint.Contains("army-wei-1"))
                    targetsGoneHex = true;

        bool honestLimitation = spStale.Frontier != null && spStale.Frontier.HasCurrentlyUnseen
            && spStale.Frontier.CurrentlyUnseenCellCount > 0
            && spStale.Frontier.SightBoundaryCellCount > 0
            && spStale.Frontier.HistoryLimitationNote.Contains("current sight boundary only")
            && spStale.Frontier.HistoryLimitationNote.Contains("does not track whether a hex has ever been explored")
            && spText.Contains("[stale]") && spText.Contains("currently_unseen=")
            && spText.Contains("sight_boundary_cells=") && !spText.Contains("unexplored");

        Check(weiMoved && sawVisible && shuSteppedAway && hasScouted && hasLastSeenOut
            && weiLeft && marksNoLongerVisible && !targetsGoneHex && honestLimitation,
            "labels and frontier honesty: visible, stale (scouted + last-seen), no-longer-visible, and current-sight frontier");
    }
}
