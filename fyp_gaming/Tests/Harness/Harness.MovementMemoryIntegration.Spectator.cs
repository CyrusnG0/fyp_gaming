using System;
using System.Collections.Generic;
using System.Linq;
using LuanShi.Engine;

// [LS020] Integration coverage (cont.): spectator host wiring, terminal stop, capture wording and
// hidden-world equivalence for the combined movement + memory build. Offline only.

internal static partial class Harness
{
    // ---- 7: the real spectator host alternates two LLM seats ---------------

    private static void CombinedSpectatorHostAlternatesSeatScopedPrompts()
    {
        string dir = LlmTempDir("ls020-combined-spectator");
        try
        {
            var store = new LocalLlmMemoryStore(dir);
            var engine = CombinedCaptureWorld(9707);
            CombinedStoreMemory(store, CombinedGameId, "seat-a", "seat-a remembers its own plan");
            CombinedStoreMemory(store, CombinedGameId, "seat-b", "seat-b remembers its own plan");

            var order = SeatRotation.PlayOrder(engine.State);
            var plans = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (string seat in order) plans[seat] = CombinedFakePlan(engine, seat);

            var fake = new FakeLlmTransport();
            foreach (string seat in order)
                foreach (string command in plans[seat]) fake.EnqueueSuccess(command);
            var runner = new LlmTurnRunner(fake, new LlmConfig
            {
                MaxRequestsPerTurn = 6, MaxActionsPerTurn = 4, MaxTotalRequests = 40,
            }, null, store);
            var session = new SpectatorSession(engine, runner, 1, 4242);

            session.Start();
            SpectatorDrain(session);

            MMCheck(order.Count == 2 && order[0] == "seat-a" && order[1] == "seat-b",
                "the combined fixture plays seat-a then seat-b");
            MMCheck(session.State == SpectatorState.Completed
               && session.TurnRecords.Count == 2
               && session.TurnRecords[0].SeatId == "seat-a"
               && session.TurnRecords[1].SeatId == "seat-b"
               && session.TurnRecords.All(r => r.EndedBy == "end_turn"),
                "the actual spectator host alternates two LLM players for one clean season");
            MMCheck(session.TotalRequests == runner.TotalRequests
               && session.TotalRequests == fake.CallCount
               && fake.CallCount == plans["seat-a"].Count + plans["seat-b"].Count,
                "both seats share one runner's session request accounting");

            int split = plans["seat-a"].Count;
            string aPrompts = string.Join("\n", fake.Requests.Take(split).Select(r => r.User));
            string bPrompts = string.Join("\n", fake.Requests.Skip(split).Select(r => r.User));
            MMCheck(aPrompts.Contains("seat-a remembers its own plan")
               && !aPrompts.Contains("seat-b remembers its own plan")
               && aPrompts.Contains("army_id=army-a-1")
               && !aPrompts.Contains("army_id=army-b-1"),
                "seat-a's actual host prompts carry only its own memory and candidates");
            MMCheck(bPrompts.Contains("seat-b remembers its own plan")
               && !bPrompts.Contains("seat-a remembers its own plan")
               && bPrompts.Contains("army_id=army-b-1")
               && !bPrompts.Contains("army_id=army-a-1"),
                "seat-b's actual host prompts carry only its own memory and candidates");
            MMCheck(!bPrompts.Contains("march army-a-1 -> ok")
               && !bPrompts.Contains("levy_tax city-a -> ok")
               && bPrompts.Contains("march army-b-1 -> ok"),
                "seat-b's history never contains a seat-a action line");
            MMCheck(runner.Memories.All(m => m.SeatId == "seat-b")
               && runner.Memories.Any(m => m.Content.Contains("seat-b remembers its own plan")),
                "after the second seat the runner holds only that seat's memory");
        }
        finally { LlmDelete(dir); }
    }

    // ---- 8: a winning march costs no further provider request --------------

    private static void CombinedGameOverStopsBeforeProvider()
    {
        string dir = LlmTempDir("ls020-combined-gameover");
        try
        {
            var store = new LocalLlmMemoryStore(dir);
            var engine = W2VictoryWorld(9708);
            var host = new LlmPlanningHost(engine, "seat-a");
            var fake = new FakeLlmTransport();
            fake.EnqueueSuccess(W2March("army-a-1", 2, 1));
            fake.EnqueueSuccess(W2EndTurn());
            fake.EnqueueSuccess(W2EndTurn());
            var runner = new LlmTurnRunner(fake,
                new LlmConfig { MaxRequestsPerTurn = 8, MaxActionsPerTurn = 5 }, null, store);
            runner.Begin(host, host.Token, "seat-a", 7);
            LlmDrain(runner);

            MMCheck(string.Equals(engine.State.WinnerSeatId, "seat-a", StringComparison.Ordinal)
               && runner.EndReason == EndReason.GameOver
               && fake.CallCount == 1 && host.SubmitCalls == 1,
                "the game-winning march stops with no post-game provider request");
            MMCheck(store.Load("g-001", "seat-a").Any(m => m.Type == LlmMemoryType.Fact
                    && m.Content.Contains("march army-a-1 was accepted")),
                "the winning march result is still extracted into memory");

            var sessionEngine = W2VictoryWorld(9709);
            var sessionStore = new LocalLlmMemoryStore(dir + "-spectator");
            var sessionFake = new FakeLlmTransport();
            sessionFake.EnqueueSuccess(W2March("army-a-1", 2, 1));
            sessionFake.EnqueueSuccess(W2EndTurn());
            var sessionRunner = new LlmTurnRunner(sessionFake,
                new LlmConfig { MaxRequestsPerTurn = 8, MaxActionsPerTurn = 5 },
                null, sessionStore);
            var session = new SpectatorSession(sessionEngine, sessionRunner, 2, 4242);
            session.Start();
            SpectatorDrain(session);

            MMCheck(session.State == SpectatorState.Completed
               && session.StopReason == "game_over"
               && sessionFake.CallCount == 1
               && session.TurnRecords.Count == 1
               && session.TurnRecords[0].EndReason == EndReason.GameOver
               && session.TurnRecords[0].EndedBy == "game over"
               && session.GameOverEndings == 1,
                "the actual spectator host completes on game over and never starts the next seat");
            MMCheck(sessionStore.Load("g-001", "seat-a").Any(m => m.Type == LlmMemoryType.Fact
                    && m.Content.Contains("march army-a-1 was accepted")),
                "the spectator host's winning march is also remembered");
            LlmDelete(dir + "-spectator");
        }
        finally { LlmDelete(dir); }
    }

    // ---- 9: a plain move into an owned city is not a capture ---------------

    private static void CombinedOwnedCityMoveIsNotACapture()
    {
        string dir = LlmTempDir("ls020-combined-owncity");
        try
        {
            var store = new LocalLlmMemoryStore(dir);
            var engine = MovementFixture(9710, 2, UnitTypes.Infantry);
            var host = new LlmPlanningHost(engine, "seat-a");
            var fake = new FakeLlmTransport();
            fake.EnqueueSuccess(W2March("army-a-1", 0, 0));   // own city cell, already ours
            fake.EnqueueSuccess(W2EndTurn());
            var runner = new LlmTurnRunner(fake,
                new LlmConfig { MaxRequestsPerTurn = 4, MaxActionsPerTurn = 4 }, null, store);
            runner.Begin(host, host.Token, "seat-a", 7);
            LlmDrain(runner);

            ArmyState army = engine.State.FindArmy("army-a-1");
            IReadOnlyList<LlmMemory> stored = store.Load("g-001", "seat-a");
            MMCheck(army.X == 0 && army.Z == 0
               && runner.RecentHistory.Contains("march army-a-1 -> ok")
               && runner.Activity.Any(a => a.Kind == "result" && a.Text == "Accepted by engine.")
               && runner.Activity.All(a => a.Text == null || !a.Text.Contains("Captured")),
                "moving into an already-owned city is reported as a plain accepted move");
            MMCheck(stored.Any(m => m.Content == "march army-a-1 was accepted")
               && stored.All(m => m.Content.IndexOf("Captured", StringComparison.Ordinal) < 0)
               && string.Equals(engine.State.FindCity("city-a").OwnerSeatId, "seat-a",
                    StringComparison.Ordinal),
                "no capture is claimed in memory when only the owner's city was entered");
        }
        finally { LlmDelete(dir); }
    }

    // ---- 10: hidden worlds change neither candidates nor prompt ------------

    private static void CombinedHiddenWorldCandidateEquivalence()
    {
        var baseline = HiddenWorld(9711, "baseline");
        Observation baseObs = baseline.Observe("seat-a");
        MovementView baseView = MovementPlanner.Build(baseline, "seat-a");
        var memories = new List<LlmMemory>
        {
            new LlmMemory
            {
                Id = "combined-hidden-memory", GameId = baseObs.GameId, SeatId = "seat-a",
                Type = LlmMemoryType.Lesson, Content = "march army-a-1 was accepted",
                Season = baseObs.Season, Importance = 2, Confidence = 1,
            },
        };
        string basePrompt = PromptBuilder.UserPrompt(baseObs, ActionCatalogue.Build(baseObs),
            new List<string>(), 4, 3, baseView, memories);

        MMCheck(baseView.Armies.Count == 1 && baseView.Armies[0].Options.Count > 0
           && basePrompt.Contains("MOVEMENT OPTIONS")
           && basePrompt.Contains("RELEVANT LONG-TERM MEMORY")
           && basePrompt.Contains("march army-a-1 was accepted"),
            "the combined baseline prompt carries both candidates and memory");

        string[] variants = { "army", "adjacent", "city", "tower" };
        foreach (string variant in variants)
        {
            var world = HiddenWorld(9711, variant);
            Observation variantObs = world.Observe("seat-a");
            string variantPrompt = PromptBuilder.UserPrompt(variantObs,
                ActionCatalogue.Build(variantObs), new List<string>(), 4, 3,
                MovementPlanner.Build(world, "seat-a"), memories);
            MMCheck(MovementPlanner.Build(world, "seat-a").ToJson() == baseView.ToJson()
               && variantPrompt == basePrompt,
                "hidden " + variant + ": candidates and combined prompt stay byte-identical");
        }
    }
}
