using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace LuanShi.Engine
{
    /// <summary>
    /// Opt-in full action reference prompt material: one compact description of every order the
    /// model may emit in this build — what it does, what it costs, the exact equation the engine
    /// applies and a one-line example — plus a short season-resolution context block. Kept in its
    /// own partial so the disabled prompt text, versions and schema hash stay byte-identical when
    /// <see cref="LlmConfig.EnableActionReference"/> is off. Every number comes from
    /// <see cref="BalanceConfig"/>/<see cref="UnitTypes"/>, so the text cannot drift from the
    /// validator. The reference advertises no order the parser does not accept: engine-only
    /// orders (gift, declare_war, the espionage missions, the garrison transfers) are named as
    /// unavailable, mirroring the diplomacy addendum's "NOT available" line. The human-readable
    /// long form lives in docs/LLM_ACTION_REFERENCE.md.
    /// </summary>
    public static partial class PromptBuilder
    {
        /// <summary>Distinct identity for the opt-in full action reference.</summary>
        public const string ActionReferencePromptVersion = "demo-slice-10-actions-1";

        /// <summary>
        /// Appended to the planning system prompt when the reference is enabled. Domestic orders
        /// and march are always in vocabulary; attack, research and diplomacy lines point at
        /// their own opt-in sections instead of duplicating their shapes.
        /// </summary>
        public static string ActionReferenceAddendum()
        {
            string cp = Number(BalanceConfig.CommandPointsPerSeason);
            string orderCost = Number(BalanceConfig.CommandPointCost(ActionType.LevyTax));
            string cityAssaultCost = Number(BalanceConfig.CommandPointCost(ActionType.AttackCity));
            string winterDesertion = Percent(BalanceConfig.DesertionTroopLossPct
                                             + BalanceConfig.WinterDesertionExtraTroopLoss);
            return "\n\nACTION REFERENCE (every order in this build; the engine is authoritative):\n" +
                   "- Your faction receives " + cp + " command points each season. An order costs " +
                   orderCost + " point unless stated; a rejected order costs nothing but uses one request.\n" +
                   "DOMESTIC ORDERS (target_id = one of your own city ids):\n" +
                   "- levy_tax: city gold +" + Number(BalanceConfig.TaxLevyAmount) + ", city morale -" +
                   Number(BalanceConfig.TaxLevyMoraleCost) + ". Use for quick gold while morale stays high. " +
                   "e.g. {\"type\":\"levy_tax\",\"target_id\":\"city-a\"}\n" +
                   "- lighten_labor: city gold -" + Number(BalanceConfig.LightenLaborGoldCost) +
                   ", morale +" + Number(BalanceConfig.LightenLaborMoraleGain) +
                   ". Use to undo tax damage. e.g. {\"type\":\"lighten_labor\",\"target_id\":\"city-a\"}\n" +
                   "- reclaim: that city's future harvests +" + Percent(BalanceConfig.ReclaimPctPerUse / 100f) +
                   ", once per city per season, cap +" + Number(BalanceConfig.ReclaimPctCap) +
                   "%. e.g. {\"type\":\"reclaim\",\"target_id\":\"city-b\"}\n" +
                   "- recruit: gold and " + Number(BalanceConfig.RecruitPopCost) +
                   " population become +" + Number(BalanceConfig.RecruitTroopGain) +
                   " garrison troops; args.unit_type is \"infantry\" in this build. " +
                   "e.g. {\"type\":\"recruit\",\"target_id\":\"city-a\",\"args\":{\"unit_type\":\"infantry\"}}\n" +
                   "- train: city training +1 (cap " + Number(BalanceConfig.TrainingCap(0)) + ", or " +
                   Number(BalanceConfig.TrainingCap(1)) + " with military research 1). Combat strength is " +
                   "multiplied by (1 + training x " + Percent(BalanceConfig.CombatTrainingBonusPerLevel) +
                   "). e.g. {\"type\":\"train\",\"target_id\":\"city-a\"}\n" +
                   "- fortify: city defense +1 (max " + Number(BalanceConfig.DefenseMax) +
                   "). A garrison defends at x(1 + defense x " +
                   Percent(BalanceConfig.CombatDefenseBonusPerLevel) +
                   "). e.g. {\"type\":\"fortify\",\"target_id\":\"city-a\"}\n" +
                   "- relief: city food -" + Number(BalanceConfig.ReliefFoodCost) + ", morale +" +
                   Number(BalanceConfig.ReliefMoraleGain) +
                   ". Use when food is plentiful and morale nears " +
                   Number(BalanceConfig.RevoltRiskMoraleThreshold) +
                   ". e.g. {\"type\":\"relief\",\"target_id\":\"city-b\"}\n" +
                   "MARCH (target_id = your own army id, param_a/param_b = a destination from MOVEMENT OPTIONS):\n" +
                   "- Budget " + Number(BalanceConfig.ArmyMoveCostPerSeason) + " hex-cost (cavalry " +
                   Number(BalanceConfig.MarchPointsCavalry) + ", siege " +
                   Number(BalanceConfig.MarchPointsSiege) + "), x" +
                   Decimal(BalanceConfig.WinterMarchBudgetMult) +
                   " in winter; plains cost 1, forest cost 2 per hex. One march per army per season " +
                   "(cavalry " + Number(BalanceConfig.CavalryMarchesPerSeason) +
                   "). Marching into a neutral city or an unowned watchtower captures it; an enemy " +
                   "city needs attack_city. e.g. {\"type\":\"march\",\"target_id\":\"army-a-1\",\"param_a\":4,\"param_b\":2}\n" +
                   "COMBAT EQUATIONS (attack_army/attack_city only when listed in CANDIDATE OPTIONS; " +
                   "see the ATTACK section):\n" +
                   "- strength = troops x (1 + training x " + Percent(BalanceConfig.CombatTrainingBonusPerLevel) +
                   ") x (" + Decimal(BalanceConfig.CombatMoraleFloor) + " + morale / " +
                   Number((int)BalanceConfig.CombatMoraleScale) +
                   ") x research_mult x type_mult; research_mult is " +
                   Decimal(1f + BalanceConfig.ResearchMilCombatBonus) +
                   " with military research 4, else 1.0.\n" +
                   "- Field battle: up to " + Number(BalanceConfig.CombatRounds) +
                   " rounds. Counter cycle: infantry beats archer, cavalry beats infantry, archer " +
                   "beats cavalry (x" + Decimal(BalanceConfig.UnitCounterBonus) + " advantaged, x" +
                   Decimal(BalanceConfig.UnitCounterPenalty) + " disadvantaged); siege armies fight at x" +
                   Decimal(BalanceConfig.SiegeFieldPenalty) + "; cavalry attacking an army on plains " +
                   "gains another x" + Decimal(BalanceConfig.CavalryPlainsChargeBonus) +
                   ". Each round a side loses " + Percent(BalanceConfig.CombatLossPerRound) +
                   " of the enemy's strength (+/-" + Percent(BalanceConfig.CombatVariance) +
                   "); infantry losses x" + Decimal(BalanceConfig.InfantryLossTakenMult) +
                   "; morale falls about 1 per 1% of troops lost that round. Troops 0 destroys the " +
                   "army; morale 0 routs it (-" + Percent(BalanceConfig.CombatRoutExtraLoss) +
                   " of remaining troops and it retreats, destroyed if it cannot); after " +
                   Number(BalanceConfig.CombatRounds) + " rounds the defender holds. Winner +" +
                   Number(BalanceConfig.CombatVictoryMoraleGain) + " morale and +" +
                   Number(BalanceConfig.PrestigeBattleVictory) +
                   " prestige. e.g. {\"type\":\"attack_army\",\"target_id\":\"army-a-1\",\"param_a\":2,\"param_b\":1}\n" +
                   "- City assault (costs " + cityAssaultCost + "): garrison_strength = garrison x " +
                   "(1 + city training x " + Percent(BalanceConfig.CombatTrainingBonusPerLevel) +
                   ") x (1 + city defense x " + Percent(BalanceConfig.CombatDefenseBonusPerLevel) +
                   ") x research_mult x garrison_type_mult (archer garrison x" +
                   Decimal(BalanceConfig.ArcherSiegeDefenseBonus) + "); siege engines attack at x" +
                   Decimal(BalanceConfig.SiegeVsCityBonus) +
                   ". A defender's army on the city hex merges into the garrison first. Garrison 0 " +
                   "captures the city (+" + Number(BalanceConfig.PrestigeCityCaptured) +
                   " prestige). There is no minimum troop ratio. " +
                   "e.g. {\"type\":\"attack_city\",\"target_id\":\"army-a-1\",\"param_a\":1,\"param_b\":2}\n" +
                   "- Attacking without a declared war is a sneak attack: the victim's trust -" +
                   Number(BalanceConfig.SurpriseAttackTrustPenalty) + ", your prestige -" +
                   Number(BalanceConfig.SurpriseAttackPrestigePenalty) +
                   "; attacking a treaty partner breaks the treaty with heavier penalties.\n" +
                   "RESEARCH (when its rules section appears above): buy the next level of " +
                   "agri/military/espionage/governance; levels cost " +
                   string.Join("/", BalanceConfig.ResearchLevelCosts) + " research points; +" +
                   Number(BalanceConfig.ResearchPointsPerSeason) +
                   " points arrive each season. e.g. {\"type\":\"research\",\"args\":{\"line\":\"military\"}}\n" +
                   "DIPLOMACY (when its rules section appears above): send_message, propose_treaty " +
                   "and respond_treaty are free; an accepted treaty takes effect at the next season " +
                   "rollover.\n" +
                   "- These orders are NOT available in this build: convert_garrison, garrison_army, " +
                   "gift, declare_war, break_treaty, scout, rumor, sow_distrust. Do not emit them.\n" +
                   "SEASON RESOLUTION CONTEXT:\n" +
                   "- Harvest = city base x (1 + reclaim%) x season x agriculture research; seasons " +
                   "are Spring x" + Decimal(BalanceConfig.SpringFoodYieldMult) + ", Summer x" +
                   Decimal(BalanceConfig.SummerFoodYieldMult) + ", Autumn x" +
                   Decimal(BalanceConfig.AutumnFoodYieldMult) + ", Winter x" +
                   Decimal(BalanceConfig.WinterFoodYieldMult) + ".\n" +
                   "- City morale under " + Number(BalanceConfig.MoraleYieldPenaltyThreshold) +
                   " halves its food and gold yields; under " +
                   Number(BalanceConfig.RevoltRiskMoraleThreshold) +
                   " it risks a revolt (no yields that season, garrison -" +
                   Percent(BalanceConfig.RevoltGarrisonLossPct) + ").\n" +
                   "- An army the food pool cannot feed deserts: -" +
                   Percent(BalanceConfig.DesertionTroopLossPct) + " troops (-" + winterDesertion +
                   " in winter), morale -" + Number(BalanceConfig.DesertionMoraleLoss) +
                   ". A city whose food runs out starves: population -" +
                   Percent(BalanceConfig.StarvationPopLoss) + ".\n" +
                   "- end_turn finishes your turn and costs nothing. " +
                   "e.g. {\"type\":\"end_turn\",\"target_id\":null}";
        }

        /// <summary>
        /// The reference composes with every other opt-in: it never replaces an identity, it only
        /// appends "+actions" (or claims the base identity when nothing else did). Planning only:
        /// the reference describes march and the planning options, so domestic-only hosts keep
        /// their prompt and version unchanged.
        /// </summary>
        public static string CombinedVersionFor(bool planning, bool inquiry, LlmPersona persona,
            bool attack, bool deliberation, bool rich, bool research, bool spatial, bool diplomacy,
            bool actionReference)
        {
            string baseVersion = CombinedVersionFor(planning, inquiry, persona, attack, deliberation,
                rich, research, spatial, diplomacy);
            if (!actionReference || !planning) return baseVersion;
            return baseVersion == PromptVersion ? ActionReferencePromptVersion
                                                : baseVersion + "+actions";
        }

        public static string CombinedSystemPromptFor(bool planning, bool inquiry, LlmPersona persona,
            bool attack, bool deliberation, bool rich, bool research, bool spatial, bool diplomacy,
            bool actionReference)
        {
            string text = CombinedSystemPromptFor(planning, inquiry, persona, attack, deliberation,
                rich, research, spatial, diplomacy);
            if (!actionReference || !planning) return text;
            return text + ActionReferenceAddendum();
        }

        public static string CombinedSchemaHashFor(bool planning, bool inquiry, LlmPersona persona,
            bool attack, bool deliberation, bool rich, bool research, bool spatial, bool diplomacy,
            bool actionReference)
        {
            if (!actionReference || !planning)
                return CombinedSchemaHashFor(planning, inquiry, persona, attack, deliberation, rich,
                    research, spatial, diplomacy);
            byte[] bytes = Encoding.UTF8.GetBytes(CombinedSystemPromptFor(planning, inquiry, persona,
                attack, deliberation, rich, research, spatial, diplomacy, true));
            byte[] hash;
            using (SHA256 sha = SHA256.Create())
                hash = sha.ComputeHash(bytes);
            var sb = new StringBuilder(16);
            for (int i = 0; i < 8; i++)
                sb.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }
    }
}
