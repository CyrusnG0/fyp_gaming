namespace LuanShi.Engine
{
    /// <summary>
    /// All tunable numbers for the strategic layer live here (project rule: no magic
    /// numbers in code). Values marked TBD are placeholders awaiting team balance decisions.
    /// </summary>
    public static class BalanceConfig
    {
        public const int CommandPointsPerSeason = 5;      // LOCKED (ADR-010): 號令 per faction per season
        public const int ArmyMoveCostPerSeason = 6;       // TBD: total hex-cost an army may spend per season

        public const int CityBaseFoodYield = 200;         // TBD: passive food per owned city per season (pre-開墾)

        public const int ReclaimPctStart = 0;             // TBD: 開墾 start (ACTIONS.md §9)
        public const int ReclaimPctPerUse = 15;           // LOCKED (ADR-010): 開墾 D3, +15% food yield per use
        public const int ReclaimPctCap = 60;              // LOCKED (ADR-010): 開墾 cumulative cap

        public const int MoraleStart = 60;                // TBD: 民心 start, range MoraleMin..MoraleMax
        public const int MoraleMin = 0;
        public const int MoraleMax = 100;
        public const int MoraleYieldPenaltyThreshold = 30; // LOCKED (ADR-010): 民心 below this loses yields
        public const float MoraleYieldPenalty = 0.5f;     // LOCKED (ADR-010): food and gold yield reduction

        public const int DefenseStart = 0;                // TBD: 城防, range 0..DefenseMax
        public const int DefenseMax = 5;
        public const int TrainingStart = 0;               // TBD: 練兵, range 0..TrainingMax
        public const int TrainingMax = 5;
        public const int PrestigeStart = 50;              // TBD: 威望 start, range PrestigeMin..PrestigeMax
        public const int PrestigeMin = 0;
        public const int PrestigeMax = 100;
        public const int TrustStart = 0;                  // TBD: 信任 per ordered faction pair
        public const int TrustMin = -100;
        public const int TrustMax = 100;

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
