using System;
using System.Collections.Generic;
using System.Globalization;
using LuanShi.Engine;

// [LS-018] Manual topic expansion: seasonal economy, army supply, field combat, city assault, unit
// types, diplomacy, research and movement.
//
// These tests pin the expanded English manual to the numbers the engine actually uses. Every value
// is compared against BalanceConfig / BalanceProfile / UnitTypes (or the helper that renders them),
// so a balance change fails here instead of leaving stale prose. The added topics are rules
// references, not model action offers: they must never advertise an action or a command point cost.

internal static partial class Harness
{
    private static readonly string[] InvestigationManualTopicIds =
    {
        "seasonal_economy", "army_supply", "field_combat", "city_assault",
        "unit_type_modifiers", "diplomacy", "research", "movement",
    };

    private static void InvestigationManualTopicsExpansion()
    {
        InvestigationManualTopicIndex();
        InvestigationManualEconomyValues();
        InvestigationManualCombatValues();
        InvestigationManualDiplomacyAndResearchValues();
        InvestigationManualMovementValues();
        InvestigationManualProfileValues();
        InvestigationManualTopicBounds();
        InvestigationManualTopicUnavailableAndParity();
    }

    // ---- helpers -----------------------------------------------------------

    private static InvestigationService InvestigationManualService(BalanceProfile balance,
        InvestigationOptions options = null)
    {
        var observation = new Observation
        {
            GameId = "g-manual", SeatId = "seat-a", Season = 1, SeasonName = "Spring",
            Phase = "player_orders", CommandPoints = BalanceConfig.CommandPointsPerSeason,
            Balance = balance,
        };
        return new InvestigationService(observation.GameId, observation.SeatId, observation, null, options);
    }

    private static string InvestigationInvariant(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string InvestigationInvariant(float value)
        => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string InvestigationFactNote(ManualLookupResult result, string name)
    {
        foreach (ManualFact fact in result.Facts)
            if (fact.Name == name) return fact.Note;
        return null;
    }

    private static bool InvestigationHasCaveat(ManualLookupResult result)
    {
        foreach (string note in result.Notes)
            if (note != null
                && note.IndexOf("command availability", StringComparison.OrdinalIgnoreCase) >= 0
                && note.IndexOf("action catalogue is authoritative", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        return false;
    }

    // ---- index and shape ---------------------------------------------------

    private static void InvestigationManualTopicIndex()
    {
        InvestigationService service = InvestigationManualService(BalanceProfile.Standard);
        ManualLookupResult index = service.ManualLookup("");
        Check(index.Ok && index.Topics.Count == 17,
            "[LS-018] the English index now lists the nine foundation and eight mechanics topics");
        Check(index.Topics.Count == InvestigationCatalogue.Topics.Count + InvestigationManualTopicIds.Length,
            "[LS-018] the index is the frozen catalogue plus the manual-topic additions");

        foreach (string id in InvestigationManualTopicIds)
        {
            TopicIndexEntry entry = null;
            foreach (TopicIndexEntry candidate in index.Topics)
                if (candidate.Id == id) entry = candidate;
            Check(entry != null && !entry.LlmActionExposed,
                "[LS-018] mechanics topic is indexed and not advertised as a model action: " + id);

            ManualLookupResult detail = service.ManualLookup(id);
            Check(detail.Ok && detail.TopicId == id && detail.Implemented,
                "[LS-018] mechanics topic resolves by id: " + id);
            Check(!detail.LlmActionExposed && detail.CommandPointCost == -1,
                "[LS-018] mechanics topic is not an action and has no command point cost: " + id);
            Check(detail.Facts.Count >= 8, "[LS-018] mechanics topic carries usable rule facts: " + id);
            Check(InvestigationParses(detail.ToJson()),
                "[LS-018] mechanics topic JSON is valid: " + id);
            Check(InvestigationHasCaveat(detail),
                "[LS-018] mechanics topic states that rules do not imply commands: " + id);
        }

        ManualLookupResult alias = service.ManualLookup("battle");
        Check(alias.Ok && alias.TopicId == "field_combat",
            "[LS-018] alias search reaches the field-battle topic");
    }

    // ---- response bounds ---------------------------------------------------

    private static void InvestigationManualTopicBounds()
    {
        var options = new InvestigationOptions { MaxResponseChars = 512, MaxTextChars = 600 };
        InvestigationService service = InvestigationManualService(BalanceProfile.Standard, options);

        foreach (string topic in InvestigationManualTopicIds)
        {
            ManualLookupResult bounded = service.ManualLookup(topic);
            Check(bounded.ToJson().Length <= options.MaxResponseChars,
                "[LS-018] a tight cap keeps the mechanics topic inside the budget: " + topic);
            Check(InvestigationParses(bounded.ToJson()),
                "[LS-018] a trimmed mechanics topic stays valid JSON: " + topic);
            Check(bounded.Ok && bounded.ReturnedCount == 1,
                "[LS-018] a mechanics topic still answers at cap 512: " + topic);
        }

        var minimum = new InvestigationOptions { MaxResponseChars = InvestigationOptions.MinimumResponseChars };
        InvestigationService tight = InvestigationManualService(BalanceProfile.Standard, minimum);
        foreach (string topic in InvestigationManualTopicIds)
        {
            ManualLookupResult bounded = tight.ManualLookup(topic);
            Check(bounded.ToJson().Length <= minimum.MaxResponseChars
                  && InvestigationParses(bounded.ToJson()),
                "[LS-018] the minimum cap bounds the mechanics topic without breaking JSON: " + topic);
            Check(!bounded.Ok && bounded.ErrorCode == InvestigationResult.ResponseTooLargeCode,
                "[LS-018] a mechanics topic too large for the minimum cap fails explicitly: " + topic);
        }

        ManualLookupResult repeated = service.ManualLookup("research");
        Check(repeated.ToJson() == service.ManualLookup("research").ToJson(),
            "[LS-018] bounded mechanics answers are deterministic");
    }

    // ---- unavailable topics and hidden-state independence ------------------

    private static void InvestigationManualTopicUnavailableAndParity()
    {
        InvestigationService service = InvestigationManualService(BalanceProfile.Standard);
        foreach (string topic in new[] { "navy", "plague" })
        {
            ManualLookupResult unsupported = service.ManualLookup(topic);
            Check(!unsupported.Ok && unsupported.ErrorCode == "unsupported_topic"
                  && unsupported.Topics.Count == 17,
                "[LS-018] a genuinely absent mechanic returns unsupported with the bounded index: " + topic);
            Check(InvestigationParses(unsupported.ToJson()),
                "[LS-018] an unsupported mechanics query stays valid JSON: " + topic);
        }

        GameEngine visibleWorld = InvestigationEngine(1921, false, out Observation visible);
        GameEngine hiddenWorld = InvestigationEngine(1921, true, out Observation hidden);
        InvestigationService visibleService = InvestigationServiceFor(visibleWorld, visible);
        InvestigationService hiddenService = InvestigationServiceFor(hiddenWorld, hidden);
        foreach (string topic in InvestigationManualTopicIds)
            Check(visibleService.ManualLookup(topic).ToJson() == hiddenService.ManualLookup(topic).ToJson(),
                "[LS-018] hidden world variation cannot change a mechanics answer: " + topic);
    }

    // ---- economy and supply ------------------------------------------------

    private static void InvestigationManualEconomyValues()
    {
        InvestigationService service = InvestigationManualService(BalanceProfile.Standard);

        ManualLookupResult economy = service.ManualLookup("economy");
        Check(economy.Ok && economy.TopicId == "seasonal_economy",
            "[LS-018] economy alias resolves to the seasonal economy topic");
        Check(InvestigationFact(economy, "base_food_yield_per_city")
              == InvestigationInvariant(BalanceProfile.Standard.CityBaseFoodYield),
            "[LS-018] base harvest matches the standard profile");
        Check(InvestigationFact(economy, "opening_food_per_city")
              == InvestigationInvariant(BalanceProfile.Standard.StartingFood)
              && InvestigationFact(economy, "opening_gold_per_city")
              == InvestigationInvariant(BalanceProfile.Standard.StartingGold),
            "[LS-018] opening economy matches the standard profile");
        Check(InvestigationFact(economy, "opening_population_per_city")
              == InvestigationInvariant(BalanceConfig.StartPopulation),
            "[LS-018] opening population matches BalanceConfig");
        Check(InvestigationFact(economy, "spring_food_multiplier")
              == "×" + InvestigationInvariant(BalanceConfig.SeasonFoodYieldMult(1))
              && InvestigationFact(economy, "autumn_food_multiplier")
              == "×" + InvestigationInvariant(BalanceConfig.SeasonFoodYieldMult(3))
              && InvestigationFact(economy, "winter_food_multiplier")
              == "×" + InvestigationInvariant(BalanceConfig.SeasonFoodYieldMult(4)),
            "[LS-018] season harvest multipliers match BalanceConfig");
        Check(InvestigationFact(economy, "gold_per_population")
              == InvestigationInvariant(BalanceConfig.TaxGoldPerPop)
              && InvestigationFact(economy, "food_eaten_per_population")
              == InvestigationInvariant(BalanceConfig.FoodPerPop),
            "[LS-018] tax and population food match BalanceConfig");
        Check(InvestigationFact(economy, "morale_yield_penalty")
              == "民心 < " + InvestigationInvariant(BalanceConfig.MoraleYieldPenaltyThreshold)
              + ": yields ×" + InvestigationInvariant(1f - BalanceConfig.MoraleYieldPenalty),
            "[LS-018] the morale yield penalty matches BalanceConfig");
        Check(InvestigationFact(economy, "starvation_population_loss")
              == InvestigationText.Percent(BalanceConfig.StarvationPopLoss),
            "[LS-018] starvation loss matches BalanceConfig");
        Check(InvestigationFact(economy, "population_growth_rate")
              == InvestigationText.Percent(BalanceConfig.PopGrowthRate)
              && InvestigationFact(economy, "population_growth_variance")
              == "±" + InvestigationText.Percent(BalanceConfig.PopGrowthVariance),
            "[LS-018] population growth rate and variance match BalanceConfig");
        Check(InvestigationFact(economy, "population_growth_rule")
              .IndexOf("truncates to whole people", StringComparison.Ordinal) >= 0
              && InvestigationFactNote(economy, "population_growth_rule")
                  .IndexOf("No growth", StringComparison.Ordinal) >= 0,
            "[LS-018] population growth is truncated and skipped in a famine season");
        bool exhaustiveClaim = false;
        foreach (string note in economy.Notes)
            if (note != null && note.IndexOf("Only these income", StringComparison.Ordinal) >= 0)
                exhaustiveClaim = true;
        Check(!exhaustiveClaim, "[LS-018] the economy topic no longer claims to be exhaustive");
        Check(InvestigationFact(economy, "revolt_threshold")
              == "民心 < " + InvestigationInvariant(BalanceConfig.RevoltRiskMoraleThreshold),
            "[LS-018] the revolt threshold matches BalanceConfig");
        Check(InvestigationFact(economy, "revolt_chance")
              == "(" + InvestigationInvariant(BalanceConfig.RevoltRiskMoraleThreshold)
              + " − 民心) × " + InvestigationText.Percent(BalanceConfig.RevoltChancePerMoralePoint)
              + " per season",
            "[LS-018] the revolt chance formula matches BalanceConfig");
        Check(InvestigationFact(economy, "revolt_effect")
              .IndexOf(InvestigationText.Percent(BalanceConfig.RevoltGarrisonLossPct),
                  StringComparison.Ordinal) >= 0,
            "[LS-018] the revolt garrison loss matches BalanceConfig");

        ManualLookupResult supply = service.ManualLookup("supply");
        Check(supply.Ok && supply.TopicId == "army_supply",
            "[LS-018] supply alias resolves to the army supply topic");
        foreach (string type in UnitTypes.All)
            Check(InvestigationFact(supply, "ration_per_1000_" + type)
                  == InvestigationInvariant(BalanceProfile.Standard.TroopFoodUpkeep(1000, type)),
                "[LS-018] ration per 1,000 matches the standard profile: " + type);
        Check(InvestigationFact(supply, "desertion_loss")
              == InvestigationText.Percent(BalanceConfig.DesertionTroopLossPct)
              && InvestigationFact(supply, "winter_extra_desertion")
              == InvestigationText.Percent(BalanceConfig.WinterDesertionExtraTroopLoss) + " in 冬",
            "[LS-018] desertion shares match BalanceConfig");
        Check(InvestigationFact(supply, "desertion_morale_loss")
              == "−" + InvestigationInvariant(BalanceConfig.DesertionMoraleLoss) + " 士氣",
            "[LS-018] desertion morale loss matches BalanceConfig");
        Check(InvestigationFact(supply, "supply_meter") == "absent",
            "[LS-018] army supply states that no supply meter is modelled");
    }
}
