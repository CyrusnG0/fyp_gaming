using System;
using System.Collections.Generic;
using LuanShi.Engine;

internal static partial class Harness
{
    private static void RunSeasonalModifiers()
    {
        // ------------------------------------------------ 天時 seasonal modifiers
        Section("seasonal modifiers (design §3 table, §10.1 嚴冬)");

        Check(BalanceConfig.SeasonName(1) == "Spring" && BalanceConfig.SeasonName(2) == "Summer"
           && BalanceConfig.SeasonName(3) == "Autumn" && BalanceConfig.SeasonName(4) == "Winter"
           && BalanceConfig.SeasonName(5) == "Spring",
              "seasons cycle 春→夏→秋→冬 (Season 5 wraps to Spring)");

        {
            // single loyal city, no army: food = 1000 + harvest − eaten(200). Base yield 200, reclaim 0.
            var expected = new Dictionary<int, int> { { 1, 940 }, { 2, 1000 }, { 3, 1200 }, { 4, 860 } };
            foreach (var kv in expected)
            {
                var e = Build(3, kv.Key, morale: 100, food: 1000);
                e.EndSeason();
                int food = e.State.FindCity("city-a").Food;
                Check(food == kv.Value,
                    $"season {kv.Key} ({BalanceConfig.SeasonName(kv.Key)}) food {food}, expected {kv.Value} " +
                    $"[x{BalanceConfig.SeasonFoodYieldMult(kv.Key)}]");
            }
        }

        {
            // winter march budget: 6 → floor(6 × 0.7) = 4
            Check(BalanceConfig.MarchBudget(2) == 6, $"summer march budget = {BalanceConfig.MarchBudget(2)} (expected 6)");
            Check(BalanceConfig.MarchBudget(4) == 4, $"winter march budget = {BalanceConfig.MarchBudget(4)} (expected 4)");

            var summer = Build(3, 2, morale: 100, food: 5000);
            var summerArmy = AddArmy(summer, "seat-a", 0, 2000);
            var summerMove = summer.Submit(new ActionCommand { Type = ActionType.March, ActorSeatId = "seat-a", TargetId = summerArmy.Id, ParamA = 6, ParamB = 0 });
            Check(summerMove.Ok && summerArmy.X == 6, $"summer march of cost 6 is allowed ({summerMove.Error})");

            var winter = Build(3, 4, morale: 100, food: 5000);
            var winterArmy = AddArmy(winter, "seat-a", 0, 2000);
            var winterFar = winter.Submit(new ActionCommand { Type = ActionType.March, ActorSeatId = "seat-a", TargetId = winterArmy.Id, ParamA = 6, ParamB = 0 });
            Check(!winterFar.Ok && winterFar.Error.Contains("max cost 4"),
                  $"winter march of cost 6 is refused ({winterFar.Error})");
            var winterNear = winter.Submit(new ActionCommand { Type = ActionType.March, ActorSeatId = "seat-a", TargetId = winterArmy.Id, ParamA = 4, ParamB = 0 });
            Check(winterNear.Ok && winterArmy.X == 4, $"winter march of cost 4 is allowed ({winterNear.Error})");
        }
    }
}
