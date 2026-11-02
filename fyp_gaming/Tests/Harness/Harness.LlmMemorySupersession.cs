using System;
using System.Collections.Generic;
using System.Linq;
using LuanShi.Engine;

internal static partial class Harness
{
    private static void RunLlmMemorySupersession()
    {
        Section("LLM memory: three-version supersession and idempotence [MEMORY-SUPERSESSION]");

        var a = Memory("a", LlmMemoryType.Fact,
            "reclaim city-a was accepted", 1);
        var b = Memory("b", LlmMemoryType.Lesson,
            "reclaim city-a was rejected: no orders", 2);
        var c = Memory("c", LlmMemoryType.Lesson,
            "reclaim city-a was rejected: not enough gold", 3);

        LlmMemory[][] permutations =
        {
            new[] { a, b, c }, new[] { a, c, b }, new[] { b, a, c },
            new[] { b, c, a }, new[] { c, a, b }, new[] { c, b, a },
        };

        bool onlyNewest = true;
        foreach (LlmMemory[] order in permutations)
        {
            IReadOnlyList<LlmMemory> result = LlmMemoryLifecycle.Consolidate(order, 3, 8, 100);
            onlyNewest &= ActiveIds(result).SequenceEqual(new[] { "c" })
                && result.Single(m => m.Id == "a").SupersededBy == "c"
                && result.Single(m => m.Id == "b").SupersededBy == "c";
        }
        Check(onlyNewest,
            "[MEMORY-SUPERSESSION] A/B/C leave only C active in every input permutation");

        // Equal-season ties use the documented ordinal-id tie-break, independent of order.
        var tieA = Memory("tie-a", LlmMemoryType.Lesson, "levy city-a was rejected: no gold", 2);
        var tieB = Memory("tie-b", LlmMemoryType.Fact, "levy city-a was accepted", 2);
        var tieC = Memory("tie-c", LlmMemoryType.Lesson, "levy city-a was rejected: no orders", 2);
        bool tieDeterministic = true;
        foreach (LlmMemory[] order in new[]
        {
            new[] { tieA, tieB, tieC }, new[] { tieC, tieB, tieA }, new[] { tieB, tieA, tieC },
        })
        {
            IReadOnlyList<LlmMemory> result = LlmMemoryLifecycle.Consolidate(order, 2, 8, 100);
            tieDeterministic &= ActiveIds(result).SequenceEqual(new[] { "tie-c" })
                && result.Single(m => m.Id == "tie-a").SupersededBy == "tie-c"
                && result.Single(m => m.Id == "tie-b").SupersededBy == "tie-c";
        }
        Check(tieDeterministic,
            "[MEMORY-SUPERSESSION] equal-season conflicts converge deterministically on one winner");

        // Repeated consolidation is stable and cannot make a superseded version active again.
        IReadOnlyList<LlmMemory> once = LlmMemoryLifecycle.Consolidate(new[] { a, b, c }, 3, 8, 100);
        IReadOnlyList<LlmMemory> twice = LlmMemoryLifecycle.Consolidate(once, 3, 8, 100);
        IReadOnlyList<LlmMemory> thrice = LlmMemoryLifecycle.Consolidate(twice, 3, 8, 100);
        Check(ActiveIds(once).SequenceEqual(new[] { "c" })
              && ActiveIds(twice).SequenceEqual(new[] { "c" })
              && ActiveIds(thrice).SequenceEqual(new[] { "c" }),
            "[MEMORY-SUPERSESSION] repeated consolidation never resurrects A or B");

        IReadOnlyList<LlmMemory> preSuperseded = LlmMemoryLifecycle.Consolidate(new[]
        {
            new LlmMemory
            {
                Id = "old", Type = LlmMemoryType.Fact, Content = "levy city-a was accepted",
                Season = 1, SupersededBy = "c",
            },
            c,
        }, 3, 8, 100);
        Check(ActiveIds(preSuperseded).SequenceEqual(new[] { "c" }),
            "[MEMORY-SUPERSESSION] an already-superseded input cannot become active");

        // Dedup merges metadata with order-independent max rules before conflict resolution.
        var duplicateOld = Memory("dup-old", LlmMemoryType.Fact,
            "reclaim city-a was accepted", 2, importance: 1, confidence: 1);
        var duplicateNew = Memory("dup-new", LlmMemoryType.Fact,
            "reclaim city-a was accepted", 3, importance: 2, confidence: 3);
        bool metadataMerged = true;
        foreach (LlmMemory[] order in new[]
        {
            new[] { duplicateOld, duplicateNew }, new[] { duplicateNew, duplicateOld },
        })
        {
            IReadOnlyList<LlmMemory> result = LlmMemoryLifecycle.Consolidate(order, 3, 8, 100);
            LlmMemory survivor = result.Single();
            metadataMerged &= survivor.Id == "dup-new"
                && survivor.Importance == 2 && survivor.Confidence == 3;
        }
        Check(metadataMerged,
            "[MEMORY-SUPERSESSION] duplicate metadata merges deterministically into the newest record");

        string dir = LlmTempDir("memory-supersession");
        try
        {
            var store = new LocalLlmMemoryStore(dir);
            foreach (LlmMemory memory in new[] { a, b, c })
            {
                memory.GameId = "game-a";
                memory.SeatId = "seat-a";
                store.Append(memory);
            }

            Check(store.Consolidate("game-a", "seat-a", 3, 8, 100),
                "[MEMORY-SUPERSESSION] store compaction completes");
            IReadOnlyList<LlmMemory> stored = store.Load("game-a", "seat-a");
            Check(ActiveIds(stored).SequenceEqual(new[] { "c" })
                  && stored.Single(m => m.Id == "a").SupersededBy == "c"
                  && stored.Single(m => m.Id == "b").SupersededBy == "c",
                "[MEMORY-SUPERSESSION] persisted A/B/C retain one active version and explicit metadata");

            Check(store.Consolidate("game-a", "seat-a", 3, 8, 100),
                "[MEMORY-SUPERSESSION] repeated store compaction completes");
            Check(ActiveIds(store.Load("game-a", "seat-a")).SequenceEqual(new[] { "c" }),
                "[MEMORY-SUPERSESSION] repeated store compaction keeps only the newest version active");

            // Replaying an old engine outcome after compaction must still lose to C.
            store.Append(a);
            store.Consolidate("game-a", "seat-a", 3, 8, 100);
            Check(ActiveIds(store.Load("game-a", "seat-a")).SequenceEqual(new[] { "c" }),
                "[MEMORY-SUPERSESSION] a replayed old outcome cannot resurrect itself");
        }
        finally { LlmDelete(dir); }
    }

    private static LlmMemory Memory(string id, string type, string content, int season,
        int importance = 1, int confidence = 1)
    {
        return new LlmMemory
        {
            Id = id, GameId = "game-a", SeatId = "seat-a", Type = type, Content = content,
            Season = season, Importance = importance, Confidence = confidence,
        };
    }

    private static string[] ActiveIds(IReadOnlyList<LlmMemory> memories)
    {
        return memories.Where(m => string.IsNullOrEmpty(m.SupersededBy))
            .Select(m => m.Id)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
    }
}
