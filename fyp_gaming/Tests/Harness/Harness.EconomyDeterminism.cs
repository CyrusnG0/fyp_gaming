using System;
using System.Collections.Generic;
using LuanShi.Engine;

internal static partial class Harness
{
    private static void RunEconomyDeterminism()
    {
        Section("economy determinism (exact decimal vs Mono F64 / CoreCLR F32)");

        // 1. Exact cross-runtime harvest counterexample:
        //    (int)(float)(200 * (1f + 5 / 100f) * 0.5f * 2f * 1.1f) gave 231 in Mono vs 230 in CoreCLR.
        //    Exact base-10 arithmetic gives 231 on every runtime.
        {
            var e = MakeEconomyCaseEngine(
                BalanceProfile.Standard, season: 3, pop: 0, food: 1000, gold: 0,
                garrison: 0, garrisonType: UnitTypes.Infantry, morale: 25, reclaimPct: 5,
                agriLevel: 1, govLevel: 0);
            e.EndSeason();
            var c = e.State.Cities[0];
            Check(c.Food == 1231, $"harvest counterexample (200 * 1.05 * 0.5 * 2 * 1.1) == 231 (got {c.Food - 1000})");
        }

        // 2. Original S1 tax & food consumption case from the 5-season demo:
        //    9000 * 0.01f gave 89 in Mono F64 vs 90 in CoreCLR F32; 9000 * 0.02f + 50f gave 229 vs 230.
        //    Exact base-10 arithmetic gives tax=90, harvest=210 (300*0.7), eaten=230 -> Food=1480, Gold=990.
        {
            var e = MakeEconomyCaseEngine(
                BalanceProfile.EasyResearch, season: 1, pop: 9000, food: 1500, gold: 900,
                garrison: 1000, garrisonType: UnitTypes.Infantry, morale: 80, reclaimPct: 0,
                agriLevel: 0, govLevel: 0);
            e.EndSeason();
            var c = e.State.Cities[0];
            Check(c.Gold == 990, $"original tax case (9000 * 0.01) == 90 (got {c.Gold - 900})");
            Check(c.Food == 1480, $"original S1 food net (1500 + 210 - 230) == 1480 (got {c.Food})");
        }

        // 3. S5 harvest edge case: 300 * 1.6 * 0.7 gave 335 in Mono F64 vs 336 in CoreCLR F32.
        {
            var e = MakeEconomyCaseEngine(
                BalanceProfile.EasyResearch, season: 1, pop: 0, food: 1000, gold: 0,
                garrison: 0, garrisonType: UnitTypes.Infantry, morale: 80, reclaimPct: 60,
                agriLevel: 0, govLevel: 0);
            e.EndSeason();
            var c = e.State.Cities[0];
            Check(c.Food == 1336, $"S5 harvest edge (300 * 1.6 * 0.7) == 336 (got {c.Food - 1000})");
        }

        // 4. Varied valid settings matrix across profiles, seasons, morale, reclaim, research, and unit types.
        var profiles = new[] { BalanceProfile.Standard, BalanceProfile.EasyResearch, BalanceProfile.FoodEasy };
        int[] seasons = { 1, 2, 3, 4 };
        int[] morales = { 25, 30, 80 };
        int[] reclaims = { 0, 5, 10, 15, 25, 60 };
        int[] agriLevels = { 0, 1, 5 };
        int[] govLevels = { 0, 1 };
        int[] pops = { 9000, 10000, 10555 };
        string[] unitTypes = { UnitTypes.Infantry, UnitTypes.Cavalry, UnitTypes.Siege };

        int casesChecked = 0;
        bool allMatrixMatched = true;
        foreach (var profile in profiles)
        foreach (int season in seasons)
        foreach (int morale in morales)
        foreach (int reclaim in reclaims)
        {
            int agri = agriLevels[casesChecked % agriLevels.Length];
            int gov = govLevels[casesChecked % govLevels.Length];
            int pop = pops[casesChecked % pops.Length];
            string unitType = unitTypes[casesChecked % unitTypes.Length];
            int garrison = 750;
            int startFood = 5000;
            int startGold = 1000;

            var e = MakeEconomyCaseEngine(profile, season, pop, startFood, startGold,
                garrison, unitType, morale, reclaim, agri, gov);
            e.EndSeason();
            var c = e.State.Cities[0];

            decimal yieldFactor = morale < BalanceConfig.MoraleYieldPenaltyThreshold ? 0.5m : 1m;
            decimal seasonMult = BalanceConfig.SeasonFoodYieldMultDecimal(season);
            decimal agriMult = BalanceConfig.ResearchFoodYieldMultDecimal(agri);
            decimal govMult = BalanceConfig.ResearchTaxMultDecimal(gov);

            int expTax = (int)(pop * BalanceConfig.TaxGoldPerPopDecimal * yieldFactor * govMult);
            int expHarvest = (int)(profile.CityBaseFoodYield * (1m + reclaim / 100m) * yieldFactor * seasonMult * agriMult);
            int expEaten = (int)(pop * BalanceConfig.FoodPerPopDecimal + profile.TroopFoodUpkeepDecimal(garrison, unitType));
            int expFood = startFood + expHarvest - expEaten;
            int expGold = startGold + expTax;

            if (c.Gold != expGold || c.Food != expFood)
            {
                allMatrixMatched = false;
                Console.WriteLine($"  MATRIX MISMATCH profile={profile.Key} s={season} m={morale} r={reclaim} agri={agri} gov={gov} pop={pop} unit={unitType}: got G={c.Gold} F={c.Food}, exp G={expGold} F={expFood}");
            }
            casesChecked++;
        }
        Check(allMatrixMatched && casesChecked == 216,
            $"varied valid settings matrix ({casesChecked} cases) matches exact decimal gold & food");

        // 5. Starvation population loss & desertion checks.
        {
            var e = MakeEconomyCaseEngine(
                BalanceProfile.Standard, season: 4, pop: 9055, food: 0, gold: 100,
                garrison: 0, garrisonType: UnitTypes.Infantry, morale: 80, reclaimPct: 0,
                agriLevel: 0, govLevel: 0);
            e.State.Armies.Add(new ArmyState
            {
                Id = "army-starve", OwnerSeatId = "seat-0", Name = "StarveArmy",
                X = 5, Z = 0, Troops = 1335, Morale = 60, UnitType = UnitTypes.Infantry
            });
            e.EndSeason();
            var c = e.State.Cities[0];
            var a = e.State.Armies[0];
            // Harvest: 200 * 0.3 = 60; Eaten: (int)(9055 * 0.02) = 181 -> Food = -121 -> Starvation lost = (int)(9055 * 0.10) = 905 -> Pop = 8150
            Check(c.Food == 0 && c.Population == 8150, $"starvation pop loss exact (pop={c.Population}, exp=8150)");
            // Winter desertion: 15% of 1335 = (int)(1335 * 0.15) = 200 -> Troops = 1135
            Check(a.Troops == 1135, $"winter desertion loss exact (troops={a.Troops}, exp=1135)");
        }
    }

    private static GameEngine MakeEconomyCaseEngine(
        BalanceProfile profile, int season, int pop, int food, int gold,
        int garrison, string garrisonType, int morale, int reclaimPct,
        int agriLevel, int govLevel)
    {
        var e = new GameEngine(12345, balance: profile);
        e.State.Season = season;
        e.State.Map = Corridor(12);
        var fac = new FactionState { SeatId = "seat-0", Name = "Wei", Controller = ControllerType.Human };
        fac.Research[ResearchLine.Agri] = agriLevel;
        fac.Research[ResearchLine.Governance] = govLevel;
        e.State.Factions.Add(fac);
        e.State.Cities.Add(new CityState
        {
            Id = "city-0", Name = "TestCity", OwnerSeatId = "seat-0",
            X = 5, Z = 0, Population = pop, Food = food, Gold = gold,
            Garrison = garrison, GarrisonType = garrisonType,
            Morale = morale, ReclaimPct = reclaimPct
        });
        return e;
    }
}
