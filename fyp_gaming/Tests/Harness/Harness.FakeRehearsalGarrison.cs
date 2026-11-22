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
        FakeRehearsalLowCaps();
        FakeRehearsalCapReachedExactly();
        FakeRehearsalRejectedOrderEndsTurnEarly();
        FakeRehearsalMissingCityEndsEarly();
    }

    private static readonly FakeRehearsalOptions FakeRehearsalWithGarrison =
        new FakeRehearsalOptions { Research = true, Diplomacy = true, Garrison = true };

    // The default per-turn caps (6 requests, 5 actions) apply: the longest scripted turn must fit them.
    private static LlmConfig FakeRehearsalGarrisonConfig(int maxActions = 5, int maxRequests = 6, int maxInvalid = 2)
        => new LlmConfig
        {
            EnableResearch = true, EnableDiplomacy = true, EnableGarrisonConversion = true,
            MaxTotalRequests = 200, MaxActionsPerTurn = maxActions, MaxRequestsPerTurn = maxRequests,
            MaxInvalidPerTurn = maxInvalid,
        };

    private sealed class FakeRehearsalOutcome
    {
        public SpectatorSession Session;
        public FakeRehearsalDirector Director;
        public FakeLlmTransport Fake;
        public MemoryRecorderSink Sink;
    }

    // The director believes in `believed` caps (the same as the runner's unless a test says otherwise).
    private static FakeRehearsalOutcome FakeRehearsalPlay(GameEngine engine, LlmConfig config,
        FakeRehearsalOptions options, int seasons, LlmConfig believed = null)
    {
        believed = believed ?? config;
        var shared = new FakeRehearsalOptions
        {
            Research = options.Research, Diplomacy = options.Diplomacy, Garrison = options.Garrison,
            MaxActionsPerTurn = believed.MaxActionsPerTurn, MaxRequestsPerTurn = believed.MaxRequestsPerTurn,
        };
        var outcome = new FakeRehearsalOutcome { Fake = new FakeLlmTransport(), Sink = new MemoryRecorderSink() };
        outcome.Director = new FakeRehearsalDirector(engine,
            () => outcome.Session == null ? null : outcome.Session.ActiveSeatId, shared,
            seat => MovementPlanner.Build(engine, seat), engine.State.Season);
        int turns = seasons * SeatRotation.PlayOrder(engine.State).Count;
        for (int i = 0; i < turns; i++) outcome.Director.EnqueueSeatTurn(outcome.Fake);
        var runner = new LlmTurnRunner(outcome.Fake, config, outcome.Sink);
        outcome.Session = SpectatorNew(engine, runner, seasons);
        outcome.Session.Start();
        SpectatorDrain(outcome.Session);
        return outcome;
    }

    // Every reply must have gone to the seat that was acting, turn by turn: group the replies the director
    // handed out by (season, seat) and compare them with the turn records and the engine log.
    private static bool FakeRehearsalRepliesStayWithTheirTurn(FakeRehearsalOutcome run, out string why)
    {
        var dispatched = run.Director.Dispatched;
        int at = 0;
        foreach (SpectatorTurnRecord record in run.Session.TurnRecords)
        {
            for (int i = 0; i < record.Requests; i++, at++)
            {
                if (at >= dispatched.Count) { why = "fewer replies than requests"; return false; }
                FakeRehearsalDispatch d = dispatched[at];
                if (d.SeatId != record.SeatId || d.Season != record.Season || d.Index != i)
                { why = "reply " + at + " went to " + d.SeatId + "/" + d.Season + "#" + d.Index
                        + " but turn is " + record.SeatId + "/" + record.Season + "#" + i; return false; }
            }
        }
        why = at == dispatched.Count ? null : (dispatched.Count - at) + " replies were never read";
        return why == null;
    }

    private static void FakeRehearsalGarrisonSequence()
    {
        GameEngine engine = SpectatorEngine();
        foreach (FactionState faction in engine.State.Factions) faction.ResearchPoints = 100;
        FakeRehearsalOutcome run = FakeRehearsalPlay(engine, FakeRehearsalGarrisonConfig(), FakeRehearsalWithGarrison, 2);
        SpectatorSession session = run.Session;

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

        FakeRehearsalRepliesCheck(run, "the full sequence");
        string rows = string.Join("\n", run.Sink.RunLines);
        // run rows hold the model text and the engine command as escaped JSON strings
        Check(rows.Contains("\\\"type\\\":\\\"recruit\\\"") && rows.Contains("\\\"type\\\":\\\"convert_garrison\\\"")
              && rows.Contains("\\\"type\\\":\\\"march\\\"") && rows.Contains("\"result_ok\":true")
              && !rows.Contains("\"result_ok\":false") && rows.Contains(FakeTurnPlan.Label),
            "the run rows name the recruit, convert_garrison and march orders and carry the FAKE label");
        Check(ExchangeIds(run.Sink.ExchangeLines, "request").Count == run.Fake.CallCount,
            "every scripted reply has an exchange record");
    }

    private static void FakeRehearsalRepliesCheck(FakeRehearsalOutcome run, string what)
    {
        string why;
        Check(FakeRehearsalRepliesStayWithTheirTurn(run, out why),
            what + ": every reply went to the seat and turn that read it" + (why == null ? "" : " (" + why + ")"));
    }

    private static void FakeRehearsalGarrisonFallbackKeepsQueueAligned()
    {
        // No seat has the population to recruit: the garrison steps become labelled levies, never an early end_turn.
        GameEngine engine = SpectatorEngine();
        foreach (CityState city in engine.State.Cities) city.Population = 10;
        FakeRehearsalOutcome run = FakeRehearsalPlay(engine, FakeRehearsalGarrisonConfig(), FakeRehearsalWithGarrison, 2);
        SpectatorSession session = run.Session;

        Check(session.State == SpectatorState.Completed && session.TurnRecords.Count == 4
              && session.TurnRecords.All(r => r.EndedBy == "end_turn"),
            "with no legal recruit the match completes and every seat still ends its own turn");
        Check(CountEvents(engine.Log, "recruit") == 0 && CountEvents(engine.Log, "garrison_converted") == 0,
            "no recruit or conversion happens when it is not legal");
        FakeRehearsalRepliesCheck(run, "unavailable garrison steps");
        Check(string.Join("\n", run.Sink.RunLines).Contains("garrison step unavailable"),
            "the substituted levy says the garrison step was unavailable");
    }

    private static void FakeRehearsalRefusedOrderIsReported()
    {
        // Research before the seat holds the points is scripted anyway: the engine, not the script, refuses it,
        // and the refusal is counted, logged and recorded while the turn still ends on its own end_turn.
        GameEngine engine = SpectatorEngine();
        foreach (FactionState faction in engine.State.Factions) faction.ResearchPoints = 0;
        FakeRehearsalOutcome run = FakeRehearsalPlay(engine, FakeRehearsalGarrisonConfig(),
            new FakeRehearsalOptions { Research = true, Garrison = true }, 1);
        SpectatorSession session = run.Session;

        Check(session.State == SpectatorState.Completed && session.TurnRecords.All(r => r.EndedBy == "end_turn")
              && session.TurnRecords.All(r => r.Rejected == 1 && r.Accepted == 3),
            "each seat has its three garrison orders accepted and its unaffordable research order rejected");
        Check(CountEvents(engine.Log, "research") == 0 && CountEvents(engine.Log, "action_rejected") == 2,
            "the engine logged the two refusals and bought no research level");
        Check(string.Join("\n", run.Sink.RunLines).Contains("\"result_ok\":false"),
            "the run rows record the refused order as not ok");
        FakeRehearsalRepliesCheck(run, "refused orders");
    }

    private static void FakeRehearsalGarrisonOffIsUnchanged()
    {
        GameEngine engine = SpectatorEngine();
        FakeRehearsalOutcome run = FakeRehearsalPlay(engine, FakeRehearsalGarrisonConfig(), FakeRehearsalAll, 1);
        Check(run.Session.State == SpectatorState.Completed && CountEvents(engine.Log, "recruit") == 0
              && CountEvents(engine.Log, "garrison_converted") == 0,
            "with the garrison switch off no recruit or conversion is scripted");
        FakeRehearsalRepliesCheck(run, "garrison off");
    }

    // Per-turn caps below three, at three and above: the script shortens to what the caps allow, each turn
    // still closes on its own end_turn, and no reply is read by a seat it was not built for.
    private static void FakeRehearsalLowCaps()
    {
        // actions, requests, expected recruit/convert/march per run, expected research orders
        int[][] cases =
        {
            new[] { 1, 6, 0, 0, 0, 0 }, new[] { 2, 6, 4, 0, 0, 0 }, new[] { 3, 6, 4, 4, 0, 0 },
            new[] { 4, 6, 4, 4, 4, 0 }, new[] { 5, 2, 4, 0, 0, 0 }, new[] { 5, 3, 4, 4, 0, 0 },
            new[] { 6, 7, 4, 4, 4, 4 },
        };
        foreach (int[] c in cases)
        {
            GameEngine engine = SpectatorEngine();
            foreach (FactionState faction in engine.State.Factions) faction.ResearchPoints = 100;
            FakeRehearsalOutcome run = FakeRehearsalPlay(engine, FakeRehearsalGarrisonConfig(c[0], c[1]),
                FakeRehearsalWithGarrison, 2);
            string label = "caps actions=" + c[0] + " requests=" + c[1];
            Check(run.Session.State == SpectatorState.Completed && run.Session.TurnRecords.Count == 4
                  && run.Session.TurnRecords.All(r => r.EndedBy == "end_turn" && r.Rejected == 0
                      && r.Requests <= c[1] && r.Actions <= c[0]),
                label + ": every turn ends on its own end_turn inside the caps, nothing rejected");
            Check(CountEvents(engine.Log, "recruit") == c[2] && CountEvents(engine.Log, "garrison_converted") == c[3]
                  && CountEvents(engine.Log, "army_moved") == c[4] && CountEvents(engine.Log, "research") == c[5],
                label + ": the script keeps what fits: recruit " + CountEvents(engine.Log, "recruit")
                + ", convert " + CountEvents(engine.Log, "garrison_converted")
                + ", march " + CountEvents(engine.Log, "army_moved")
                + ", research " + CountEvents(engine.Log, "research"));
            FakeRehearsalRepliesCheck(run, label);
        }
    }

    // The runner ends a turn on its action cap without reading another reply. Here the script believes in
    // larger caps than the runner enforces, so every turn is cut off exactly at the cap with replies left over.
    private static void FakeRehearsalCapReachedExactly()
    {
        GameEngine engine = SpectatorEngine();
        foreach (FactionState faction in engine.State.Factions) faction.ResearchPoints = 100;
        FakeRehearsalOutcome run = FakeRehearsalPlay(engine, FakeRehearsalGarrisonConfig(3, 6),
            FakeRehearsalWithGarrison, 2, FakeRehearsalGarrisonConfig(5, 6));
        var records = run.Session.TurnRecords;
        Check(run.Session.State == SpectatorState.Completed && records.Count == 4
              && records.All(r => r.Requests == 3 && r.Accepted == 3 && r.Rejected == 0 && r.EndedBy != "end_turn"),
            "each turn stops exactly at the action cap of three, before its end_turn is read");
        Check(run.Fake.CallCount == 12 && run.Director.Dispatched.Count == 12,
            "only the twelve replies the turns asked for were read; the rest stayed queued");
        Check(run.Director.Dispatched.Select(d => d.SeatId).SequenceEqual(
                new[] { "seat-a", "seat-a", "seat-a", "seat-b", "seat-b", "seat-b",
                        "seat-a", "seat-a", "seat-a", "seat-b", "seat-b", "seat-b" }),
            "the replies read follow the seat rotation");
        Check(run.Director.Dispatched.Where(d => d.Index == 0).All(d => d.OrderType == "recruit")
              && run.Director.Dispatched.Where(d => d.Index == 2).All(d => d.OrderType == "march"),
            "every turn, including the second seat's, starts with its own recruit and reaches its march");
        Check(CountEvents(engine.Log, "recruit") == 4 && CountEvents(engine.Log, "army_moved") == 4,
            "the engine received each seat's own orders");
        FakeRehearsalRepliesCheck(run, "cap reached exactly");
    }

    // A rejected order that hits the invalid-order cap ends the turn before end_turn is read. That leftover
    // reply must not become the next seat's order.
    private static void FakeRehearsalRejectedOrderEndsTurnEarly()
    {
        GameEngine engine = SpectatorEngine();
        foreach (FactionState faction in engine.State.Factions) faction.ResearchPoints = 0;
        FakeRehearsalOutcome run = FakeRehearsalPlay(engine, FakeRehearsalGarrisonConfig(5, 6, 1),
            new FakeRehearsalOptions { Research = true, Garrison = true }, 2);
        var records = run.Session.TurnRecords;
        Check(run.Session.State == SpectatorState.Completed && records.Count == 4
              && records.All(r => r.Rejected == 1 && r.Accepted == 3 && r.EndedBy != "end_turn"),
            "a rejected research order at an invalid cap of one ends every turn before its end_turn");
        Check(run.Director.Dispatched.Where(d => d.Index == 0).All(d => d.OrderType == "recruit"),
            "the seat after an early-ended turn still starts with its own recruit, not a leftover reply");
        Check(CountEvents(engine.Log, "action_rejected") == 4 && CountEvents(engine.Log, "research") == 0,
            "only the refused research orders were rejected");
        FakeRehearsalRepliesCheck(run, "rejected order ends the turn");
    }

    // A seat that owns no city has no legal garrison step: it ends its turn at once, and the replies it did
    // not use are read by the other seat as that seat's own steps.
    private static void FakeRehearsalMissingCityEndsEarly()
    {
        GameEngine engine = SpectatorEngine();
        engine.State.Cities.Find(c => c.Id == "city-b").OwnerSeatId = "seat-a";
        FakeRehearsalOutcome run = FakeRehearsalPlay(engine, FakeRehearsalGarrisonConfig(),
            FakeRehearsalWithGarrison, 2);
        FakeRehearsalRepliesCheck(run, "a seat with no city");
        Check(run.Session.TurnRecords.Where(r => r.SeatId == "seat-b").All(r => r.Requests == 1 && r.EndedBy == "end_turn")
              && run.Session.TurnRecords.Where(r => r.SeatId == "seat-a").All(r => r.Requests > 3),
            "the seat with no city ends at its first request, so replies are left over for the other seat");
        Check(run.Director.Dispatched.All(d => d.SeatId == "seat-a" || d.OrderType == "end_turn"),
            "the seat with no city is only ever given end_turn, never an order for a city it does not own");
        Check(engine.Log.Records.Where(e => e.EventType == "recruit" || e.EventType == "garrison_converted"
                  || e.EventType == "army_moved").All(e => e.ActorSeatId == "seat-a"),
            "every garrison order in the engine log belongs to the seat that owns a city");
        Check(run.Director.Dispatched.Where(d => d.SeatId == "seat-a" && d.Index == 0)
                  .All(d => d.OrderType == "recruit"),
            "the seat that does own a city is not given the other seat's leftover end_turn");
    }
}
