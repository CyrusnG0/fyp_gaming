using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace LuanShi.Engine
{
    /// <summary>
    /// Opt-in action reference (<see cref="LlmConfig.EnableActionReference"/>): one compact block of
    /// rules, costs and consequences for the orders this match actually enables. It states facts,
    /// never a strategy. Every number is read from <see cref="BalanceConfig"/>, <see cref="UnitTypes"/>
    /// or the match's <see cref="BalanceProfile"/>, so the text cannot drift from the validator, and
    /// each order family is described only when its flag is on — an enabled order is never called
    /// unavailable and a disabled one is never advertised. The block adds no map, city or army data,
    /// so it carries nothing a seat could not already see. When the flag is off none of this runs and
    /// the prompt text, version and hash are byte-identical to before.
    /// </summary>
    public static partial class PromptBuilder
    {
        /// <summary>
        /// Appended to the prompt version. It sits before the garrison-conversion suffix so that
        /// suffix stays last for <see cref="IsGarrisonConversionPromptVersion"/>.
        /// </summary>
        public const string ActionReferenceVersionSuffix = "+actions-1";

        /// <summary>
        /// Marks the revised live attack wording (an explicit "no minimum troop ratio" statement and the
        /// morale-only rejection line) under an unchanged base version id, so a run record can tell it
        /// from earlier attack-2 prompts that said nothing about the ratio. It sits after
        /// <see cref="ActionReferenceVersionSuffix"/> and before the garrison suffix. Historical-rule
        /// mode never carries it: it uses the legacy attack identity instead. Old identities without it
        /// stay valid post-removal identities.
        /// </summary>
        public const string AssaultTextVersionSuffix = "+assault-text-1";

        public static bool IsActionReferencePromptVersion(string version)
        {
            if (string.IsNullOrEmpty(version)) return false;
            string stripped = StripGarrisonSuffix(version);
            return stripped.EndsWith(ActionReferenceVersionSuffix, StringComparison.Ordinal);
        }

        /// <summary>Removes the garrison-conversion suffix, then the assault-text suffix beneath it.</summary>
        private static string StripGarrisonSuffix(string version)
        {
            if (IsGarrisonConversionPromptVersion(version))
                version = version.Substring(0, version.Length - (version.EndsWith(GarrisonConversionVersionSuffix, StringComparison.Ordinal)
                    ? GarrisonConversionVersionSuffix.Length : LegacyGarrisonConversionVersionSuffix.Length));
            // The unit-types suffix sits between the assault-text and garrison suffixes.
            if (!string.IsNullOrEmpty(version) && version.EndsWith(UnitTypesVersionSuffix, StringComparison.Ordinal))
                version = version.Substring(0, version.Length - UnitTypesVersionSuffix.Length);
            return !string.IsNullOrEmpty(version) && version.EndsWith(AssaultTextVersionSuffix, StringComparison.Ordinal)
                ? version.Substring(0, version.Length - AssaultTextVersionSuffix.Length)
                : version;
        }

        /// <summary>Removes the garrison-conversion suffix, then the action-reference suffix.</summary>
        internal static string StripActionReferenceSuffix(string version)
        {
            if (string.IsNullOrEmpty(version)) return version;
            version = StripGarrisonSuffix(version);
            return version.EndsWith(ActionReferenceVersionSuffix, StringComparison.Ordinal)
                ? version.Substring(0, version.Length - ActionReferenceVersionSuffix.Length)
                : version;
        }

        public static string ActionReferenceHash(string system)
        {
            byte[] hash;
            using (SHA256 sha = SHA256.Create()) hash = sha.ComputeHash(Encoding.UTF8.GetBytes(system));
            var result = new StringBuilder(16);
            for (int i = 0; i < 8; i++) result.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
            return result.ToString();
        }

        /// <summary>
        /// The one statement of the city-assault troop requirement, shared by the attack rules and
        /// this reference so they cannot disagree. <paramref name="assaultRatioEnforced"/> is the rule
        /// the engine applies to the match (<see cref="GameState.RequireAssaultTroopRatio"/>); the
        /// retained <see cref="BalanceConfig.AssaultTroopRatio"/> constant is only the historical
        /// multiple and never decides whether the rule applies.
        /// </summary>
        internal static string AssaultRequirementText(bool assaultRatioEnforced)
            => assaultRatioEnforced && BalanceConfig.AssaultTroopRatio > 1
                ? "needs at least " + Number(BalanceConfig.AssaultTroopRatio) + " times the defending troops unless the army is a siege army"
                : "has no minimum troop ratio";

        /// <summary>
        /// Historical-rule mode: swaps the live attack rules for the old 3:1 wording in a system text
        /// and maps its version to the matching legacy identity, which replay reads as the old rule.
        /// Fails closed (false) when the live attack block is not present exactly once.
        /// </summary>
        internal static bool TryApplyHistoricalAssaultRule(string system, string version,
            out string newSystem, out string newVersion)
        {
            newSystem = system; newVersion = version;
            if (string.IsNullOrEmpty(system) || string.IsNullOrEmpty(version)) return false;
            string live = AttackAddendum(false);
            int at = system.IndexOf(live, StringComparison.Ordinal);
            if (at < 0 || system.IndexOf(live, at + live.Length, StringComparison.Ordinal) >= 0) return false;
            string[,] map =
            {
                { AttackPromptVersion, LegacyAttackPromptVersion },
                { ResearchPromptVersion, LegacyResearchPromptVersion },
                { RichResearchPromptVersion, LegacyRichResearchPromptVersion },
                { DeliberationResearchPromptVersion, LegacyDeliberationResearchPromptVersion },
            };
            for (int i = 0; i < map.GetLength(0); i++)
            {
                string from = map[i, 0];
                if (!version.StartsWith(from, StringComparison.Ordinal)) continue;
                if (version.Length > from.Length && version[from.Length] != '+') continue;
                newSystem = system.Substring(0, at) + AttackAddendum(true) + system.Substring(at + live.Length);
                newVersion = map[i, 1] + version.Substring(from.Length);
                return true;
            }
            return false;
        }

        public static string ActionReferenceAddendum(BalanceProfile profile, bool attack, bool research,
            bool diplomacy, bool garrisonConversion, bool assaultRatioEnforced = false)
        {
            if (profile == null) profile = BalanceProfile.Standard;
            string cp = Number(BalanceConfig.CommandPointsPerSeason);
            string order = Number(BalanceConfig.CommandPointCost(ActionType.LevyTax));
            string recruitGold = Number(profile.RecruitGoldCost(UnitTypes.Infantry, BalanceConfig.RecruitTroopGain));
            string pop = Number(BalanceConfig.RecruitPopCost);
            string troops = Number(BalanceConfig.RecruitTroopGain);
            string march = Number(BalanceConfig.CommandPointCost(ActionType.March));
            var sb = new StringBuilder(4096);
            sb.Append("\n\nACTION REFERENCE (opt-in; values are for balance profile \"").Append(profile.Key)
              .Append("\"; the engine is authoritative):\n");
            sb.Append("- You receive ").Append(cp).Append(" command points each season. An order costs ")
              .Append(order).Append(" unless stated; end_turn is free. A rejected order costs nothing but uses a request.\n");
            sb.Append("DOMESTIC ORDERS (target_id = one of your own city ids):\n");
            sb.Append("- levy_tax: that city's gold +").Append(Number(BalanceConfig.TaxLevyAmount))
              .Append(", morale -").Append(Number(BalanceConfig.TaxLevyMoraleCost)).Append(".\n");
            sb.Append("- lighten_labor: that city's gold -").Append(Number(profile.LightenLaborGoldCost))
              .Append(", morale +").Append(Number(BalanceConfig.LightenLaborMoraleGain)).Append(".\n");
            sb.Append("- reclaim: that city's future harvest +").Append(Number(BalanceConfig.ReclaimPctPerUse))
              .Append(" percentage points, once per city per season, cumulative cap +")
              .Append(Number(BalanceConfig.ReclaimPctCap)).Append(".\n");
            sb.Append("- relief: that city's food -").Append(Number(BalanceConfig.ReliefFoodCost))
              .Append(", morale +").Append(Number(BalanceConfig.ReliefMoraleGain)).Append(".\n");
            sb.Append("- train: city training +1 (cap ").Append(Number(BalanceConfig.TrainingCap(0)))
              .Append(", or ").Append(Number(BalanceConfig.TrainingCap(1)))
              .Append(" with military research level 1); combat strength is multiplied by (1 + training x ")
              .Append(Percent(BalanceConfig.CombatTrainingBonusPerLevel)).Append(").\n");
            sb.Append("- fortify: city defense +1 (max ").Append(Number(BalanceConfig.DefenseMax))
              .Append("); a garrison defends at x(1 + defense x ")
              .Append(Percent(BalanceConfig.CombatDefenseBonusPerLevel)).Append(").\n");
            AppendRecruitChain(sb, profile, recruitGold, pop, troops, march, attack, garrisonConversion);
            AppendMarchRules(sb, profile, march);
            if (attack) AppendAttackRules(sb, assaultRatioEnforced);
            if (research)
                sb.Append("- research costs ").Append(Number(BalanceConfig.CommandPointCost(ActionType.Research)))
                  .Append(" command point(s) and research points; see RESEARCH ORDERS above for the lines and prices.\n");
            if (diplomacy)
                sb.Append("- send_message costs ").Append(Number(BalanceConfig.CommandPointCost(ActionType.SendMessage)))
                  .Append(", propose_treaty ").Append(Number(BalanceConfig.CommandPointCost(ActionType.ProposeTreaty)))
                  .Append(" and respond_treaty ").Append(Number(BalanceConfig.CommandPointCost(ActionType.RespondTreaty)))
                  .Append(" command points; see DIPLOMACY ORDERS above.\n");
            AppendSeasonResolution(sb, profile);
            return sb.ToString();
        }

        private static void AppendRecruitChain(StringBuilder sb, BalanceProfile profile, string recruitGold,
            string pop, string troops, string march, bool attack, bool garrisonConversion)
        {
            string upkeep = Decimal(profile.TroopFoodUpkeep(BalanceConfig.RecruitTroopGain, UnitTypes.Infantry));
            string foodNeed = "Garrison and field troops eat food every season (1,000 infantry eat about " + upkeep
                + " food per season in this profile).";
            sb.Append("RECRUITING AND ARMIES:\n");
            sb.Append("- recruit (target_id = your city, args.unit_type \"infantry\"): costs ").Append(recruitGold)
              .Append(" gold and ").Append(pop).Append(" population from that city and adds ").Append(troops)
              .Append(" troops to that city's garrison. It needs that much gold and population in the city, and the " +
                      "garrison's unit type must match. The population is not returned when the troops later move or fall. ")
              .Append("Population supplies the city's tax gold (").Append(Decimal(BalanceConfig.TaxGoldPerPop))
              .Append(" per person per season), its season-end growth and its food eaten (")
              .Append(Decimal(BalanceConfig.FoodPerPop)).Append(" per person). ").Append(foodNeed).Append('\n');
            if (garrisonConversion)
            {
                sb.Append("- Chain: recruit adds troops to a city's garrison; convert_garrison (")
                  .Append(Number(BalanceConfig.CommandPointCost(ActionType.ConvertGarrison)))
                  .Append(" command point(s), no gold or population) moves that many troops from the garrison into a " +
                          "field army standing on the city, forming one or merging into an own army of the same type there; ")
                  .Append("only a field army can march");
                if (attack) sb.Append(" or attack");
                sb.Append(".\n");
            }
            else
            {
                sb.Append("- Recruited troops stay in the city's garrison and defend it; this match has no order that turns " +
                          "a garrison into a field army, so only armies that already exist can march");
                if (attack) sb.Append(" or attack");
                sb.Append(".\n");
            }
        }

        private static void AppendMarchRules(StringBuilder sb, BalanceProfile profile, string march)
        {
            sb.Append("MARCH (target_id = your own army id; param_a/param_b = a destination from MOVEMENT OPTIONS): costs ")
              .Append(march).Append(" command point(s). Route cost per season: infantry ")
              .Append(Number(UnitTypes.MovePoints(UnitTypes.Infantry))).Append(", cavalry ")
              .Append(Number(UnitTypes.MovePoints(UnitTypes.Cavalry))).Append(", siege ")
              .Append(Number(UnitTypes.MovePoints(UnitTypes.Siege))).Append(", x")
              .Append(Decimal(BalanceConfig.WinterMarchBudgetMult)).Append(" in winter. One march per army per season (cavalry ")
              .Append(Number(BalanceConfig.CavalryMarchesPerSeason)).Append("). Marching into a neutral city or an unowned watch tower captures it.\n");
        }

        private static void AppendAttackRules(StringBuilder sb, bool assaultRatioEnforced)
        {
            sb.Append("COMBAT (attack_army and attack_city only when listed in CANDIDATE OPTIONS; see ATTACK AND CAPTURE above):\n");
            sb.Append("- strength = troops x (1 + training x ").Append(Percent(BalanceConfig.CombatTrainingBonusPerLevel))
              .Append(") x (").Append(Decimal(BalanceConfig.CombatMoraleFloor)).Append(" + morale / ")
              .Append(Number((int)BalanceConfig.CombatMoraleScale)).Append(") x research and unit-type multipliers.\n");
            sb.Append("- Field battle: up to ").Append(Number(BalanceConfig.CombatRounds)).Append(" rounds; each round a side loses ")
              .Append(Percent(BalanceConfig.CombatLossPerRound)).Append(" of the enemy's strength (+/-")
              .Append(Percent(BalanceConfig.CombatVariance)).Append("). Troops 0 destroys an army; morale 0 routs it. ")
              .Append("Unit counters: infantry beats archer, cavalry beats infantry, archer beats cavalry (x")
              .Append(Decimal(BalanceConfig.UnitCounterBonus)).Append(" / x").Append(Decimal(BalanceConfig.UnitCounterPenalty))
              .Append("). After the last round the defender holds. A winner gains +")
              .Append(Number(BalanceConfig.PrestigeBattleVictory)).Append(" prestige.\n");
            sb.Append("- attack_city (").Append(Number(BalanceConfig.CommandPointCost(ActionType.AttackCity)))
              .Append(" command points) ").Append(AssaultRequirementText(assaultRatioEnforced))
              .Append("; the defender's garrison strength uses city training and defense. Garrison 0 captures the city (+")
              .Append(Number(BalanceConfig.PrestigeCityCaptured)).Append(" prestige).\n");
            sb.Append("- Attacking without a declared war is a sneak attack: the victim's trust -")
              .Append(Number(BalanceConfig.SurpriseAttackTrustPenalty)).Append(" and your prestige -")
              .Append(Number(BalanceConfig.SurpriseAttackPrestigePenalty)).Append(".\n");
        }

        private static void AppendSeasonResolution(StringBuilder sb, BalanceProfile profile)
        {
            sb.Append("SEASON RESOLUTION (this profile):\n");
            sb.Append("- Each owned city harvests ").Append(Number(profile.CityBaseFoodYield))
              .Append(" food x (1 + reclaim) x season, and pays tax from its population. Morale under ")
              .Append(Number(BalanceConfig.MoraleYieldPenaltyThreshold)).Append(" halves both yields; under ")
              .Append(Number(BalanceConfig.RevoltRiskMoraleThreshold)).Append(" a revolt can stop them and cost garrison.\n");
            sb.Append("- A city whose food falls below zero starves: population -")
              .Append(Percent(BalanceConfig.StarvationPopLoss)).Append(". An army the food pool cannot feed loses ")
              .Append(Percent(BalanceConfig.DesertionTroopLossPct)).Append(" of its troops (")
              .Append(Percent(BalanceConfig.DesertionTroopLossPct + BalanceConfig.WinterDesertionExtraTroopLoss))
              .Append(" in winter).\n");
        }
    }
}
