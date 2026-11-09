using System;
using System.Collections.Generic;
using LuanShi.Engine;

// [new] Opt-in balance profiles (2026-11-09): the standard profile must reproduce every existing
// number and rounding path, the easy-research profile applies its documented reductions only, and
// two matches with different profiles never share rules. Prefix every assertion with [new].

internal static partial class Harness
{
    private static void RunBalanceProfile()
    {
        Section("[new] balance profiles: standard parity + opt-in easy-research reductions");

        // ------------------------------------------------ selection and immutable values
        Check(BalanceProfile.Standard.IsStandard
              && BalanceProfile.Standard.StartingFood == BalanceConfig.StartFood
              && BalanceProfile.Standard.StartingGold == BalanceConfig.StartGold
              && BalanceProfile.Standard.CityBaseFoodYield == BalanceConfig.CityBaseFoodYield
              && BalanceProfile.Standard.ArmyFoodUpkeepMultiplier == 1f
              && BalanceProfile.Standard.DomesticGoldCostMultiplier == 1f,
              "[new] the standard profile is exactly the shipped BalanceConfig values");
        Check(BalanceProfile.EasyResearch.Key == BalanceProfile.EasyResearchKey
              && !BalanceProfile.EasyResearch.IsStandard
              && BalanceProfile.EasyResearch.StartingFood == 1500
              && BalanceProfile.EasyResearch.StartingGold == 900
              && BalanceProfile.EasyResearch.CityBaseFoodYield == 300
              && BalanceProfile.EasyResearch.ArmyFoodUpkeepMultiplier == 0.5f
              && BalanceProfile.EasyResearch.DomesticGoldCostMultiplier == 0.5f,
              "[new] easy-research is 1500 food / 900 gold / 300 yield / x0.5 upkeep / x0.5 domestic gold");

        BalanceProfile.TryGet(null, out BalanceProfile fromNull);
        BalanceProfile.TryGet("", out BalanceProfile fromEmpty);
        BalanceProfile.TryGet("standard", out BalanceProfile fromStandard);
        BalanceProfile.TryGet("easy-research", out BalanceProfile fromEasy);
        bool unknownRejected = !BalanceProfile.TryGet("easy", out BalanceProfile fromUnknown)
                               && fromUnknown == null;
        Check(fromNull == BalanceProfile.Standard && fromEmpty == BalanceProfile.Standard
              && fromStandard == BalanceProfile.Standard && fromEasy == BalanceProfile.EasyResearch
              && unknownRejected,
              "[new] profile selection: default standard, known key easy-research, unknown key rejected");

        // ------------------------------------------------ the shipped constants stay untouched
        Check(BalanceConfig.StartFood == 500 && BalanceConfig.StartGold == 300
              && BalanceConfig.CityBaseFoodYield == 200
              && BalanceConfig.LightenLaborGoldCost == 100
              && BalanceConfig.RecruitGoldCostInfantry == 100,
              "[new] BalanceConfig keeps the standard numbers (the profile is opt-in, not a edit)");

        // ------------------------------------------------ domestic gold costs, rounded up
        Check(BalanceProfile.Standard.LightenLaborGoldCost == BalanceConfig.LightenLaborGoldCost
              && BalanceProfile.Standard.RecruitGoldCost(UnitTypes.Infantry, 1000) == 100
              && BalanceProfile.Standard.RecruitGoldCost(UnitTypes.Archer, 1000) == 150
              && BalanceProfile.Standard.RecruitGoldCost(UnitTypes.Cavalry, 1000) == 300
              && BalanceProfile.Standard.RecruitGoldCost(UnitTypes.Siege, 1000) == 500,
              "[new] standard domestic gold costs are unchanged (lighten labor and per-type recruit)");
        Check(BalanceProfile.EasyResearch.LightenLaborGoldCost == 50
              && BalanceProfile.EasyResearch.RecruitGoldCost(UnitTypes.Infantry, 1000) == 50
              && BalanceProfile.EasyResearch.RecruitGoldCost(UnitTypes.Archer, 1000) == 75
              && BalanceProfile.EasyResearch.RecruitGoldCost(UnitTypes.Cavalry, 1000) == 150
              && BalanceProfile.EasyResearch.RecruitGoldCost(UnitTypes.Siege, 1000) == 250,
              "[new] easy domestic gold costs are halved per type (50/75/150/250)");
        Check(BalanceProfile.EasyResearch.DomesticGoldCost(101) == 51
              && BalanceProfile.EasyResearch.DomesticGoldCost(1) == 1
              && BalanceProfile.Standard.DomesticGoldCost(101) == 101,
              "[new] a halved domestic gold cost rounds up and never rounds a positive cost to zero");

        // ------------------------------------------------ the engine carries the chosen profile
        Check(ReferenceEquals(new GameEngine(1).Balance, BalanceProfile.Standard)
              && ReferenceEquals(new GameEngine(1, "g-001", BalanceProfile.Standard).Balance,
                                 BalanceProfile.Standard)
              && ReferenceEquals(new GameEngine(1, "g-001", BalanceProfile.EasyResearch).Balance,
                                 BalanceProfile.EasyResearch),
              "[new] the engine defaults to standard and stores the profile passed to its constructor");

        // ------------------------------------------------ standard engine replay is byte-identical
        string defaultReplay = ProfileReplay(null);
        string standardReplay = ProfileReplay(BalanceProfile.Standard);
        Check(defaultReplay == standardReplay && defaultReplay.Length > 0,
              $"[new] an explicit standard profile replays byte-identically to no profile ({defaultReplay.Length} chars)");

        // ------------------------------------------------ starting resources come from the profile
        var owned = new CityState { Id = "city-x", Name = "X", OwnerSeatId = "seat-a" };
        BalanceProfile.EasyResearch.ApplyStartingResources(owned);
        var standardOwned = new CityState { Id = "city-y", Name = "Y", OwnerSeatId = "seat-a" };
        BalanceProfile.Standard.ApplyStartingResources(standardOwned);
        var neutral = new CityState { Id = "city-n", Name = "N", X = 4, Z = 0, Food = 200, Gold = 100 };
        BalanceProfile.EasyResearch.ApplyStartingResources(neutral);
        Check(owned.Food == 1500 && owned.Gold == 900
              && owned.Population == BalanceConfig.StartPopulation,
              $"[new] easy starting resources are 1500 food / 900 gold ({owned.Food}/{owned.Gold})");
        Check(standardOwned.Food == 500 && standardOwned.Gold == 300,
              $"[new] standard starting resources stay 500 food / 300 gold ({standardOwned.Food}/{standardOwned.Gold})");
        Check(neutral.Food == 200 && neutral.Gold == 100,
              "[new] a neutral city keeps its own placeholder values");

        // ------------------------------------------------ season resolution: yield and upkeep
        int standardHarvest = ProfileHarvest(BalanceProfile.Standard);
        int easyHarvest = ProfileHarvest(BalanceProfile.EasyResearch);
        Check(standardHarvest == 200 && easyHarvest == 300,
              $"[new] city base food yield is 200 standard vs 300 easy (summer harvest {standardHarvest}/{easyHarvest})");

        int standardArmyNeed = ProfileArmyNeed(BalanceProfile.Standard);
        int easyArmyNeed = ProfileArmyNeed(BalanceProfile.EasyResearch);
        Check(standardArmyNeed == 200 && easyArmyNeed == 100,
              $"[new] 2,000 infantry eat 200 standard vs 100 easy food ({standardArmyNeed}/{easyArmyNeed})");
        Check(BalanceProfile.EasyResearch.TroopFoodUpkeep(2000, UnitTypes.Cavalry) == 200f
              && BalanceProfile.Standard.TroopFoodUpkeep(2000, UnitTypes.Cavalry) == 400f,
              "[new] the x0.5 upkeep applies per 兵種 (2,000 騎兵 400 → 200)");

        int standardGarrisonNeed = ProfileGarrisonNeed(BalanceProfile.Standard);
        int easyGarrisonNeed = ProfileGarrisonNeed(BalanceProfile.EasyResearch);
        Check(standardGarrisonNeed == 200 && easyGarrisonNeed == 100,
              $"[new] a 2,000 garrison also eats 200 standard vs 100 easy food ({standardGarrisonNeed}/{easyGarrisonNeed})");

        // ------------------------------------------------ domestic gold cost validation/deduction
        const int PoorGold = 60;
        {
            var easy = ProfileScenario(BalanceProfile.EasyResearch, gold: PoorGold);
            var easyCity = easy.State.FindCity("city-a");
            int cpBefore = easy.State.FindFaction("seat-a").CommandPoints;
            var easyOrder = easy.Submit(DomesticOrder("seat-a", ActionType.LightenLabor, "city-a"));
            var lightened = Find(easy.Log, "labor_lightened");
            Check(easyOrder.Ok && easyCity.Gold == PoorGold - 50
                  && lightened != null && lightened.Payload["gold_spent"] == "50",
                  $"[new] easy 輕徭 is accepted and deducts 50 gold ({easyCity.Gold}, {easyOrder.Error})");
            Check(easy.State.FindFaction("seat-a").CommandPoints == cpBefore - 1,
                  "[new] easy 輕徭 still costs exactly one command point");
        }
        {
            var standard = ProfileScenario(BalanceProfile.Standard, gold: PoorGold);
            var standardCity = standard.State.FindCity("city-a");
            var standardOrder = standard.Submit(DomesticOrder("seat-a", ActionType.LightenLabor, "city-a"));
            Check(!standardOrder.Ok && standardOrder.Error.Contains("needs 100")
                  && standardCity.Gold == PoorGold && Find(standard.Log, "labor_lightened") == null,
                  $"[new] standard 輕徭 still needs 100 gold and refuses 60 ({standardOrder.Error})");
        }
        {
            var easy = ProfileScenario(BalanceProfile.EasyResearch, gold: PoorGold);
            var easyCity = easy.State.FindCity("city-a");
            int populationBefore = easyCity.Population;
            var recruit = easy.Submit(RecruitOrder("seat-a", "city-a", UnitTypes.Infantry));
            Check(recruit.Ok && easyCity.Gold == PoorGold - 50
                  && easyCity.Garrison == 1000
                  && easyCity.Population == populationBefore - BalanceConfig.RecruitPopCost,
                  $"[new] easy 募兵 infantry is accepted at 50 gold ({easyCity.Gold}, {recruit.Error})");
            Check(easy.State.FindFaction("seat-a").CommandPoints == BalanceConfig.CommandPointsPerSeason - 1,
                  "[new] easy 募兵 still costs exactly one command point");
        }
        {
            var standard = ProfileScenario(BalanceProfile.Standard, gold: PoorGold);
            var recruit = standard.Submit(RecruitOrder("seat-a", "city-a", UnitTypes.Infantry));
            Check(!recruit.Ok && recruit.Error.Contains("needs 100"),
                  $"[new] standard 募兵 still needs 100 gold and refuses 60 ({recruit.Error})");
        }

        // ------------------------------------------------ catalogue agrees with the engine
        bool catalogueAgrees = true;
        var mismatches = new List<string>();
        foreach (int gold in new[] { 0, 49, 50, 51, 99, 100, 101 })
        {
            foreach (BalanceProfile profile in new[] { BalanceProfile.Standard, BalanceProfile.EasyResearch })
            {
                var probe = ProfileScenario(profile, gold: gold);
                ActionOption option = FindCatalogueOption(probe, ActionType.LightenLabor);
                bool accepted = probe.Submit(DomesticOrder("seat-a", ActionType.LightenLabor, "city-a")).Ok;
                if (option == null || option.LikelyAffordable != accepted)
                {
                    catalogueAgrees = false;
                    mismatches.Add($"{profile.Key}@{gold}: catalogue={option?.LikelyAffordable} engine={accepted}");
                }
            }
        }
        Check(catalogueAgrees,
              "[new] advertised lighten-labor affordability matches the engine for both profiles "
              + $"({mismatches.Count} mismatches)");

        var easyObs = ProfileScenario(BalanceProfile.EasyResearch, gold: PoorGold).Observe("seat-a");
        var standardObs = ProfileScenario(BalanceProfile.Standard, gold: PoorGold).Observe("seat-a");
        ActionOption easyLabor = FindCatalogueOption(easyObs, ActionType.LightenLabor);
        ActionOption easyRecruit = FindCatalogueOption(easyObs, ActionType.Recruit);
        ActionOption standardLabor = FindCatalogueOption(standardObs, ActionType.LightenLabor);
        ActionOption standardRecruit = FindCatalogueOption(standardObs, ActionType.Recruit);
        Check(easyLabor != null && easyLabor.LikelyAffordable
              && easyRecruit != null && easyRecruit.LikelyAffordable
              && standardLabor != null && !standardLabor.LikelyAffordable
              && standardRecruit != null && !standardRecruit.LikelyAffordable,
              "[new] at 60 gold the easy catalogue advertises 輕徭/募兵 as affordable and standard does not");
        Check(easyLabor != null && standardLabor != null
              && easyLabor.CommandPointCost == 1 && standardLabor.CommandPointCost == 1,
              "[new] command-point costs are unchanged by the profile (1 for 輕徭)");

        // ------------------------------------------------ two matches, no leakage
        {
            var easy = ProfileScenario(BalanceProfile.EasyResearch, population: 0, food: 0);
            var standard = ProfileScenario(BalanceProfile.Standard, population: 0, food: 0);
            easy.EndSeason();
            int easyFirst = easy.State.FindCity("city-a").Food;
            standard.EndSeason();
            int standardAfterEasy = standard.State.FindCity("city-a").Food;
            easy.BeginSeason();
            easy.State.Season = 2;   // keep Summer so the control yield is the same 300
            easy.EndSeason();
            int easySecond = easy.State.FindCity("city-a").Food;
            Check(easyFirst == 300 && standardAfterEasy == 200 && easySecond - easyFirst == 300,
                  $"[new] two matches keep separate rules (easy {easyFirst}+{easySecond - easyFirst}, standard {standardAfterEasy})");
            Check(!ReferenceEquals(easy.Balance, standard.Balance)
                  && ReferenceEquals(standard.Balance, BalanceProfile.Standard)
                  && BalanceProfile.EasyResearch.LightenLaborGoldCost == 50,
                  "[new] the immutable profile instances are untouched by a standard match");
        }

        // ------------------------------------------------ evidence record
        string easyJson = BalanceProfile.EasyResearch.ToJson();
        string standardJson = BalanceProfile.Standard.ToJson();
        Check(easyJson.Contains("\"profile\":\"easy-research\"")
              && easyJson.Contains("\"starting_food\":1500")
              && easyJson.Contains("\"army_food_upkeep_mult\":0.5")
              && standardJson.Contains("\"profile\":\"standard\""),
              "[new] the profile serialises for run records and evidence notes: " + easyJson);
    }

    /// <summary>Standard-vs-profile replay of the same domestic path; profile null = engine default.</summary>
    private static string ProfileReplay(BalanceProfile profile)
    {
        var e = new GameEngine(17, "g-001", profile);
        e.State.Map = Corridor(12);
        e.State.Factions.Add(new FactionState
        {
            SeatId = "seat-a", Name = "Wei", Controller = ControllerType.Human,
        });
        e.State.Cities.Add(new CityState
        {
            Id = "city-a", Name = "City A", X = 0, Z = 0, OwnerSeatId = "seat-a",
            Population = 10000, Food = 5000, Gold = 1000, Garrison = 0, Morale = BalanceConfig.MoraleStart,
        });
        e.BeginSeason();
        e.State.Season = 2;   // Summer: SeasonFoodYieldMult == 1
        e.Submit(DomesticOrder("seat-a", ActionType.LevyTax, "city-a"));
        e.Submit(DomesticOrder("seat-a", ActionType.LightenLabor, "city-a"));
        e.Submit(DomesticOrder("seat-a", ActionType.Reclaim, "city-a"));
        e.EndSeason();
        return e.Log.ToJsonLines();
    }

    /// <summary>One owned city with no population or garrison, Summer, empty stores: the harvest is the
    /// only food change, so the season's closing food is exactly the city base yield.</summary>
    private static int ProfileHarvest(BalanceProfile profile)
    {
        var e = ProfileScenario(profile, population: 0, food: 0);
        e.EndSeason();
        return e.State.FindCity("city-a").Food;
    }

    /// <summary>Food a 2,000-troop army draws in one season (with vs without the army).</summary>
    private static int ProfileArmyNeed(BalanceProfile profile)
    {
        var withArmy = ProfileScenario(profile, population: 0, food: 5000);
        AddArmy(withArmy, "seat-a", 5, 2000);
        withArmy.EndSeason();
        var control = ProfileScenario(profile, population: 0, food: 5000);
        control.EndSeason();
        return control.State.FindCity("city-a").Food - withArmy.State.FindCity("city-a").Food;
    }

    /// <summary>Food a 2,000-troop garrison eats in one season (with vs without the garrison).</summary>
    private static int ProfileGarrisonNeed(BalanceProfile profile)
    {
        var withGarrison = ProfileScenario(profile, population: 0, food: 5000);
        withGarrison.State.FindCity("city-a").Garrison = 2000;
        withGarrison.EndSeason();
        var control = ProfileScenario(profile, population: 0, food: 5000);
        control.EndSeason();
        return control.State.FindCity("city-a").Food - withGarrison.State.FindCity("city-a").Food;
    }

    /// <summary>One human faction with one city at (0,0); Summer unless asked otherwise.</summary>
    private static GameEngine ProfileScenario(BalanceProfile profile, int gold = 1000, int food = 5000,
        int population = 10000, int season = 2)
    {
        var e = new GameEngine(17, "g-001", profile);
        e.State.Map = Corridor(12);
        e.State.Factions.Add(new FactionState
        {
            SeatId = "seat-a", Name = "Wei", Controller = ControllerType.Human,
        });
        e.State.Cities.Add(new CityState
        {
            Id = "city-a", Name = "City A", X = 0, Z = 0, OwnerSeatId = "seat-a",
            Population = population, Food = food, Gold = gold, Garrison = 0,
            Morale = BalanceConfig.MoraleStart,
        });
        e.BeginSeason();
        e.State.Season = season;
        return e;
    }

    private static ActionCommand RecruitOrder(string seat, string cityId, string unitType)
        => new ActionCommand
        {
            Type = ActionType.Recruit,
            ActorSeatId = seat,
            TargetId = cityId,
            Args = new Dictionary<string, string> { { "unit_type", unitType } },
        };

    private static ActionOption FindCatalogueOption(GameEngine engine, string type)
        => FindCatalogueOption(engine.Observe("seat-a"), type);

    private static ActionOption FindCatalogueOption(Observation observation, string type)
        => FindCatalogueOption(ActionCatalogue.Build(observation), type);

    private static ActionOption FindCatalogueOption(List<ActionOption> options, string type)
    {
        foreach (ActionOption option in options)
            if (option.Type == type && option.TargetId == "city-a") return option;
        return null;
    }
}
