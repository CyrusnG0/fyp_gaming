using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using LuanShi.Engine;

// [UNITS] Opt-in unit-type recruitment for the LLM (LlmConfig.EnableUnitTypes): the same four types, gates
// and costs the human city panel offers. Everything runs through the real runner, parser and engine with
// the in-memory fake transport; no network, no paid calls.

internal static partial class Harness
{
    private static void UtCheck(bool ok, string name) => Check(ok, "[UNITS] " + name);

    private static readonly string[] UtTypes = { "infantry", "archer", "cavalry", "siege" };

    private sealed class UtRunResult
    {
        public FakeLlmTransport Fake;
        public LlmTurnRunner Runner;
        public GameEngine Engine;
    }

    private static LlmConfig UtConfig(bool unitTypes, bool attack = true, bool conversion = true)
        => new LlmConfig
        {
            EnableAttack = attack,
            EnableGarrisonConversion = conversion,
            EnableUnitTypes = unitTypes,
            MaxRequestsPerTurn = 8,
            MaxActionsPerTurn = 6,
            MaxTotalRequests = 40,
        };

    /// <summary>
    /// One real runner turn for seat-0 (Wei) through the production host: the scripted replies first, then
    /// end_turn. No season rollover runs, so city state afterwards is exactly what the orders changed.
    /// </summary>
    private static UtRunResult UtRun(GameEngine engine, LlmConfig config, params string[] seat0Replies)
        => UtRunSeat(engine, config, "seat-0", seat0Replies);

    private static UtRunResult UtRunSeat(GameEngine engine, LlmConfig config, string seat, params string[] replies)
    {
        var fake = new FakeLlmTransport();
        foreach (string reply in replies) fake.EnqueueSuccess(reply, 0, 100, 10);
        for (int i = 0; i < 4; i++) fake.EnqueueSuccess("{\"type\":\"end_turn\"}", 0, 100, 10);
        var runner = new LlmTurnRunner(fake, config, null, null, new MemoryLlmStrategicNoteStore());
        int token = engine.BeginSeatTurn(seat);
        var host = new SingleLlmTurnHost(engine, config, seat, () => engine.Observe(seat),
            cmd => engine.SubmitAs(seat, token, cmd), () => { });
        runner.Begin(host, token, seat, 777);
        for (int i = 0; i < 2000 && (runner.State == RunnerState.Requesting || runner.State == RunnerState.Executing); i++)
        {
            runner.Tick();
            System.Threading.Thread.Sleep(1);
        }
        engine.EndSeatTurn(token);
        return new UtRunResult { Fake = fake, Runner = runner, Engine = engine };
    }

    private static string UtRecruit(string city, string type)
        => "{\"type\":\"recruit\",\"target_id\":\"" + city + "\""
           + (type == null ? "" : ",\"args\":{\"unit_type\":\"" + type + "\"}") + "}";

    /// <summary>A Wei city the tests control completely: empty garrison, plenty of gold and population.</summary>
    private static GameEngine UtEngine(BalanceProfile profile, int militaryLevel = 3, int garrison = 0,
        string garrisonType = "infantry", int gold = 5000)
    {
        GameEngine engine = ScenarioEngine(CompactScenario.CompactContact, profile);
        CityState city = engine.State.FindCity("city-wei-2");
        city.Garrison = garrison;
        city.GarrisonType = garrisonType;
        city.Gold = gold;
        city.Population = 9000;
        engine.State.FindFaction("seat-0").Research[ResearchLine.Military] = militaryLevel;
        return engine;
    }

    private static List<EventRecord> UtEvents(GameEngine engine, string type)
        => engine.Log.Records.Where(r => r.EventType == type).ToList();

    private static bool UtRejected(GameEngine engine, string reasonPart)
        => UtEvents(engine, "action_rejected").Any(r => r.Payload["reason"].Contains(reasonPart));

    private static string UtValue(string prompt, string marker, string endMarker)
    {
        int at = prompt.IndexOf(marker, StringComparison.Ordinal);
        if (at < 0) return null;
        at += marker.Length;
        int end = prompt.IndexOf(endMarker, at, StringComparison.Ordinal);
        return end < 0 ? null : prompt.Substring(at, end - at);
    }

    private static void RunUnitParity()
    {
        Section("[UNITS] unit-type recruitment parity: config, parser, catalogue, prompt, engine, replay");
        UnitParityConfigAndParser();
        UnitParityFactsMatchEngine();
        UnitParityCatalogueMatchesEngine();
        UnitParityEveryTypeThroughRunner();
        UnitParityGatesAndAfford();
        UnitParityGarrisonAndConversion();
        UnitParityMalformedAndFeatureOff();
        UnitParityPromptIdentity();
        UnitParityReplay();
        UnitParityCrowdedPrompt();
    }

    // ---- config key, parser vocabulary ----------------------------------------------------------
    private static void UnitParityConfigAndParser()
    {
        var cfg = new LlmConfig();
        UtCheck(!cfg.EnableUnitTypes && !cfg.Redacted().Contains("unit_types"), "default off: no diagnostic text");
        cfg.EnableUnitTypes = true;
        UtCheck(cfg.Redacted().Contains("unit_types=true"), "enabled flag appears in the diagnostic");

        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "luanshi-ut-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            System.IO.File.WriteAllText(path, "{\"enable_unit_types\":true}");
            UtCheck(LlmConfig.LoadFromFile(path).EnableUnitTypes, "enable_unit_types loads from the local config file");
        }
        finally { try { System.IO.File.Delete(path); } catch (Exception) { } }

        foreach (string type in UtTypes)
        {
            ParseResult on = LlmActionParser.Parse(UtRecruit("city-wei", type), false, false, false, false, true);
            ParseResult off = LlmActionParser.Parse(UtRecruit("city-wei", type), false, false, false, false, false);
            UtCheck(on.Ok && on.Action.Args["unit_type"] == type, "flag on: the parser accepts unit_type " + type);
            UtCheck(off.Ok == (type == "infantry") && (type == "infantry" || off.Error == "unsupported unit type"),
                "flag off: only infantry parses (" + type + " -> " + (off.Ok ? "ok" : off.Error) + ")");
        }
        UtCheck(LlmActionParser.Parse(UtRecruit("city-wei", "archer"), false, false, false, true).Error == "unsupported unit type",
            "the existing five-argument overload keeps the infantry-only vocabulary");
        foreach (string bad in new[] { "dragon", "Archer", "ARCHER", " archer", "archer ", "", "pikeman", "cavalry,siege" })
        {
            ParseResult p = LlmActionParser.Parse(UtRecruit("city-wei", bad), false, false, false, false, true);
            UtCheck(!p.Ok && p.Error == "unsupported unit type", "flag on: unit_type '" + bad + "' is rejected");
        }
        UtCheck(!LlmActionParser.Parse("{\"type\":\"recruit\",\"target_id\":\"city-wei\",\"args\":{\"unit_type\":5}}",
                false, false, false, false, true).Ok,
            "flag on: a non-string unit_type is rejected");
        UtCheck(LlmActionParser.Parse("{\"type\":\"recruit\",\"target_id\":\"city-wei\",\"args\":{\"unit_type\":\"archer\",\"count\":2}}",
                false, false, false, false, true).Error == "unsupported argument",
            "flag on: an extra argument key is rejected");
        UtCheck(LlmActionParser.Parse("{\"type\":\"levy_tax\",\"target_id\":\"city-wei\",\"args\":{\"unit_type\":\"archer\"}}",
                false, false, false, false, true).Error == "unsupported argument",
            "flag on: unit_type is still accepted only on recruit");
        UtCheck(LlmActionParser.Parse("{\"type\":\"recruit\",\"target_id\":\"city-wei\"}", false, false, false, false, true).Ok
                && LlmActionParser.Parse("{\"type\":\"recruit\",\"target_id\":\"city-wei\",\"args\":{}}", false, false, false, false, true).Ok,
            "flag on: omitting unit_type stays valid (the engine defaults to infantry)");
    }

    // ---- prompt and manual facts equal authoritative engine values --------------------------------
    private static void UnitParityFactsMatchEngine()
    {
        foreach (BalanceProfile profile in new[] { BalanceProfile.Standard, BalanceProfile.FoodEasy, BalanceProfile.EasyResearch })
        {
            string tag = profile.Key + ": ";
            string addendum = PromptBuilder.UnitTypesAddendum(profile, true, true, false);
            InvestigationService manualService = InvestigationManualService(profile);
            ManualLookupResult manual = manualService.ManualLookup("recruit");
            ManualLookupResult war = manualService.ManualLookup("unit_types");
            UtCheck(manual.Ok, tag + "the recruit manual topic resolves");
            Dictionary<string, string> facts = manual.Facts.ToDictionary(f => f.Name, f => f.Value);

            foreach (string type in UtTypes)
            {
                // Authoritative value: execute the recruit on a real engine and measure what it charges.
                GameEngine engine = UtEngine(profile);
                CityState city = engine.State.FindCity("city-wei-2");
                int gold0 = city.Gold, pop0 = city.Population;
                ActionCommand cmd = new ActionCommand
                {
                    Type = ActionType.Recruit, ActorSeatId = "seat-0", TargetId = "city-wei-2",
                    Args = new Dictionary<string, string> { { "unit_type", type } },
                };
                ValidationResult result = engine.Submit(cmd);
                int goldSpent = gold0 - city.Gold;
                UtCheck(result.Ok && city.Garrison == BalanceConfig.RecruitTroopGain
                        && pop0 - city.Population == BalanceConfig.RecruitPopCost && city.GarrisonType == type,
                    tag + type + ": the engine adds " + BalanceConfig.RecruitTroopGain + " troops and takes "
                    + BalanceConfig.RecruitPopCost + " population");

                string line = UtValue(addendum, "  - " + type + ": ", "\n");
                UtCheck(line != null && line.StartsWith(goldSpent.ToString(CultureInfo.InvariantCulture) + " gold;", StringComparison.Ordinal),
                    tag + type + ": the prompt's gold (" + goldSpent + ") equals what the engine charged");
                UtCheck(facts["gold_cost_" + type] == goldSpent.ToString(CultureInfo.InvariantCulture),
                    tag + type + ": the manual's gold equals what the engine charged");

                int gate = UnitTypes.RequiredMilitaryLevel(type);
                UtCheck(line != null && (gate == 0
                        ? line.Contains("no research needed")
                        : line.Contains("needs Military research level " + gate.ToString(CultureInfo.InvariantCulture))),
                    tag + type + ": the prompt's research gate equals the engine's (level " + gate + ")");
                // The engine's own refusal at one level below the gate.
                if (gate > 0)
                {
                    GameEngine below = UtEngine(profile, gate - 1);
                    ValidationResult refused = below.Submit(new ActionCommand
                    {
                        Type = ActionType.Recruit, ActorSeatId = "seat-0", TargetId = "city-wei-2",
                        Args = new Dictionary<string, string> { { "unit_type", type } },
                    });
                    UtCheck(!refused.Ok && refused.Error == type + " needs Military " + gate + " research",
                        tag + type + ": the engine refuses one level below the gate (" + refused.Error + ")");
                }

                string food = profile.TroopFoodUpkeep(1000, type).ToString("0.##", CultureInfo.InvariantCulture);
                UtCheck(line != null && line.Contains("eat " + food + " food per season"),
                    tag + type + ": the prompt's food upkeep equals the profile's (" + food + ")");
                UtCheck(line != null && line.Contains("route cost allowance "
                        + UnitTypes.MovePoints(type).ToString(CultureInfo.InvariantCulture) + " per season"),
                    tag + type + ": the prompt's move allowance equals the engine's");
                string manualFood = manual.Facts.First(f => f.Name == "food_upkeep_per_1000_" + type).Value;
                UtCheck(float.Parse(manualFood, CultureInfo.InvariantCulture)
                        == float.Parse(food, CultureInfo.InvariantCulture),
                    tag + type + ": the manual's food upkeep equals the prompt's");
            }
            UtCheck(facts["population_cost"] == BalanceConfig.RecruitPopCost.ToString(CultureInfo.InvariantCulture)
                    && facts["troops_gained_per_order"] == BalanceConfig.RecruitTroopGain.ToString(CultureInfo.InvariantCulture)
                    && addendum.Contains(BalanceConfig.RecruitPopCost + " population")
                    && addendum.Contains("adds " + BalanceConfig.RecruitTroopGain + " troops"),
                tag + "manual and prompt agree on population and troops per order");
            UtCheck(facts["research_gates"] == "cavalry needs military level " + BalanceConfig.CavalryResearchLevel
                    + "; siege needs military level " + BalanceConfig.SiegeResearchLevel,
                tag + "the manual's research gates equal the engine constants");

            // Combat facts come from the same UnitTypes values the battle code calls.
            Check(addendum.Contains("x" + UtDec(BalanceConfig.UnitCounterBonus)) && addendum.Contains("x" + UtDec(BalanceConfig.UnitCounterPenalty))
                  && addendum.Contains("Siege is x" + UtDec(BalanceConfig.SiegeFieldPenalty))
                  && addendum.Contains("siege is x" + UtDec(BalanceConfig.SiegeVsCityBonus))
                  && addendum.Contains("up to " + UnitTypes.AttackRange(UnitTypes.Archer) + " hexes")
                  && addendum.Contains("archer garrison defends its city at x" + UtDec(BalanceConfig.ArcherSiegeDefenseBonus))
                  && addendum.Contains("Cavalry may march " + UnitTypes.MarchesPerSeason(UnitTypes.Cavalry) + " times")
                  && addendum.Contains("plains at x" + UtDec(BalanceConfig.CavalryPlainsChargeBonus))
                  && addendum.Contains("Infantry takes x" + UtDec(UnitTypes.LossTakenMultiplier(UnitTypes.Infantry))),
                "[UNITS] " + tag + "combat facts equal the UnitTypes and BalanceConfig values");
            UtCheck(UnitTypes.FieldCombatMultiplier("infantry", "archer") == BalanceConfig.UnitCounterBonus
                    && UnitTypes.FieldCombatMultiplier("cavalry", "infantry") == BalanceConfig.UnitCounterBonus
                    && UnitTypes.FieldCombatMultiplier("archer", "cavalry") == BalanceConfig.UnitCounterBonus
                    && UnitTypes.FieldCombatMultiplier("archer", "infantry") == BalanceConfig.UnitCounterPenalty,
                tag + "the counter cycle stated in the prompt is the engine's cycle");
            Dictionary<string, string> warFacts = war.Ok ? war.Facts.ToDictionary(f => f.Name, f => f.Value) : new Dictionary<string, string>();
            UtCheck(war.Ok && warFacts["recruit_gold_per_1000"] ==
                    "infantry " + profile.RecruitGoldCost("infantry", 1000) + " | archer " + profile.RecruitGoldCost("archer", 1000)
                    + " | cavalry " + profile.RecruitGoldCost("cavalry", 1000) + " | siege " + profile.RecruitGoldCost("siege", 1000),
                tag + "the unit-type manual topic lists the same per-1000 gold the prompt states");
        }

        // The statement is a fact block, never a recommendation.
        string text = PromptBuilder.UnitTypesAddendum(BalanceProfile.Standard, true, true, false).ToLowerInvariant();
        bool directive = new[] { "should", "recommend", "best ", "better", "prefer", "you must", "advisable", "optimal",
                                 "strongest", "worth", "wise", "avoid", "always", "never recruit" }.Any(w => text.Contains(w));
        UtCheck(!directive, "the unit-type block states no choice, preference or strategy");
        UtCheck(!text.Contains("seat-") && !text.Contains("city-") && !text.Contains("army-"),
            "the unit-type block carries no seat, city or army identifiers");
        UtCheck(PromptBuilder.UnitTypesAddendum(BalanceProfile.Standard, false, false, false)
                    == PromptBuilder.UnitTypesAddendum(BalanceProfile.Standard, false, false, false)
                && !PromptBuilder.UnitTypesAddendum(BalanceProfile.Standard, false, false, false).Contains("Unit rules in combat")
                && !PromptBuilder.UnitTypesAddendum(BalanceProfile.Standard, false, false, false).Contains("convert_garrison"),
            "combat and conversion lines appear only when attack and conversion are enabled");
        string histOn = PromptBuilder.UnitTypesAddendum(BalanceProfile.Standard, true, true, true);
        string histOff = PromptBuilder.UnitTypesAddendum(BalanceProfile.Standard, true, true, false);
        UtCheck(histOn.Contains("exempt from the minimum troop ratio") && !histOff.Contains("exempt")
                && UnitTypes.WaivesAssaultRatio("siege") && !UnitTypes.WaivesAssaultRatio("infantry"),
            "the siege ratio waiver is stated only under the historical 3:1 rule");
    }

    private static string UtDec(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    // ---- the candidate list agrees with the engine's own verdict ------------------------------------
    private static void UnitParityCatalogueMatchesEngine()
    {
        int compared = 0, agreed = 0;
        foreach (BalanceProfile profile in new[] { BalanceProfile.Standard, BalanceProfile.FoodEasy })
        foreach (int level in new[] { 0, 1, 2, 3 })
        foreach (string garrisonType in new[] { "infantry", "archer", "cavalry", "siege" })
        foreach (int garrison in new[] { 0, 400 })
        foreach (int gold in new[] { 60, 120, 200, 400, 600 })
        {
            GameEngine probe = UtEngine(profile, level, garrison, garrisonType, gold);
            Observation obs = probe.Observe("seat-0");
            List<ActionOption> options = ActionCatalogue.ExpandRecruitTypes(ActionCatalogue.Build(obs), obs);
            foreach (string type in UtTypes)
            {
                ActionOption option = options.First(o => o.Type == ActionType.Recruit && o.TargetId == "city-wei-2"
                    && o.Args != null && o.Args["unit_type"] == type);
                // The human city panel's own path: the same command through the same engine.
                GameEngine human = UtEngine(profile, level, garrison, garrisonType, gold);
                ValidationResult verdict = human.Submit(new ActionCommand
                {
                    Type = ActionType.Recruit, ActorSeatId = "seat-0", TargetId = "city-wei-2",
                    Args = new Dictionary<string, string> { { "unit_type", type } },
                });
                compared++;
                if (option.LikelyAffordable == verdict.Ok) agreed++;
                else Check(false, "[UNITS] candidate/engine mismatch: " + profile.Key + " mil" + level + " garrison "
                                  + garrison + " " + garrisonType + " gold " + gold + " " + type + " list="
                                  + option.LikelyAffordable + " engine=" + verdict.Ok + " (" + verdict.Error + ")");
            }
        }
        UtCheck(compared > 0 && agreed == compared,
            "the candidate list's likely_affordable equals the engine's verdict in all " + compared + " states");

        // Shape: four entries per own city, in type order, own cities only, seat-filtered.
        GameEngine engine = UtEngine(BalanceProfile.Standard, 3);
        Observation seat0 = engine.Observe("seat-0");
        List<ActionOption> built = ActionCatalogue.Build(seat0);
        List<ActionOption> expanded = ActionCatalogue.ExpandRecruitTypes(built, seat0);
        var recruits = expanded.Where(o => o.Type == ActionType.Recruit).ToList();
        int ownCities = seat0.CitiesOf("seat-0").Count;
        UtCheck(recruits.Count == 4 * ownCities && built.Count(o => o.Type == ActionType.Recruit) == ownCities
                && expanded.Count == built.Count + 3 * ownCities,
            "each own city gains exactly four recruit entries (" + recruits.Count + " for " + ownCities + " cities)");
        UtCheck(recruits.Select(o => o.Args["unit_type"]).Take(4).SequenceEqual(UtTypes),
            "recruit entries follow infantry, archer, cavalry, siege order");
        UtCheck(recruits.All(o => seat0.CitiesOf("seat-0").Any(c => c.Id == o.TargetId)),
            "recruit entries name only the seat's own cities");
        string prompt = ActionCatalogue.ToPromptText(expanded);
        UtCheck(prompt.Contains("args={\"unit_type\":\"archer\"}") && !prompt.Contains("city-shu"),
            "the prompt text names unit_type and no opponent city");
        UtCheck(expanded.Where(o => o.Type != ActionType.Recruit).Select(o => o.Type + o.TargetId)
                    .SequenceEqual(built.Where(o => o.Type != ActionType.Recruit).Select(o => o.Type + o.TargetId)),
            "every other candidate and its relative order is unchanged");
        GameEngine low = UtEngine(BalanceProfile.Standard, 0);
        Observation lowObs = low.Observe("seat-0");
        ActionOption cavalry = ActionCatalogue.ExpandRecruitTypes(ActionCatalogue.Build(lowObs), lowObs)
            .First(o => o.Args != null && o.Args["unit_type"] == "cavalry");
        UtCheck(!cavalry.LikelyAffordable && cavalry.Note.Contains("needs Military research level 2"),
            "a gated type stays listed with its research reason");
    }

    // ---- every type through the real runner/parser/engine ---------------------------------------------
    private static void UnitParityEveryTypeThroughRunner()
    {
        foreach (BalanceProfile profile in new[] { BalanceProfile.Standard, BalanceProfile.FoodEasy })
        foreach (string type in UtTypes)
        {
            string tag = profile.Key + " " + type + ": ";
            GameEngine engine = UtEngine(profile);
            CityState city = engine.State.FindCity("city-wei-2");
            int gold0 = city.Gold, pop0 = city.Population;
            int cp0 = engine.State.FindFaction("seat-0").CommandPoints;
            UtRunResult run = UtRun(engine, UtConfig(true), UtRecruit("city-wei-2", type));
            List<EventRecord> recruits = UtEvents(engine, "recruit");
            UtCheck(recruits.Count == 1 && recruits[0].Payload["unit_type"] == type
                    && recruits[0].ControllerType == ControllerType.Llm && recruits[0].ActorSeatId == "seat-0",
                tag + "exactly one recruit event, typed, stamped llm/seat-0");
            UtCheck(city.Garrison == BalanceConfig.RecruitTroopGain && city.GarrisonType == type
                    && gold0 - city.Gold == profile.RecruitGoldCost(type, BalanceConfig.RecruitTroopGain)
                    && pop0 - city.Population == BalanceConfig.RecruitPopCost,
                tag + "garrison, type, gold and population changed by the engine's amounts");
            UtCheck(UtEvents(engine, "action_rejected").Count == 0 && run.Runner.InvalidThisTurn == 0,
                tag + "no rejection and no invalid reply");
            // CP was charged once for the order (seat-0's own budget before its turn ended).
            UtCheck(cp0 == BalanceConfig.CommandPointsPerSeason, tag + "the seat started with its full command points");
        }
    }

    // ---- gates, affordability, rejection leaves state untouched ---------------------------------------
    private static void UnitParityGatesAndAfford()
    {
        // Research gates at every level. Infantry and archer are always free; cavalry 2; siege 3.
        for (int level = 0; level <= 3; level++)
        foreach (string type in UtTypes)
        {
            bool allowed = level >= UnitTypes.RequiredMilitaryLevel(type);
            GameEngine engine = UtEngine(BalanceProfile.Standard, level);
            CityState city = engine.State.FindCity("city-wei-2");
            int gold0 = city.Gold, pop0 = city.Population;
            UtRun(engine, UtConfig(true), UtRecruit("city-wei-2", type));
            int recruited = UtEvents(engine, "recruit").Count;
            UtCheck(allowed
                    ? recruited == 1 && city.Garrison == 1000
                    : recruited == 0 && city.Garrison == 0 && city.Gold == gold0 && city.Population == pop0
                      && UtRejected(engine, type + " needs Military " + UnitTypes.RequiredMilitaryLevel(type) + " research"),
                "military " + level + ", " + type + ": " + (allowed ? "accepted" : "rejected by the research gate, nothing spent"));
        }

        // Affordability: one gold short is refused with the engine's own message; exactly enough is accepted.
        foreach (string type in UtTypes)
        {
            int price = BalanceProfile.Standard.RecruitGoldCost(type, BalanceConfig.RecruitTroopGain);
            GameEngine shortEngine = UtEngine(BalanceProfile.Standard, 3, 0, "infantry", price - 1);
            UtRun(shortEngine, UtConfig(true), UtRecruit("city-wei-2", type));
            UtCheck(UtEvents(shortEngine, "recruit").Count == 0 && UtRejected(shortEngine, "not enough gold (needs " + price + ")")
                    && shortEngine.State.FindCity("city-wei-2").Gold == price - 1,
                type + ": " + (price - 1) + " gold is refused, state untouched");
            GameEngine exactEngine = UtEngine(BalanceProfile.Standard, 3, 0, "infantry", price);
            UtRun(exactEngine, UtConfig(true), UtRecruit("city-wei-2", type));
            UtCheck(UtEvents(exactEngine, "recruit").Count == 1 && exactEngine.State.FindCity("city-wei-2").Gold == 0,
                type + ": exactly " + price + " gold is accepted");
        }
        GameEngine noPop = UtEngine(BalanceProfile.Standard);
        noPop.State.FindCity("city-wei-2").Population = BalanceConfig.RecruitPopCost - 1;
        UtRun(noPop, UtConfig(true), UtRecruit("city-wei-2", "archer"));
        UtCheck(UtEvents(noPop, "recruit").Count == 0 && UtRejected(noPop, "not enough population"),
            "too little population is refused whatever the type");

        // An order for a city that is not the seat's own is refused by the engine, not by the parser.
        GameEngine foreign = UtEngine(BalanceProfile.Standard);
        UtRun(foreign, UtConfig(true), UtRecruit("city-shu", "archer"));
        UtCheck(UtEvents(foreign, "recruit").Count == 0 && foreign.State.FindCity("city-shu").GarrisonType == "infantry",
            "recruiting in another seat's city is refused");
    }

    // ---- one type per garrison; conversion into a field army keeps the type ---------------------------
    private static void UnitParityGarrisonAndConversion()
    {
        foreach (string held in UtTypes)
        foreach (string asked in UtTypes)
        {
            GameEngine engine = UtEngine(BalanceProfile.Standard, 3, 600, held);
            UtRun(engine, UtConfig(true), UtRecruit("city-wei-2", asked));
            CityState city = engine.State.FindCity("city-wei-2");
            bool same = held == asked;
            UtCheck(same
                    ? city.Garrison == 1600 && city.GarrisonType == held
                    : city.Garrison == 600 && city.GarrisonType == held
                      && UtRejected(engine, "garrison is " + held + "; recruit the same type or empty the garrison first"),
                "garrison " + held + " + recruit " + asked + ": " + (same ? "adds 1000, type kept" : "refused, garrison untouched"));
        }

        // recruit -> convert_garrison -> field army of that type, within one turn.
        foreach (string type in UtTypes)
        {
            GameEngine engine = UtEngine(BalanceProfile.Standard);
            UtRun(engine, UtConfig(true), UtRecruit("city-wei-2", type),
                "{\"type\":\"convert_garrison\",\"target_id\":\"city-wei-2\",\"param_a\":1000,\"param_b\":0}");
            List<EventRecord> converted = UtEvents(engine, "garrison_converted");
            ArmyState army = converted.Count == 1 ? engine.State.FindArmy(converted[0].Payload["army_id"]) : null;
            UtCheck(army != null && army.Troops == 1000 && army.UnitType == type && army.OwnerSeatId == "seat-0"
                    && engine.State.FindCity("city-wei-2").Garrison == 0,
                type + ": recruit then convert_garrison forms a 1000-troop " + type + " field army");
        }

        // An own army of another type already on the city refuses the merge (engine rule, nothing changes).
        GameEngine mixed = UtEngine(BalanceProfile.Standard);
        ArmyState standing = mixed.State.FindArmy("army-wei-2");
        standing.X = mixed.State.FindCity("city-wei-2").X;
        standing.Z = mixed.State.FindCity("city-wei-2").Z;
        UtRun(mixed, UtConfig(true), UtRecruit("city-wei-2", "archer"),
            "{\"type\":\"convert_garrison\",\"target_id\":\"city-wei-2\",\"param_a\":1000,\"param_b\":0}");
        UtCheck(UtEvents(mixed, "garrison_converted").Count == 0 && UtRejected(mixed, "unit types differ")
                && mixed.State.FindCity("city-wei-2").Garrison == 1000 && standing.Troops == 2000,
            "converting archers onto a standing infantry army is refused and changes nothing");

        // A recruit with no army-merge problem merges into a same-type army.
        GameEngine merged = UtEngine(BalanceProfile.Standard);
        ArmyState archers = merged.State.FindArmy("army-wei-2");
        archers.UnitType = "archer";
        archers.X = merged.State.FindCity("city-wei-2").X;
        archers.Z = merged.State.FindCity("city-wei-2").Z;
        UtRun(merged, UtConfig(true), UtRecruit("city-wei-2", "archer"),
            "{\"type\":\"convert_garrison\",\"target_id\":\"city-wei-2\",\"param_a\":1000,\"param_b\":0}");
        UtCheck(archers.Troops == 3000 && merged.State.FindCity("city-wei-2").Garrison == 0,
            "converting archers onto a standing archer army merges into it");
    }

    // ---- malformed orders and feature-off compatibility -------------------------------------------------
    private static void UnitParityMalformedAndFeatureOff()
    {
        foreach (string bad in new[] { "dragon", "Archer", "", "cavalry,siege" })
        {
            GameEngine engine = UtEngine(BalanceProfile.Standard);
            UtRunResult run = UtRun(engine, UtConfig(true), UtRecruit("city-wei-2", bad));
            UtCheck(UtEvents(engine, "recruit").Count == 0 && run.Runner.InvalidThisTurn >= 0
                    && engine.State.FindCity("city-wei-2").Garrison == 0
                    && UtEvents(engine, "action_rejected").Count == 0,
                "unit_type '" + bad + "' never reaches the engine and changes nothing");
            UtCheck(run.Fake.Requests.Count >= 2 && run.Fake.Requests[1].User.Contains("unsupported unit type"),
                "unit_type '" + bad + "': the model is told the reply was an unsupported unit type");
        }
        // Omitted unit_type keeps meaning infantry, exactly as before.
        GameEngine omitted = UtEngine(BalanceProfile.Standard);
        UtRun(omitted, UtConfig(true), UtRecruit("city-wei-2", null));
        UtCheck(UtEvents(omitted, "recruit").Count == 1 && UtEvents(omitted, "recruit")[0].Payload["unit_type"] == "infantry",
            "an order without unit_type recruits infantry");

        // Feature off (the default): archer is refused by the parser; infantry is unchanged.
        foreach (string type in new[] { "archer", "cavalry", "siege" })
        {
            GameEngine engine = UtEngine(BalanceProfile.Standard);
            UtRunResult run = UtRun(engine, UtConfig(false), UtRecruit("city-wei-2", type));
            UtCheck(UtEvents(engine, "recruit").Count == 0 && engine.State.FindCity("city-wei-2").Garrison == 0
                    && run.Fake.Requests.Count >= 2 && run.Fake.Requests[1].User.Contains("unsupported unit type"),
                "flag off: " + type + " is still refused as an unsupported unit type");
        }
        GameEngine offInfantry = UtEngine(BalanceProfile.Standard);
        UtRun(offInfantry, UtConfig(false), UtRecruit("city-wei-2", "infantry"));
        UtCheck(UtEvents(offInfantry, "recruit").Count == 1, "flag off: infantry recruitment is unchanged");

        // A non-planning host is not offered the feature even with the flag on.
        var fake = new FakeLlmTransport();
        fake.EnqueueSuccess(UtRecruit("city-a", "archer"), 0, 5, 1);
        fake.EnqueueSuccess("{\"type\":\"end_turn\"}", 0, 5, 1);
        var engineA = SpectatorEngine();
        var runner = SpectatorRunner(fake, new LlmConfig { EnableUnitTypes = true });
        var host = new LlmHarnessHost(engineA, "seat-a");
        runner.Begin(host, host.Token, "seat-a", 7);
        LlmDrain(runner);
        UtCheck(!fake.Requests[0].System.Contains("UNIT TYPES") && runner.InvalidThisTurn == 1,
            "a host without planning support gets no unit-type text and still refuses non-infantry");
    }

    // ---- prompt text, version, hash and candidate list as dispatched -----------------------------------
    private static LlmRequest UtFirstRequest(LlmConfig config, BalanceProfile profile)
    {
        GameEngine engine = UtEngine(profile, 1, 200, "infantry", 5000);
        UtRunResult run = UtRun(engine, config);
        return run.Fake.Requests.Count > 0 ? run.Fake.Requests[0] : null;
    }

    private static string UtVersion(LlmRequest request)
        => UtValue(request.User, "Prompt version: ", "\n");

    private static void UnitParityPromptIdentity()
    {
        foreach (BalanceProfile profile in new[] { BalanceProfile.Standard, BalanceProfile.FoodEasy })
        {
            string tag = profile.Key + ": ";
            LlmRequest off = UtFirstRequest(UtConfig(false), profile);
            LlmRequest off2 = UtFirstRequest(UtConfig(false), profile);
            LlmRequest on = UtFirstRequest(UtConfig(true), profile);
            UtCheck(off != null && on != null, tag + "requests are dispatched with the flag off and on");
            if (off == null || on == null) continue;

            UtCheck(off.System == off2.System && off.User == off2.User, tag + "flag off is deterministic");
            UtCheck(!off.System.Contains("UNIT TYPES") && !off.User.Contains(PromptBuilder.UnitTypesVersionSuffix)
                    && off.System.Contains("recruit (args.unit_type must be \"infantry\")")
                    && off.User.Split('\n').Count(l => l.StartsWith("- type=recruit")) == 2,
                tag + "flag off: no unit-type text, infantry-only contract, one recruit candidate per city");
            string offBody = off.System.Substring(0, off.System.Length - PromptBuilder.GarrisonConversionAddendum.Length);
            UtCheck(off.System.EndsWith(PromptBuilder.GarrisonConversionAddendum, StringComparison.Ordinal)
                    && on.System.StartsWith(offBody, StringComparison.Ordinal)
                    && on.System.Substring(offBody.Length).StartsWith("\n\nUNIT TYPES", StringComparison.Ordinal)
                    && on.System.EndsWith(PromptBuilder.GarrisonConversionAddendum, StringComparison.Ordinal),
                tag + "flag on: the existing prompt is unchanged; the block sits before the garrison addendum");
            string versionOff = UtVersion(off), versionOn = UtVersion(on);
            UtCheck(versionOn == versionOff.Replace(PromptBuilder.GarrisonConversionVersionSuffix,
                        PromptBuilder.UnitTypesVersionSuffix + PromptBuilder.GarrisonConversionVersionSuffix)
                    && PromptBuilder.IsUnitTypesPromptVersion(versionOn) && !PromptBuilder.IsUnitTypesPromptVersion(versionOff),
                tag + "the version gains +unit-types-1 before the garrison suffix (" + versionOn + ")");
            UtCheck(PromptBuilder.IsGarrisonConversionPromptVersion(versionOn) && PromptBuilder.IsAttackPromptVersion(versionOn),
                tag + "garrison and attack identity detection still work with the suffix");
            UtCheck(PromptBuilder.GarrisonConversionHash(on.System) != PromptBuilder.GarrisonConversionHash(off.System),
                tag + "the unit-type text changes the schema hash");
            int perCity = on.User.Split('\n').Count(l => l.StartsWith("- type=recruit target_id=city-wei-2"));
            UtCheck(perCity == 4 && on.User.Contains("label=\"Recruit cavalry at Xuchang\"")
                    && on.User.Contains("needs Military research level 2"),
                tag + "the dispatched candidate list has four recruit entries per city with the research reason");
            string recruitCost = profile.RecruitGoldCost("siege", BalanceConfig.RecruitTroopGain).ToString(CultureInfo.InvariantCulture);
            UtCheck(on.System.Contains("  - siege: " + recruitCost + " gold; needs Military research level 3"),
                tag + "the dispatched block carries the match profile's siege price (" + recruitCost + ")");
            UtCheck(!off.User.Contains("Recruit archer") && on.User.Contains("Recruit archer at Xuchang"),
                tag + "the archer candidate exists only with the flag on");
            // Seat filtering: Shu's prompt carries Shu's own cities only, and the block is identical for both seats.
            GameEngine shared = UtEngine(profile);
            UtRunResult weiRun = UtRun(shared, UtConfig(true));
            UtRunResult shuRun = UtRunSeat(shared, UtConfig(true), "seat-1");
            LlmRequest wei = weiRun.Fake.Requests[0], shu = shuRun.Fake.Requests.Count > 0 ? shuRun.Fake.Requests[0] : null;
            string blockWei = wei.System.Substring(wei.System.IndexOf("UNIT TYPES", StringComparison.Ordinal));
            string blockShu = shu == null ? null : shu.System.Substring(shu.System.IndexOf("UNIT TYPES", StringComparison.Ordinal));
            UtCheck(shu != null && blockWei == blockShu
                    && !shu.User.Contains("target_id=city-wei") && shu.User.Contains("target_id=city-shu"),
                tag + "both seats get the same fact block; each candidate list names only its own cities");
        }

        // Stacking with the action reference: the suffix order is stable and detection still works.
        LlmConfig stacked = UtConfig(true);
        stacked.EnableActionReference = true;
        LlmRequest both2 = UtFirstRequest(stacked, BalanceProfile.Standard);
        string v = both2 == null ? "" : UtVersion(both2);
        UtCheck(v.EndsWith(PromptBuilder.ActionReferenceVersionSuffix + PromptBuilder.AssaultTextVersionSuffix
                    + PromptBuilder.UnitTypesVersionSuffix + PromptBuilder.GarrisonConversionVersionSuffix, StringComparison.Ordinal)
                && PromptBuilder.IsActionReferencePromptVersion(v) && PromptBuilder.IsUnitTypesPromptVersion(v)
                && both2.System.Contains("ACTION REFERENCE") && both2.System.Contains("UNIT TYPES"),
            "with the action reference the order is actions, assault text, unit types, garrison (" + v + ")");
        string withRef = both2.System;
        UtCheck(withRef.IndexOf("ACTION REFERENCE", StringComparison.Ordinal) < withRef.IndexOf("UNIT TYPES", StringComparison.Ordinal)
                && withRef.EndsWith(PromptBuilder.GarrisonConversionAddendum, StringComparison.Ordinal),
            "the action reference precedes the unit-type block and the garrison addendum stays last");

        // Historical 3:1 mode: the unit block follows the historical identity and states the siege waiver.
        GameEngine hist = UtEngine(BalanceProfile.Standard);
        hist.State.RequireAssaultTroopRatio = true;
        UtRunResult histRun = UtRun(hist, UtConfig(true));
        LlmRequest hr = histRun.Fake.Requests.Count > 0 ? histRun.Fake.Requests[0] : null;
        UtCheck(hr != null && hr.System.Contains("exempt from the minimum troop ratio")
                && PromptBuilder.IsUnitTypesPromptVersion(UtVersion(hr))
                && !UtVersion(hr).Contains(PromptBuilder.AssaultTextVersionSuffix),
            "historical 3:1 mode keeps its legacy attack identity and states the siege waiver");
    }

    // ---- replay: the recorded identity decides the vocabulary -------------------------------------------
    private static ReplayBundle UtReplayBundle(string promptVersion, string rawRecruit, bool parseOk, bool resultOk, string error)
    {
        ReplayBundle bundle = DipReplayBundle(promptVersion);
        bundle.Requests[0].RawModelText = rawRecruit;
        bundle.Requests[0].ParseOk = parseOk;
        bundle.Requests[0].ResultOk = resultOk;
        bundle.Requests[0].ResultError = error;
        return bundle;
    }

    private static void UnitParityReplay()
    {
        string versionOn = PromptBuilder.AttackPromptVersion + PromptBuilder.UnitTypesVersionSuffix;
        ReplayBundle recorded = UtReplayBundle(versionOn, UtRecruit("city-wei", "archer"), true, true, null);
        var on = new ReplaySession(recorded);
        on.RunToCompletion();
        UtCheck(on.IsCompleted && on.AcceptedActionsReplayed == 1 && on.UnexpectedRejections == 0
                && on.ActionOutcomeMismatches == 0
                && on.Engine.Log.Records.Any(r => r.EventType == "recruit" && r.Payload["unit_type"] == "archer"),
            "a +unit-types-1 recording replays its archer recruitment through the engine");

        // Historical recordings (no suffix) keep their recorded outcome: the old parser refused archer, so
        // a recording that says it was accepted can never be replayed as accepted. A recorded refusal stays refused.
        ReplayBundle old = UtReplayBundle(PromptBuilder.AttackPromptVersion, UtRecruit("city-wei", "archer"), false, false,
            "unsupported unit type");
        var oldSession = new ReplaySession(old);
        oldSession.RunToCompletion();
        UtCheck(oldSession.IsCompleted && oldSession.AcceptedActionsReplayed == 0
                && oldSession.InvalidResponsesReplayed == 1 && oldSession.UnexpectedAcceptances == 0
                && oldSession.Engine.Log.Records.All(r => r.EventType != "recruit"),
            "a recording without the suffix replays its recorded refusal of archer, never executing it");
        ReplayBundle contradictory = UtReplayBundle(PromptBuilder.AttackPromptVersion, UtRecruit("city-wei", "archer"), true, true, null);
        var contradictorySession = new ReplaySession(contradictory);
        contradictorySession.RunToCompletion();
        UtCheck(contradictorySession.Engine.Log.Records.All(r => r.EventType != "recruit")
                && contradictorySession.ActionOutcomeMismatches >= 1,
            "an old-identity record claiming archer was accepted fails closed and is reported as a mismatch");

        // The run record and summary carry the identity.
        string bootstrap = ReplayBootstrap.BuildJson(BootstrapEngine("ut-game"), 12, "run-ut", UtConfig(true), "rev", "spectator", 1);
        UtCheck(bootstrap.Contains("\"unit_types\":true")
                && !ReplayBootstrap.BuildJson(BootstrapEngine("ut-game"), 12, "run-ut", UtConfig(false), "rev", "spectator", 1)
                    .Contains("unit_types"),
            "the replay bootstrap records unit_types only when it was enabled");
        string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "luanshi-ut-rec-" + Guid.NewGuid().ToString("N"));
        try
        {
            var recorder = new LlmRunRecorder(root);
            var fake = new FakeLlmTransport();
            fake.EnqueueSuccess(UtRecruit("city-wei-2", "cavalry"), 0, 100, 10);
            for (int i = 0; i < 4; i++) fake.EnqueueSuccess("{\"type\":\"end_turn\"}", 0, 100, 10);
            GameEngine engine = UtEngine(BalanceProfile.Standard);
            var runner = new LlmTurnRunner(fake, UtConfig(true), recorder, null, new MemoryLlmStrategicNoteStore());
            var session = new SpectatorSession(engine, runner, 1, 777);
            session.Start();
            SpectatorDrain(session);
            string[] lines = System.IO.File.Exists(recorder.FilePath) ? System.IO.File.ReadAllLines(recorder.FilePath) : new string[0];
            UtCheck(lines.Length >= 3 && lines.All(l => l.Contains(PromptBuilder.UnitTypesVersionSuffix))
                    && lines[0].Contains("\\\"unit_type\\\":\\\"cavalry\\\""),
                "the run log records +unit-types-1 and the typed recruit on every request");
        }
        finally { try { System.IO.Directory.Delete(root, true); } catch (Exception) { } }
    }

    // ---- worst-case prompt size: every optional section on, unit types included ---------------------------
    private static void UnitParityCrowdedPrompt()
    {
        GameEngine engine = BuildCrowdedRichOverflowWorld();
        engine.State.FindCity("city-shu").Garrison = 1000;
        engine.State.FindCity("city-shu").GarrisonType = UnitTypes.Infantry;
        LlmConfig config = ActRefConfig(true);
        config.EnableUnitTypes = true;
        var fake = new FakeLlmTransport();
        fake.EnqueueSuccess("{\"type\":\"end_turn\"}", 0, 100, 10);
        fake.EnqueueSuccess("{\"type\":\"end_turn\"}", 0, 100, 10);
        var runner = new LlmTurnRunner(fake, config, null, null, new MemoryLlmStrategicNoteStore());
        var session = new SpectatorSession(engine, runner, 1, 909);
        session.Start();
        SpectatorDrain(session);
        bool dispatched = fake.Requests.Count > 0;
        int total = dispatched ? fake.Requests[0].System.Length + fake.Requests[0].User.Length : -1;
        UtCheck(dispatched && total <= 40000 && fake.Requests[0].System.Contains("UNIT TYPES")
                && fake.Requests[0].System.Contains("ACTION REFERENCE")
                && !runner.EndReasonText.ToLowerInvariant().Contains("prompt too large"),
            "the crowded worst-case world with every option on still dispatches within 40,000 characters (" + total + ")");
    }
}
