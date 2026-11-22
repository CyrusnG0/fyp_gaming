using System.Collections.Generic;
using System.Linq;
using LuanShi.Engine;

// The scripted two-seat FAKE rehearsal: every reply below is written by the script, not by a model.
// The point is to prove which scripted orders reach the authoritative engine and the run logs.

internal static partial class Harness
{
    private static void RunFakeRehearsalScript()
    {
        Section("Spectator FAKE rehearsal: both seats, research and a treaty answer reach the engine");
        FakeRehearsalPlanShape();
        FakeRehearsalOptInOrdersOnly();
        FakeRehearsalReachesEngineAndLogs();
    }

    private static readonly FakeRehearsalOptions FakeRehearsalAll =
        new FakeRehearsalOptions { Research = true, Diplomacy = true };

    private static void FakeRehearsalPlanShape()
    {
        GameEngine engine = SpectatorEngine();
        List<string> order = SeatRotation.PlayOrder(engine.State);
        IReadOnlyList<string> first = FakeRehearsalScript.Build(null, engine.Observe("seat-a"), "seat-a",
            order, true, FakeRehearsalAll);
        IReadOnlyList<string> second = FakeRehearsalScript.Build(null, engine.Observe("seat-b"), "seat-b",
            order, true, FakeRehearsalAll);
        IReadOnlyList<string> later = FakeRehearsalScript.Build(null, engine.Observe("seat-a"), "seat-a",
            order, false, FakeRehearsalAll);

        Check(first.Count == 4 && first[1].Contains("\"type\":\"propose_treaty\"")
              && first[1].Contains("\"target_id\":\"seat-b\"") && first[2].Contains("\"type\":\"research\"")
              && first[3].Contains("\"type\":\"end_turn\""),
            "the first seat proposes a treaty to the second, researches, and ends its turn last");
        Check(second.Count == 4 && second[1].Contains("\"type\":\"respond_treaty\"")
              && second[1].Contains("\"target_id\":\"seat-a\"") && second[1].Contains("\"response\":\"accept\""),
            "the second seat answers the first seat's offer in the same opening season");
        Check(later.Count == 3 && !later.Any(l => l.Contains("treaty")) && later[1].Contains("\"type\":\"research\""),
            "after the opening season there is no new treaty script, only research");
        Check(first.Concat(second).Concat(later).All(l => l.Contains(FakeTurnPlan.Label)),
            "every scripted reply is labelled FAKE");
    }

    private static void FakeRehearsalOptInOrdersOnly()
    {
        GameEngine engine = SpectatorEngine();
        List<string> order = SeatRotation.PlayOrder(engine.State);
        Observation obs = engine.Observe("seat-a");
        IReadOnlyList<string> baseline = FakeTurnPlan.Build(null, obs, "seat-a");
        IReadOnlyList<string> off = FakeRehearsalScript.Build(null, obs, "seat-a", order, true,
            new FakeRehearsalOptions());
        Check(off.SequenceEqual(baseline),
            "with research and diplomacy off the script is exactly the existing levy + end_turn plan");

        IReadOnlyList<string> researchOnly = FakeRehearsalScript.Build(null, obs, "seat-a", order, true,
            new FakeRehearsalOptions { Research = true });
        Check(researchOnly.Count == 3 && !researchOnly.Any(l => l.Contains("treaty")),
            "diplomacy off means no treaty order is scripted");

        foreach (string line in FakeRehearsalScript.Build(null, obs, "seat-a", order, true, FakeRehearsalAll))
        {
            bool ok = LlmActionParser.Parse(line, false, true, true).Ok;
            bool refusedWhenOff = !LlmActionParser.Parse(line).Ok;
            bool opt = line.Contains("research") || line.Contains("treaty");
            Check(ok && (!opt || refusedWhenOff),
                "a scripted order parses with its switch on and is refused before the engine with it off: "
                + (line.Length > 40 ? line.Substring(0, 40) : line));
        }

        Check(!LlmActionParser.Parse("{\"type\":\"convert_garrison\",\"target_id\":\"city-a\",\"param_a\":1}",
                true, true, true).Ok,
            "garrison conversion is not in this branch's model vocabulary, so no fake plan can script it");
    }

    private static void FakeRehearsalReachesEngineAndLogs()
    {
        GameEngine engine = SpectatorEngine();
        foreach (FactionState faction in engine.State.Factions) faction.ResearchPoints = 100;

        var fake = new FakeLlmTransport();
        List<string> order = SeatRotation.PlayOrder(engine.State);
        for (int season = 0; season < 2; season++)
            foreach (string seat in order)
                foreach (string reply in FakeRehearsalScript.Build(null, engine.Observe(seat), seat, order,
                             season == 0, FakeRehearsalAll))
                    fake.EnqueueSuccess(reply, 0, 120, 20);

        var config = new LlmConfig
        {
            MaxRequestsPerTurn = 8, MaxActionsPerTurn = 6, MaxInvalidPerTurn = 6, MaxTotalRequests = 40,
            EnableResearch = true, EnableDiplomacy = true,
        };
        var sink = new MemoryRecorderSink();
        var runner = new LlmTurnRunner(fake, config, sink);
        SpectatorSession session = SpectatorNew(engine, runner, 2);
        session.Start();
        SpectatorDrain(session);

        Check(session.State == SpectatorState.Completed && session.TurnRecords.Count == 4,
            "both seats play both seasons and the match completes");
        Check(session.TurnRecords.All(r => r.Rejected == 0 && r.Invalid == 0 && r.Accepted >= 2),
            "no scripted order is rejected or invalid in the fixture");
        Check(session.TurnRecords.Select(r => r.SeatId).Distinct().Count() == 2,
            "the records cover both seats");

        Check(CountEvents(engine.Log, "research") == 4
              && engine.State.FindFaction("seat-a").ResearchLevel(ResearchLine.Agri) == 2
              && engine.State.FindFaction("seat-b").ResearchLevel(ResearchLine.Agri) == 2,
            "each seat's research order reaches the engine and buys a level per season");
        Check(CountEvents(engine.Log, "treaty_proposed") == 1 && CountEvents(engine.Log, "treaty_ratified") == 1
              && engine.State.Diplomacy.HasTreaty(TreatyType.Nap, "seat-a", "seat-b"),
            "the proposal, the answer and the rollover ratification all reach the engine");

        List<string> requests = ExchangeIds(sink.ExchangeLines, "request");
        List<string> results = ExchangeIds(sink.ExchangeLines, "result");
        string rows = string.Join("\n", sink.RunLines);
        Check(requests.Count == fake.CallCount && results.SequenceEqual(requests),
            "every scripted reply has one exchange request and one result under the same id");
        Check(rows.Contains("propose_treaty") && rows.Contains("respond_treaty") && rows.Contains("research"),
            "the run rows name the research, proposal and answer orders");
        Check(rows.Contains(FakeTurnPlan.Label),
            "the run rows carry the FAKE label");
    }
}
