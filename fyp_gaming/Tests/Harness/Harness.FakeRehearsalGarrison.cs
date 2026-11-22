using System.Collections.Generic;
using System.Linq;
using LuanShi.Engine;

// Scripted FAKE rehearsal, garrison part: recruit -> convert_garrison -> march beside the two-seat research
// and treaty sequence. Every reply is written by the script, never by a model. The engine log is the
// authority for what was accepted.

internal static partial class Harness
{
    private static void RunFakeRehearsalGarrison()
    {
        Section("Spectator FAKE rehearsal: recruit -> convert_garrison -> march on both seats");
        FakeRehearsalGarrisonSequence();
        FakeRehearsalGarrisonFallbackKeepsQueueAligned();
        FakeRehearsalRefusedOrderIsReported();
        FakeRehearsalGarrisonOffIsUnchanged();
    }

    private static readonly FakeRehearsalOptions FakeRehearsalWithGarrison =
        new FakeRehearsalOptions { Research = true, Diplomacy = true, Garrison = true };

    // The default per-turn caps (6 requests, 5 actions) apply: the longest scripted turn must fit them.
    private static LlmConfig FakeRehearsalGarrisonConfig()
        => new LlmConfig
        {
            EnableResearch = true, EnableDiplomacy = true, EnableGarrisonConversion = true,
            MaxTotalRequests = 60,
        };

    private static void FakeRehearsalQueue(FakeLlmTransport fake, GameEngine engine, int seasons,
        FakeRehearsalOptions options)
    {
        List<string> order = SeatRotation.PlayOrder(engine.State);
        for (int season = 0; season < seasons; season++)
            foreach (string seat in order)
            {
                string who = seat;
                FakeRehearsalScript.EnqueueSeatTurn(fake, engine, seat, order, season == 0, options,
                    () => MovementPlanner.Build(engine, who));
            }
    }

    private static SpectatorSession FakeRehearsalRun(GameEngine engine, FakeLlmTransport fake, LlmConfig config,
        MemoryRecorderSink sink, int seasons)
    {
        var runner = new LlmTurnRunner(fake, config, sink);
        SpectatorSession session = SpectatorNew(engine, runner, seasons);
        session.Start();
        SpectatorDrain(session);
        return session;
    }

    private static void FakeRehearsalGarrisonSequence()
    {
        GameEngine engine = SpectatorEngine();
        foreach (FactionState faction in engine.State.Factions) faction.ResearchPoints = 100;
        var fake = new FakeLlmTransport();
        FakeRehearsalQueue(fake, engine, 2, FakeRehearsalWithGarrison);
        var sink = new MemoryRecorderSink();
        SpectatorSession session = FakeRehearsalRun(engine, fake, FakeRehearsalGarrisonConfig(), sink, 2);

        Check(session.State == SpectatorState.Completed && session.TurnRecords.Count == 4
              && session.TurnRecords.All(r => r.EndedBy == "end_turn" && r.Rejected == 0 && r.Invalid == 0),
            "both seats finish both seasons by end_turn with no rejected or invalid order");
        Check(CountEvents(engine.Log, "recruit") == 4 && CountEvents(engine.Log, "garrison_converted") == 4
              && CountEvents(engine.Log, "army_moved") == 4,
            "every turn reaches the engine as recruit, then convert_garrison, then march");
        Check(CountEvents(engine.Log, "research") == 2 && CountEvents(engine.Log, "treaty_proposed") == 1
              && CountEvents(engine.Log, "treaty_responded") == 1 && CountEvents(engine.Log, "treaty_ratified") == 1,
            "the treaty exchange (season 1) and research (season 2) fit beside the garrison sequence");
        Check(engine.State.ArmiesOf("seat-a").Count == 2 && engine.State.ArmiesOf("seat-b").Count == 2
              && engine.State.ArmiesOf("seat-a").All(a => a.Troops == BalanceConfig.RecruitTroopGain),
            "each seat owns the two armies it converted, with the recruited troops and none invented");

        var types = engine.Log.Records.Where(e => e.ActorSeatId == "seat-a" && e.Season == 1)
            .Select(e => e.EventType).Where(t => t == "recruit" || t == "garrison_converted" || t == "army_moved"
                                           || t == "research" || t == "treaty_proposed").ToList();
        Check(string.Join(",", types) == "recruit,garrison_converted,army_moved,treaty_proposed",
            "the first seat's opening turn runs in the scripted order: " + string.Join(",", types));

        string rows = string.Join("\n", sink.RunLines);
        // run rows hold the model text and the engine command as escaped JSON strings
        Check(rows.Contains("\\\"type\\\":\\\"recruit\\\"") && rows.Contains("\\\"type\\\":\\\"convert_garrison\\\"")
              && rows.Contains("\\\"type\\\":\\\"march\\\"") && rows.Contains("\"result_ok\":true")
              && !rows.Contains("\"result_ok\":false") && rows.Contains(FakeTurnPlan.Label),
            "the run rows name the recruit, convert_garrison and march orders and carry the FAKE label");
        Check(ExchangeIds(sink.ExchangeLines, "request").Count == fake.CallCount,
            "every scripted reply has an exchange record");
    }

    private static void FakeRehearsalGarrisonFallbackKeepsQueueAligned()
    {
        // No seat has the population to recruit and research points are 0: the garrison steps become labelled levies
        // (never an early end_turn), and the refused orders are reported, not hidden.
        GameEngine engine = SpectatorEngine();
        foreach (CityState city in engine.State.Cities) city.Population = 10;
        var fake = new FakeLlmTransport();
        FakeRehearsalQueue(fake, engine, 2, FakeRehearsalWithGarrison);
        var sink = new MemoryRecorderSink();
        SpectatorSession session = FakeRehearsalRun(engine, fake, FakeRehearsalGarrisonConfig(), sink, 2);

        // per turn: 3 steps + one extra (treaty in season 1, research in season 2) + end_turn
        int queued = 4 * (3 + 1 + 1);
        Check(session.State == SpectatorState.Completed && session.TurnRecords.Count == 4
              && session.TurnRecords.All(r => r.EndedBy == "end_turn"),
            "with no legal recruit the match completes and every seat still ends its own turn");
        Check(CountEvents(engine.Log, "recruit") == 0 && CountEvents(engine.Log, "garrison_converted") == 0,
            "no recruit or conversion happens when it is not legal");
        Check(fake.CallCount == queued,
            "every queued reply was used by its own seat, so none leaked into another turn");
        Check(string.Join("\n", sink.RunLines).Contains("garrison step unavailable"),
            "the substituted levy says the garrison step was unavailable");
    }

    private static void FakeRehearsalRefusedOrderIsReported()
    {
        // Research before the seat holds the points is scripted anyway: the engine, not the script, refuses it,
        // and the refusal is counted, logged and recorded while the turn still ends on its own end_turn.
        GameEngine engine = SpectatorEngine();
        foreach (FactionState faction in engine.State.Factions) faction.ResearchPoints = 0;
        var fake = new FakeLlmTransport();
        FakeRehearsalQueue(fake, engine, 1, new FakeRehearsalOptions { Research = true, Garrison = true });
        var sink = new MemoryRecorderSink();
        SpectatorSession session = FakeRehearsalRun(engine, fake, FakeRehearsalGarrisonConfig(), sink, 1);

        Check(session.State == SpectatorState.Completed && session.TurnRecords.All(r => r.EndedBy == "end_turn")
              && session.TurnRecords.All(r => r.Rejected == 1 && r.Accepted == 3),
            "each seat has its three garrison orders accepted and its unaffordable research order rejected");
        Check(CountEvents(engine.Log, "research") == 0 && CountEvents(engine.Log, "action_rejected") == 2,
            "the engine logged the two refusals and bought no research level");
        Check(string.Join("\n", sink.RunLines).Contains("\"result_ok\":false"),
            "the run rows record the refused order as not ok");
    }

    private static void FakeRehearsalGarrisonOffIsUnchanged()
    {
        GameEngine engine = SpectatorEngine();
        var fake = new FakeLlmTransport();
        FakeRehearsalQueue(fake, engine, 1, FakeRehearsalAll);
        SpectatorSession session = FakeRehearsalRun(engine, fake, FakeRehearsalGarrisonConfig(), null, 1);
        Check(session.State == SpectatorState.Completed && CountEvents(engine.Log, "recruit") == 0
              && CountEvents(engine.Log, "garrison_converted") == 0,
            "with the garrison switch off no recruit or conversion is scripted");
    }
}
