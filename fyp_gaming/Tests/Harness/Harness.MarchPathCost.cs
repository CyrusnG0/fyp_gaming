using System;
using LuanShi.Engine;

internal static partial class Harness
{
    /// <summary>
    /// The public GameEngine.MarchPathCost is the one route rule the UI highlight, the validator and the
    /// planner share. With visible routes required it must ignore unseen terrain (same answer whether or not
    /// an unseen shortcut exists), and every destination it accepts the validator must accept too.
    /// </summary>
    private static void RunMarchPathCost()
    {
        Console.WriteLine("\n-- GameEngine.MarchPathCost (public route rule for the UI) --");
        bool plainMatchesMap = true, visibleIgnoresShortcut = true, shortcutAdvertisedWhenOff = false;
        bool validatorAgrees = true;
        int checkedTiles = 0;
        int budget = BalanceConfig.MarchBudget(1, UnitTypes.Infantry);

        var openOff = BuildUnseenShortcutWorld(707, shortcutOpen: true);
        shortcutAdvertisedWhenOff = openOff.MarchPathCost(SeatShu, 2, 2, 3, 5, budget)
            != BuildUnseenShortcutWorld(707, shortcutOpen: false).MarchPathCost(SeatShu, 2, 2, 3, 5, budget);
        for (int x = 0; x < openOff.State.Map.Width; x++)
            for (int z = 0; z < openOff.State.Map.Height; z++)
                if (openOff.MarchPathCost(SeatShu, 2, 2, x, z, budget) != openOff.State.Map.PathCost(2, 2, x, z, budget))
                    plainMatchesMap = false;

        var open = BuildUnseenShortcutWorld(707, shortcutOpen: true);
        var blocked = BuildUnseenShortcutWorld(707, shortcutOpen: false);
        open.State.RequireVisibleMarchRoutes = true;
        blocked.State.RequireVisibleMarchRoutes = true;
        for (int x = 0; x < open.State.Map.Width; x++)
            for (int z = 0; z < open.State.Map.Height; z++)
            {
                int a = open.MarchPathCost(SeatShu, 2, 2, x, z, budget);
                int b = blocked.MarchPathCost(SeatShu, 2, 2, x, z, budget);
                if (open.InSight(SeatShu, x, z) && a != b) visibleIgnoresShortcut = false;
                if (a < 0 || (x == 2 && z == 2) || open.State.CityAt(x, z) != null) continue;
                checkedTiles++;
                var clone = BuildUnseenShortcutWorld(707, shortcutOpen: true);
                clone.State.RequireVisibleMarchRoutes = true;
                if (!clone.Submit(SpatialMarchCmd(SeatShu, ShortcutArmyId, x, z)).Ok) validatorAgrees = false;
            }

        Check(plainMatchesMap && shortcutAdvertisedWhenOff,
            "MarchPathCost without the visible-route rule equals the plain map path (and sees the unseen shortcut)");
        Check(visibleIgnoresShortcut && checkedTiles > 0,
            $"MarchPathCost with visible routes is identical with or without an unseen shortcut ({checkedTiles} tiles)");
        Check(validatorAgrees,
            "every destination MarchPathCost accepts is accepted by the march validator");
    }
}
