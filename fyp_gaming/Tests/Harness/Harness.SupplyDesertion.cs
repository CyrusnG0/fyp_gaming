using System;
using System.Collections.Generic;
using LuanShi.Engine;

internal static partial class Harness
{
    private static void RunSupplyDesertion()
    {
        // ------------------------------------------------ 逃兵 army supply & desertion
        Section("army supply & desertion (design §5.1 補給, §6.3 斷補 10%/季)");

        {
            var e = Build(7, 1, morale: 100, food: 0, population: 1000);
            var army = AddArmy(e, "seat-a", 5, 2000);
            e.EndSeason();
            var desertion = Find(e.Log, "desertion");
            Check(desertion != null, "an unfed army deserts");
            Check(desertion != null && desertion.Payload["cause"] == "no_food", "desertion cause = no_food");
            Check(desertion != null && desertion.Payload["troops_lost"] == "200", "desertion costs 10% of the troops (2000 → 1800)");
            Check(army.Troops == 1800, $"troops after desertion = {army.Troops}");
            Check(army.Morale == 45, $"desertion costs 15 士氣 (60 → {army.Morale})");
            Check(desertion != null && desertion.Visibility == "owner_only", "desertion is owner-only");

            // the pool is untouched when the army cannot be fed at all (no partial ration):
            // the same city without the army ends the season with exactly the same food
            var control = Build(7, 1, morale: 100, food: 0, population: 1000);
            control.EndSeason();
            Check(e.State.FindCity("city-a").Food == control.State.FindCity("city-a").Food,
                  $"nothing is consumed on a failed ration (food {e.State.FindCity("city-a").Food}, control {control.State.FindCity("city-a").Food})");
        }

        {
            // winter bites harder: 10% + 5% (design §10.1 嚴冬)
            var e = Build(7, 4, morale: 100, food: 0, population: 1000);
            var army = AddArmy(e, "seat-a", 5, 2000);
            e.EndSeason();
            var desertion = Find(e.Log, "desertion");
            Check(desertion != null && desertion.Payload["troops_lost"] == "300", "winter desertion loses 15% (3000 of 2000 → 1700)");
            Check(army.Troops == 1700, $"winter troops after desertion = {army.Troops}");
        }

        {
            // a fed army eats from the city it stands on first
            var e = Build(7, 1, morale: 100, food: 1000, population: 1000);
            e.State.Cities.Add(new CityState
            {
                Id = "city-b", Name = "City B", X = 5, Z = 0, OwnerSeatId = "seat-a",
                Population = 1000, Food = 1000, Gold = 1000, Garrison = 0, Morale = 100,
            });
            AddArmy(e, "seat-a", 0, 1000);          // need = 100; stands on city-a
            e.EndSeason();
            var a = e.State.FindCity("city-a");
            var b = e.State.FindCity("city-b");
            Check(a.Food == 1020, $"the standing city pays the ration (city A {a.Food}, expected 1020)");
            Check(b.Food == 1120, $"the other city pays nothing (city B {b.Food}, expected 1120)");
            Check(CountEvents(e.Log, "desertion") == 0, "no desertion when the pool covers the ration");
        }

        {
            // an army in the field draws on the whole pool, proportionally
            var e = Build(7, 1, morale: 100, food: 1000, population: 1000);
            e.State.Cities[0].X = 9;                 // keep both cities off the army's hex
            e.State.Cities.Add(new CityState
            {
                Id = "city-b", Name = "City B", X = 0, Z = 0, OwnerSeatId = "seat-a",
                Population = 1000, Food = 1000, Gold = 1000, Garrison = 0, Morale = 100,
            });
            AddArmy(e, "seat-a", 5, 1000);          // need = 100 → 50 from each equal store
            e.EndSeason();
            Check(e.State.FindCity("city-a").Food == 1070, $"proportional pool draw, city A ({e.State.FindCity("city-a").Food}, expected 1070)");
            Check(e.State.FindCity("city-b").Food == 1070, $"proportional pool draw, city B ({e.State.FindCity("city-b").Food}, expected 1070)");
        }
    }
}
