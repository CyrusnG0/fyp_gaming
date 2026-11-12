using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace LuanShi.Engine
{
    /// <summary>
    /// Rule text for the expanded manual topics. Every number is read from <see cref="BalanceConfig"/>,
    /// the match's <see cref="BalanceProfile"/> or the matching <see cref="UnitTypes"/> helper, so a
    /// profile change is described rather than contradicted. Nothing here reads game state.
    /// </summary>
    internal static partial class InvestigationManualTopicContent
    {
        /// <summary>
        /// Writes the facts/notes for one added topic. Returns false when the id is not one of the
        /// topics this expansion serves, so the caller can fail closed with <c>unsupported_topic</c>.
        /// </summary>
        public static bool TryFill(string topicId, List<ManualFact> facts, List<string> notes,
            BalanceProfile balance)
        {
            switch (topicId)
            {
                case "seasonal_economy": FillSeasonalEconomy(facts, notes, balance); return true;
                case "army_supply": FillArmySupply(facts, notes, balance); return true;
                case "field_combat": FillFieldCombat(facts, notes); return true;
                case "city_assault": FillCityAssault(facts, notes); return true;
                case "unit_type_modifiers": FillUnitTypes(facts, notes, balance); return true;
                case "diplomacy": FillDiplomacy(facts, notes); return true;
                case "research": FillResearch(facts, notes); return true;
                case "movement": FillMovement(facts, notes); return true;
                default: return false;
            }
        }

        // ---- 天時 / 民變 / 稅收 (seasonal economy) -----------------------------

        private static void FillSeasonalEconomy(List<ManualFact> facts, List<string> notes,
            BalanceProfile balance)
        {
            facts.Add(Fact("base_food_yield_per_city", InvestigationText.Number(balance.CityBaseFoodYield),
                "Passive harvest per owned city per season, before 開墾, 民心, season and research."));
            facts.Add(Fact("opening_food_per_city", InvestigationText.Number(balance.StartingFood),
                "Every owned city starts with this food under the match's balance profile."));
            facts.Add(Fact("opening_gold_per_city", InvestigationText.Number(balance.StartingGold),
                "Every owned city starts with this gold under the match's balance profile."));
            facts.Add(Fact("opening_population_per_city", InvestigationText.Number(BalanceConfig.StartPopulation),
                null));
            facts.Add(Fact("spring_food_multiplier", Mult(BalanceConfig.SeasonFoodYieldMult(1)),
                "春: seasons 1, 5, 9, ..."));
            facts.Add(Fact("summer_food_multiplier", Mult(BalanceConfig.SeasonFoodYieldMult(2)),
                "夏: seasons 2, 6, ..."));
            facts.Add(Fact("autumn_food_multiplier", Mult(BalanceConfig.SeasonFoodYieldMult(3)),
                "秋: seasons 3, 7, ... (大收)"));
            facts.Add(Fact("winter_food_multiplier", Mult(BalanceConfig.SeasonFoodYieldMult(4)),
                "冬: seasons 4, 8, ..."));
            facts.Add(Fact("harvest_formula",
                "base × (1 + reclaim% / 100) × morale factor × season × research",
                "Evaluated once per owned city at the end of each season."));
            facts.Add(Fact("gold_per_population", Num(BalanceConfig.TaxGoldPerPop),
                "Tax gold per citizen per season, before 民心 and 吏治 research."));
            facts.Add(Fact("food_eaten_per_population", Num(BalanceConfig.FoodPerPop),
                "Citizens eat this much food per season; the garrison's ration is added on top."));
            facts.Add(Fact("morale_yield_penalty",
                "民心 < " + InvestigationText.Number(BalanceConfig.MoraleYieldPenaltyThreshold)
                + ": yields ×" + Num(1f - BalanceConfig.MoraleYieldPenalty),
                "Both harvest and tax are scaled by the same morale factor."));
            facts.Add(Fact("starvation_population_loss",
                InvestigationText.Percent(BalanceConfig.StarvationPopLoss),
                "A city whose food goes negative loses this share of its population and its food is set to 0."));
            facts.Add(Fact("population_growth_rate", InvestigationText.Percent(BalanceConfig.PopGrowthRate),
                "Base per-season growth for every owned city that did not starve."));
            facts.Add(Fact("population_growth_variance",
                "±" + InvestigationText.Percent(BalanceConfig.PopGrowthVariance),
                "Seeded spread added to the base rate each season."));
            facts.Add(Fact("population_growth_rule",
                "population grows by (base ± seeded variance) and truncates to whole people",
                "No growth in a season where the city's food went negative; that city only loses population to famine."));
            facts.Add(Fact("revolt_threshold",
                "民心 < " + InvestigationText.Number(BalanceConfig.RevoltRiskMoraleThreshold),
                "A revolt is rolled once per city per season, before the yields."));
            facts.Add(Fact("revolt_chance",
                "(" + InvestigationText.Number(BalanceConfig.RevoltRiskMoraleThreshold)
                + " − 民心) × " + InvestigationText.Percent(BalanceConfig.RevoltChancePerMoralePoint)
                + " per season",
                "吏治 L3 律法 halves the chance from the season after it is bought."));
            facts.Add(Fact("revolt_effect",
                "no harvest and no tax that season; garrison −"
                + InvestigationText.Percent(BalanceConfig.RevoltGarrisonLossPct)
                + "; 民心 +" + InvestigationText.Number(BalanceConfig.RevoltMoraleGainAfterVenting),
                "The revolt event is public."));
            facts.Add(Fact("reclaim_yield_cap",
                "+" + InvestigationText.Number(BalanceConfig.ReclaimPctCap) + "%",
                "開墾 adds " + InvestigationText.Number(BalanceConfig.ReclaimPctPerUse)
                + "% per order up to this cumulative cap."));
            notes.Add("Money and food are per-city fields; the faction food pool is only used to pay army rations.");
            notes.Add("This topic covers the per-city economy only; army upkeep and desertion are in army_supply.");
            notes.Add("Population growth is seeded, so a fixed seed replays the same growth.");
        }

        // ---- 補給 / 逃兵 (army supply) -----------------------------------------

        private static void FillArmySupply(List<ManualFact> facts, List<string> notes,
            BalanceProfile balance)
        {
            foreach (string type in UnitTypes.All)
                facts.Add(Fact("ration_per_1000_" + type,
                    Num(balance.TroopFoodUpkeep(1000, type)),
                    "Seasonal food for 1,000 troops; includes the profile's army upkeep multiplier."));
            facts.Add(Fact("ration_formula",
                "troops × food-per-troop(unit type) × profile army upkeep multiplier",
                "Garrisons and field armies eat the same ration."));
            facts.Add(Fact("payment_order",
                "the city under the army first, then the faction's cities in proportion to remaining stores",
                "An army away from its own cities still draws on the national food pool."));
            facts.Add(Fact("shortfall",
                "the army eats nothing and deserts",
                "A faction pool that cannot cover the whole ration feeds nobody."));
            facts.Add(Fact("desertion_loss", InvestigationText.Percent(BalanceConfig.DesertionTroopLossPct),
                "Share of the army lost when the ration cannot be paid."));
            facts.Add(Fact("winter_extra_desertion",
                InvestigationText.Percent(BalanceConfig.WinterDesertionExtraTroopLoss) + " in 冬",
                "Added to the base desertion share in a winter season."));
            facts.Add(Fact("desertion_morale_loss",
                "−" + InvestigationText.Number(BalanceConfig.DesertionMoraleLoss) + " 士氣",
                "The loss is applied with the deserters, after the seasonal 士氣 recovery."));
            facts.Add(Fact("supply_meter", "absent",
                "There is no 補給 0–100 field and no distance-from-home rule; food stock is the only limiter."));
            facts.Add(Fact("garrison_upkeep",
                "garrisons eat their unit type's ration; cities also feed their population", null));
            facts.Add(Fact("event", "desertion", "Logged owner_only with the troops lost and the cause."));
            notes.Add("Army supply is resolved at season end; there is no supply train, depot or range mechanic.");
        }

        // ---- 研究 (research) --------------------------------------------------

        private static void FillResearch(List<ManualFact> facts, List<string> notes)
        {
            facts.Add(Fact("lines", string.Join(" | ", ResearchLine.All),
                "Each line has its own level from 0 to " + InvestigationText.Number(BalanceConfig.ResearchMaxLevel) + "."));
            facts.Add(Fact("max_level", InvestigationText.Number(BalanceConfig.ResearchMaxLevel), null));
            var costs = new StringBuilder();
            for (int level = 1; level <= BalanceConfig.ResearchMaxLevel; level++)
            {
                if (costs.Length > 0) costs.Append(" / ");
                costs.Append(InvestigationText.Number(BalanceConfig.ResearchLevelCost(level)));
            }
            facts.Add(Fact("level_costs", costs.ToString(),
                "Research points for level 1 through level " + InvestigationText.Number(BalanceConfig.ResearchMaxLevel) + "."));
            facts.Add(Fact("command_point_cost", InvestigationText.Number(BalanceConfig.ResearchCommandPoints),
                "One research order buys one level."));
            facts.Add(Fact("points_per_season", InvestigationText.Number(BalanceConfig.ResearchIncome(0)),
                "Base income credited at season rollover."));
            facts.Add(Fact("points_per_season_with_governance_l4",
                InvestigationText.Number(BalanceConfig.ResearchIncome(4)),
                "吏治 L4 學堂 multiplies research income."));
            facts.Add(Fact("income_timing",
                "credited at rollover; a level bought after rollover pays from the next season",
                "Other effects are read at the point of use, so harvest and combat bonuses apply in the current season."));
            facts.Add(Fact("agri_l1_food", "+" + InvestigationText.Percent(BalanceConfig.ResearchAgriFoodBonus),
                "農政 L1 開墾 harvest bonus, stacking multiplicatively with 開墾 and 天時."));
            facts.Add(Fact("agri_l5_food", "+" + InvestigationText.Percent(BalanceConfig.ResearchAgriL5FoodBonus),
                "農政 L5 神農 adds a second harvest multiplier."));
            facts.Add(Fact("agri_food_multiplier_l5",
                Mult(BalanceConfig.ResearchFoodYieldMult(BalanceConfig.ResearchMaxLevel)), null));
            facts.Add(Fact("base_training_cap", InvestigationText.Number(BalanceConfig.TrainingCap(0)),
                "練兵 cap before military research."));
            facts.Add(Fact("military_l1_training_cap", InvestigationText.Number(BalanceConfig.TrainingCap(1)),
                "兵法 L1 練兵 raises the cap."));
            facts.Add(Fact("military_l4_combat",
                Mult(BalanceConfig.ResearchCombatStrengthMult(4)),
                "兵法 L4 兵法 multiplies that faction's 戰力 on both attack and defence."));
            facts.Add(Fact("military_unit_gates",
                "騎兵 military " + InvestigationText.Number(BalanceConfig.CavalryResearchLevel)
                + "; 攻城器械 military " + InvestigationText.Number(BalanceConfig.SiegeResearchLevel),
                "Recruiting a gated unit type is rejected until its level is reached."));
            facts.Add(Fact("espionage_mission_gates",
                "scout " + InvestigationText.Number(BalanceConfig.ScoutResearchLevel)
                + "; rumor " + InvestigationText.Number(BalanceConfig.RumorResearchLevel)
                + "; sow_distrust " + InvestigationText.Number(BalanceConfig.SowDistrustResearchLevel),
                "Each mission also has its own gold cost."));
            facts.Add(Fact("espionage_accuracy",
                "banded below level " + InvestigationText.Number(BalanceConfig.ScoutExactAccuracyLevel)
                + "; exact at or above it", null));
            facts.Add(Fact("governance_l1_tax", Mult(BalanceConfig.ResearchTaxMult(1)),
                "吏治 L1 賦稅 multiplies tax gold."));
            facts.Add(Fact("governance_l2_morale",
                "+" + InvestigationText.Number(BalanceConfig.ResearchGovMoralePerSeason) + " 民心 per season",
                "Applied to every owned city at season end."));
            facts.Add(Fact("governance_l3_revolt", Mult(BalanceConfig.ResearchGovRevoltMult),
                "吏治 L3 律法 scales revolt chance; it is synced at rollover, so it takes hold from the next season."));
            facts.Add(Fact("governance_l5_prestige",
                "+" + InvestigationText.Number(BalanceConfig.ResearchGovPrestigeL5) + " 威望 once",
                "Paid when the level is bought."));
            facts.Add(Fact("privacy", "own seat only",
                "Research levels, points and the purchase event are private to the owning seat."));
            facts.Add(Fact("deferred", "not modelled",
                "The 學堂 building's separate +3 research points per season is not implemented."));
            notes.Add("Research is bought with the research action; command points and research points are separate pools.");
        }

        // ---- shared formatting -------------------------------------------------

        private static ManualFact Fact(string name, string value, string note)
        {
            return new ManualFact { Name = name, Value = value, Note = note };
        }

        private static string Num(float value)
            => value.ToString("0.####", CultureInfo.InvariantCulture);

        private static string Mult(float value)
            => "×" + value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
