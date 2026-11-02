using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using LuanShi.Engine;

// [LS-018] Bounds, determinism, non-mutation and fail-closed context handling.

internal static partial class Harness
{
    private static void InvestigationBoundsAndNonMutation()
    {
        GameEngine engine = InvestigationHistoryEngine(out Observation observation);
        InvestigationService service = InvestigationServiceFor(engine, observation);

        Check(service.SearchHistory("", -1).ErrorCode == "invalid_limit",
            "[LS-018] negative history limits are rejected");
        Check(service.SearchHistory("", 51).ErrorCode == "limit_too_large",
            "[LS-018] excessive history limits are rejected");
        Check(service.SearchHistory("", 10, -1).ErrorCode == "invalid_offset",
            "[LS-018] negative pagination offsets are rejected");
        Check(service.SearchHistory("", 10, 1001).ErrorCode == "offset_too_large",
            "[LS-018] excessive pagination offsets are rejected");
        Check(service.SearchHistory(new string('q', 201), 10).ErrorCode == "query_too_long",
            "[LS-018] oversized history queries are rejected");
        Check(service.SearchHistory(null, 10).Ok, "[LS-018] a null query is treated as empty history search");
        Check(service.SearchHistory("", 0).Ok, "[LS-018] a zero limit uses the conservative default");

        Observation empty = new Observation
        {
            GameId = "g-001", SeatId = "seat-a", Season = 1, SeasonName = "Spring",
            Phase = "player_orders", CommandPoints = 5,
        };
        InvestigationService emptyService = InvestigationServiceFor(engine, empty);
        Check(emptyService.SearchHistory("", 10).Ok
              && emptyService.SearchHistory("", 10).ReturnedCount == 0,
            "[LS-018] empty history succeeds with zero hits");
        Check(emptyService.RecallMemory("", 10).Ok
              && emptyService.RecallMemory("", 10).ReturnedCount == 0,
            "[LS-018] empty memory succeeds with zero hits");

        InvestigationObservationBounds();
        InvestigationMemoryBounds();
        InvestigationNonMutationAndDeterminism(engine, observation);
    }

    private static void InvestigationObservationBounds()
    {
        var options = InvestigationOptions.Defaults();
        Observation large = InvestigationLargeObservation(300, 5000);
        InvestigationService service = InvestigationServiceFor(
            InvestigationEngine(1871, false, out _), large, null, options);

        HistorySearchResult first = service.SearchHistory("", options.MaxHistoryLimit, 0);
        Check(first.Ok && first.Hits.Count <= options.MaxHistoryLimit && first.Truncated,
            "[LS-018] large history is capped with explicit truncation");
        HistorySearchResult second = service.SearchHistory("", options.MaxHistoryLimit, first.NextOffset);
        Check(second.Ok && second.Hits.Count > 0
              && second.Hits[0].Seq < first.Hits[first.Hits.Count - 1].Seq,
            "[LS-018] the next history page continues deterministically");
        Check(first.ToJson().Length <= options.EffectiveResponseChars
              && InvestigationParses(first.ToJson()),
            "[LS-018] a large history response stays bounded and remains valid JSON");
        Check(first.Hits.Count > 0 && first.Hits[0].Text.Length <= options.MaxTextChars,
            "[LS-018] oversized event text is clipped, not emitted whole");
        Check(first.Hits[0].Payload.Count <= options.MaxPayloadEntries,
            "[LS-018] event payload entries are bounded");

        var tight = new InvestigationOptions { MaxResponseChars = 2048, MaxTextChars = 5000 };
        InvestigationService tightService = InvestigationServiceFor(
            InvestigationEngine(1872, false, out _), large, null, tight);
        HistorySearchResult tightResult = tightService.SearchHistory("", tight.MaxHistoryLimit, 0);
        Check(tightResult.Ok && tightResult.ToJson().Length <= tight.EffectiveResponseChars
              && InvestigationParses(tightResult.ToJson()),
            "[LS-018] the serialized size bound trims whole hits without breaking JSON");

        HistorySearchResult repeated = service.SearchHistory("", options.MaxHistoryLimit, 0);
        Check(repeated.ToJson() == first.ToJson(),
            "[LS-018] repeated large-history calls are deterministic");
    }

    private static void InvestigationMemoryBounds()
    {
        InvestigationOptions options = InvestigationOptions.Defaults();
        List<LlmMemory> memories = InvestigationLargeMemories(300, 5000);
        GameEngine engine = InvestigationEngine(1881, false, out Observation observation);
        InvestigationService service = InvestigationServiceFor(engine, observation, memories, options);

        MemoryRecallResult first = service.RecallMemory("", options.MaxMemoryLimit);
        Check(first.Ok && first.Memories.Count <= options.MaxMemoryLimit && first.Truncated,
            "[LS-018] large memory collections are capped with explicit truncation");
        Check(first.Memories.Count > 0 && first.Memories[0].Content.Length <= options.MaxTextChars,
            "[LS-018] oversized memory content is clipped");
        Check(first.ToJson().Length <= options.EffectiveResponseChars
              && InvestigationParses(first.ToJson()),
            "[LS-018] a large memory response stays bounded and remains valid JSON");

        var tight = new InvestigationOptions { MaxResponseChars = 2048, MaxTextChars = 5000 };
        MemoryRecallResult tightResult = InvestigationServiceFor(engine, observation, memories, tight)
            .RecallMemory("", tight.MaxMemoryLimit);
        Check(tightResult.Ok && tightResult.ToJson().Length <= tight.EffectiveResponseChars
              && InvestigationParses(tightResult.ToJson()),
            "[LS-018] the memory size bound trims whole hits without breaking JSON");

        Check(service.RecallMemory("", options.MaxMemoryLimit).ToJson() == first.ToJson(),
            "[LS-018] repeated large-memory calls are deterministic");
    }

    private static void InvestigationNonMutationAndDeterminism(GameEngine engine, Observation observation)
    {
        var memories = new List<LlmMemory>
        {
            InvestigationMemory("own", observation.GameId, observation.SeatId, LlmMemoryType.Fact,
                "We need food", 2),
        };
        InvestigationService service = InvestigationServiceFor(engine, observation, memories);

        string canonicalBefore = CanonicalFingerprint(engine);
        string observationBefore = observation.ToJson();
        int[] orderBefore = InvestigationEventOrder(observation);
        int commandPointsBefore = engine.State.FindFaction("seat-a").CommandPoints;
        string memoryBefore = memories[0].Content;

        string firstManual = service.ManualLookup("turn").ToJson();
        string firstEntity = service.InspectEntity("city", "city-a").ToJson();
        string firstHistory = service.SearchHistory("", 10).ToJson();
        string firstMemory = service.RecallMemory("", 10).ToJson();

        for (int i = 0; i < 3; i++)
        {
            service.ManualLookup("turn");
            service.InspectEntity("city", "city-a");
            service.SearchHistory("", 10);
            service.RecallMemory("", 10);
        }

        Check(CanonicalFingerprint(engine) == canonicalBefore,
            "[LS-018] repeated inquiry leaves canonical state and the log byte-equivalent");
        Check(observation.ToJson() == observationBefore
              && InvestigationSameOrder(orderBefore, InvestigationEventOrder(observation)),
            "[LS-018] repeated inquiry leaves the injected observation unchanged");
        Check(engine.State.FindFaction("seat-a").CommandPoints == commandPointsBefore,
            "[LS-018] inquiry never spends game command points");
        Check(memories[0].Content == memoryBefore,
            "[LS-018] inquiry never rewrites injected memories");

        Check(service.ManualLookup("turn").ToJson() == firstManual
              && service.InspectEntity("city", "city-a").ToJson() == firstEntity
              && service.SearchHistory("", 10).ToJson() == firstHistory
              && service.RecallMemory("", 10).ToJson() == firstMemory,
            "[LS-018] all four operations are deterministic for identical inputs");
    }

    private static void InvestigationContextFailClosed()
    {
        GameEngine engine = InvestigationEngine(1891, false, out Observation observation);
        var good = new InvestigationService(observation.GameId, observation.SeatId, observation, null);
        Check(good.ContextValid, "[LS-018] a matching host context is accepted");

        var wrongGame = new InvestigationService("g-other", observation.SeatId, observation, null);
        var wrongSeat = new InvestigationService(observation.GameId, "seat-b", observation, null);
        var nullObservation = new InvestigationService(observation.GameId, observation.SeatId, null, null);
        var emptyContext = new InvestigationService("", observation.SeatId, observation, null);

        Check(!wrongGame.ContextValid && !wrongSeat.ContextValid
              && !nullObservation.ContextValid && !emptyContext.ContextValid,
            "[LS-018] mismatched, null or missing contexts are invalid");
        Check(wrongGame.ContextErrorMessage != null && wrongSeat.ContextErrorMessage != null,
            "[LS-018] a fail-closed context carries a safe generic reason");

        Check(wrongGame.ManualLookup("turn").ErrorCode == "context_mismatch"
              && wrongGame.InspectEntity("city", "city-a").ErrorCode == "context_mismatch"
              && wrongGame.SearchHistory("", 10).ErrorCode == "context_mismatch"
              && wrongGame.RecallMemory("", 10).ErrorCode == "context_mismatch",
            "[LS-018] a wrong match context fails every operation closed");
        Check(wrongSeat.SearchHistory("", 10).ErrorCode == "context_mismatch"
              && nullObservation.InspectEntity("city", "city-a").ErrorCode == "context_mismatch"
              && emptyContext.ManualLookup("turn").ErrorCode == "context_mismatch",
            "[LS-018] wrong player, null observation and empty context all fail closed");

        Check(InvestigationParses(wrongSeat.SearchHistory("", 10).ToJson()),
            "[LS-018] context failures still produce a typed, valid envelope");
    }

    // ---- large fixtures ----------------------------------------------------

    private static Observation InvestigationLargeObservation(int count, int textLength)
    {
        var observation = new Observation
        {
            GameId = "g-001", SeatId = "seat-a", Season = 6, SeasonName = "Summer",
            Phase = "player_orders", CommandPoints = 5,
        };
        for (int i = 0; i < count; i++)
        {
            string text = new string('t', textLength) + " event " + i.ToString();
            var payload = new Dictionary<string, string>
            {
                { "city_id", "city-a" },
                { "amount", i.ToString() },
                { "detail", new string('p', textLength) },
            };
            var record = new EventRecord
            {
                Seq = i + 1, GameId = "g-001", Season = (i % 4) + 1, Phase = "player_orders",
                ActorSeatId = "seat-a", ControllerType = ControllerType.Human,
                EventType = "tax_levied", Visibility = "public",
                Payload = new ReadOnlyDictionary<string, string>(payload),
                SourceActionId = "a-" + (i + 1), Text = text,
            };
            observation.Events.Add(new EventView(record, payload, null, text));
        }
        return observation;
    }

    private static List<LlmMemory> InvestigationLargeMemories(int count, int textLength)
    {
        var memories = new List<LlmMemory>(count);
        for (int i = 0; i < count; i++)
        {
            memories.Add(new LlmMemory
            {
                Id = "m-" + i, GameId = "g-001", SeatId = "seat-a",
                Type = LlmMemoryType.Fact, Content = new string('m', textLength) + " memory " + i.ToString(),
                Source = "turn_result", Season = (i % 10) + 1,
                Importance = (i % 5) + 1, Confidence = 1,
            });
        }
        return memories;
    }
}
