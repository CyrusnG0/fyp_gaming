using System;
using System.Collections.Generic;
using System.Globalization;
using LuanShi.Engine;

// [new] Deterministic spatial context + movement view for the prompt-size probe.
internal static partial class Harness
{
    private static MovementView ProbeMovement(PromptSizeProbeCase c)
    {
        var map = new SpatialMapBounds
        {
            Width = 80,
            Height = 52,
            FogCityRange = 3,
            FogArmyRange = 2,
            FogWatchTowerRange = 2,
            CenterX = 40,
            CenterZ = 26,
        };

        var own = new List<SpatialEntitySummary>();
        var plans = new List<ArmyMovePlan>();
        var frontiers = new List<SpatialArmyFrontier>();
        for (int a = 0; a < c.ArmyCount; a++)
        {
            int ax = 8 + a * 7;
            int az = 8 + (a % 3) * 5;
            string aid = "army-own-" + a.ToString(CultureInfo.InvariantCulture);
            own.Add(new SpatialEntitySummary
            {
                Kind = "army",
                Id = aid,
                Name = "Probe Host " + a.ToString(CultureInfo.InvariantCulture),
                OwnerSeatId = "seat-1",
                Allegiance = "own",
                Knowledge = "visible",
                X = ax,
                Z = az,
                Strength = 2200 + a * 100,
                UnitType = UnitTypes.Cavalry,
                RefId = "city-own-0",
                RefDistance = 5,
                RefDirection = "W",
                CenterDistance = Math.Abs(ax - 40) + Math.Abs(az - 26),
                CenterDirection = "SW",
                CenterDeltaX = 40 - ax,
                CenterDeltaZ = 26 - az,
                WestEdgeDist = ax,
                EastEdgeDist = 79 - ax,
                SouthEdgeDist = az,
                NorthEdgeDist = 51 - az,
            });

            var options = new List<MoveOption>(c.OptionsPerArmy);
            for (int o = 0; o < c.OptionsPerArmy; o++)
            {
                options.Add(new MoveOption
                {
                    ArmyId = aid,
                    X = (ax + o + 1) % 80,
                    Z = (az + o) % 52,
                    PathCost = 1 + (o % 4),
                    Kind = "move",
                    Note = o % 3 == 0 ? "terrain cost counted" : null,
                    GridDistance = 1 + (o % 8),
                    Direction = SpatialGeometry.CompassOrder[o % SpatialGeometry.CompassOrder.Length],
                    ExploresFrontier = o % 4 == 0,
                    ObjectiveHint = o % 6 == 0 ? "frontier W (toward map_center)" : null,
                });
            }
            plans.Add(new ArmyMovePlan
            {
                ArmyId = aid,
                ArmyName = "Probe Host " + a.ToString(CultureInfo.InvariantCulture),
                UnitType = UnitTypes.Cavalry,
                X = ax,
                Z = az,
                MarchesLeft = 2,
                Budget = 6,
                CanMarch = true,
                TotalReachable = c.OptionsPerArmy,
                Truncated = false,
                SelectionMode = "diversified",
                Options = options,
            });
            frontiers.Add(new SpatialArmyFrontier
            {
                ArmyId = aid,
                X = ax,
                Z = az,
                FrontierDirections = "W, NW, N",
                BorderDirections = "none",
                FrontierCellCount = 7,
            });
        }

        var visible = new List<SpatialEntitySummary>();
        for (int i = 0; i < 18; i++)
        {
            visible.Add(new SpatialEntitySummary
            {
                Kind = i % 3 == 0 ? "city" : "army",
                Id = "visible-" + i.ToString(CultureInfo.InvariantCulture),
                Name = "Spotted " + i.ToString(CultureInfo.InvariantCulture),
                OwnerSeatId = i % 2 == 0 ? "seat-0" : null,
                Allegiance = i % 2 == 0 ? "foreign" : "neutral",
                Knowledge = "visible",
                X = (i * 4) % 80,
                Z = (i * 3) % 52,
                Strength = 900 + i * 40,
                UnitType = i % 2 == 0 ? UnitTypes.Cavalry : UnitTypes.Infantry,
                WestEdgeDist = (i * 4) % 80,
                EastEdgeDist = 79 - (i * 4) % 80,
                SouthEdgeDist = (i * 3) % 52,
                NorthEdgeDist = 51 - (i * 3) % 52,
                RefId = "army-own-1",
                RefDistance = 4,
                RefDirection = "E",
                CenterDistance = Math.Abs((i * 4) % 80 - 40) + Math.Abs((i * 3) % 52 - 26),
                CenterDirection = "N",
            });
        }

        var stale = new List<SpatialStaleRecord>();
        for (int i = 0; i < 8; i++)
        {
            stale.Add(new SpatialStaleRecord
            {
                Kind = i % 2 == 0 ? "scouted_city" : "army_last_seen",
                Id = "stale-" + i.ToString(CultureInfo.InvariantCulture),
                Name = "Old Intel " + i.ToString(CultureInfo.InvariantCulture),
                OwnerSeatId = "seat-0",
                X = (i * 9) % 80,
                Z = (i * 6) % 52,
                RecordedSeason = 50 + i,
                ValidUntilSeason = 62,
                Knowledge = "stale",
                Summary = "last observed outside current sight",
            });
        }

        var bearings = new List<SpatialObjectiveBearing>();
        for (int i = 0; i < 12; i++)
        {
            bearings.Add(new SpatialObjectiveBearing
            {
                ArmyId = "army-own-" + (i % c.ArmyCount).ToString(CultureInfo.InvariantCulture),
                TargetKind = i % 2 == 0 ? "city" : "army",
                TargetId = "target-" + i.ToString(CultureInfo.InvariantCulture),
                TargetName = "Objective " + i.ToString(CultureInfo.InvariantCulture),
                Knowledge = i % 3 == 0 ? "stale" : "visible",
                TargetX = (i * 5) % 80,
                TargetZ = (i * 4) % 52,
                GridDistance = 1 + i % 7,
                DeltaX = 3 - i % 5,
                DeltaZ = i % 4 - 2,
                Direction = SpatialGeometry.CompassOrder[i % SpatialGeometry.CompassOrder.Length],
            });
        }

        var rows = new List<string>(c.VisibleRowCount);
        for (int i = 0; i < c.VisibleRowCount; i++)
            rows.Add("z=" + i.ToString(CultureInfo.InvariantCulture) + ".."
                + (i + 1).ToString(CultureInfo.InvariantCulture) + ": x=0..79");
        var terrain = new List<string>();
        for (int i = 0; i < 10; i++)
            terrain.Add("(x=" + i.ToString(CultureInfo.InvariantCulture) + ",z=3) cost=2");

        var ctx = new SpatialContextView
        {
            SeatId = "seat-1",
            Season = 60,
            Map = map,
            OwnEntities = own,
            VisibleOthers = visible,
            StaleIntel = stale,
            ObjectiveBearings = bearings,
            ArmyFrontiers = frontiers,
            Frontier = new SpatialFrontierSummary
            {
                VisibleCellCount = 2140,
                TotalCellCount = 4160,
                CurrentlyUnseenCellCount = 2020,
                SightBoundaryCellCount = 96,
                HasCurrentlyUnseen = true,
                UnseenMinX = 0,
                UnseenMaxX = 79,
                UnseenMinZ = 0,
                UnseenMaxZ = 51,
                VisibleMinX = 2,
                VisibleMaxX = 63,
                VisibleMinZ = 3,
                VisibleMaxZ = 44,
                WestUnseenCols = 2,
                EastUnseenCols = 16,
                SouthUnseenCols = 3,
                NorthUnseenCols = 8,
                VisibleRowRanges = rows,
                VisibleTerrainNotes = terrain,
                FrontierDirectionsFromOwn = "W, NW, N, NE",
                HistoryLimitationNote = "current sight boundary only; the engine does not track "
                    + "whether a hex has ever been explored",
            },
        };

        return new MovementView
        {
            SeatId = "seat-1",
            Season = 60,
            CommandPoints = 6,
            MarchOrderCost = 1,
            Armies = plans,
            SpatialContext = ctx,
        };
    }
}
