namespace LuanShi.Engine
{
    /// <summary>
    /// 兵種 (design §5.3/§5.4, ADR-019): four types, exactly one per field army and per city garrison.
    /// The four string keys are the frozen LLM contract; every number lives in <see cref="BalanceConfig"/>.
    /// </summary>
    public static class UnitTypes
    {
        public const string Infantry = "infantry"; // 步兵
        public const string Archer = "archer";     // 弓兵
        public const string Cavalry = "cavalry";   // 騎兵
        public const string Siege = "siege";       // 攻城器械

        public static readonly string[] All = { Infantry, Archer, Cavalry, Siege };

        public static bool IsValid(string type)
            => type == Infantry || type == Archer || type == Cavalry || type == Siege;

        /// <summary>Absent/unknown type reads as infantry (schema v1 backward compatibility).</summary>
        public static string Normalize(string type) => IsValid(type) ? type : Infantry;

        /// <summary>Design §9: 騎兵 needs 兵法 2, 攻城器械 needs 兵法 3; 步/弓 are free.</summary>
        public static int RequiredMilitaryLevel(string type)
        {
            switch (Normalize(type))
            {
                case Cavalry: return BalanceConfig.CavalryResearchLevel;
                case Siege: return BalanceConfig.SiegeResearchLevel;
                default: return 0;
            }
        }

        /// <summary>Design §5.4: gold per 1,000 troops, scaled to the recruited amount.</summary>
        public static int RecruitGoldCost(string type, int troops)
        {
            int perThousand;
            switch (Normalize(type))
            {
                case Archer: perThousand = BalanceConfig.RecruitGoldCostArcher; break;
                case Cavalry: perThousand = BalanceConfig.RecruitGoldCostCavalry; break;
                case Siege: perThousand = BalanceConfig.RecruitGoldCostSiege; break;
                default: perThousand = BalanceConfig.RecruitGoldCostInfantry; break;
            }
            return (int)((long)perThousand * troops / 1000);
        }

        /// <summary>Design §5.4: 糧 per troop per season (步/弓 0.1, 騎 0.2, 器械 0.05).</summary>
        public static float FoodPerTroop(string type)
        {
            switch (Normalize(type))
            {
                case Cavalry: return BalanceConfig.FoodPerTroopCavalry;
                case Siege: return BalanceConfig.FoodPerTroopSiege;
                case Archer: return BalanceConfig.FoodPerTroopArcher;
                default: return BalanceConfig.FoodPerTroopInfantry;
            }
        }

        /// <summary>Design §5.4: 步/弓 6, 騎 8, 器械 3 hex per season (before the 冬 multiplier).</summary>
        public static int MovePoints(string type)
        {
            switch (Normalize(type))
            {
                case Cavalry: return BalanceConfig.MarchPointsCavalry;
                case Siege: return BalanceConfig.MarchPointsSiege;
                default: return BalanceConfig.ArmyMoveCostPerSeason;
            }
        }

        /// <summary>Design §4.3: an attacking 攻城器械 army waives the 3:1 assault requirement.</summary>
        public static bool WaivesAssaultRatio(string type) => Normalize(type) == Siege;

        /// <summary>Design §5.3/§5.4: an 弓兵 garrison defends a city at +25%.</summary>
        public static float SiegeDefenseMultiplier(string garrisonType)
            => Normalize(garrisonType) == Archer ? BalanceConfig.ArcherSiegeDefenseBonus : 1f;

        /// <summary>
        /// Design §5.3: 步>弓, 騎>步, 弓>騎 (剋 ×1.3, 被剋 ×0.8); 器械 is ×0.5 in field battles and
        /// neither beats nor is beaten by the cycle.
        /// </summary>
        public static float FieldCombatMultiplier(string mine, string enemy)
        {
            string my = Normalize(mine), other = Normalize(enemy);
            if (my == Siege) return BalanceConfig.SiegeFieldPenalty;
            if (other == Siege) return 1f;
            if (Beats(my, other)) return BalanceConfig.UnitCounterBonus;
            if (Beats(other, my)) return BalanceConfig.UnitCounterPenalty;
            return 1f;
        }

        /// <summary>Display label (design names, used by the UI's composition lines).</summary>
        public static string Label(string type)
        {
            switch (Normalize(type))
            {
                case Archer: return "弓兵";
                case Cavalry: return "騎兵";
                case Siege: return "攻城器械";
                default: return "步兵";
            }
        }

        // ---- signature specials (P15, design §5.3) ------------------------------------------

        /// <summary>Attack range in hexes: 弓兵 2, everyone else 1 (design §5.3 弓兵 ranged).</summary>
        public static int AttackRange(string type)
            => Normalize(type) == Archer ? BalanceConfig.ArcherAttackRange : 1;

        /// <summary>March orders allowed per season: 騎兵 2, everyone else 1 (design §5.3 騎兵).</summary>
        public static int MarchesPerSeason(string type)
            => Normalize(type) == Cavalry ? BalanceConfig.CavalryMarchesPerSeason : 1;

        /// <summary>Field-battle losses taken multiplier: 步兵 ×0.85 (design §5.3 耐打), others 1.</summary>
        public static float LossTakenMultiplier(string type)
            => Normalize(type) == Infantry ? BalanceConfig.InfantryLossTakenMult : 1f;

        /// <summary>Siege attacker multiplier vs 城防: 器械 ×1.3 (design §5.3 相剋, OQ-29), others 1.</summary>
        public static float SiegeVsCityMultiplier(string type)
            => Normalize(type) == Siege ? BalanceConfig.SiegeVsCityBonus : 1f;

        /// <summary>
        /// The attacking side's field multiplier: the 相剋 cycle, plus 平原衝鋒 when 騎兵 attacks an
        /// army standing on plains (design §5.3). The defender never gets the charge bonus.
        /// </summary>
        public static float FieldAttackMultiplier(string attackerType, string defenderType, bool defenderOnPlains)
        {
            float multiplier = FieldCombatMultiplier(attackerType, defenderType);
            if (Normalize(attackerType) == Cavalry && defenderOnPlains)
                multiplier *= BalanceConfig.CavalryPlainsChargeBonus;
            return multiplier;
        }

        /// <summary>True when the 騎兵 平原衝鋒 bonus applies (attacker is cavalry, defender on plains).</summary>
        public static bool IsPlainsCharge(string attackerType, bool defenderOnPlains)
            => Normalize(attackerType) == Cavalry && defenderOnPlains;

        private static bool Beats(string a, string b)
            => (a == Infantry && b == Archer)
            || (a == Cavalry && b == Infantry)
            || (a == Archer && b == Cavalry);
    }
}
