using System;
using System.Collections.Generic;
using LuanShi.Engine;

// [LS-018] Observation-only investigation foundation.
//
// Pins the read-only API contract: discoverable English topics, current-observation entity
// inspection, own filtered history search, own-seat memory recall, and the privacy/non-mutation
// guarantees that make it safe to hand to a later runner slice. These tests use only the pure C#
// harness: no provider, no network, no Unity editor.

internal static partial class Harness
{
    private static void RunInvestigation()
    {
        Section("[new] LS-018 read-only investigation foundation");

        InvestigationCatalogueAndManual();
        InvestigationEntityInspection();
        InvestigationHistorySearch();
        InvestigationMemoryRecall();
        InvestigationHiddenWorldParity();
        InvestigationBoundsAndNonMutation();
        InvestigationContextFailClosed();
    }

    // ---- fixtures ----------------------------------------------------------

    /// <summary>
    /// Same visible world in both variants. The only difference is an out-of-sight enemy city/army,
    /// which must be absent from seat-a's observation and from every tool response.
    /// </summary>
    private static GameEngine InvestigationEngine(int seed, bool includeHidden, out Observation observation)
    {
        GameEngine engine = Build(seed, 2, morale: 100, food: 5000, garrison: 500, sight: false);
        engine.State.Armies.Add(new ArmyState
        {
            Id = "army-a", Name = "A Army", OwnerSeatId = "seat-a", Troops = 2000,
            UnitType = UnitTypes.Infantry, X = 1, Z = 0, Morale = 60,
        });
        // A visible neutral city with a null name exercises nullable field handling.
        engine.State.Cities.Add(new CityState
        {
            Id = "city-n", Name = null, X = 2, Z = 0, OwnerSeatId = null,
            Population = 100, Food = 50, Gold = 0, Garrison = 0, Morale = 50,
        });

        if (includeHidden)
        {
            engine.State.Cities.Add(new CityState
            {
                Id = "city-b", Name = "Hidden B", X = 9, Z = 0, OwnerSeatId = "seat-b",
                Population = 9999, Food = 9999, Gold = 9999, Garrison = 9999, Morale = 100,
            });
            engine.State.Armies.Add(new ArmyState
            {
                Id = "army-b", Name = "Hidden B Army", OwnerSeatId = "seat-b", Troops = 7777,
                UnitType = UnitTypes.Cavalry, X = 9, Z = 0, Morale = 100,
            });
        }

        observation = engine.Observe("seat-a");
        // A scouted-only report is knowledge the seat already has, but this slice deliberately does
        // not promote stale reports into current-observation facts. Both world variants carry it.
        observation.ScoutedCities.Add(new ScoutedCityView
        {
            CityId = "city-scouted", Name = "Old Report", OwnerSeatId = "seat-b", X = 8, Z = 0,
            UntilSeason = 1, Accuracy = BalanceConfig.ScoutAccuracyBand,
            GarrisonBand = "few", FoodBand = "few",
        });
        return engine;
    }

    private static GameEngine InvestigationHistoryEngine(out Observation observation)
    {
        GameEngine engine = InvestigationEngine(1811, false, out _);
        engine.Submit(new ActionCommand
        {
            Type = ActionType.LevyTax, ActorSeatId = "seat-a", TargetId = "city-a",
        });
        engine.Submit(new ActionCommand
        {
            Type = ActionType.Train, ActorSeatId = "seat-a", TargetId = "city-a",
        });
        observation = engine.Observe("seat-a");
        return engine;
    }

    private static InvestigationService InvestigationServiceFor(GameEngine engine, Observation observation,
        IReadOnlyList<LlmMemory> memories = null, InvestigationOptions options = null)
    {
        return new InvestigationService(engine.State.GameId, observation.SeatId, observation, memories, options);
    }

    private static LlmMemory InvestigationMemory(string id, string gameId, string seatId, string type,
        string content, int season, int importance = 1, int confidence = 1,
        string scope = LlmMemoryScope.Game, string visibility = LlmMemoryVisibility.Seat,
        string supersededBy = null, int schemaVersion = LlmMemory.CurrentSchemaVersion)
    {
        return new LlmMemory
        {
            Id = id, GameId = gameId, SeatId = seatId, Type = type, Content = content,
            Source = "turn_result", Season = season, Importance = importance,
            Confidence = confidence, Scope = scope, Visibility = visibility,
            SupersededBy = supersededBy, SchemaVersion = schemaVersion,
        };
    }

    private static string InvestigationFact(ManualLookupResult result, string name)
    {
        foreach (ManualFact fact in result.Facts)
            if (fact.Name == name) return fact.Value;
        return null;
    }

    private static bool InvestigationParses(string json)
        => StrictJson.TryParse(json, out _, out _);

    private static int[] InvestigationEventOrder(Observation observation)
    {
        var seqs = new int[observation.Events.Count];
        for (int i = 0; i < observation.Events.Count; i++) seqs[i] = observation.Events[i].Seq;
        return seqs;
    }

    private static bool InvestigationSameOrder(int[] left, int[] right)
    {
        if (left == null || right == null || left.Length != right.Length) return false;
        for (int i = 0; i < left.Length; i++) if (left[i] != right[i]) return false;
        return true;
    }

    // ---- discovery and rules ----------------------------------------------

    private static void InvestigationCatalogueAndManual()
    {
        InvestigationToolCatalogueResult catalogue = InvestigationService.Catalogue();
        Check(catalogue.Ok && catalogue.Tools.Count == 4, "[LS-018] tool catalogue lists four operations");
        Check(catalogue.Tools[0].Name == "manual_lookup"
              && catalogue.Tools[1].Name == "inspect_entity"
              && catalogue.Tools[2].Name == "search_history"
              && catalogue.Tools[3].Name == "recall_memory",
            "[LS-018] tool names are stable and discoverable");
        bool allReadOnly = true;
        foreach (InvestigationToolDescriptor tool in catalogue.Tools)
            allReadOnly = allReadOnly && tool.ReadOnly && !tool.MutatesGameState;
        Check(allReadOnly, "[LS-018] every advertised tool is explicitly read-only");
        Check(InvestigationParses(catalogue.ToJson()), "[LS-018] catalogue JSON is valid");

        GameEngine engine = InvestigationEngine(1821, false, out Observation observation);
        InvestigationService service = InvestigationServiceFor(engine, observation);

        ManualLookupResult index = service.ManualLookup("");
        Check(index.Ok && index.Topics.Count == 9, "[LS-018] empty topic returns the nine-topic index");
        Check(index.CommandPointCost == -1 && index.Facts.Count == 0,
            "[LS-018] the index is not itself an action");
        Check(InvestigationParses(index.ToJson()), "[LS-018] topic index JSON is valid");

        ManualLookupResult turn = service.ManualLookup("command points");
        Check(turn.Ok && turn.TopicId == "turn_and_command_points",
            "[LS-018] alias search finds the turn/command-point topic");
        Check(turn.RulesVersion == InvestigationOptions.RulesVersion && turn.Implemented,
            "[LS-018] rules answers carry an explicit version and implemented status");
        Check(InvestigationFact(turn, "command_points_per_season")
              == BalanceConfig.CommandPointsPerSeason.ToString(),
            "[LS-018] command points match BalanceConfig");
        Check(InvestigationFact(turn, "attack_city_cost")
              == BalanceConfig.CommandPointsPerAttackCity.ToString()
              && InvestigationFact(turn, "talk_cost") == BalanceConfig.CommandPointsPerTalk.ToString(),
            "[LS-018] action costs match BalanceConfig");

        ManualLookupResult fog = service.ManualLookup("fog");
        Check(fog.Ok && fog.TopicId == "fog_of_war", "[LS-018] fuzzy topic search finds fog rules");
        Check(InvestigationFact(fog, "own_city_sight_radius") == BalanceConfig.FogCityRange.ToString()
              && InvestigationFact(fog, "own_army_sight_radius") == BalanceConfig.FogArmyRange.ToString()
              && InvestigationFact(fog, "watchtower_sight_radius") == BalanceConfig.FogWatchTowerRange.ToString(),
            "[LS-018] fog radii match BalanceConfig");

        ManualLookupResult tax = service.ManualLookup(ActionType.LevyTax);
        Check(tax.Ok && tax.LlmActionExposed && tax.CommandPointCost == BalanceConfig.CommandPointsPerOrder,
            "[LS-018] domestic action is marked as currently exposed to the LLM");
        Check(InvestigationFact(tax, "gold_gain") == "+" + BalanceConfig.TaxLevyAmount
              && InvestigationFact(tax, "morale_change") == "-" + BalanceConfig.TaxLevyMoraleCost,
            "[LS-018] levy-tax effects match BalanceConfig");

        ManualLookupResult recruit = service.ManualLookup("recruit");
        Check(recruit.Ok
              && InvestigationFact(recruit, "gold_cost_infantry") == BalanceConfig.RecruitGoldCostInfantry.ToString()
              && InvestigationFact(recruit, "gold_cost_cavalry") == BalanceConfig.RecruitGoldCostCavalry.ToString()
              && InvestigationFact(recruit, "gold_cost_siege") == BalanceConfig.RecruitGoldCostSiege.ToString(),
            "[LS-018] recruit costs match BalanceConfig");

        foreach (string domestic in new[]
        {
            ActionType.LevyTax, ActionType.LightenLabor, ActionType.Reclaim, ActionType.Recruit,
            ActionType.Train, ActionType.Fortify, ActionType.Relief,
        })
        {
            ManualLookupResult action = service.ManualLookup(domestic);
            Check(action.Ok && action.LlmActionExposed && action.TopicId == domestic,
                "[LS-018] the seven domestic actions are individually discoverable: " + domestic);
        }

        ManualLookupResult unsupported = service.ManualLookup("combat");
        Check(!unsupported.Ok && unsupported.ErrorCode == "unsupported_topic"
              && unsupported.Topics.Count == 9,
            "[LS-018] unsupported rules return an explicit bounded index, not invented content");
        Check(InvestigationParses(unsupported.ToJson()), "[LS-018] unsupported-topic JSON is valid");

        ManualLookupResult ambiguous = service.ManualLookup("re");
        Check(!ambiguous.Ok && ambiguous.ErrorCode == "ambiguous_topic",
            "[LS-018] ambiguous topic searches fail with the index instead of guessing");

        ManualLookupResult toolong = service.ManualLookup(new string('x', 65));
        Check(!toolong.Ok && toolong.ErrorCode == "query_too_long",
            "[LS-018] oversized topic requests are rejected");
    }
}
