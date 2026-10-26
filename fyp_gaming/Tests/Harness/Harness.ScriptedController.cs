using System;
using System.Collections.Generic;
using LuanShi.Engine;

internal static partial class Harness
{
    private static void RunScriptedController()
    {
        // ------------------------------------------------ scripted AI reacts to the new pressure
        Section("ScriptedController (relief priority, winter march budget)");

        {
            // 民心 < 25 outranks every spending order: the relief comes first in the plan
            var e = Build(5, 1, morale: 20, food: 5000);
            e.State.FindCity("city-a").Gold = 100;      // so 徵稅 would otherwise be attractive
            var plan = ScriptedController.PlanTurn(e.State, "seat-a");
            Check(plan.Count > 0 && plan[0].Type == ActionType.Relief && plan[0].TargetId == "city-a",
                  $"a revolt-risk city is relieved first (first order: {(plan.Count > 0 ? plan[0].Type : "none")})");
        }

        {
            // the scripted march obeys the winter budget (a cost-6 move is not even planned)
            var summer = Build(5, 2, morale: 100, food: 5000);
            summer.State.Cities.Add(new CityState { Id = "city-b", Name = "City B", X = 6, Z = 0, OwnerSeatId = null, Population = 10000, Food = 500, Gold = 0, Garrison = 0 });
            AddArmy(summer, "seat-a", 0, 3000);
            Check(HasPlan(summer, ActionType.March), "summer: the scripted seat plans the cost-6 march");

            var winter = Build(5, 4, morale: 100, food: 5000);
            winter.State.Cities.Add(new CityState { Id = "city-b", Name = "City B", X = 6, Z = 0, OwnerSeatId = null, Population = 10000, Food = 500, Gold = 0, Garrison = 0 });
            AddArmy(winter, "seat-a", 0, 3000);
            Check(!HasPlan(winter, ActionType.March), "winter: the scripted seat plans no march it cannot afford");
        }
    }
}
