using System;
using System.Linq;
using LuanShi.Engine;

internal static partial class Harness
{
    private static void RunLlmMemoryIsolation()
    {
        Section("LLM memory: match identity and runner isolation [MEMORY-ISOLATION]");

        string firstId = GameIdentity.NewMatchId();
        string secondId = GameIdentity.NewMatchId();
        Check(!string.IsNullOrWhiteSpace(firstId) && firstId != secondId
              && firstId.StartsWith("match-", StringComparison.Ordinal),
            "[MEMORY-ISOLATION] every new match gets a distinct non-empty identity");

        // The legacy deterministic fixtures keep their explicitly named default id.
        Check(new GameEngine(17).State.GameId == "g-001"
              && new GameEngine(17, "match-explicit").State.GameId == "match-explicit",
            "[MEMORY-ISOLATION] engine identity defaults and explicit identities are preserved");

        string dir = LlmTempDir("memory-isolation");
        try
        {
            var store = new LocalLlmMemoryStore(dir);
            string previousId = GameIdentity.NewMatchId();
            var previousEngine = LlmEngineWithGame(previousId, 17);
            LlmMemory previousMemory = LlmMemoryExtractor.FromAction(previousId, "seat-a", 2,
                new LlmAction { Type = ActionType.Reclaim, TargetId = "city-a" }, true, null);
            Check(store.Append(previousMemory),
                "[MEMORY-ISOLATION] fixture writes previous-match memory");

            // Same seed, fresh identity: a genuinely new match must not inherit the old memory.
            GameEngine sameSeedNewMatch = LlmEngineWithGame(GameIdentity.NewMatchId(), 17);
            Check(RunnerMemoryCount(store, sameSeedNewMatch, "seat-a", 17) == 0,
                "[MEMORY-ISOLATION] a new match with the same seed starts with no inherited memory");

            // Different seed, fresh identity: still isolated.
            GameEngine differentSeedNewMatch = LlmEngineWithGame(GameIdentity.NewMatchId(), 29);
            Check(RunnerMemoryCount(store, differentSeedNewMatch, "seat-a", 29) == 0,
                "[MEMORY-ISOLATION] a new match with a different seed starts with no inherited memory");

            // Recreating a runner for the same explicitly identified engine preserves its memory.
            Check(RunnerMemoryCount(store, previousEngine, "seat-a", 17) == 1,
                "[MEMORY-ISOLATION] a recreated runner loads the same match's memory");
            Check(RunnerMemoryCount(store, LlmEngineWithGame(previousId, 31), "seat-a", 31) == 1,
                "[MEMORY-ISOLATION] an explicit match identity outlives a runner seed change");

            // A runner-created memory survives runner recreation under the same engine identity.
            GameEngine acceptedEngine = LlmEngineWithGame(previousId, 17);
            var acceptedFake = new FakeLlmTransport();
            acceptedFake.EnqueueSuccess("{\"type\":\"reclaim\",\"target_id\":\"city-a\"}");
            acceptedFake.EnqueueSuccess("{\"type\":\"end_turn\"}");
            var acceptedRunner = new LlmTurnRunner(acceptedFake, new LlmConfig(), null, store);
            acceptedRunner.Begin(new LlmHarnessHost(acceptedEngine, "seat-a"), 1, "seat-a", 17);
            LlmDrain(acceptedRunner);
            LlmMemory written = store.Load(previousId, "seat-a")
                .FirstOrDefault(m => m.Id != previousMemory.Id);
            LlmTurnRunner recreated = EndTurnRunner(store, acceptedEngine, "seat-a", 17);
            Check(written != null && recreated.Memories.Any(m => m.Id == written.Id),
                "[MEMORY-ISOLATION] runner recreation keeps the memories written by the previous runner");

            // Seat isolation at the runner boundary, not only inside the store.
            GameEngine twoSeatEngine = LlmEngineWithGame(GameIdentity.NewMatchId(), 17);
            twoSeatEngine.State.Factions.Add(new FactionState
            {
                SeatId = "seat-b", Name = "Shu", Controller = ControllerType.Llm,
            });
            string twoSeatGame = twoSeatEngine.State.GameId;
            store.Append(LlmMemoryExtractor.FromAction(twoSeatGame, "seat-a", 2,
                new LlmAction { Type = ActionType.Reclaim, TargetId = "city-a" }, true, null));
            store.Append(LlmMemoryExtractor.FromAction(twoSeatGame, "seat-b", 2,
                new LlmAction { Type = ActionType.LevyTax, TargetId = "city-a" }, true, null));
            var seatBRunner = EndTurnRunner(store, twoSeatEngine, "seat-b", 17);
            Check(seatBRunner.Memories.Count == 1
                  && seatBRunner.Memories.All(m => m.SeatId == "seat-b"),
                "[MEMORY-ISOLATION] a runner loads only its own seat's memories");
        }
        finally { LlmDelete(dir); }
    }

    private static GameEngine LlmEngineWithGame(string gameId, int seed)
    {
        GameEngine engine = Build(seed, 3, morale: 100, food: 5000);
        engine.State.GameId = gameId;
        engine.State.FindFaction("seat-a").Controller = ControllerType.Llm;
        return engine;
    }

    private static int RunnerMemoryCount(ILlmMemoryStore store, GameEngine engine,
        string seatId, int seed)
    {
        return EndTurnRunner(store, engine, seatId, seed).Memories.Count;
    }

    private static LlmTurnRunner EndTurnRunner(ILlmMemoryStore store, GameEngine engine,
        string seatId, int seed)
    {
        var fake = new FakeLlmTransport();
        fake.EnqueueSuccess("{\"type\":\"end_turn\"}");
        var runner = new LlmTurnRunner(fake, new LlmConfig(), null, store);
        runner.Begin(new LlmHarnessHost(engine, seatId), 1, seatId, seed);
        LlmDrain(runner);
        return runner;
    }
}
