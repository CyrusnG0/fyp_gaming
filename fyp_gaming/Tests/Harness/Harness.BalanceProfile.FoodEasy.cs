using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using LuanShi.Engine;

// [new] Opt-in combat-test profile (BalanceProfile.FoodEasy, 2026-11-14): plentiful food so
// starting forces sustain 20 seasons without dedicated food actions while preserving Standard and
// EasyResearch untouched.

internal static partial class Harness
{
    private static void RunFoodEasyProfile()
    {
        Section("[new] food-easy balance profile: constants, parity, evidence and 20-season sustainability");

        // ------------------------------------------------ explicit constants & non-interference
        Check(BalanceConfig.FoodEasyStartFood == 3000
              && BalanceConfig.FoodEasyStartGold == 900
              && BalanceConfig.FoodEasyCityBaseFoodYield == 600
              && BalanceConfig.FoodEasyArmyFoodUpkeepMultiplier == 0.25f
              && BalanceConfig.FoodEasyDomesticGoldCostMultiplier == 0.5f,
              "[new] BalanceConfig exposes explicit FoodEasy constants");
        Check(BalanceConfig.StartFood == 500 && BalanceConfig.StartGold == 300
              && BalanceConfig.CityBaseFoodYield == 200
              && BalanceConfig.EasyStartFood == 1500 && BalanceConfig.EasyStartGold == 900
              && BalanceConfig.EasyCityBaseFoodYield == 300
              && BalanceConfig.EasyArmyFoodUpkeepMultiplier == 0.5f
              && BalanceConfig.EasyDomesticGoldCostMultiplier == 0.5f,
              "[new] existing standard and easy-research constants remain untouched");

        Check(BalanceProfile.FoodEasy.Key == BalanceProfile.FoodEasyKey
              && BalanceProfile.FoodEasy.Key == "food-easy"
              && !BalanceProfile.FoodEasy.IsStandard
              && BalanceProfile.FoodEasy.StartingFood == 3000
              && BalanceProfile.FoodEasy.StartingGold == 900
              && BalanceProfile.FoodEasy.CityBaseFoodYield == 600
              && BalanceProfile.FoodEasy.ArmyFoodUpkeepMultiplier == 0.25f
              && BalanceProfile.FoodEasy.DomesticGoldCostMultiplier == 0.5f,
              "[new] BalanceProfile.FoodEasy has 3000 food / 900 gold / 600 yield / x0.25 upkeep / x0.5 domestic gold");

        bool gotFoodEasy = BalanceProfile.TryGet("food-easy", out BalanceProfile selectedFoodEasy);
        bool gotEasy = BalanceProfile.TryGet("easy-research", out BalanceProfile selectedEasy);
        bool gotStandard = BalanceProfile.TryGet("standard", out BalanceProfile selectedStandard);
        bool rejectedCaps = !BalanceProfile.TryGet("FOOD-EASY", out BalanceProfile rejectedProfile)
                            && rejectedProfile == null;
        Check(gotFoodEasy && ReferenceEquals(selectedFoodEasy, BalanceProfile.FoodEasy)
              && gotEasy && ReferenceEquals(selectedEasy, BalanceProfile.EasyResearch)
              && gotStandard && ReferenceEquals(selectedStandard, BalanceProfile.Standard)
              && rejectedCaps,
              "[new] TryGet resolves 'food-easy' without disturbing standard/easy-research or exact matching");

        Check(ProfileReplay(BalanceProfile.Standard) == ProfileReplay(null),
              "[new] standard profile replay parity stays byte-identical");

        // ------------------------------------------------ domestic gold, upkeep & seasonal yields
        Check(BalanceProfile.FoodEasy.LightenLaborGoldCost == 50
              && BalanceProfile.FoodEasy.RecruitGoldCost(UnitTypes.Infantry, 1000) == 50
              && BalanceProfile.FoodEasy.RecruitGoldCost(UnitTypes.Archer, 1000) == 75
              && BalanceProfile.FoodEasy.RecruitGoldCost(UnitTypes.Cavalry, 1000) == 150
              && BalanceProfile.FoodEasy.RecruitGoldCost(UnitTypes.Siege, 1000) == 250
              && BalanceProfile.FoodEasy.DomesticGoldCost(101) == 51,
              "[new] food-easy shares 0.5x domestic gold costs rounded up (50/75/150/250)");

        Check(Math.Abs(BalanceProfile.FoodEasy.TroopFoodUpkeep(2000, UnitTypes.Infantry) - 50f) < 0.001f
              && Math.Abs(BalanceProfile.FoodEasy.TroopFoodUpkeep(1000, UnitTypes.Archer) - 25f) < 0.001f
              && Math.Abs(BalanceProfile.FoodEasy.TroopFoodUpkeep(1000, UnitTypes.Cavalry) - 50f) < 0.001f
              && Math.Abs(BalanceProfile.FoodEasy.TroopFoodUpkeep(1000, UnitTypes.Siege) - 12.5f) < 0.001f,
              "[new] food-easy quarters seasonal troop food upkeep per unit type");

        var ownedCity = new CityState { Id = "city-wei", Name = "Wei Capital", OwnerSeatId = "seat-0" };
        var neutralCity = new CityState { Id = "city-south", Name = "Southern City", OwnerSeatId = null, Food = 200, Gold = 100 };
        BalanceProfile.FoodEasy.ApplyStartingResources(ownedCity);
        BalanceProfile.FoodEasy.ApplyStartingResources(neutralCity);
        Check(ownedCity.Food == 3000 && ownedCity.Gold == 900 && ownedCity.Population == BalanceConfig.StartPopulation
              && neutralCity.Food == 200 && neutralCity.Gold == 100,
              "[new] ApplyStartingResources seeds 3000 food / 900 gold on owned cities only");

        Check(ProfileHarvest(BalanceProfile.FoodEasy) == 600
              && ProfileArmyNeed(BalanceProfile.FoodEasy) == 50
              && ProfileGarrisonNeed(BalanceProfile.FoodEasy) == 50,
              "[new] food-easy Summer harvest=600, 2000-infantry army upkeep=50, garrison=50");

        int springHarvest = HarvestOnly(BalanceProfile.FoodEasy, 1);
        int autumnHarvest = HarvestOnly(BalanceProfile.FoodEasy, 3);
        int winterHarvest = HarvestOnly(BalanceProfile.FoodEasy, 4);
        Check(springHarvest == 420 && autumnHarvest == 1200 && winterHarvest == 180,
              $"[new] seasonal food-easy harvests are Spring={springHarvest}, Autumn={autumnHarvest}, Winter={winterHarvest}");

        bool catalogueParity = true;
        foreach (int gold in new[] { 0, 49, 50, 51, 99, 100 })
        {
            var probe = ProfileScenario(BalanceProfile.FoodEasy, gold: gold);
            ActionOption option = FindCatalogueOption(probe, ActionType.LightenLabor);
            bool accepted = probe.Submit(DomesticOrder("seat-a", ActionType.LightenLabor, "city-a")).Ok;
            if (option == null || option.LikelyAffordable != accepted) catalogueParity = false;
        }
        Check(catalogueParity, "[new] food-easy ActionCatalogue affordability matches engine across gold thresholds");

        // ------------------------------------------------ run evidence, prompt, recorder, manual visibility
        string foodEasyJson = BalanceProfile.FoodEasy.ToJson();
        Check(foodEasyJson.Contains("\"profile\":\"food-easy\"")
              && foodEasyJson.Contains("\"starting_food\":3000")
              && foodEasyJson.Contains("\"starting_gold\":900")
              && foodEasyJson.Contains("\"city_base_food_yield\":600")
              && foodEasyJson.Contains("\"army_food_upkeep_mult\":0.25")
              && foodEasyJson.Contains("\"domestic_gold_cost_mult\":0.5"),
              "[new] BalanceProfile.FoodEasy.ToJson emits complete evidence fields: " + foodEasyJson);

        VerifyFoodEasyRunEvidence();

        // ------------------------------------------------ reproducible 20-season scripted sustainability test
        TwentySeasonOutcome foodEasyRun = RunTwentySeasonNoFoodScript(BalanceProfile.FoodEasy, printTable: true);
        TwentySeasonOutcome easyControlRun = RunTwentySeasonNoFoodScript(BalanceProfile.EasyResearch, printTable: false);

        Check(foodEasyRun.SeasonsCompleted == 20
              && foodEasyRun.StarvationEvents == 0
              && foodEasyRun.DesertionEvents == 0
              && foodEasyRun.WeiMinFood > 0
              && foodEasyRun.ShuMinFood > 0
              && foodEasyRun.WeiFinalTroops == 8000
              && foodEasyRun.ShuFinalTroops == 8000
              && foodEasyRun.MinArmyMorale == BalanceConfig.ArmyMoraleStart,
              $"[new] 20-season starting-army script sustains under food-easy with 0 starvation/desertion "
              + $"(Wei min/end={foodEasyRun.WeiMinFood}/{foodEasyRun.WeiEndFood}, "
              + $"Shu min/end={foodEasyRun.ShuMinFood}/{foodEasyRun.ShuEndFood}, "
              + $"troops={foodEasyRun.WeiFinalTroops}/{foodEasyRun.ShuFinalTroops})");

        Check(easyControlRun.SeasonsCompleted == 20
              && (easyControlRun.WeiMinFood == 0 || easyControlRun.ShuMinFood == 0)
              && easyControlRun.DesertionEvents > 0,
              $"[new] identical 20-season 0-food-action control under easy-research exhausts food "
              + $"(Wei min/end={easyControlRun.WeiMinFood}/{easyControlRun.WeiEndFood}, "
              + $"Shu min/end={easyControlRun.ShuMinFood}/{easyControlRun.ShuEndFood}, "
              + $"desertions={easyControlRun.DesertionEvents}, starvations={easyControlRun.StarvationEvents})");
    }

    private static int HarvestOnly(BalanceProfile profile, int season)
    {
        var e = ProfileScenario(profile, population: 0, food: 0, season: season);
        e.EndSeason();
        return e.State.FindCity("city-a").Food;
    }

    private static void VerifyFoodEasyRunEvidence()
    {
        var engine = new GameEngine(12, "food-easy-evidence", BalanceProfile.FoodEasy);
        engine.State.Map = ReplaySession.BuildDefaultStaggeredHexMap(26, 17);
        engine.State.Factions.Add(new FactionState { SeatId = "seat-0", Name = "Wei", Controller = ControllerType.Llm });
        engine.State.Factions.Add(new FactionState { SeatId = "seat-1", Name = "Shu", Controller = ControllerType.Llm });

        var weiCity = new CityState { Id = "city-wei", Name = "Wei Capital", X = 3, Z = 8, OwnerSeatId = "seat-0" };
        var shuCity = new CityState { Id = "city-shu", Name = "Chengdu", X = 22, Z = 8, OwnerSeatId = "seat-1" };
        engine.Balance.ApplyStartingResources(weiCity);
        engine.Balance.ApplyStartingResources(shuCity);
        engine.State.Cities.Add(weiCity);
        engine.State.Cities.Add(shuCity);
        engine.BeginSeason();

        Observation obs = engine.Observe("seat-0");
        var investigation = new InvestigationService(obs.GameId, obs.SeatId, obs, null);
        ManualLookupResult reclaim = investigation.ManualLookup("reclaim");
        ManualLookupResult recruit = investigation.ManualLookup("recruit");
        ManualLookupResult labor = investigation.ManualLookup("lighten_labor");
        Check(InvestigationFact(reclaim, "base_food_yield") == "600"
              && InvestigationFact(labor, "gold_cost") == "50"
              && InvestigationFact(recruit, "gold_cost_infantry") == "50"
              && InvestigationFact(recruit, "food_upkeep_per_1000_infantry") == "25",
              "[new] manual_lookup reflects food-easy yield (600), domestic gold (50) and upkeep (25/1000)");

        var fake = new FakeLlmTransport();
        fake.EnqueueSuccess("{\"type\":\"end_turn\"}");
        fake.EnqueueSuccess("{\"type\":\"end_turn\"}");
        var config = new LlmConfig { EnablePersonas = true, EnableManualLookup = true };
        string dir = LlmTempDir("food-easy-evidence");
        try
        {
            var runner = new LlmTurnRunner(fake, config, new LlmRunRecorder(dir));
            var session = new SpectatorSession(engine, runner, 1, 12);
            session.Start();
            SpectatorDrain(session);

            bool promptHasProfile = fake.Requests.Count == 2
                && fake.Requests[0].User.Contains("Match balance profile:")
                && fake.Requests[0].User.Contains("\"profile\":\"food-easy\"")
                && fake.Requests[0].User.Contains("\"city_base_food_yield\":600")
                && fake.Requests[0].User.Contains("\"army_food_upkeep_mult\":0.25");
            Check(promptHasProfile, "[new] PromptBuilder surfaces food-easy JSON in TURN CONTEXT");

            string summaryPath = new SpectatorSummary(dir).Write(session, config, fake);
            string summaryJson = File.ReadAllText(summaryPath);
            Check(summaryJson.Contains("\"balance_profile\":{\"profile\":\"food-easy\"")
                  && summaryJson.Contains("\"starting_food\":3000")
                  && summaryJson.Contains("\"city_base_food_yield\":600")
                  && summaryJson.Contains("\"army_food_upkeep_mult\":0.25"),
                  "[new] SpectatorSummary records food-easy profile in pilot summary JSON");

            string[] jsonlFiles = Directory.GetFiles(dir, "*.jsonl");
            string jsonlText = jsonlFiles.Length == 1 ? File.ReadAllText(jsonlFiles[0]) : "";
            Check(jsonlText.Contains("\"balance_profile\":{\"profile\":\"food-easy\"")
                  && jsonlText.Contains("\"starting_food\":3000"),
                  "[new] LlmRunRecorder records food-easy profile in per-request JSONL");
        }
        finally { LlmDelete(dir); }
    }

    private struct TwentySeasonOutcome
    {
        public int SeasonsCompleted;
        public int WeiMinFood;
        public int ShuMinFood;
        public int WeiEndFood;
        public int ShuEndFood;
        public int WeiFinalTroops;
        public int ShuFinalTroops;
        public int MinArmyMorale;
        public int StarvationEvents;
        public int DesertionEvents;
    }

    private static TwentySeasonOutcome RunTwentySeasonNoFoodScript(BalanceProfile profile, bool printTable)
    {
        var engine = new GameEngine(12345, "food-curve-" + profile.Key, profile);
        engine.State.Map = ReplaySession.BuildDefaultStaggeredHexMap(26, 17);
        engine.State.Factions.Add(new FactionState { SeatId = "seat-0", Name = "Wei", Controller = ControllerType.Llm, Prestige = 50 });
        engine.State.Factions.Add(new FactionState { SeatId = "seat-1", Name = "Shu", Controller = ControllerType.Llm, Prestige = 50 });

        var cityWei = new CityState { Id = "city-wei", Name = "Wei Capital", X = 3, Z = 8, OwnerSeatId = "seat-0" };
        var cityShu = new CityState { Id = "city-shu", Name = "Chengdu", X = 22, Z = 8, OwnerSeatId = "seat-1" };
        engine.Balance.ApplyStartingResources(cityWei);
        engine.Balance.ApplyStartingResources(cityShu);
        engine.State.Cities.Add(cityWei);
        engine.State.Cities.Add(cityShu);
        engine.State.Cities.Add(new CityState { Id = "city-south", Name = "Southern City", X = 12, Z = 2, OwnerSeatId = null, Population = 3000, Food = 200, Gold = 100 });
        engine.State.WatchTowers.Add(new WatchTowerState { Id = "watch-tower-1", Name = "Watch Tower", X = 6, Z = 2, OwnerSeatId = null });

        void AddStartArmy(string id, string seat, int x, int z) => engine.State.Armies.Add(new ArmyState
        {
            Id = id,
            Name = seat == "seat-0" ? "Wei Army" : "Shu Army",
            OwnerSeatId = seat,
            Troops = BalanceConfig.StartFieldArmyTroops,
            UnitType = UnitTypes.Infantry,
            X = x,
            Z = z,
            Morale = BalanceConfig.ArmyMoraleStart,
        });
        AddStartArmy("army-wei-1", "seat-0", 2, 6);
        AddStartArmy("army-wei-2", "seat-0", 2, 8);
        AddStartArmy("army-wei-3", "seat-0", 2, 10);
        AddStartArmy("army-wei-4", "seat-0", 4, 12);
        AddStartArmy("army-shu-1", "seat-1", 23, 6);
        AddStartArmy("army-shu-2", "seat-1", 23, 8);
        AddStartArmy("army-shu-3", "seat-1", 23, 10);
        AddStartArmy("army-shu-4", "seat-1", 21, 4);

        int weiMinFood = cityWei.Food;
        int shuMinFood = cityShu.Food;
        if (printTable)
        {
            Console.WriteLine("  [20-season script] profile=" + profile.Key + " seed=12345 map=26x17 start_troops=8000/side zero_food_actions=true");
            Console.WriteLine($"  S00 (Start )  Wei: food={cityWei.Food,4} pop={cityWei.Population,5} gold={cityWei.Gold,4} | Shu: food={cityShu.Food,4} pop={cityShu.Population,5} gold={cityShu.Gold,4}");
        }

        int seasonsCompleted = 0;
        engine.BeginSeason();
        for (int season = 1; season <= 20; season++)
        {
            // Scripted turn: both seats submit zero domestic/food orders and advance the season.
            engine.EndSeason();
            seasonsCompleted = season;
            if (cityWei.Food < weiMinFood) weiMinFood = cityWei.Food;
            if (cityShu.Food < shuMinFood) shuMinFood = cityShu.Food;

            if (printTable)
            {
                int wTroops = TotalTroops(engine.State, "seat-0");
                int sTroops = TotalTroops(engine.State, "seat-1");
                Console.WriteLine($"  S{season:00} ({BalanceConfig.SeasonName(season),-6})  Wei: food={cityWei.Food,4} pop={cityWei.Population,5} gold={cityWei.Gold,4} troops={wTroops} | Shu: food={cityShu.Food,4} pop={cityShu.Population,5} gold={cityShu.Gold,4} troops={sTroops}");
            }

            if (season < 20) engine.BeginSeason();
        }

        int minMorale = BalanceConfig.ArmyMoraleStart;
        foreach (var army in engine.State.Armies)
            if (army.Morale < minMorale) minMorale = army.Morale;

        return new TwentySeasonOutcome
        {
            SeasonsCompleted = seasonsCompleted,
            WeiMinFood = weiMinFood,
            ShuMinFood = shuMinFood,
            WeiEndFood = cityWei.Food,
            ShuEndFood = cityShu.Food,
            WeiFinalTroops = TotalTroops(engine.State, "seat-0"),
            ShuFinalTroops = TotalTroops(engine.State, "seat-1"),
            MinArmyMorale = minMorale,
            StarvationEvents = CountEvents(engine.Log, "starvation"),
            DesertionEvents = CountEvents(engine.Log, "desertion"),
        };
    }

    private static int TotalTroops(GameState state, string seatId)
    {
        int total = 0;
        foreach (var army in state.Armies)
            if (army.OwnerSeatId == seatId) total += army.Troops;
        return total;
    }
}
