namespace LuanShi.Engine
{
    /// <summary>
    /// All tunable numbers for the strategic layer live here (project rule: no magic
    /// numbers in code). Values marked TBD are placeholders awaiting team balance decisions.
    /// </summary>
    public static class BalanceConfig
    {
        public const int CommandPointsPerSeason = 3;      // TBD: orders per faction per season
        public const int ArmyMoveCostPerSeason = 6;       // TBD: total hex-cost an army may spend per season

        public const int FarmFoodYield = 400;             // TBD: food from one 屯田 order
        public const int CityBaseFoodYield = 200;         // TBD: passive food per owned city per season

        public const int RecruitGoldCost = 200;           // TBD: 徵兵 cost
        public const int RecruitPopCost = 1000;           // TBD: population drawn into service
        public const int RecruitTroopGain = 1000;         // TBD: garrison troops gained

        public const float PopGrowthRate = 0.05f;         // TBD: per-season population growth
        public const float PopGrowthVariance = 0.02f;     // TBD: seeded random +/- on growth
        public const float TaxGoldPerPop = 0.01f;         // TBD: gold per citizen per season
        public const float FoodPerPop = 0.02f;            // TBD: food eaten per citizen per season
        public const float FoodPerGarrisonTroop = 0.1f;   // TBD: food eaten per garrison troop
        public const float StarvationPopLoss = 0.10f;     // TBD: pop fraction lost when food runs out

        public const int StartPopulation = 10000;         // TBD
        public const int StartFood = 500;                 // TBD
        public const int StartGold = 300;                 // TBD
        public const int StartFieldArmyTroops = 2000;     // TBD
    }
}
