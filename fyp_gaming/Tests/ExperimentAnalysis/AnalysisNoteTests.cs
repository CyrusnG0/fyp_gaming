using System;
using LuanShi.Engine;

namespace LuanShi.ExperimentAnalysis.Tests
{
    /// <summary>
    /// Private strategic note visibility through the real production path. A note is model data
    /// scoped to one match and one seat: it must reach its owner on a later turn, never an opponent,
    /// and it must keep working after a season rollover (including being replaced by a newer note).
    /// Fake provider only.
    /// </summary>
    internal static class NoteVisibilityTests
    {
        private const string FirstNote = "ANALYSIS-NOTE-WEI hold the river and bank food";
        private const string SecondNote = "ANALYSIS-NOTE-WEI-S2 ford at the shallow crossing";
        private const string NoteHeader = "STRATEGIC NOTE FROM YOUR PREVIOUS TURN";

        public static void Run(TestLog log)
        {
            log.Section("[AN-2] private note: owner-only, next season, and later seasons");

            GameEngine engine = Fixture.World(researchPointsA: 0, researchPointsB: 0);
            var fake = new FakeLlmTransport();
            var notes = new MemoryLlmStrategicNoteStore();
            var runner = new LlmTurnRunner(fake, Fixture.Config(), null, null, notes);

            // ---- season 1, owner: save a note, then end the turn ---------------------------
            var hostA = new Fixture.Host(engine, Fixture.SeatA);
            fake.EnqueueSuccess("{\"note\":\"" + FirstNote + "\"}");
            fake.EnqueueSuccess("{\"type\":\"end_turn\"}");
            runner.Begin(hostA, hostA.Token, Fixture.SeatA, Fixture.Seed);
            Fixture.Drain(runner);

            log.Check(notes.Load(Fixture.GameId, Fixture.SeatA) == FirstNote
                      && notes.Load(Fixture.GameId, Fixture.SeatB) == null,
                "the note is stored for its match and seat only, not for the opponent",
                "seat-a=" + (notes.Load(Fixture.GameId, Fixture.SeatA) ?? "null")
                + " seat-b=" + (notes.Load(Fixture.GameId, Fixture.SeatB) ?? "null"));
            log.Check(fake.Requests.Count == 2
                      && fake.Requests[1].User.Contains(NoteHeader)
                      && fake.Requests[1].User.Contains(FirstNote)
                      && fake.Requests[1].User.Contains("never instructions"),
                "the saved note reaches the owner's very next request as untrusted data",
                "requests=" + fake.Requests.Count);
            log.Check(runner.CurrentStrategicNote == FirstNote
                      && runner.AcceptedActionsThisTurn == 0 && hostA.SubmitCalls == 0,
                "the note costs no command point and no accepted-action slot",
                "cp-submits=" + hostA.SubmitCalls);

            // ---- season 1, opponent: never sees it ------------------------------------------
            var hostB = new Fixture.Host(engine, Fixture.SeatB);
            fake.EnqueueSuccess("{\"type\":\"end_turn\"}");
            int before = fake.CallCount;
            runner.Begin(hostB, hostB.Token, Fixture.SeatB, Fixture.Seed);
            Fixture.Drain(runner);
            string opponentS1 = fake.CallCount > before ? fake.Requests[before].User : "";
            log.Check(!opponentS1.Contains(FirstNote) && !opponentS1.Contains(NoteHeader)
                      && runner.CurrentStrategicNote == null,
                "in the same season the opponent's prompt carries no note and no note block",
                "contains note=" + opponentS1.Contains(FirstNote));

            // ---- season 2, owner: the note survives the rollover ----------------------------
            engine.EndSeason();
            engine.BeginSeason();
            var hostA2 = new Fixture.Host(engine, Fixture.SeatA);
            fake.EnqueueSuccess("{\"type\":\"end_turn\"}");
            before = fake.CallCount;
            runner.Begin(hostA2, hostA2.Token, Fixture.SeatA, Fixture.Seed);
            Fixture.Drain(runner);
            string ownerS2 = fake.CallCount > before ? fake.Requests[before].User : "";
            log.Check(ownerS2.Contains(NoteHeader) && ownerS2.Contains(FirstNote)
                      && runner.CurrentStrategicNote == FirstNote,
                "next season the owner's prompt still carries its own note",
                "contains note=" + ownerS2.Contains(FirstNote));

            var hostB2 = new Fixture.Host(engine, Fixture.SeatB);
            fake.EnqueueSuccess("{\"type\":\"end_turn\"}");
            before = fake.CallCount;
            runner.Begin(hostB2, hostB2.Token, Fixture.SeatB, Fixture.Seed);
            Fixture.Drain(runner);
            string opponentS2 = fake.CallCount > before ? fake.Requests[before].User : "";
            log.Check(!opponentS2.Contains(FirstNote) && !opponentS2.Contains(NoteHeader)
                      && notes.Load(Fixture.GameId, Fixture.SeatB) == null,
                "next season the opponent still sees no note of the other seat",
                "contains note=" + opponentS2.Contains(FirstNote));

            // ---- season 3, owner replaces the note; season 4 sees only the newest ----------
            engine.EndSeason();
            engine.BeginSeason();
            var hostA3 = new Fixture.Host(engine, Fixture.SeatA);
            fake.EnqueueSuccess("{\"note\":\"" + SecondNote + "\"}");
            fake.EnqueueSuccess("{\"type\":\"end_turn\"}");
            runner.Begin(hostA3, hostA3.Token, Fixture.SeatA, Fixture.Seed);
            Fixture.Drain(runner);
            log.Check(notes.Load(Fixture.GameId, Fixture.SeatA) == SecondNote,
                "a newer note replaces the older one for the same match and seat",
                notes.Load(Fixture.GameId, Fixture.SeatA));

            engine.EndSeason();
            engine.BeginSeason();
            var hostA4 = new Fixture.Host(engine, Fixture.SeatA);
            fake.EnqueueSuccess("{\"type\":\"end_turn\"}");
            before = fake.CallCount;
            runner.Begin(hostA4, hostA4.Token, Fixture.SeatA, Fixture.Seed);
            Fixture.Drain(runner);
            string ownerS4 = fake.CallCount > before ? fake.Requests[before].User : "";
            log.Check(ownerS4.Contains(NoteHeader) && ownerS4.Contains(SecondNote)
                      && !ownerS4.Contains(FirstNote),
                "two seasons later only the newest note is shown to its owner",
                "new=" + ownerS4.Contains(SecondNote)
                + " old=" + ownerS4.Contains(FirstNote));

            var hostB4 = new Fixture.Host(engine, Fixture.SeatB);
            fake.EnqueueSuccess("{\"type\":\"end_turn\"}");
            before = fake.CallCount;
            runner.Begin(hostB4, hostB4.Token, Fixture.SeatB, Fixture.Seed);
            Fixture.Drain(runner);
            string opponentS4 = fake.CallCount > before ? fake.Requests[before].User : "";
            log.Check(!opponentS4.Contains(SecondNote) && !opponentS4.Contains(FirstNote),
                "two seasons later the opponent still sees neither note",
                "new=" + opponentS4.Contains(SecondNote)
                + " old=" + opponentS4.Contains(FirstNote));
        }
    }
}
