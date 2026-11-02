using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using LuanShi.Engine;

// [LS-018] Response-bound and pagination-progress review regressions.
//
// 1. MaxResponseChars is a hard cap on EVERY tool response (success, index, error, catalogue,
//    entity and empty paths), measured after JSON escaping. Tight-but-valid budgets structurally
//    trim or return a bounded explicit error; impossible caps are rejected, never expanded.
// 2. A successful history page always advances next_offset, so a client following next_offset
//    terminates and never silently skips history. A page that cannot fit even a clipped hit
//    returns the distinct bounded response_too_large error instead of an empty repeated page.

internal static partial class Harness
{
    private static void InvestigationResponseBounds()
    {
        InvestigationResponseBoundConfiguration();
        InvestigationAllToolsUnderProbeBudget();
        InvestigationHistoryPaginationProgress();
        InvestigationTightAndErrorPaths();
    }

    // ---- helpers -----------------------------------------------------------

    private static InvestigationOptions InvestigationBoundOptions(int responseChars, int textChars)
        => new InvestigationOptions { MaxResponseChars = responseChars, MaxTextChars = textChars };

    private static void InvestigationCheckBounded(InvestigationResult result,
        InvestigationOptions options, string label)
    {
        string json = result.ToJson();
        Check(json.Length <= options.MaxResponseChars,
            "[LS-018] " + label + " respects MaxResponseChars after escaping");
        Check(InvestigationParses(json), "[LS-018] " + label + " remains valid JSON");
        Check(InvestigationEnvelopeMatches(json, result),
            "[LS-018] " + label + " carries consistent ok/returned/truncated metadata");
    }

    private static bool InvestigationHasRawControl(string json)
    {
        foreach (char c in json) if (c < ' ') return true;
        return false;
    }

    private static bool InvestigationEnvelopeMatches(string json, InvestigationResult result)
    {
        if (!StrictJson.TryParse(json, out JsonNode node, out _)) return false;
        if (!(node is JsonObject obj)) return false;
        if (!obj.TryGet("ok", out JsonNode okNode) || !(okNode is JsonBool ok)) return false;
        if (!obj.TryGet("returned", out JsonNode countNode) || !(countNode is JsonInt count)) return false;
        if (!obj.TryGet("truncated", out JsonNode truncNode) || !(truncNode is JsonBool trunc)) return false;
        return ok.Value == result.Ok && count.Value == result.ReturnedCount
            && trunc.Value == result.Truncated;
    }

    /// <summary>Hostile quoted/backslash/newline/tab/control text on every served surface.</summary>
    private static Observation InvestigationHostileObservation(int eventCount, int textLength)
    {
        var observation = new Observation
        {
            GameId = "g-001", SeatId = "seat-a", Season = 3, SeasonName = "Autumn",
            Phase = "player_orders", CommandPoints = 5,
        };
        string hostilePrefix = "\"quoted\" \\ backslash\nnewline\ttab" + (char)1;
        observation.Cities.Add(new CityView
        {
            Id = "city-long", Name = hostilePrefix + new string('c', textLength),
            X = 0, Z = 0, OwnerSeatId = "seat-a", Population = 100, Food = 100, Gold = 10,
            Garrison = 100, GarrisonType = UnitTypes.Infantry, Morale = 60, Defense = 1,
            Training = 1, ReclaimPct = 0,
        });
        observation.Armies.Add(new ArmyView
        {
            Id = "army-long", Name = hostilePrefix + new string('a', textLength),
            OwnerSeatId = "seat-a", Troops = 100, UnitType = UnitTypes.Infantry,
            X = 1, Z = 1, Morale = 70,
        });

        for (int i = 0; i < eventCount; i++)
        {
            var payload = new Dictionary<string, string>
            {
                { "city_id", "city-a" },
                { "quote", "\"" + (char)2 + new string('p', textLength) },
            };
            var args = new Dictionary<string, string> { { "note", hostilePrefix } };
            var record = new EventRecord
            {
                Seq = i + 1, GameId = "g-001", Season = (i % 4) + 1, Phase = "player_orders",
                ActorSeatId = "seat-a", ControllerType = ControllerType.Human,
                EventType = "tax_levied", Visibility = "public",
                Payload = new ReadOnlyDictionary<string, string>(payload),
                SourceActionId = "a-" + (i + 1),
                Text = hostilePrefix + " event " + i + " " + new string('e', textLength),
            };
            observation.Events.Add(new EventView(record, payload, args, record.Text));
        }
        return observation;
    }

    // ---- 1. cap validation and global enforcement --------------------------

    private static void InvestigationResponseBoundConfiguration()
    {
        GameEngine engine = InvestigationEngine(1911, false, out Observation observation);

        bool rejected = false;
        try
        {
            InvestigationServiceFor(engine, observation, null,
                new InvestigationOptions { MaxResponseChars = InvestigationOptions.MinimumResponseChars - 1 });
        }
        catch (ArgumentOutOfRangeException ex)
        {
            rejected = ex.ParamName == "MaxResponseChars";
        }
        Check(rejected, "[LS-018] a cap below the bounded-error envelope is rejected, not expanded");

        rejected = false;
        try
        {
            InvestigationService.Catalogue(new InvestigationOptions { MaxResponseChars = 1 });
        }
        catch (ArgumentOutOfRangeException)
        {
            rejected = true;
        }
        Check(rejected, "[LS-018] catalogue configuration is validated as well");

        var minimum = InvestigationBoundOptions(InvestigationOptions.MinimumResponseChars, 1000);
        InvestigationService service = InvestigationServiceFor(engine, observation, null, minimum);
        Check(service.ContextValid, "[LS-018] the minimum supported response cap is accepted");
        InvestigationCheckBounded(InvestigationService.Catalogue(minimum), minimum,
            "minimum-cap catalogue");
    }

    private static void InvestigationAllToolsUnderProbeBudget()
    {
        var options = InvestigationBoundOptions(512, 1000);
        Observation observation = InvestigationHostileObservation(1, 1000);
        var memories = new List<LlmMemory>
        {
            InvestigationMemory("m-1", observation.GameId, observation.SeatId,
                LlmMemoryType.Fact, "\"believed\" \\ food\n\t" + (char)1 + new string('m', 1000), 2),
        };
        var service = new InvestigationService(
            observation.GameId, observation.SeatId, observation, memories, options);

        InvestigationToolCatalogueResult catalogue = InvestigationService.Catalogue(options);
        InvestigationCheckBounded(catalogue, options, "catalogue");
        Check(catalogue.Ok && catalogue.ReturnedCount == catalogue.Tools.Count,
            "[LS-018] catalogue trimming keeps returned count consistent");

        foreach (string topic in new[] { "", "index", "recruit", "combat", "re" })
        {
            ManualLookupResult manual = service.ManualLookup(topic);
            InvestigationCheckBounded(manual, options, "manual_lookup('" + topic + "')");
            if (!manual.Ok)
                Check(manual.ReturnedCount == 0,
                    "[LS-018] failed manual_lookup reports zero returned entries");
            else if (manual.IsTopicIndex)
                Check(manual.ReturnedCount == manual.Topics.Count,
                    "[LS-018] index/manual trimming keep the topic count consistent");
            else
                Check(manual.ReturnedCount == 1,
                    "[LS-018] a detailed manual answer still reports its one topic");
        }

        ManualLookupResult recruit = service.ManualLookup("recruit");
        Check(recruit.Ok && recruit.ToJson().Length <= options.MaxResponseChars,
            "[LS-018] the 2697-char recruit manual is bounded at cap 512");

        EntityInspectionResult city = service.InspectEntity("city", "city-long");
        InvestigationCheckBounded(city, options, "inspect_entity city");
        Check(city.Ok && city.Found && city.City != null && city.ReturnedCount == 1,
            "[LS-018] a 915-char city inspection fits cap 512 by clipping text");
        Check(city.Truncated, "[LS-018] clipped entity text is reported as truncated");

        EntityInspectionResult army = service.InspectEntity("army", "army-long");
        InvestigationCheckBounded(army, options, "inspect_entity army");
        Check(army.Ok && army.Found && army.Army != null,
            "[LS-018] a long army inspection stays bounded");

        EntityInspectionResult missing = service.InspectEntity("city", "city-never-seen");
        InvestigationCheckBounded(missing, options, "inspect_entity not found");
        Check(!missing.Ok && missing.ErrorCode == "entity_not_found" && !missing.Truncated,
            "[LS-018] a small entity failure is unchanged by the response bound");

        EntityInspectionResult badKind = service.InspectEntity("fleet", "city-long");
        InvestigationCheckBounded(badKind, options, "inspect_entity unsupported kind");

        HistorySearchResult history = service.SearchHistory("", 10);
        InvestigationCheckBounded(history, options, "search_history one long event");
        Check(history.Ok && history.ReturnedCount == 1 && history.Hits.Count == 1,
            "[LS-018] history fits at least one clipped hit at cap 512");
        Check(history.NextOffset == -1, "[LS-018] a single fitted event terminates pagination");
        Check(history.Truncated && history.Hits[0].Text.Length < 1000,
            "[LS-018] the frontier history hit is explicitly clipped, not dropped");

        string historyJson = history.ToJson();
        Check(!InvestigationHasRawControl(historyJson),
            "[LS-018] history output never emits unnamed control characters raw");
        Check(historyJson.IndexOf("\\\"", StringComparison.Ordinal) >= 0,
            "[LS-018] quotes in untrusted event text are escaped");

        Observation small = InvestigationHostileObservation(1, 5);
        var smallService = new InvestigationService(small.GameId, small.SeatId, small, null,
            InvestigationBoundOptions(16000, 600));
        string escapedJson = smallService.SearchHistory("", 10).ToJson();
        Check(escapedJson.IndexOf("\\u0001", StringComparison.Ordinal) >= 0
              && escapedJson.IndexOf("\\u0002", StringComparison.Ordinal) >= 0
              && escapedJson.IndexOf("\\n", StringComparison.Ordinal) >= 0
              && escapedJson.IndexOf("\\t", StringComparison.Ordinal) >= 0
              && escapedJson.IndexOf("\\\"", StringComparison.Ordinal) >= 0,
            "[LS-018] control, newline, tab and quote characters are JSON-escaped");

        HistorySearchResult hostileQuery = service.SearchHistory("\"\\\n\t" + (char)1, 10);
        InvestigationCheckBounded(hostileQuery, options, "search_history hostile query");

        MemoryRecallResult memory = service.RecallMemory("", 10);
        InvestigationCheckBounded(memory, options, "recall_memory one long memory");
        Check(memory.Ok && memory.ReturnedCount == 1 && memory.Memories.Count == 1
              && memory.Truncated && memory.Memories[0].Content.Length < 1000,
            "[LS-018] memory recall keeps one clipped hit and reports truncation");
        InvestigationCheckBounded(service.RecallMemory("\"\\\n" + (char)1, 10),
            options, "recall_memory hostile query");
    }

    // ---- 2. pagination progress and termination ----------------------------

    private static void InvestigationHistoryPaginationProgress()
    {
        var options = InvestigationBoundOptions(512, 1000);
        Observation observation = InvestigationHostileObservation(4, 1000);
        var service = new InvestigationService(
            observation.GameId, observation.SeatId, observation, null, options);

        var seen = new List<int>();
        int offset = 0;
        int pages = 0;
        while (offset >= 0 && pages < 25)
        {
            HistorySearchResult page = service.SearchHistory("", 10, offset);
            InvestigationCheckBounded(page, options, "pagination page " + pages);
            Check(page.Ok, "[LS-018] every bounded pagination page succeeds");
            if (page.Hits.Count == 0)
            {
                Check(page.NextOffset == -1,
                    "[LS-018] an empty history page terminates instead of repeating");
                break;
            }
            Check(page.NextOffset < 0 || page.NextOffset > offset,
                "[LS-018] a successful continuation strictly advances next_offset");
            foreach (HistoryHit hit in page.Hits) seen.Add(hit.Seq);
            offset = page.NextOffset;
            pages++;
        }

        Check(pages < 25, "[LS-018] following next_offset terminates");
        var expected = new List<int>();
        foreach (EventView view in observation.Events) expected.Add(view.Seq);
        expected.Sort();
        seen.Sort();
        bool same = expected.Count == seen.Count;
        for (int i = 0; same && i < expected.Count; i++) same = expected[i] == seen[i];
        Check(same, "[LS-018] following next_offset returns every history event exactly once");
    }

    // ---- 3. empty/error paths and the tightest supported budget ------------

    private static void InvestigationTightAndErrorPaths()
    {
        var options = InvestigationBoundOptions(InvestigationOptions.MinimumResponseChars, 1000);
        Observation observation = InvestigationHostileObservation(1, 1000);
        var memories = new List<LlmMemory>
        {
            InvestigationMemory("m-1", observation.GameId, observation.SeatId,
                LlmMemoryType.Fact, new string('m', 1000), 2),
        };
        var service = new InvestigationService(
            observation.GameId, observation.SeatId, observation, memories, options);

        HistorySearchResult history = service.SearchHistory("", 10);
        InvestigationCheckBounded(history, options, "minimum-cap history cannot-fit");
        Check(!history.Ok && history.ErrorCode == "response_too_large"
              && history.ReturnedCount == 0 && history.Truncated,
            "[LS-018] history that cannot fit one clipped hit returns the distinct bounded error");

        MemoryRecallResult memory = service.RecallMemory("", 10);
        InvestigationCheckBounded(memory, options, "minimum-cap memory cannot-fit");
        Check(!memory.Ok && memory.ErrorCode == "response_too_large" && memory.Truncated,
            "[LS-018] memory that cannot fit one clipped hit returns the bounded error");

        EntityInspectionResult entity = service.InspectEntity("city", "city-long");
        InvestigationCheckBounded(entity, options, "minimum-cap entity cannot-fit");
        Check(!entity.Ok && entity.ErrorCode == "response_too_large",
            "[LS-018] an entity skeleton too large for the minimum cap returns the bounded error");

        ManualLookupResult manual = service.ManualLookup("");
        InvestigationCheckBounded(manual, options, "minimum-cap manual index");
        Check(!manual.Ok && manual.ErrorCode == "response_too_large",
            "[LS-018] a manual envelope that cannot fit returns the bounded error");

        InvestigationToolCatalogueResult catalogue = InvestigationService.Catalogue(options);
        InvestigationCheckBounded(catalogue, options, "minimum-cap catalogue");
        Check(catalogue.Ok && catalogue.ReturnedCount == catalogue.Tools.Count,
            "[LS-018] the catalogue still fits the minimum cap by trimming tools");

        var emptyObservation = new Observation
        {
            GameId = "g-001", SeatId = "seat-a", Season = 1, SeasonName = "Spring",
            Phase = "player_orders", CommandPoints = 5,
        };
        var emptyService = new InvestigationService("g-001", "seat-a", emptyObservation, null, options);
        HistorySearchResult emptyHistory = emptyService.SearchHistory("", 10);
        InvestigationCheckBounded(emptyHistory, options, "empty history");
        Check(emptyHistory.Ok && emptyHistory.ReturnedCount == 0
              && emptyHistory.NextOffset == -1 && !emptyHistory.Truncated,
            "[LS-018] empty history stays successful, terminated and untruncated");
        MemoryRecallResult emptyMemory = emptyService.RecallMemory("", 10);
        InvestigationCheckBounded(emptyMemory, options, "empty memory");
        Check(emptyMemory.Ok && emptyMemory.ReturnedCount == 0,
            "[LS-018] empty memory stays successful under the cap");

        Check(!service.SearchHistory("", -1).Ok
              && service.SearchHistory("", -1).ErrorCode == "invalid_limit",
            "[LS-018] limit errors still surface at the minimum cap");
        InvestigationCheckBounded(service.SearchHistory("", -1), options, "history limit error");
        InvestigationCheckBounded(service.SearchHistory("", 10, -1), options, "history offset error");
        InvestigationCheckBounded(service.SearchHistory(new string('q', 201), 10), options,
            "history query error");
        InvestigationCheckBounded(service.InspectEntity("fleet", "city-long"), options,
            "entity kind error");
        InvestigationCheckBounded(service.RecallMemory("", -1), options, "memory limit error");
        InvestigationCheckBounded(service.ManualLookup(new string('x', 65)), options,
            "manual topic error");

        var wrongGame = new InvestigationService("g-other", "seat-a", observation, null, options);
        InvestigationCheckBounded(wrongGame.ManualLookup("turn"), options, "context manual");
        InvestigationCheckBounded(wrongGame.InspectEntity("city", "city-long"), options,
            "context entity");
        InvestigationCheckBounded(wrongGame.SearchHistory("", 10), options, "context history");
        InvestigationCheckBounded(wrongGame.RecallMemory("", 10), options, "context memory");
        Check(wrongGame.ManualLookup("turn").ErrorCode == "context_mismatch"
              && wrongGame.SearchHistory("", 10).ErrorCode == "context_mismatch",
            "[LS-018] error paths remain typed and bounded at the minimum cap");
    }
}
