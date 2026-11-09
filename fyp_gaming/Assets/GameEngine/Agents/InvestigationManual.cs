using System;
using System.Collections.Generic;

namespace LuanShi.Engine
{
    /// <summary>
    /// Builds the bounded English manual_lookup answer from the combined topic library
    /// (<see cref="InvestigationManualTopics.All"/>) plus BalanceConfig and the existing action
    /// contract. No game-state object is read and no rule is invented here.
    /// </summary>
    internal static class InvestigationManual
    {
        // The engine applies one level per train/fortify order (ACTIONS.md D5/D6). This is a named
        // documentation constant, not a new tunable: the cap and every combat number come from BalanceConfig.
        private const int LevelGainPerOrder = 1;

        private const string CoverageNote =
            "Implemented rules only, rendered from the engine's balance configuration. Espionage mission "
            + "detail, runner/UI behaviour and unimplemented features are not served by this foundation.";

        private const string InquiryNote =
            "Read-only inquiry spends no game command points here; runner investigation budgets are deferred.";

        public static ManualLookupResult Lookup(string topic, InvestigationOptions options, BalanceProfile balance = null)
        {
            balance = balance ?? BalanceProfile.Standard;
            options = options ?? InvestigationOptions.Defaults();
            var result = new ManualLookupResult
            {
                RulesVersion = InvestigationOptions.RulesVersion,
                CommandPointCost = -1,
                Topics = BuildIndex(options),
            };

            if (topic != null && topic.Length > options.MaxTopicLength)
            {
                result.Fail("query_too_long", "The topic request exceeds the configured length bound.");
                result.Notes = new List<string> { CoverageNote }.AsReadOnly();
                return result;
            }

            string normalized = InvestigationText.Normalize(topic);
            if (normalized.Length == 0 || normalized == "index" || normalized == "topics"
                || normalized == "list" || normalized == "help")
            {
                FillIndex(result);
                result.Succeed(result.Topics.Count);
                return result;
            }

            var exact = new List<InvestigationTopicDefinition>();
            var partial = new List<InvestigationTopicDefinition>();
            foreach (InvestigationTopicDefinition candidate in InvestigationManualTopics.All)
            {
                if (IsExact(candidate, normalized)) exact.Add(candidate);
                else if (IsPartial(candidate, normalized)) partial.Add(candidate);
            }

            if (exact.Count == 1) return FillDetail(result, exact[0], balance);
            if (exact.Count > 1 || partial.Count > 1)
            {
                result.Fail("ambiguous_topic",
                    "Several implemented topics match. Use a topic id from the returned index.");
                result.Notes = new List<string> { CoverageNote }.AsReadOnly();
                return result;
            }
            if (partial.Count == 1) return FillDetail(result, partial[0], balance);

            result.Fail("unsupported_topic",
                "No implemented English topic matches. Use the bounded topic index.");
            result.Notes = new List<string> { CoverageNote }.AsReadOnly();
            return result;
        }

        private static void FillIndex(ManualLookupResult result)
        {
            result.IsTopicIndex = true;
            result.Title = "English topic index";
            result.Category = "index";
            result.Implemented = true;
            result.LlmActionExposed = false;
            result.Facts = new List<ManualFact>().AsReadOnly();
            result.Notes = new List<string> { CoverageNote, InquiryNote }.AsReadOnly();
        }

        private static ManualLookupResult FillDetail(ManualLookupResult result,
            InvestigationTopicDefinition topic, BalanceProfile balance)
        {
            result.TopicId = topic.Id;
            result.Title = topic.Title;
            result.Category = topic.Category;
            result.Implemented = true;
            result.LlmActionExposed = topic.LlmActionExposed;
            result.CommandPointCost = topic.ActionType == null
                ? -1
                : BalanceConfig.CommandPointCost(topic.ActionType);

            var facts = new List<ManualFact>();
            var notes = new List<string>();

            switch (topic.Id)
            {
                case "turn_and_command_points": FillTurn(facts, notes); break;
                case "fog_of_war": FillFog(facts, notes); break;
                case ActionType.LevyTax: FillLevyTax(facts); break;
                case ActionType.LightenLabor: FillLightenLabor(facts, balance); break;
                case ActionType.Reclaim: FillReclaim(facts, notes, balance); break;
                case ActionType.Recruit: FillRecruit(facts, balance); break;
                case ActionType.Train: FillTrain(facts); break;
                case ActionType.Fortify: FillFortify(facts); break;
                case ActionType.Relief: FillRelief(facts); break;
                default:
                    if (!InvestigationManualTopicContent.TryFill(topic.Id, facts, notes, balance))
                    {
                        result.Fail("unsupported_topic", "No implemented rule content is available.");
                        result.Notes = new List<string> { CoverageNote }.AsReadOnly();
                        return result;
                    }
                    break;
            }

            if (topic.ActionType != null)
            {
                facts.Insert(0, Fact("target", "one owned city (target_id)",
                    "Only cities in this seat's observation are candidates."));
                facts.Insert(1, Fact("llm_action_exposed", topic.LlmActionExposed ? "yes" : "no",
                    "The current LLM action catalogue advertises the seven domestic actions for owned cities."));
                notes.Add("The engine's single validator remains authoritative; advertised actions are candidates.");
            }
            else
            {
                notes.Add(InvestigationManualTopics.ActionCaveat);
            }

            result.Facts = facts.AsReadOnly();
            notes.Add(CoverageNote);
            notes.Add(InquiryNote);
            result.Notes = notes.AsReadOnly();
            result.Succeed(1);
            return result;
        }

        private static void FillTurn(List<ManualFact> facts, List<string> notes)
        {
            facts.Add(Fact("command_points_per_season",
                InvestigationText.Number(BalanceConfig.CommandPointsPerSeason),
                "One shared 號令 pool per seat per season."));
            facts.Add(Fact("default_order_cost",
                InvestigationText.Number(BalanceConfig.CommandPointsPerOrder),
                "Default cost of one accepted order."));
            facts.Add(Fact("attack_city_cost",
                InvestigationText.Number(BalanceConfig.CommandPointsPerAttackCity),
                "Cost of a 攻城 (attack_city) order."));
            facts.Add(Fact("talk_cost",
                InvestigationText.Number(BalanceConfig.CommandPointsPerTalk),
                "send_message, propose_treaty and respond_treaty are free."));
            facts.Add(Fact("research_cost",
                InvestigationText.Number(BalanceConfig.ResearchCommandPoints),
                "Cost of one research order."));
            facts.Add(Fact("espionage_mission_cost",
                InvestigationText.Number(BalanceConfig.CommandPointsPerSpyMission),
                "Cost of one scout, rumor or sow_distrust mission."));
            facts.Add(Fact("spending_rule", "accepted orders only",
                "The validator checks the cost before the pool is debited; a rejected order spends nothing."));
            facts.Add(Fact("investigation_cost", "0 game command points",
                "Inquiry does not consume the action pool in LS-018."));
            notes.Add("Every controller uses the same command pipeline and validator; inquiry is separate from actions.");
        }

        private static void FillFog(List<ManualFact> facts, List<string> notes)
        {
            facts.Add(Fact("own_city_sight_radius",
                InvestigationText.Number(BalanceConfig.FogCityRange),
                "Own cities reveal foreign entities within this hex radius."));
            facts.Add(Fact("own_army_sight_radius",
                InvestigationText.Number(BalanceConfig.FogArmyRange),
                "Own armies reveal foreign entities within this hex radius."));
            facts.Add(Fact("watchtower_sight_radius",
                InvestigationText.Number(BalanceConfig.FogWatchTowerRange),
                "An owned watchtower uses its own vision radius."));
            facts.Add(Fact("outside_sight", "absent, not redacted",
                "Foreign entities outside every radius are not part of the observation."));
            facts.Add(Fact("history_projection", "frozen at emission",
                "Event audiences are fixed when emitted; later sight never reveals an old undisclosed event."));
            facts.Add(Fact("scout_accuracy",
                "banded below espionage level " + InvestigationText.Number(BalanceConfig.ScoutExactAccuracyLevel)
                + "; exact at or above it",
                "Scouted city reports are a separate, explicitly banded knowledge source."));
            facts.Add(Fact("scout_report_window",
                "current season plus " + InvestigationText.Number(BalanceConfig.ScoutReportExtraSeasons),
                "A scout report is stale after its covered seasons."));
            notes.Add("This inspection tool serves current-observation cities and armies only; it does not "
                    + "promote stale scouted reports into current facts.");
            notes.Add("Observations are detached copies; inspection cannot mutate canonical game state.");
        }

        private static void FillLevyTax(List<ManualFact> facts)
        {
            facts.Add(Fact("gold_gain", "+" + InvestigationText.Number(BalanceConfig.TaxLevyAmount), null));
            facts.Add(Fact("morale_change", "-" + InvestigationText.Number(BalanceConfig.TaxLevyMoraleCost),
                "民心 change, clamped by the engine."));
            facts.Add(Fact("event", "tax_levied", "Public with a limited cross-seat projection."));
        }

        private static void FillLightenLabor(List<ManualFact> facts, BalanceProfile balance)
        {
            facts.Add(Fact("gold_cost", InvestigationText.Number(balance.LightenLaborGoldCost), null));
            facts.Add(Fact("morale_gain", "+" + InvestigationText.Number(BalanceConfig.LightenLaborMoraleGain),
                "民心 change, clamped by the engine."));
            facts.Add(Fact("event", "labor_lightened", null));
        }

        private static void FillReclaim(List<ManualFact> facts, List<string> notes, BalanceProfile balance)
        {
            facts.Add(Fact("base_food_yield", InvestigationText.Number(balance.CityBaseFoodYield),
                "Per city per season before modifiers."));
            facts.Add(Fact("food_yield_gain_per_use", "+" + InvestigationText.Number(BalanceConfig.ReclaimPctPerUse) + "%",
                "Cumulative, stored as the city's reclaim percentage."));
            facts.Add(Fact("cumulative_cap", InvestigationText.Number(BalanceConfig.ReclaimPctCap) + "%",
                "Further reclaim orders are rejected at the cap."));
            facts.Add(Fact("limit", "once per city per season", null));
            facts.Add(Fact("event", "land_reclaimed", null));
            notes.Add("`farm` remains a legacy alias that the engine maps to `reclaim`.");
        }

        private static void FillRecruit(List<ManualFact> facts, BalanceProfile balance)
        {
            facts.Add(Fact("troops_gained_per_order", InvestigationText.Number(BalanceConfig.RecruitTroopGain), null));
            facts.Add(Fact("population_cost", InvestigationText.Number(BalanceConfig.RecruitPopCost), null));
            facts.Add(Fact("gold_cost_infantry", InvestigationText.Number(balance.RecruitGoldCost(UnitTypes.Infantry, BalanceConfig.RecruitTroopGain)),
                "Per 1,000 troops."));
            facts.Add(Fact("gold_cost_archer", InvestigationText.Number(balance.RecruitGoldCost(UnitTypes.Archer, BalanceConfig.RecruitTroopGain)),
                "Per 1,000 troops."));
            facts.Add(Fact("gold_cost_cavalry", InvestigationText.Number(balance.RecruitGoldCost(UnitTypes.Cavalry, BalanceConfig.RecruitTroopGain)),
                "Per 1,000 troops."));
            facts.Add(Fact("gold_cost_siege", InvestigationText.Number(balance.RecruitGoldCost(UnitTypes.Siege, BalanceConfig.RecruitTroopGain)),
                "Per 1,000 troops."));
            foreach (string type in new[] { UnitTypes.Infantry, UnitTypes.Archer, UnitTypes.Cavalry, UnitTypes.Siege })
                facts.Add(Fact("food_upkeep_per_1000_" + type,
                    balance.TroopFoodUpkeep(1000, type).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
                    "Seasonal troop food before other consumption and rounding."));
            facts.Add(Fact("unit_type",
                UnitTypes.Infantry + " | " + UnitTypes.Archer + " | " + UnitTypes.Cavalry + " | " + UnitTypes.Siege,
                "Optional args.unit_type; the engine defaults to infantry."));
            facts.Add(Fact("research_gates",
                "cavalry needs military level " + InvestigationText.Number(BalanceConfig.CavalryResearchLevel)
                + "; siege needs military level " + InvestigationText.Number(BalanceConfig.SiegeResearchLevel),
                null));
            facts.Add(Fact("garrison_type_rule",
                "a non-empty garrison must match the recruited type; an empty garrison adopts it", null));
            facts.Add(Fact("event", "recruit", "owner_and_observers visibility."));
        }

        private static void FillTrain(List<ManualFact> facts)
        {
            facts.Add(Fact("training_gain", "+" + InvestigationText.Number(LevelGainPerOrder), null));
            facts.Add(Fact("base_training_cap", InvestigationText.Number(BalanceConfig.TrainingMax),
                "Military research can raise the cap."));
            facts.Add(Fact("combat_bonus_per_level",
                InvestigationText.Percent(BalanceConfig.CombatTrainingBonusPerLevel), "Applies to the garrison."));
            facts.Add(Fact("event", "troops_trained", null));
        }

        private static void FillFortify(List<ManualFact> facts)
        {
            facts.Add(Fact("defense_gain", "+" + InvestigationText.Number(LevelGainPerOrder), null));
            facts.Add(Fact("defense_cap", InvestigationText.Number(BalanceConfig.DefenseMax), null));
            facts.Add(Fact("defender_bonus_per_level",
                InvestigationText.Percent(BalanceConfig.CombatDefenseBonusPerLevel), null));
            facts.Add(Fact("event", "city_fortified", null));
        }

        private static void FillRelief(List<ManualFact> facts)
        {
            facts.Add(Fact("food_cost", InvestigationText.Number(BalanceConfig.ReliefFoodCost), null));
            facts.Add(Fact("morale_gain", "+" + InvestigationText.Number(BalanceConfig.ReliefMoraleGain),
                "民心 change, clamped by the engine."));
            facts.Add(Fact("event", "disaster_relieved", null));
        }

        private static ManualFact Fact(string name, string value, string note)
        {
            return new ManualFact { Name = name, Value = value, Note = note };
        }

        private static bool IsExact(InvestigationTopicDefinition topic, string normalized)
        {
            if (InvestigationText.Normalize(topic.Id) == normalized) return true;
            if (topic.Aliases == null) return false;
            foreach (string alias in topic.Aliases)
                if (InvestigationText.Normalize(alias) == normalized) return true;
            return false;
        }

        private static bool IsPartial(InvestigationTopicDefinition topic, string normalized)
        {
            if (normalized.Length == 0) return false;
            if (InvestigationText.Normalize(topic.Id).IndexOf(normalized, StringComparison.Ordinal) >= 0) return true;
            if (InvestigationText.Normalize(topic.Title).IndexOf(normalized, StringComparison.Ordinal) >= 0) return true;
            if (topic.Aliases == null) return false;
            foreach (string alias in topic.Aliases)
                if (InvestigationText.Normalize(alias).IndexOf(normalized, StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        private static IReadOnlyList<TopicIndexEntry> BuildIndex(InvestigationOptions options)
        {
            var entries = new List<TopicIndexEntry>();
            int limit = options.MaxTopicIndexEntries;
            foreach (InvestigationTopicDefinition topic in InvestigationManualTopics.All)
            {
                if (entries.Count >= limit) break;
                entries.Add(new TopicIndexEntry
                {
                    Id = topic.Id,
                    Title = topic.Title,
                    Category = topic.Category,
                    LlmActionExposed = topic.LlmActionExposed,
                });
            }
            return entries.AsReadOnly();
        }
    }
}
