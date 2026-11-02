using System;
using System.Collections.Generic;
using LuanShi.Engine;

// [LS-020] Dispatch through the real LS-018 investigation service.
//
// A successfully parsed request is handed to an injected, host-configured service. These tests use
// a real pure-C# GameEngine fixture and detached observation only (no provider, network or editor),
// then prove the call is read-only: canonical log, city state, observation and injected memories
// are byte-for-byte unchanged after every tool runs.

internal static partial class Harness
{
    private static void InvestigationRequestDispatch()
    {
        GameEngine engine = InvestigationHistoryEngine(out Observation observation);
        string gameId = engine.State.GameId;
        var memories = new List<LlmMemory>
        {
            InvestigationMemory("m-food-1", gameId, "seat-a", LlmMemoryType.Lesson,
                "Food shortage lesson: stock food before winter.", 2, importance: 3, confidence: 2),
            InvestigationMemory("m-foreign", gameId, "seat-b", LlmMemoryType.Fact,
                "Foreign seat secret about food.", 2),
        };
        InvestigationService service = InvestigationServiceFor(engine, observation, memories);
        var dispatcher = new InvestigationDispatcher(service);

        int logCount = engine.Log.Records.Count;
        int eventCount = observation.Events.Count;
        int cityCount = observation.Cities.Count;
        CityState city = engine.State.Cities.Find(c => c.Id == "city-a");
        int food = city.Food, gold = city.Gold, garrison = city.Garrison;
        string memoryBefore = memories[0].Content;

        InvestigationRequestParseResult manual = InvestigationParse(
            "{\"tool\":\"manual_lookup\",\"topic\":\"recruit\"}");
        Check(manual.Ok, "[LS-020] valid manual_lookup parses before dispatch");
        InvestigationResult manualResult = dispatcher.Dispatch(manual.Request);
        Check(manualResult is ManualLookupResult && manualResult.Ok
              && manualResult.Tool == "manual_lookup"
              && ((ManualLookupResult)manualResult).TopicId == "recruit",
            "[LS-020] dispatch runs the real manual_lookup tool");

        InvestigationRequestParseResult inspect = InvestigationParse(
            "{\"tool\":\"inspect_entity\",\"kind\":\"city\",\"id\":\"city-a\"}");
        Check(inspect.Ok, "[LS-020] valid inspect_entity parses before dispatch");
        InvestigationResult inspectResult = dispatcher.Dispatch(inspect.Request);
        var entity = inspectResult as EntityInspectionResult;
        Check(entity != null && entity.Ok && entity.Found && entity.City != null
              && entity.City.Id == "city-a" && entity.Knowledge == "current_observation",
            "[LS-020] dispatch runs the real inspect_entity tool");

        InvestigationRequestParseResult history = InvestigationParse(
            "{\"tool\":\"search_history\",\"query\":\"tax\",\"limit\":10,\"offset\":0}");
        Check(history.Ok, "[LS-020] valid search_history parses before dispatch");
        InvestigationResult historyResult = dispatcher.Dispatch(history.Request);
        var found = historyResult as HistorySearchResult;
        Check(found != null && found.Ok && found.ReturnedCount > 0,
            "[LS-020] dispatch runs the real search_history tool");

        InvestigationRequestParseResult memory = InvestigationParse(
            "{\"tool\":\"recall_memory\",\"query\":\"food\",\"limit\":5}");
        Check(memory.Ok, "[LS-020] valid recall_memory parses before dispatch");
        InvestigationResult memoryResult = dispatcher.Dispatch(memory.Request);
        var recalled = memoryResult as MemoryRecallResult;
        Check(recalled != null && recalled.Ok && recalled.ReturnedCount == 1
              && recalled.Memories[0].MemoryId == "m-food-1",
            "[LS-020] dispatch runs the real recall_memory tool and scopes to own seat");

        foreach (InvestigationResult result in new[]
        {
            manualResult, inspectResult, historyResult, memoryResult,
        })
        {
            string json = result.ToJson();
            Check(json.Length <= service.Options.MaxResponseChars
                  && StrictJson.TryParse(json, out _, out _),
                "[LS-020] dispatched " + result.Tool + " keeps the bounded LS-018 envelope");
        }

        Check(engine.Log.Records.Count == logCount
              && observation.Events.Count == eventCount
              && observation.Cities.Count == cityCount,
            "[LS-020] dispatch emits no canonical event and mutates no observation");
        Check(city.Food == food && city.Gold == gold && city.Garrison == garrison,
            "[LS-020] dispatch mutates no canonical city state");
        Check(memories[0].Content == memoryBefore
              && memories[1].SeatId == "seat-b",
            "[LS-020] dispatch leaves injected memories untouched");

        // A parser failure never produces a request, so there is nothing for a host to dispatch.
        InvestigationRequestParseResult rejected = InvestigationParse(
            "{\"tool\":\"search_history\",\"seat_id\":\"seat-b\"}");
        Check(!rejected.Ok && rejected.Request == null,
            "[LS-020] rejected requests expose no dispatchable object");

        bool nullRejected = false;
        try
        {
            dispatcher.Dispatch(null);
        }
        catch (ArgumentNullException ex)
        {
            nullRejected = ex.ParamName == "request";
        }
        Check(nullRejected, "[LS-020] dispatch rejects a null request defensively");

        bool serviceRejected = false;
        try
        {
            new InvestigationDispatcher(null);
        }
        catch (ArgumentNullException ex)
        {
            serviceRejected = ex.ParamName == "service";
        }
        Check(serviceRejected, "[LS-020] dispatch requires an injected service");
    }
}
