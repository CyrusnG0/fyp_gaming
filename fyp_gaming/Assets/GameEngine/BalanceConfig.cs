namespace LuanShi.Engine
{
    /// <summary>
    /// All tunable numbers for the strategic layer live here (project rule: no magic
    /// numbers in code). Values marked TBD are placeholders awaiting team balance decisions.
    /// </summary>
    public static class BalanceConfig
    {
        public const int CommandPointsPerSeason = 5;      // LOCKED (ADR-010): 號令 per faction per season
        public const int CommandPointsPerOrder = 1;       // default 號令 per accepted command (§3 domestic)
        public const int CommandPointsPerAttackCity = 2;  // LOCKED (ACTIONS.md §4 M3): 攻城 costs 2
        public const int ArmyMoveCostPerSeason = 6;       // TBD: total hex-cost an army may spend per season
        public const int FogWatchTowerRange = 5;          // watchtower vision radius

        public const int CityBaseFoodYield = 200;         // TBD: passive food per owned city per season (pre-開墾)

        public const int TaxLevyAmount = 200;             // TBD: 徵稅 D1 gold gained (design §11 quotes no number)
        public const int TaxLevyMoraleCost = 5;           // LOCKED (design §11): 徵稅 民心 −5
        public const int LightenLaborGoldCost = 100;      // TBD: 輕徭 D2 gold cost (design §11 quotes 金 − only)
        public const int LightenLaborMoraleGain = 8;      // LOCKED (design §11): 輕徭 民心 +8

        public const int ReclaimPctStart = 0;             // TBD: 開墾 D3 start (ACTIONS.md §9)
        public const int ReclaimPctPerUse = 15;           // LOCKED (ADR-010): +15% food yield per use
        public const int ReclaimPctCap = 60;              // LOCKED (ADR-010): 開墾 cumulative cap

        public const int ReliefFoodCost = 300;            // TBD: 賑災 D7 food cost (design §11 quotes 糧 − only)
        public const int ReliefMoraleGain = 15;           // LOCKED (design §11): 賑災 民心 +15

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

        public const int RecruitGoldCost = 200;           // TBD: 募兵 D4 cost
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

        // ---- 戰鬥 M2/M3 (design §6, ACTIONS.md §4) ------------------------------

        public const int ArmyMoraleStart = 60;             // TBD: 士氣 of a new army
        public const int ArmyMoraleRegainPerSeason = 10;   // TBD: 士氣 regained per season, up to ArmyMoraleStart
        public const int CombatRounds = 5;                 // LOCKED (design §6.2): ≤5 rounds per battle
        public const float CombatLossPerRound = 0.08f;     // LOCKED (ACTIONS.md M2): loss/round = enemy 戰力 × 8%
        public const float CombatVariance = 0.15f;         // TBD: seeded ±15% on each round's losses
        public const float CombatTrainingBonusPerLevel = 0.10f;   // LOCKED (design §5.1/§6.1): 訓練 +10% 戰力/級
        public const float CombatMoraleFloor = 0.5f;       // LOCKED (design §6.1): 士氣 0 → ×0.5
        public const float CombatMoraleScale = 200f;       // LOCKED (design §6.1): 戰力 × (floor + 士氣 / scale)
        public const float CombatDefenseBonusPerLevel = 0.15f;    // TBD: 城防 +15% defender 戰力 per level
        public const float CombatMoraleLossPerLossPercent = 1f;   // TBD: 士氣 −1 per % of troops lost in a round (§6.2)
        public const int CombatRoutMoraleThreshold = 0;    // LOCKED (ACTIONS.md M2): 士氣 0 → 潰散 (design §5.1 says <30)
        public const float CombatRoutExtraLoss = 0.30f;    // LOCKED (design §6.2): a 潰散 army loses a further 30%
        public const int CombatVictoryMoraleGain = 10;     // TBD: winning restores 士氣 (design §5.1: 勝仗)
        public const int AssaultTroopRatio = 3;            // LOCKED (design §4.3): 強攻 needs ≥3:1 unless 器械

        // ---- 名聲: victory prestige and 背盟 / 偷襲 penalties (design §7.3–§7.4) -

        public const int PrestigeBattleVictory = 3;        // TBD: 威望 for winning a field battle
        public const int PrestigeCityCaptured = 5;         // TBD: 威望 for storming a city
        public const int SurpriseAttackTrustPenalty = 40;  // LOCKED (design §7.3 偷襲): victim's trust −40
        public const int SurpriseAttackPrestigePenalty = 12;   // LOCKED (design §7.3 偷襲): 威望 −12
        public const int BetrayalTrustPenalty = 50;        // LOCKED (design §7.3 背盟): victim's trust −50
        public const int BetrayalPrestigePenalty = 15;     // LOCKED (design §7.3 背盟): 威望 −15
        public const int BetrayalThirdPartyTrustPenalty = 15;  // LOCKED (design §7.4): every other faction −15
        public const int TruceBreakTrustPenalty = 30;      // LOCKED (design §7.3 撕毀停戰): victim's trust −30
        public const int TruceBreakPrestigePenalty = 10;   // LOCKED (design §7.3 撕毀停戰): 威望 −10

        // ---- scripted-AI policy (a baseline behaviour, not a game rule) --------

        public const int ScriptedAttackTroopAdvantagePercent = 50;   // AI attacks only with this troop edge

        // ---- 外交 P1-P6 (ACTIONS.md §5) -----------------------------------------

        public const int CommandPointsPerTalk = 0;         // LOCKED (design §2, ACTIONS.md §2): 說話/提案/回覆 free
        public const int GiftGoldPerTrustUnit = 1000;      // LOCKED (design §7.3): 贈禮 per 1,000 金
        public const int GiftTrustPerUnit = 3;             // LOCKED (design §7.3): 對方對你的信任 +3
        public const int GiftPrestigePerUnit = 1;          // LOCKED (design §7.3): 你的威望 +1
        public const int DeclareWarPrestigeCost = 5;       // LOCKED (design §11 #18): 正式開戰 威望 −5
        public const int TreatyDurationSeasons = 6;        // TBD: treaty term when the offer names none
        public const int TreatyDurationMinSeasons = 1;     // TBD
        public const int TreatyDurationMaxSeasons = 12;    // TBD

        // ---- 戰爭迷霧 (ACTIONS.md §9, design §8.1) ------------------------------

        public const int FogCityRange = 3;   // LOCKED (§9): an own city reveals ±3 hexes
        public const int FogArmyRange = 2;   // LOCKED (§9): an own army reveals ±2 hexes

        public const int ScriptedNapTrustThreshold = 0;        // AI signs/offers 互不侵犯 at or above this 信任
        public const int ScriptedAllianceTrustThreshold = 30;  // TBD
        public const int ScriptedTruceTrustThreshold = -50;    // AI signs/offers 停戰 at or above this 信任

        // ---- 天時 seasonal modifiers (design §3 「季節對糧食的影響」 table) --------
        // Seasons cycle 春 → 夏 → 秋 → 冬 by (Season − 1) % 4; SeasonReport/observation carry the name.

        public const float SpringFoodYieldMult = 0.7f;    // LOCKED (design §3 table): 春 糧產 ×0.7
        public const float SummerFoodYieldMult = 1.0f;    // LOCKED (design §3 table): 夏 糧產 ×1.0
        public const float AutumnFoodYieldMult = 2.0f;    // LOCKED (design §3 table): 秋 糧產 ×2.0 (大收)
        public const float WinterFoodYieldMult = 0.3f;    // LOCKED (design §3 table): 冬 糧產 ×0.3
        public const float WinterMarchBudgetMult = 0.7f;  // LOCKED (design §3 table + §10.1 嚴冬): 行軍 ×0.7
        public const float WinterDesertionExtraTroopLoss = 0.05f;  // LOCKED (design §10.1 嚴冬): 逃兵 +5%

        private static readonly string[] SeasonNames = { "Spring", "Summer", "Autumn", "Winter" };  // 春/夏/秋/冬

        /// <summary>0-based position in the 春夏秋冬 cycle (design §3); 1-based seasons start at 春.</summary>
        public static int SeasonIndex(int season) => ((season - 1) % 4 + 4) % 4;

        /// <summary>English season name for reports/observations (UI strings are English).</summary>
        public static string SeasonName(int season) => SeasonNames[SeasonIndex(season)];

        public static bool IsWinter(int season) => SeasonIndex(season) == 3;

        /// <summary>§3 天時 harvest multiplier: food yield for the whole map this season.</summary>
        public static float SeasonFoodYieldMult(int season)
        {
            switch (SeasonIndex(season))
            {
                case 0: return SpringFoodYieldMult;
                case 1: return SummerFoodYieldMult;
                case 2: return AutumnFoodYieldMult;
                default: return WinterFoodYieldMult;
            }
        }

        /// <summary>Effective per-army hex budget this season: 冬 marches at WinterMarchBudgetMult (§10.1 嚴冬).</summary>
        public static int MarchBudget(int season)
            => (int)(ArmyMoveCostPerSeason * (IsWinter(season) ? WinterMarchBudgetMult : 1f));

        // ---- 民變 revolt (design §4.1「民心 0–100；低於 25 有民變風險」+ §10.2「產出停擺」) ----

        public const int RevoltRiskMoraleThreshold = 25;   // LOCKED (design §4.1 line 149): 民心 <25 → 民變風險
        public const float RevoltChancePerMoralePoint = 0.02f;  // TBD: chance = (25 − 民心) × 2% per season
        public const float RevoltGarrisonLossPct = 0.10f;  // TBD: garrison lost to the rising (design silent)
        public const int RevoltMoraleGainAfterVenting = 15;     // TBD: 民心 +15 once the anger is spent (clamp ≤100)

        // ---- 逃兵 army supply & desertion (design §5.1 補給, §6.3 斷補, §10.1 嚴冬) ----

        // Design gives armies a 補給 0–100 field that hits 0 away from home ("歸零則每季逃兵", §5.1) and
        // prices 斷補 at 10%/season (§6.3). MVP simplification: no 補給 field — an army eats
        // Troops × FoodPerGarrisonTroop per season out of its faction's food; failing that, it deserts.
        public const float DesertionTroopLossPct = 0.10f;  // LOCKED (design §6.3 斷補): 逃兵 10%/季
        public const int DesertionMoraleLoss = 15;         // TBD: 士氣 lost with the deserters (design silent)

        /// <summary>
        /// 號令 cost of an action — the spec's "CP" column lives here so neither a handler nor a UI
        /// label hardcodes it (ACTIONS.md §3–§5). Anything the spec does not price costs the default.
        /// </summary>
        public static int CommandPointCost(string actionType)
        {
            switch (actionType)
            {
                // §2: 遣使 / 提案 / 回覆 are free — talking never costs 號令.
                case ActionType.SendMessage:
                case ActionType.ProposeTreaty:
                case ActionType.RespondTreaty: return CommandPointsPerTalk;
                case ActionType.AttackCity: return CommandPointsPerAttackCity;
                default: return CommandPointsPerOrder;
            }
        }
    }
}
