using System.Collections.Generic;

namespace LuanShi.Engine
{
    // Combat, diplomacy and movement topics of the expanded manual. Numbers come from BalanceConfig
    // and the UnitTypes helpers that the engine itself calls, so the manual cannot drift from the
    // validator or claim a rule the engine does not implement.

    internal static partial class InvestigationManualTopicContent
    {
        // ---- 野戰 M2 (field battle) -------------------------------------------

        private static void FillFieldCombat(List<ManualFact> facts, List<string> notes)
        {
            facts.Add(Fact("command_point_cost",
                InvestigationText.Number(BalanceConfig.CommandPointCost(ActionType.AttackArmy)),
                "Default order cost for an attack_army order."));
            facts.Add(Fact("attack_range",
                "same hex or " + InvestigationText.Number(UnitTypes.AttackRange(UnitTypes.Infantry))
                + " hex away; 弓兵 up to " + InvestigationText.Number(UnitTypes.AttackRange(UnitTypes.Archer))
                + " hexes",
                "Range is measured in hex steps, so the same hex counts as distance 0."));
            facts.Add(Fact("rounds", InvestigationText.Number(BalanceConfig.CombatRounds),
                "A battle stops early when a side is destroyed or routed."));
            facts.Add(Fact("loss_per_round",
                InvestigationText.Percent(BalanceConfig.CombatLossPerRound) + " of enemy 戰力",
                "Each side loses this share of the opposing side's strength each round."));
            facts.Add(Fact("loss_variance", "±" + InvestigationText.Percent(BalanceConfig.CombatVariance),
                "Seeded per round, so a fixed seed replays identically."));
            facts.Add(Fact("strength_formula",
                "troops × (1 + training × " + InvestigationText.Percent(BalanceConfig.CombatTrainingBonusPerLevel)
                + ") × (" + Num(BalanceConfig.CombatMoraleFloor) + " + 士氣 / "
                + InvestigationText.Number((int)BalanceConfig.CombatMoraleScale) + ") × research × type",
                "The 戰力 one side brings to a round."));
            facts.Add(Fact("training_bonus",
                InvestigationText.Percent(BalanceConfig.CombatTrainingBonusPerLevel) + " per level",
                "An army fights at the best 訓練 level among its faction's cities."));
            facts.Add(Fact("counter_bonus", Mult(UnitTypes.FieldCombatMultiplier(UnitTypes.Infantry, UnitTypes.Archer)),
                "Attacking with the type that beats the defender's."));
            facts.Add(Fact("counter_penalty", Mult(UnitTypes.FieldCombatMultiplier(UnitTypes.Archer, UnitTypes.Infantry)),
                "Attacking with the type that is beaten."));
            facts.Add(Fact("siege_field_penalty",
                Mult(UnitTypes.FieldCombatMultiplier(UnitTypes.Siege, UnitTypes.Infantry)),
                "攻城器械 in a field battle."));
            facts.Add(Fact("plains_charge", Mult(BalanceConfig.CavalryPlainsChargeBonus),
                "騎兵 attacking a defender standing on plains; the defender never gets this bonus."));
            facts.Add(Fact("infantry_loss_taken", Mult(UnitTypes.LossTakenMultiplier(UnitTypes.Infantry))
                + " losses taken in field battles",
                "City assault and garrison defence do not apply this multiplier."));
            facts.Add(Fact("morale_rout",
                "士氣 " + InvestigationText.Number(BalanceConfig.CombatRoutMoraleThreshold),
                "A broken army cannot attack; it loses a further "
                + InvestigationText.Percent(BalanceConfig.CombatRoutExtraLoss)
                + " and only a survivor retreats to the nearest own free city. If that extra loss destroys "
                + "it, or no own city has a free hex, it is destroyed."));
            facts.Add(Fact("morale_loss_per_round",
                "−" + Num(BalanceConfig.CombatMoraleLossPerLossPercent) + " 士氣 per % of troops lost",
                "Applied to the side that took the losses."));
            facts.Add(Fact("victory_rewards",
                "the winning side's surviving army 士氣 +"
                + InvestigationText.Number(BalanceConfig.CombatVictoryMoraleGain)
                + "; the winner 威望 +" + InvestigationText.Number(BalanceConfig.PrestigeBattleVictory),
                "The defender is also the winner and receives both rewards when it holds after the final "
                + "round (defender_holds); 士氣 and 威望 are clamped to their configured ranges."));
            facts.Add(Fact("unfinished_battle",
                "after " + InvestigationText.Number(BalanceConfig.CombatRounds)
                + " rounds the defender holds", "There is no carry-over battle state between seasons."));
            facts.Add(Fact("attack_legitimacy",
                "alliance or NAP betrayal: trust −"
                + InvestigationText.Number(BalanceConfig.BetrayalTrustPenalty)
                + ", 威望 −" + InvestigationText.Number(BalanceConfig.BetrayalPrestigePenalty)
                + ", every other faction −" + InvestigationText.Number(BalanceConfig.BetrayalThirdPartyTrustPenalty)
                + "; truce break: trust −" + InvestigationText.Number(BalanceConfig.TruceBreakTrustPenalty)
                + ", 威望 −" + InvestigationText.Number(BalanceConfig.TruceBreakPrestigePenalty)
                + "; undeclared attack: 偷襲 trust −"
                + InvestigationText.Number(BalanceConfig.SurpriseAttackTrustPenalty)
                + ", 威望 −" + InvestigationText.Number(BalanceConfig.SurpriseAttackPrestigePenalty),
                "The engine prices the attack from the treaties in force when it executes; any of these starts the war."));
            notes.Add("A field battle is resolved immediately from the attack order; there is no separate battle screen.");
            notes.Add("戰力 is not shown in the observation; only an army's troops, type, position and 士氣 are.");
        }

        // ---- 攻城 M3 (city assault) -------------------------------------------

        private static void FillCityAssault(List<ManualFact> facts, List<string> notes)
        {
            facts.Add(Fact("command_point_cost",
                InvestigationText.Number(BalanceConfig.CommandPointCost(ActionType.AttackCity)),
                "attack_city costs more than a default order."));
            facts.Add(Fact("adjacency", "the target city's hex or one hex away",
                "The same hex counts as adjacent (distance 0), so a same-hex target passes the check."));
            facts.Add(Fact("assault_ratio",
                InvestigationText.Number(BalanceConfig.AssaultTroopRatio) + ":1",
                "Attacker troops must be at least this multiple of the garrison plus any defending field army."));
            facts.Add(Fact("siege_waiver",
                UnitTypes.WaivesAssaultRatio(UnitTypes.Siege) ? "yes for 攻城器械" : "no",
                "A siege army ignores the troop-ratio requirement."));
            facts.Add(Fact("defender_bonus",
                "+" + InvestigationText.Percent(BalanceConfig.CombatDefenseBonusPerLevel) + " per 城防 level",
                "城防 is raised by 修城 up to " + InvestigationText.Number(BalanceConfig.DefenseMax) + "."));
            facts.Add(Fact("archer_garrison_bonus",
                Mult(UnitTypes.SiegeDefenseMultiplier(UnitTypes.Archer)),
                "An 弓兵 garrison defends the walls at this multiplier."));
            facts.Add(Fact("siege_vs_city_bonus",
                Mult(UnitTypes.SiegeVsCityMultiplier(UnitTypes.Siege)),
                "An attacking 攻城器械 army multiplies its 戰力 against the city."));
            facts.Add(Fact("rounds", InvestigationText.Number(BalanceConfig.CombatRounds),
                "The garrison is destroyed at 0 troops (it has no 士氣 and never routs); the attacking "
                + "army may be destroyed or routed."));
            facts.Add(Fact("loss_per_round",
                InvestigationText.Percent(BalanceConfig.CombatLossPerRound) + " of enemy 戰力",
                "The city's strength uses 訓練 and 城防 instead of field 士氣."));
            facts.Add(Fact("loss_variance", "±" + InvestigationText.Percent(BalanceConfig.CombatVariance),
                "Seeded per round."));
            facts.Add(Fact("resolution", "one order, resolved in the same season",
                "There is no multi-season siege state or blockade."));
            facts.Add(Fact("defending_army",
                "a defending field army on the city hex merges into the garrison before the assault", null));
            facts.Add(Fact("capture_effect",
                "owner changes; garrison 0; 民心 " + InvestigationText.Number(BalanceConfig.MoraleMin)
                + "; the surviving army occupies the hex only if it is free; a zero-troop attacker is destroyed",
                "A city hex still held by another army is not entered by the attacker."));
            facts.Add(Fact("capture_prestige",
                "+" + InvestigationText.Number(BalanceConfig.PrestigeCityCaptured) + " 威望",
                "Winning a field battle pays "
                + InvestigationText.Number(BalanceConfig.PrestigeBattleVictory) + " instead."));
            facts.Add(Fact("neutral_cities", "not assaultable",
                "A neutral city is taken by marching into it, not by attack_city."));
            notes.Add("Only the assault itself is modelled; there is no separate siege equipment stock or wall damage track.");
        }

        // ---- 兵種 (unit type modifiers) ---------------------------------------

        private static void FillUnitTypes(List<ManualFact> facts, List<string> notes,
            BalanceProfile balance)
        {
            facts.Add(Fact("types", string.Join(" | ", UnitTypes.All),
                "Exactly one type per field army and per city garrison."));
            facts.Add(Fact("counter_cycle", "infantry > archer > cavalry > infantry",
                "The 相剋 cycle; 攻城器械 neither beats nor is beaten by it."));
            facts.Add(Fact("field_infantry_vs_archer",
                Mult(UnitTypes.FieldCombatMultiplier(UnitTypes.Infantry, UnitTypes.Archer)), null));
            facts.Add(Fact("field_archer_vs_infantry",
                Mult(UnitTypes.FieldCombatMultiplier(UnitTypes.Archer, UnitTypes.Infantry)), null));
            facts.Add(Fact("field_cavalry_vs_infantry",
                Mult(UnitTypes.FieldCombatMultiplier(UnitTypes.Cavalry, UnitTypes.Infantry)), null));
            facts.Add(Fact("field_infantry_vs_cavalry",
                Mult(UnitTypes.FieldCombatMultiplier(UnitTypes.Infantry, UnitTypes.Cavalry)), null));
            facts.Add(Fact("field_archer_vs_cavalry",
                Mult(UnitTypes.FieldCombatMultiplier(UnitTypes.Archer, UnitTypes.Cavalry)), null));
            facts.Add(Fact("field_cavalry_vs_archer",
                Mult(UnitTypes.FieldCombatMultiplier(UnitTypes.Cavalry, UnitTypes.Archer)), null));
            facts.Add(Fact("field_siege",
                Mult(UnitTypes.FieldCombatMultiplier(UnitTypes.Siege, UnitTypes.Infantry)),
                "攻城器械 in a field battle."));
            facts.Add(Fact("field_against_siege",
                Mult(UnitTypes.FieldCombatMultiplier(UnitTypes.Infantry, UnitTypes.Siege)),
                "Attacking 攻城器械 in the field."));
            facts.Add(Fact("archer_wall_bonus",
                Mult(UnitTypes.SiegeDefenseMultiplier(UnitTypes.Archer)),
                "An 弓兵 garrison defending a city."));
            facts.Add(Fact("siege_vs_city_bonus",
                Mult(UnitTypes.SiegeVsCityMultiplier(UnitTypes.Siege)),
                "攻城器械 attacking a city."));
            facts.Add(Fact("archer_attack_range",
                "up to " + InvestigationText.Number(UnitTypes.AttackRange(UnitTypes.Archer)) + " hexes",
                "Every other type attacks the same hex or one hex away."));
            facts.Add(Fact("cavalry_marches",
                InvestigationText.Number(UnitTypes.MarchesPerSeason(UnitTypes.Cavalry)) + " per season",
                "Every other type marches once per season; each march is its own order and budget."));
            facts.Add(Fact("cavalry_plains_charge",
                Mult(BalanceConfig.CavalryPlainsChargeBonus) + " on plains", null));
            facts.Add(Fact("infantry_loss_taken",
                Mult(UnitTypes.LossTakenMultiplier(UnitTypes.Infantry)) + " losses taken in field battles only",
                "City assault and garrison defence do not apply this multiplier."));
            facts.Add(Fact("move_points",
                "infantry " + InvestigationText.Number(UnitTypes.MovePoints(UnitTypes.Infantry))
                + " | archer " + InvestigationText.Number(UnitTypes.MovePoints(UnitTypes.Archer))
                + " | cavalry " + InvestigationText.Number(UnitTypes.MovePoints(UnitTypes.Cavalry))
                + " | siege " + InvestigationText.Number(UnitTypes.MovePoints(UnitTypes.Siege)),
                "Hex-cost per march before the 冬 multiplier."));
            facts.Add(Fact("food_per_1000",
                "infantry " + Num(UnitTypes.FoodPerTroop(UnitTypes.Infantry) * 1000f)
                + " | archer " + Num(UnitTypes.FoodPerTroop(UnitTypes.Archer) * 1000f)
                + " | cavalry " + Num(UnitTypes.FoodPerTroop(UnitTypes.Cavalry) * 1000f)
                + " | siege " + Num(UnitTypes.FoodPerTroop(UnitTypes.Siege) * 1000f),
                "Base ration before the profile's army upkeep multiplier."));
            facts.Add(Fact("recruit_gold_per_1000",
                "infantry " + InvestigationText.Number(balance.RecruitGoldCost(UnitTypes.Infantry, 1000))
                + " | archer " + InvestigationText.Number(balance.RecruitGoldCost(UnitTypes.Archer, 1000))
                + " | cavalry " + InvestigationText.Number(balance.RecruitGoldCost(UnitTypes.Cavalry, 1000))
                + " | siege " + InvestigationText.Number(balance.RecruitGoldCost(UnitTypes.Siege, 1000)),
                "Under the match's balance profile."));
            facts.Add(Fact("research_gates",
                "騎兵 needs military " + InvestigationText.Number(BalanceConfig.CavalryResearchLevel)
                + "; 攻城器械 needs military " + InvestigationText.Number(BalanceConfig.SiegeResearchLevel),
                null));
            facts.Add(Fact("one_type_rule",
                "a non-empty garrison or army must match the incoming type; an empty one adopts it",
                "Ordinary recruit and garrison transfers reject mixed types; the defensive assault merge below is the exception."));
            facts.Add(Fact("assault_merge_exception",
                "a defending army merges into the garrison even if its type differs; an empty garrison adopts its type, a non-empty one keeps its own",
                "This defensive merge happens during attack_city and normalises rather than mixing types."));
            notes.Add("Type multipliers are read from the same UnitTypes helpers the battle code calls.");
        }

        // ---- 外交 P1-P6 (diplomacy, treaties, reputation) ----------------------

        private static void FillDiplomacy(List<ManualFact> facts, List<string> notes)
        {
            facts.Add(Fact("treaty_types", TreatyType.Nap + " | " + TreatyType.Alliance + " | " + TreatyType.Truce,
                "互不侵犯, 同盟 and 停戰."));
            facts.Add(Fact("treaty_duration",
                "default " + InvestigationText.Number(BalanceConfig.TreatyDurationSeasons)
                + " seasons; range " + InvestigationText.Number(BalanceConfig.TreatyDurationMinSeasons)
                + "–" + InvestigationText.Number(BalanceConfig.TreatyDurationMaxSeasons),
                "An offer naming no duration uses the default."));
            facts.Add(Fact("treaty_timing",
                "accepted at the next season rollover; in force from that season",
                "Unanswered offers lapse at rollover."));
            facts.Add(Fact("treaty_expiry",
                "a treaty ends after its last season; a ratified 停戰 also ends the war", null));
            facts.Add(Fact("talk_cost",
                InvestigationText.Number(BalanceConfig.CommandPointCost(ActionType.SendMessage)) + " 號令",
                "send_message, propose_treaty and respond_treaty are free."));
            facts.Add(Fact("trust_range",
                InvestigationText.Number(BalanceConfig.TrustMin) + " to "
                + InvestigationText.Number(BalanceConfig.TrustMax)
                + "; starts at " + InvestigationText.Number(BalanceConfig.TrustStart),
                "Trust is directional: each side's view of the other is stored separately."));
            facts.Add(Fact("gift",
                InvestigationText.Number(BalanceConfig.GiftGoldPerTrustUnit) + " gold → +"
                + InvestigationText.Number(BalanceConfig.GiftTrustPerUnit) + " trust, +"
                + InvestigationText.Number(BalanceConfig.GiftPrestigePerUnit) + " 威望",
                "The receiver's trust in the giver rises; the giver gains 威望."));
            facts.Add(Fact("declare_war",
                "威望 −" + InvestigationText.Number(BalanceConfig.DeclareWarPrestigeCost)
                + "; " + InvestigationText.Number(BalanceConfig.CommandPointCost(ActionType.DeclareWar))
                + " 號令", "A formal declaration starts the war."));
            facts.Add(Fact("betrayal",
                "trust −" + InvestigationText.Number(BalanceConfig.BetrayalTrustPenalty)
                + "; 威望 −" + InvestigationText.Number(BalanceConfig.BetrayalPrestigePenalty)
                + "; every other faction's trust −"
                + InvestigationText.Number(BalanceConfig.BetrayalThirdPartyTrustPenalty),
                "Applied when an alliance or NAP is broken, whether by break_treaty or by attacking the partner."));
            facts.Add(Fact("truce_break",
                "trust −" + InvestigationText.Number(BalanceConfig.TruceBreakTrustPenalty)
                + "; 威望 −" + InvestigationText.Number(BalanceConfig.TruceBreakPrestigePenalty), null));
            facts.Add(Fact("undeclared_attack",
                "偷襲: trust −" + InvestigationText.Number(BalanceConfig.SurpriseAttackTrustPenalty)
                + "; 威望 −" + InvestigationText.Number(BalanceConfig.SurpriseAttackPrestigePenalty),
                "Attacking a seat you are not at war with starts the war."));
            facts.Add(Fact("treaty_effect",
                "diplomatic records; no city-entry or attack immunity",
                "A treaty does not let you enter another seat's cities or make them immune to attack; "
                + "the ordinary foreign-city rules still apply and aggression is priced by the penalties above."));
            facts.Add(Fact("prestige_range",
                InvestigationText.Number(BalanceConfig.PrestigeMin) + " to "
                + InvestigationText.Number(BalanceConfig.PrestigeMax)
                + "; starts at " + InvestigationText.Number(BalanceConfig.PrestigeStart),
                "威望 is public to every seat."));
            notes.Add("Victory in a field battle pays "
                + InvestigationText.Number(BalanceConfig.PrestigeBattleVictory)
                + " 威望; storming a city pays "
                + InvestigationText.Number(BalanceConfig.PrestigeCityCaptured) + ".");
        }

        // ---- 行軍 (movement and capture on march) ------------------------------

        private static void FillMovement(List<ManualFact> facts, List<string> notes)
        {
            facts.Add(Fact("command_point_cost",
                InvestigationText.Number(BalanceConfig.CommandPointCost(ActionType.March)),
                "Each march order costs this many 號令."));
            facts.Add(Fact("march_budget",
                "infantry " + InvestigationText.Number(UnitTypes.MovePoints(UnitTypes.Infantry))
                + " | archer " + InvestigationText.Number(UnitTypes.MovePoints(UnitTypes.Archer))
                + " | cavalry " + InvestigationText.Number(UnitTypes.MovePoints(UnitTypes.Cavalry))
                + " | siege " + InvestigationText.Number(UnitTypes.MovePoints(UnitTypes.Siege)),
                "Hex-cost allowed per march by unit type; every march gets a fresh budget, including 騎兵's second march."));
            // Winter seasons are 4, 8, 12, ... (design §3); both budgets below use that season.
            const int winter = 4;
            facts.Add(Fact("winter_budget",
                "×" + Num(BalanceConfig.WinterMarchBudgetMult) + ", truncated: infantry "
                + InvestigationText.Number(BalanceConfig.MarchBudget(winter, UnitTypes.Infantry))
                + ", cavalry " + InvestigationText.Number(BalanceConfig.MarchBudget(winter, UnitTypes.Cavalry))
                + ", siege " + InvestigationText.Number(BalanceConfig.MarchBudget(winter, UnitTypes.Siege)),
                "冬 reduces each march's budget."));
            facts.Add(Fact("marches_per_season",
                "騎兵 " + InvestigationText.Number(UnitTypes.MarchesPerSeason(UnitTypes.Cavalry))
                + "; every other type " + InvestigationText.Number(UnitTypes.MarchesPerSeason(UnitTypes.Infantry)),
                "Each march is its own order with its own budget."));
            facts.Add(Fact("destination_rules",
                "walkable, inside own sight, not occupied by another army, and not any city owned by another seat (allies included)",
                "A destination beyond sight is rejected before any hidden fact is revealed."));
            facts.Add(Fact("path_cost",
                "Dijkstra over per-hex terrain costs",
                "The whole path must fit that march's budget; a path out of reach is rejected."));
            facts.Add(Fact("capture_on_march",
                "a neutral city or any watchtower not already yours is captured on arrival", null));
            facts.Add(Fact("enemy_city",
                "marching into any non-neutral city owned by another seat is rejected, ally or not",
                "Attacking such a city requires the separate attack_city order, which is priced as aggression."));
            facts.Add(Fact("no_waypoints",
                "one destination per order",
                "There is no multi-season waypoint plan or standing path."));
            facts.Add(Fact("supply_distance", "absent",
                "No supply meter or distance-from-home rule is applied to marching."));
            facts.Add(Fact("rout_retreat",
                "a routed army loses " + InvestigationText.Percent(BalanceConfig.CombatRoutExtraLoss)
                + " more troops; only a survivor retreats to the nearest own city with no army on it",
                "If the extra loss destroys the army, or no own city is free, it is destroyed."));
            notes.Add("Sight and reach both bound a march, so effective movement is limited to the vision ring.");
        }
    }
}
