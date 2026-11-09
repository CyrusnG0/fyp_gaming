using System;
using LuanShi.Engine;

// [LS-018] Exact-value tests for the expanded manual topics. Each expected string is built from
// BalanceConfig / BalanceProfile / UnitTypes, so the assertions pin the wiring rather than a copy of
// the prose. Shared helpers live in Harness.Investigation.ManualTopics.cs.

internal static partial class Harness
{
    // ---- combat ------------------------------------------------------------

    private static void InvestigationManualCombatValues()
    {
        InvestigationService service = InvestigationManualService(BalanceProfile.Standard);

        ManualLookupResult field = service.ManualLookup("field combat");
        Check(field.Ok && field.TopicId == "field_combat",
            "[LS-018] field combat topic resolves");
        Check(InvestigationFact(field, "command_point_cost")
              == InvestigationInvariant(BalanceConfig.CommandPointCost(ActionType.AttackArmy)),
            "[LS-018] field battle cost matches BalanceConfig");
        Check(InvestigationFact(field, "rounds") == InvestigationInvariant(BalanceConfig.CombatRounds)
              && InvestigationFact(field, "loss_variance")
              == "±" + InvestigationText.Percent(BalanceConfig.CombatVariance),
            "[LS-018] battle rounds and variance match BalanceConfig");
        Check(InvestigationFact(field, "loss_per_round")
              == InvestigationText.Percent(BalanceConfig.CombatLossPerRound) + " of enemy 戰力",
            "[LS-018] per-round losses match BalanceConfig");
        Check(InvestigationFact(field, "counter_bonus")
              == "×" + InvestigationInvariant(UnitTypes.FieldCombatMultiplier(UnitTypes.Infantry, UnitTypes.Archer))
              && InvestigationFact(field, "counter_penalty")
              == "×" + InvestigationInvariant(UnitTypes.FieldCombatMultiplier(UnitTypes.Archer, UnitTypes.Infantry))
              && InvestigationFact(field, "siege_field_penalty")
              == "×" + InvestigationInvariant(UnitTypes.FieldCombatMultiplier(UnitTypes.Siege, UnitTypes.Infantry)),
            "[LS-018] field type multipliers match UnitTypes");
        Check(InvestigationFact(field, "plains_charge")
              == "×" + InvestigationInvariant(BalanceConfig.CavalryPlainsChargeBonus),
            "[LS-018] the cavalry charge multiplier matches the engine");
        Check(InvestigationFact(field, "infantry_loss_taken")
              == "×" + InvestigationInvariant(UnitTypes.LossTakenMultiplier(UnitTypes.Infantry))
              + " losses taken in field battles",
            "[LS-018] field combat resilience is scoped to field battles");
        Check(InvestigationFact(field, "attack_range")
              == "same hex or " + InvestigationInvariant(UnitTypes.AttackRange(UnitTypes.Infantry))
              + " hex away; 弓兵 up to " + InvestigationInvariant(UnitTypes.AttackRange(UnitTypes.Archer))
              + " hexes",
            "[LS-018] attack range matches UnitTypes and includes the same hex");
        Check(InvestigationFactNote(field, "attack_range")
              .IndexOf("distance 0", StringComparison.Ordinal) >= 0,
            "[LS-018] field combat states that the same hex counts as distance 0");
        Check(InvestigationFactNote(field, "morale_rout")
              .IndexOf("only a survivor retreats", StringComparison.Ordinal) >= 0
              && InvestigationFactNote(field, "morale_rout")
                  .IndexOf("or no own city has a free hex", StringComparison.Ordinal) >= 0,
            "[LS-018] a rout loss can destroy the army before any retreat");
        Check(InvestigationFact(field, "victory_rewards")
              == "the winning side's surviving army 士氣 +"
              + InvestigationInvariant(BalanceConfig.CombatVictoryMoraleGain)
              + "; the winner 威望 +" + InvestigationInvariant(BalanceConfig.PrestigeBattleVictory),
            "[LS-018] victory rewards name the winning side");
        Check(InvestigationFactNote(field, "victory_rewards")
              .IndexOf("defender_holds", StringComparison.Ordinal) >= 0
              && InvestigationFactNote(field, "victory_rewards")
                  .IndexOf("clamped", StringComparison.Ordinal) >= 0,
            "[LS-018] a holding defender is the winner and receives clamped rewards");
        Check(InvestigationFact(field, "attack_legitimacy")
              == "alliance or NAP betrayal: trust −"
              + InvestigationInvariant(BalanceConfig.BetrayalTrustPenalty)
              + ", 威望 −" + InvestigationInvariant(BalanceConfig.BetrayalPrestigePenalty)
              + ", every other faction −"
              + InvestigationInvariant(BalanceConfig.BetrayalThirdPartyTrustPenalty)
              + "; truce break: trust −" + InvestigationInvariant(BalanceConfig.TruceBreakTrustPenalty)
              + ", 威望 −" + InvestigationInvariant(BalanceConfig.TruceBreakPrestigePenalty)
              + "; undeclared attack: 偷襲 trust −"
              + InvestigationInvariant(BalanceConfig.SurpriseAttackTrustPenalty)
              + ", 威望 −" + InvestigationInvariant(BalanceConfig.SurpriseAttackPrestigePenalty),
            "[LS-018] attack legitimacy distinguishes betrayal, truce break and sneak attack");

        ManualLookupResult assault = service.ManualLookup("siege");
        Check(assault.Ok && assault.TopicId == "city_assault",
            "[LS-018] assault alias resolves to the city assault topic");
        Check(InvestigationFact(assault, "command_point_cost")
              == InvestigationInvariant(BalanceConfig.CommandPointCost(ActionType.AttackCity)),
            "[LS-018] city assault cost matches BalanceConfig");
        Check(InvestigationFact(assault, "assault_ratio")
              == InvestigationInvariant(BalanceConfig.AssaultTroopRatio) + ":1",
            "[LS-018] assault ratio matches BalanceConfig");
        Check(InvestigationFact(assault, "adjacency") == "the target city's hex or one hex away",
            "[LS-018] assault adjacency includes the same hex");
        Check(InvestigationFactNote(assault, "adjacency")
              .IndexOf("distance 0", StringComparison.Ordinal) >= 0,
            "[LS-018] city assault states that the same hex counts as adjacent");
        Check(InvestigationFact(assault, "defender_bonus")
              == "+" + InvestigationText.Percent(BalanceConfig.CombatDefenseBonusPerLevel) + " per 城防 level",
            "[LS-018] 城防 bonus matches BalanceConfig");
        Check(InvestigationFact(assault, "archer_garrison_bonus")
              == "×" + InvestigationInvariant(UnitTypes.SiegeDefenseMultiplier(UnitTypes.Archer))
              && InvestigationFact(assault, "siege_vs_city_bonus")
              == "×" + InvestigationInvariant(UnitTypes.SiegeVsCityMultiplier(UnitTypes.Siege)),
            "[LS-018] wall and siege multipliers match UnitTypes");
        Check(InvestigationFact(assault, "capture_prestige")
              == "+" + InvestigationInvariant(BalanceConfig.PrestigeCityCaptured) + " 威望",
            "[LS-018] capture prestige matches BalanceConfig");
        Check(InvestigationFact(assault, "capture_effect")
              == "owner changes; garrison 0; 民心 " + InvestigationInvariant(BalanceConfig.MoraleMin)
              + "; the surviving army occupies the hex only if it is free; a zero-troop attacker is destroyed",
            "[LS-018] capture effect states the conditional occupation and pyrrhic destruction");
        Check(InvestigationFactNote(assault, "rounds")
              .IndexOf("never routs", StringComparison.Ordinal) >= 0
              && InvestigationFactNote(assault, "rounds")
                  .IndexOf("may be destroyed or routed", StringComparison.Ordinal) >= 0,
            "[LS-018] the garrison never routs while the attacking army can be destroyed or routed");

        ManualLookupResult types = service.ManualLookup("unit types");
        Check(types.Ok && types.TopicId == "unit_type_modifiers",
            "[LS-018] unit-type topic resolves");
        Check(InvestigationFact(types, "field_siege")
              == "×" + InvestigationInvariant(UnitTypes.FieldCombatMultiplier(UnitTypes.Siege, UnitTypes.Infantry))
              && InvestigationFact(types, "field_against_siege")
              == "×" + InvestigationInvariant(UnitTypes.FieldCombatMultiplier(UnitTypes.Infantry, UnitTypes.Siege)),
            "[LS-018] siege field modifiers match UnitTypes");
        Check(InvestigationFact(types, "archer_attack_range")
              == "up to " + InvestigationInvariant(UnitTypes.AttackRange(UnitTypes.Archer)) + " hexes"
              && InvestigationFactNote(types, "archer_attack_range")
                  .IndexOf("same hex", StringComparison.Ordinal) >= 0
              && InvestigationFact(types, "cavalry_marches")
              == InvestigationInvariant(UnitTypes.MarchesPerSeason(UnitTypes.Cavalry)) + " per season",
            "[LS-018] archer range includes the same hex and cavalry marches match UnitTypes");
        Check(InvestigationFact(types, "move_points")
              == "infantry " + InvestigationInvariant(UnitTypes.MovePoints(UnitTypes.Infantry))
              + " | archer " + InvestigationInvariant(UnitTypes.MovePoints(UnitTypes.Archer))
              + " | cavalry " + InvestigationInvariant(UnitTypes.MovePoints(UnitTypes.Cavalry))
              + " | siege " + InvestigationInvariant(UnitTypes.MovePoints(UnitTypes.Siege)),
            "[LS-018] unit move points match UnitTypes");
        Check(InvestigationFact(types, "food_per_1000")
              == "infantry " + InvestigationInvariant(UnitTypes.FoodPerTroop(UnitTypes.Infantry) * 1000f)
              + " | archer " + InvestigationInvariant(UnitTypes.FoodPerTroop(UnitTypes.Archer) * 1000f)
              + " | cavalry " + InvestigationInvariant(UnitTypes.FoodPerTroop(UnitTypes.Cavalry) * 1000f)
              + " | siege " + InvestigationInvariant(UnitTypes.FoodPerTroop(UnitTypes.Siege) * 1000f),
            "[LS-018] unit food per 1,000 matches UnitTypes");
        Check(InvestigationFact(types, "recruit_gold_per_1000")
              == "infantry " + InvestigationInvariant(BalanceProfile.Standard.RecruitGoldCost(UnitTypes.Infantry, 1000))
              + " | archer " + InvestigationInvariant(BalanceProfile.Standard.RecruitGoldCost(UnitTypes.Archer, 1000))
              + " | cavalry " + InvestigationInvariant(BalanceProfile.Standard.RecruitGoldCost(UnitTypes.Cavalry, 1000))
              + " | siege " + InvestigationInvariant(BalanceProfile.Standard.RecruitGoldCost(UnitTypes.Siege, 1000)),
            "[LS-018] recruitment costs match the standard profile");
        Check(InvestigationFact(types, "research_gates")
              == "騎兵 needs military " + InvestigationInvariant(BalanceConfig.CavalryResearchLevel)
              + "; 攻城器械 needs military " + InvestigationInvariant(BalanceConfig.SiegeResearchLevel),
            "[LS-018] unit research gates match BalanceConfig");
        Check(InvestigationFact(types, "infantry_loss_taken")
              == "×" + InvestigationInvariant(UnitTypes.LossTakenMultiplier(UnitTypes.Infantry))
              + " losses taken in field battles only"
              && InvestigationFactNote(types, "infantry_loss_taken")
                  .IndexOf("City assault", StringComparison.Ordinal) >= 0,
            "[LS-018] 步兵 resilience is explicitly field-battle only");
        Check(InvestigationFact(types, "assault_merge_exception")
              == "a defending army merges into the garrison even if its type differs; an empty garrison "
              + "adopts its type, a non-empty one keeps its own",
            "[LS-018] the defensive-assault merge exception is documented");
        Check(InvestigationFactNote(types, "one_type_rule")
              .IndexOf("assault merge below is the exception", StringComparison.Ordinal) >= 0,
            "[LS-018] the ordinary type rule names the assault merge as its exception");
    }

    // ---- diplomacy and research -------------------------------------------

    private static void InvestigationManualDiplomacyAndResearchValues()
    {
        InvestigationService service = InvestigationManualService(BalanceProfile.Standard);

        ManualLookupResult diplomacy = service.ManualLookup("diplomacy");
        Check(diplomacy.Ok && diplomacy.TopicId == "diplomacy",
            "[LS-018] diplomacy topic resolves");
        Check(InvestigationFact(diplomacy, "treaty_types")
              == TreatyType.Nap + " | " + TreatyType.Alliance + " | " + TreatyType.Truce,
            "[LS-018] treaty types match the engine contract");
        Check(InvestigationFact(diplomacy, "treaty_duration")
              == "default " + InvestigationInvariant(BalanceConfig.TreatyDurationSeasons)
              + " seasons; range " + InvestigationInvariant(BalanceConfig.TreatyDurationMinSeasons)
              + "–" + InvestigationInvariant(BalanceConfig.TreatyDurationMaxSeasons),
            "[LS-018] treaty duration bounds match BalanceConfig");
        Check(InvestigationFact(diplomacy, "gift")
              == InvestigationInvariant(BalanceConfig.GiftGoldPerTrustUnit) + " gold → +"
              + InvestigationInvariant(BalanceConfig.GiftTrustPerUnit) + " trust, +"
              + InvestigationInvariant(BalanceConfig.GiftPrestigePerUnit) + " 威望",
            "[LS-018] gift rates match BalanceConfig");
        Check(InvestigationFact(diplomacy, "betrayal")
              == "trust −" + InvestigationInvariant(BalanceConfig.BetrayalTrustPenalty)
              + "; 威望 −" + InvestigationInvariant(BalanceConfig.BetrayalPrestigePenalty)
              + "; every other faction's trust −"
              + InvestigationInvariant(BalanceConfig.BetrayalThirdPartyTrustPenalty),
            "[LS-018] betrayal penalties match BalanceConfig");
        Check(InvestigationFact(diplomacy, "truce_break")
              == "trust −" + InvestigationInvariant(BalanceConfig.TruceBreakTrustPenalty)
              + "; 威望 −" + InvestigationInvariant(BalanceConfig.TruceBreakPrestigePenalty),
            "[LS-018] truce-break penalties match BalanceConfig");
        Check(InvestigationFact(diplomacy, "undeclared_attack")
              == "偷襲: trust −" + InvestigationInvariant(BalanceConfig.SurpriseAttackTrustPenalty)
              + "; 威望 −" + InvestigationInvariant(BalanceConfig.SurpriseAttackPrestigePenalty),
            "[LS-018] undeclared-attack penalties match BalanceConfig");
        Check(InvestigationFact(diplomacy, "treaty_effect")
              == "diplomatic records; no city-entry or attack immunity",
            "[LS-018] treaties are documented as records without city-entry or attack immunity");
        Check(InvestigationFactNote(diplomacy, "treaty_effect")
              .IndexOf("does not let you enter", StringComparison.Ordinal) >= 0,
            "[LS-018] the treaty note denies allied-city entry");

        ManualLookupResult research = service.ManualLookup("research");
        Check(research.Ok && research.TopicId == "research",
            "[LS-018] research topic resolves");
        var expectedCosts = new System.Text.StringBuilder();
        for (int level = 1; level <= BalanceConfig.ResearchMaxLevel; level++)
        {
            if (expectedCosts.Length > 0) expectedCosts.Append(" / ");
            expectedCosts.Append(InvestigationInvariant(BalanceConfig.ResearchLevelCost(level)));
        }
        Check(InvestigationFact(research, "level_costs") == expectedCosts.ToString(),
            "[LS-018] research level costs match BalanceConfig's locked table");
        Check(InvestigationFact(research, "max_level")
              == InvestigationInvariant(BalanceConfig.ResearchMaxLevel),
            "[LS-018] research level cap matches BalanceConfig");
        Check(InvestigationFact(research, "command_point_cost")
              == InvestigationInvariant(BalanceConfig.ResearchCommandPoints),
            "[LS-018] research command point cost matches BalanceConfig");
        Check(InvestigationFact(research, "points_per_season")
              == InvestigationInvariant(BalanceConfig.ResearchIncome(0))
              && InvestigationFact(research, "points_per_season_with_governance_l4")
              == InvestigationInvariant(BalanceConfig.ResearchIncome(4)),
            "[LS-018] research income and 學堂 bonus match BalanceConfig");
        Check(InvestigationFact(research, "base_training_cap")
              == InvestigationInvariant(BalanceConfig.TrainingCap(0))
              && InvestigationFact(research, "military_l1_training_cap")
              == InvestigationInvariant(BalanceConfig.TrainingCap(1)),
            "[LS-018] military training caps match BalanceConfig");
        Check(InvestigationFact(research, "military_l4_combat")
              == "×" + InvestigationInvariant(BalanceConfig.ResearchCombatStrengthMult(4)),
            "[LS-018] military combat research matches BalanceConfig");
        Check(InvestigationFact(research, "governance_l3_revolt")
              == "×" + InvestigationInvariant(BalanceConfig.ResearchGovRevoltMult),
            "[LS-018] the revolt research multiplier matches BalanceConfig");
        Check(InvestigationFact(research, "deferred") == "not modelled",
            "[LS-018] the manual names the deferred 學堂 building income");
    }

    // ---- movement ----------------------------------------------------------

    private static void InvestigationManualMovementValues()
    {
        InvestigationService service = InvestigationManualService(BalanceProfile.Standard);
        ManualLookupResult movement = service.ManualLookup("movement");
        Check(movement.Ok && movement.TopicId == "movement",
            "[LS-018] movement topic resolves");
        Check(InvestigationFact(movement, "command_point_cost")
              == InvestigationInvariant(BalanceConfig.CommandPointCost(ActionType.March)),
            "[LS-018] march cost matches BalanceConfig");
        Check(InvestigationFact(movement, "march_budget")
              == "infantry " + InvestigationInvariant(UnitTypes.MovePoints(UnitTypes.Infantry))
              + " | archer " + InvestigationInvariant(UnitTypes.MovePoints(UnitTypes.Archer))
              + " | cavalry " + InvestigationInvariant(UnitTypes.MovePoints(UnitTypes.Cavalry))
              + " | siege " + InvestigationInvariant(UnitTypes.MovePoints(UnitTypes.Siege)),
            "[LS-018] march budgets match UnitTypes");
        Check(InvestigationFactNote(movement, "march_budget")
              .IndexOf("per march", StringComparison.Ordinal) >= 0
              && InvestigationFactNote(movement, "march_budget")
                  .IndexOf("fresh budget", StringComparison.Ordinal) >= 0,
            "[LS-018] the march budget is described per march, not per season");
        const int winter = 4;
        Check(InvestigationFact(movement, "winter_budget")
              == "×" + InvestigationInvariant(BalanceConfig.WinterMarchBudgetMult)
              + ", truncated: infantry " + InvestigationInvariant(BalanceConfig.MarchBudget(winter, UnitTypes.Infantry))
              + ", cavalry " + InvestigationInvariant(BalanceConfig.MarchBudget(winter, UnitTypes.Cavalry))
              + ", siege " + InvestigationInvariant(BalanceConfig.MarchBudget(winter, UnitTypes.Siege)),
            "[LS-018] the winter march budget matches BalanceConfig");
        Check(InvestigationFact(movement, "marches_per_season")
              == "騎兵 " + InvestigationInvariant(UnitTypes.MarchesPerSeason(UnitTypes.Cavalry))
              + "; every other type " + InvestigationInvariant(UnitTypes.MarchesPerSeason(UnitTypes.Infantry)),
            "[LS-018] marches per season match UnitTypes");
        Check(InvestigationFact(movement, "destination_rules")
              == "walkable, inside own sight, not occupied by another army, and not any city owned by "
              + "another seat (allies included)",
            "[LS-018] march destinations exclude every foreign city, allies included");
        Check(InvestigationFact(movement, "enemy_city")
              == "marching into any non-neutral city owned by another seat is rejected, ally or not"
              && InvestigationFactNote(movement, "enemy_city")
                  .IndexOf("priced as aggression", StringComparison.Ordinal) >= 0,
            "[LS-018] foreign-city entry is refused for allies too and priced as aggression");
        Check(InvestigationFact(movement, "rout_retreat")
              == "a routed army loses " + InvestigationText.Percent(BalanceConfig.CombatRoutExtraLoss)
              + " more troops; only a survivor retreats to the nearest own city with no army on it",
            "[LS-018] movement rout wording covers the extra loss before retreat");
        Check(InvestigationFactNote(movement, "rout_retreat")
              .IndexOf("If the extra loss destroys the army", StringComparison.Ordinal) >= 0,
            "[LS-018] a destroyed rout leaves no retreat");
        Check(InvestigationFact(movement, "supply_distance") == "absent",
            "[LS-018] movement states that no supply-distance rule is modelled");
        Check(InvestigationFact(movement, "capture_on_march")
              == "a neutral city or any watchtower not already yours is captured on arrival",
            "[LS-018] capture on march covers neutral cities and every tower not already owned");
    }

    // ---- balance profile ---------------------------------------------------

    private static void InvestigationManualProfileValues()
    {
        InvestigationService standard = InvestigationManualService(BalanceProfile.Standard);
        InvestigationService easy = InvestigationManualService(BalanceProfile.EasyResearch);

        ManualLookupResult standardEconomy = standard.ManualLookup("seasonal_economy");
        ManualLookupResult easyEconomy = easy.ManualLookup("seasonal_economy");
        Check(InvestigationFact(easyEconomy, "base_food_yield_per_city")
              == InvestigationInvariant(BalanceProfile.EasyResearch.CityBaseFoodYield)
              && InvestigationFact(easyEconomy, "opening_food_per_city")
              == InvestigationInvariant(BalanceProfile.EasyResearch.StartingFood)
              && InvestigationFact(easyEconomy, "opening_gold_per_city")
              == InvestigationInvariant(BalanceProfile.EasyResearch.StartingGold),
            "[LS-018] the easy-research economy values come from that profile");
        Check(InvestigationFact(standardEconomy, "base_food_yield_per_city")
              != InvestigationFact(easyEconomy, "base_food_yield_per_city")
              && InvestigationFact(standardEconomy, "opening_food_per_city")
              != InvestigationFact(easyEconomy, "opening_food_per_city"),
            "[LS-018] the two profiles are described differently, not hard-coded");

        ManualLookupResult standardSupply = standard.ManualLookup("army_supply");
        ManualLookupResult easySupply = easy.ManualLookup("army_supply");
        foreach (string type in UnitTypes.All)
        {
            Check(InvestigationFact(easySupply, "ration_per_1000_" + type)
                  == InvestigationInvariant(BalanceProfile.EasyResearch.TroopFoodUpkeep(1000, type)),
                "[LS-018] the easy-research ration comes from that profile: " + type);
            Check(InvestigationFact(easySupply, "ration_per_1000_" + type)
                  != InvestigationFact(standardSupply, "ration_per_1000_" + type),
                "[LS-018] the halved upkeep is visible in the easy-research manual: " + type);
        }

        ManualLookupResult standardTypes = standard.ManualLookup("unit_type_modifiers");
        ManualLookupResult easyTypes = easy.ManualLookup("unit_type_modifiers");
        Check(InvestigationFact(easyTypes, "recruit_gold_per_1000")
              == "infantry " + InvestigationInvariant(BalanceProfile.EasyResearch.RecruitGoldCost(UnitTypes.Infantry, 1000))
              + " | archer " + InvestigationInvariant(BalanceProfile.EasyResearch.RecruitGoldCost(UnitTypes.Archer, 1000))
              + " | cavalry " + InvestigationInvariant(BalanceProfile.EasyResearch.RecruitGoldCost(UnitTypes.Cavalry, 1000))
              + " | siege " + InvestigationInvariant(BalanceProfile.EasyResearch.RecruitGoldCost(UnitTypes.Siege, 1000)),
            "[LS-018] the easy-research recruitment costs come from that profile");
        Check(InvestigationFact(standardTypes, "recruit_gold_per_1000")
              != InvestigationFact(easyTypes, "recruit_gold_per_1000"),
            "[LS-018] domestic gold costs differ between the two profiles");

        // Topics without profile-scoped values must be byte-identical under both profiles.
        foreach (string topic in new[] { "field_combat", "city_assault", "diplomacy", "research", "movement" })
            Check(standard.ManualLookup(topic).ToJson() == easy.ManualLookup(topic).ToJson(),
                "[LS-018] a profile-free topic is unchanged by the balance profile: " + topic);
    }
}
