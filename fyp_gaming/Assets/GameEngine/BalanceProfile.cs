using System;
using System.Globalization;

namespace LuanShi.Engine
{
    /// <summary>
    /// One immutable set of match-scoped balance rules. A profile is chosen when the match's engine is
    /// created and never changes afterwards, so every seat — human, LLM or scripted — plays the same
    /// rules for the whole match, and two matches running side by side cannot affect each other.
    ///
    /// <see cref="Standard"/> is the shipped balance: every field equals its <see cref="BalanceConfig"/>
    /// constant and every multiplier is 1, so a standard match behaves exactly as before. The other
    /// profiles are opt-in and are selected by the host (Unity scene bootstrap, harness, test).
    /// </summary>
    public sealed class BalanceProfile
    {
        public const string StandardKey = "standard";
        public const string EasyResearchKey = "easy-research";

        /// <summary>The shipped balance. Nothing is rescaled and no rounding changes.</summary>
        public static readonly BalanceProfile Standard = new BalanceProfile(
            StandardKey,
            startingFood: BalanceConfig.StartFood,
            startingGold: BalanceConfig.StartGold,
            cityBaseFoodYield: BalanceConfig.CityBaseFoodYield,
            armyFoodUpkeepMultiplier: 1f,
            domesticGoldCostMultiplier: 1f);

        /// <summary>
        /// Opt-in reduced profile for the research demo (2026-11-09): an opening economy three times
        /// larger in both food (1,500) and gold (900), half troop upkeep, a 1.5× passive harvest and
        /// half-price domestic gold spending. Espionage and diplomacy gold, command-point costs,
        /// research costs, populations and every other rule are identical to <see cref="Standard"/>.
        /// </summary>
        public static readonly BalanceProfile EasyResearch = new BalanceProfile(
            EasyResearchKey,
            startingFood: BalanceConfig.EasyStartFood,
            startingGold: BalanceConfig.EasyStartGold,
            cityBaseFoodYield: BalanceConfig.EasyCityBaseFoodYield,
            armyFoodUpkeepMultiplier: BalanceConfig.EasyArmyFoodUpkeepMultiplier,
            domesticGoldCostMultiplier: BalanceConfig.EasyDomesticGoldCostMultiplier);

        /// <summary>Stable schema key used by hosts, run records and evidence notes.</summary>
        public string Key { get; }

        /// <summary>Food each owned city starts with.</summary>
        public int StartingFood { get; }

        /// <summary>Gold each owned city starts with.</summary>
        public int StartingGold { get; }

        /// <summary>Passive food per owned city per season before 開墾, 民心, 天時 and 研究.</summary>
        public int CityBaseFoodYield { get; }

        /// <summary>Multiplier on the food troops eat per season (garrison and field army alike).</summary>
        public float ArmyFoodUpkeepMultiplier { get; }

        /// <summary>Multiplier on the gold cost of domestic orders (輕徭, 募兵).</summary>
        public float DomesticGoldCostMultiplier { get; }

        /// <summary>True when this profile is the shipped, unscaled balance.</summary>
        public bool IsStandard => Key == StandardKey;

        private BalanceProfile(string key, int startingFood, int startingGold, int cityBaseFoodYield,
            float armyFoodUpkeepMultiplier, float domesticGoldCostMultiplier)
        {
            Key = key;
            StartingFood = startingFood;
            StartingGold = startingGold;
            CityBaseFoodYield = cityBaseFoodYield;
            ArmyFoodUpkeepMultiplier = armyFoodUpkeepMultiplier;
            DomesticGoldCostMultiplier = domesticGoldCostMultiplier;
        }

        /// <summary>
        /// Selects a profile by its schema key. A null or empty key means <see cref="Standard"/>;
        /// an unknown key is rejected so a typo cannot silently play the wrong rules.
        /// </summary>
        public static bool TryGet(string key, out BalanceProfile profile)
        {
            if (string.IsNullOrEmpty(key) || key == StandardKey) { profile = Standard; return true; }
            if (key == EasyResearchKey) { profile = EasyResearch; return true; }
            profile = null;
            return false;
        }

        /// <summary>輕徭 D2 gold cost under this profile.</summary>
        public int LightenLaborGoldCost => DomesticGoldCost(BalanceConfig.LightenLaborGoldCost);

        /// <summary>募兵 D4 gold cost for `troops` of one 兵種 under this profile.</summary>
        public int RecruitGoldCost(string unitType, int troops)
            => DomesticGoldCost(UnitTypes.RecruitGoldCost(unitType, troops));

        /// <summary>
        /// Applies this profile's domestic gold multiplier, rounded up so a discounted cost never
        /// rounds down to zero (and a standard cost is returned unchanged).
        /// </summary>
        public int DomesticGoldCost(int baseCost)
            => (int)Math.Ceiling(baseCost * (double)DomesticGoldCostMultiplier);

        /// <summary>
        /// Unrounded seasonal food ration for one body of troops. Callers add it to the season's other
        /// consumption in float and truncate once, exactly like the pre-profile code did.
        /// </summary>
        public float TroopFoodUpkeep(int troops, string unitType)
            => troops * UnitTypes.FoodPerTroop(unitType) * ArmyFoodUpkeepMultiplier;

        /// <summary>
        /// Seeds one owned city with this profile's opening economy. Hosts call this instead of
        /// assigning <see cref="BalanceConfig.StartFood"/> / <see cref="BalanceConfig.StartGold"/>
        /// directly; neutral cities keep their own fixed placeholder values.
        /// </summary>
        public void ApplyStartingResources(CityState city)
        {
            if (city == null || city.IsNeutral) return;
            city.Population = BalanceConfig.StartPopulation;
            city.Food = StartingFood;
            city.Gold = StartingGold;
        }

        /// <summary>Compact record for run records and evidence notes.</summary>
        public string ToJson()
        {
            return "{\"profile\":" + MiniJson.Str(Key)
                 + ",\"starting_food\":" + MiniJson.Num(StartingFood)
                 + ",\"starting_gold\":" + MiniJson.Num(StartingGold)
                 + ",\"city_base_food_yield\":" + MiniJson.Num(CityBaseFoodYield)
                 + ",\"army_food_upkeep_mult\":"
                 + ArmyFoodUpkeepMultiplier.ToString("0.###", CultureInfo.InvariantCulture)
                 + ",\"domestic_gold_cost_mult\":"
                 + DomesticGoldCostMultiplier.ToString("0.###", CultureInfo.InvariantCulture)
                 + "}";
        }
    }
}
