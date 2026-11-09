using System;
using System.IO;
using LuanShi.Engine;

internal static partial class Harness
{
    private static void RunDemoProfileIntegration()
    {
        Section("[integration] profile rules, personas and spectator evidence");
        var engine = new GameEngine(12, "integration-game", BalanceProfile.EasyResearch);
        engine.State.Map = Corridor(10);
        engine.State.Factions.Add(new FactionState { SeatId = "seat-a", Name = "Wei", Controller = ControllerType.Llm });
        engine.State.Factions.Add(new FactionState { SeatId = "seat-b", Name = "Shu", Controller = ControllerType.Llm });
        foreach (var item in new[] { ("seat-a", "city-a", 0), ("seat-b", "city-b", 9) })
        {
            var city = new CityState { Id = item.Item2, Name = item.Item2, OwnerSeatId = item.Item1,
                X = item.Item3, Z = 0, Morale = 100 };
            engine.Balance.ApplyStartingResources(city);
            engine.State.Cities.Add(city);
        }
        Check(engine.State.Cities[0].Food == 1500 && engine.State.Cities[0].Gold == 900,
            "[integration] easy opening resources are applied");
        engine.BeginSeason();
        Observation obs = engine.Observe("seat-a");
        var service = new InvestigationService(obs.GameId, obs.SeatId, obs, null);
        var labor = service.ManualLookup("lighten_labor");
        var recruit = service.ManualLookup("recruit");
        var reclaim = service.ManualLookup("reclaim");
        Check(InvestigationFact(labor, "gold_cost") == "50"
              && InvestigationFact(recruit, "gold_cost_infantry") == "50"
              && InvestigationFact(recruit, "gold_cost_archer") == "75"
              && InvestigationFact(recruit, "gold_cost_cavalry") == "150"
              && InvestigationFact(recruit, "gold_cost_siege") == "250",
            "[integration] manual uses easy domestic costs for each unit");
        Check(InvestigationFact(reclaim, "base_food_yield") == "300"
              && InvestigationFact(recruit, "food_upkeep_per_1000_infantry")
                 == engine.Balance.TroopFoodUpkeep(1000, UnitTypes.Infantry).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
            "[integration] manual uses easy yield and troop upkeep");

        var fake = new FakeLlmTransport();
        fake.EnqueueSuccess("{\"type\":\"end_turn\"}");
        fake.EnqueueSuccess("{\"type\":\"end_turn\"}");
        var config = new LlmConfig { EnablePersonas = true, EnableManualLookup = true };
        string dir = LlmTempDir("profile-summary");
        var runner = new LlmTurnRunner(fake, config, new LlmRunRecorder(dir));
        var session = new SpectatorSession(engine, runner, 1, 12);
        session.Start();
        SpectatorDrain(session);
        Check(fake.Requests.Count == 2
              && fake.Requests[0].System.Contains(LlmPersona.WeiPromptText)
              && fake.Requests[1].System.Contains(LlmPersona.ShuPromptText),
            "[integration] actual spectator runner binds Wei then Shu personas");
        Check(fake.Requests.Count == 2 && fake.Requests[0].System.Contains("manual_lookup")
              && fake.Requests[1].User.Contains("easy-research")
              && fake.Requests[1].User.Contains("\"city_base_food_yield\":300"),
            "[integration] persona and manual coexist; prompt names the selected balance");
        try
        {
            string path = new SpectatorSummary(dir).Write(session, config, fake);
            string json = File.ReadAllText(path);
            Check(json.Contains("\"balance_profile\":{\"profile\":\"easy-research\"")
                  && json.Contains("\"starting_food\":1500") && json.Contains("\"starting_gold\":900"),
                "[integration] spectator summary records the selected match profile");
            string[] records = Directory.GetFiles(dir, "*.jsonl");
            string lines = records.Length == 1 ? File.ReadAllText(records[0]) : "";
            Check(lines.Contains("\"balance_profile\":{\"profile\":\"easy-research\""),
                "[integration] request log records the selected match profile");
        }
        finally { LlmDelete(dir); }
    }
}
