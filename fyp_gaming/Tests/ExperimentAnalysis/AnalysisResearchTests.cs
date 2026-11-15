using System;
using LuanShi.Engine;

namespace LuanShi.ExperimentAnalysis.Tests
{
    /// <summary>
    /// Research visibility through the real production path: <see cref="LlmTurnRunner"/> + the real
    /// <see cref="GameEngine"/> validator, the production <see cref="PromptBuilder"/> and query
    /// <see cref="ActionCatalogue"/>, with the fake provider only. The questions are the ones an
    /// evidence report depends on: what a seat was offered, what it bought, who may see that, and
    /// whether the answer survives a season rollover.
    /// </summary>
    internal static class ResearchVisibilityTests
    {
        public static void Run(TestLog log)
        {
            log.Section("[AN-1] research purchase, owner-only details and opponent prompt isolation");

            GameEngine engine = Fixture.World(researchPointsA: 987654, researchPointsB: 77);
            var fake = new FakeLlmTransport();
            var runner = new LlmTurnRunner(fake, Fixture.Config(), null, null,
                new MemoryLlmStrategicNoteStore());
            int levelOneCost = BalanceConfig.ResearchLevelCost(1);
            int orderCost = BalanceConfig.CommandPointCost(ActionType.Research);

            var hostA = new Fixture.Host(engine, Fixture.SeatA);
            fake.EnqueueSuccess("{\"type\":\"research\",\"args\":{\"line\":\"" + ResearchLine.Agri + "\"}}");
            fake.EnqueueSuccess("{\"type\":\"end_turn\"}");
            runner.Begin(hostA, hostA.Token, Fixture.SeatA, Fixture.Seed);

            string seatASystem = fake.Requests.Count > 0 ? fake.Requests[0].System : "";
            string seatAUser = fake.Requests.Count > 0 ? fake.Requests[0].User : "";
            log.Check(seatASystem.Contains("RESEARCH ORDERS")
                      && seatAUser.Contains("Prompt version: "
                                            + PromptBuilder.DeliberationResearchPromptVersion + "\n")
                      && seatAUser.Contains("type=research args={\"line\":\"agri\"}")
                      && seatAUser.Contains("label=\"Research Agriculture to level 1 for "
                                            + levelOneCost + " research points\"")
                      && seatAUser.Contains("type=research args={\"line\":\"governance\"}"),
                "the first request advertises the research rules, its own identity and every affordable line",
                "system has block=" + seatASystem.Contains("RESEARCH ORDERS"));

            Fixture.Drain(runner);
            FactionState seatA = engine.State.FindFaction(Fixture.SeatA);
            log.Check(runner.State == RunnerState.Finished && runner.EndReason == EndReason.ModelEndedTurn
                      && fake.CallCount == 2 && hostA.SubmitCalls == 1
                      && runner.AcceptedActionsThisTurn == 1,
                "one research order plus end_turn finish the turn with one accepted action",
                runner.EndReason + " calls=" + fake.CallCount + " submits=" + hostA.SubmitCalls);
            log.Check(seatA.ResearchLevel(ResearchLine.Agri) == 1
                      && seatA.ResearchPoints == 987654 - levelOneCost
                      && seatA.CommandPoints == BalanceConfig.CommandPointsPerSeason - orderCost,
                "the engine bought level 1, charged its points and one command point",
                "level=" + seatA.ResearchLevel(ResearchLine.Agri)
                + " points=" + seatA.ResearchPoints + " cp=" + seatA.CommandPoints);

            EventRecord researchEvent = Fixture.FindEvent(engine.Log, "research");
            bool ownerOnly = researchEvent != null && researchEvent.Visibility == "owner_only"
                             && researchEvent.ActorSeatId == Fixture.SeatA
                             && researchEvent.Payload.ContainsKey("line")
                             && researchEvent.Payload["line"] == ResearchLine.Agri
                             && researchEvent.Payload["level"] == "1";
            log.Check(ownerOnly,
                "the canonical research event is owner_only and carries the line and level",
                researchEvent == null ? "no research event"
                    : researchEvent.Visibility + " actor=" + researchEvent.ActorSeatId);

            bool seatBSawIt = false;
            foreach (EventView view in engine.Observe(Fixture.SeatB).Events)
                if (view.EventType == "research") seatBSawIt = true;
            Observation seatBView = engine.Observe(Fixture.SeatB);
            log.Check(!seatBSawIt && seatBView.ResearchLevel(ResearchLine.Agri) == 0
                      && seatBView.ResearchPoints == 77,
                "the opponent observation carries none of the buyer's research event, level or points",
                "level=" + seatBView.ResearchLevel(ResearchLine.Agri)
                + " points=" + seatBView.ResearchPoints);

            // ---- opponent turn: same season, its own candidates only -------------------------
            var hostB = new Fixture.Host(engine, Fixture.SeatB);
            fake.EnqueueSuccess("{\"type\":\"end_turn\"}");
            int before = fake.CallCount;
            runner.Begin(hostB, hostB.Token, Fixture.SeatB, Fixture.Seed);
            Fixture.Drain(runner);
            string seatBUser = fake.CallCount > before ? fake.Requests[before].User : "";
            log.Check(seatBUser.Contains("label=\"Research Agriculture to level 1 for "
                                         + levelOneCost + " research points\"")
                      && !seatBUser.Contains("Research Agriculture to level 2")
                      && !seatBUser.Contains((987654 - levelOneCost).ToString())
                      && !seatBUser.Contains("987654"),
                "the opponent prompt shows only its own level-1 candidate and none of the buyer's figures",
                "contains level 2=" + seatBUser.Contains("Research Agriculture to level 2"));

            // ---- next season: the purchase persists and stays private -----------------------
            engine.EndSeason();
            engine.BeginSeason();
            var hostA2 = new Fixture.Host(engine, Fixture.SeatA);
            fake.EnqueueSuccess("{\"type\":\"end_turn\"}");
            before = fake.CallCount;
            runner.Begin(hostA2, hostA2.Token, Fixture.SeatA, Fixture.Seed);
            Fixture.Drain(runner);
            string seatSAgain = fake.CallCount > before ? fake.Requests[before].User : "";
            log.Check(seatSAgain.Contains("label=\"Research Agriculture to level 2 for "
                                          + BalanceConfig.ResearchLevelCost(2) + " research points\""),
                "next season the buyer is offered level 2, so its purchase persisted",
                "level 2 candidate present=" + seatSAgain.Contains("Research Agriculture to level 2"));

            var hostB2 = new Fixture.Host(engine, Fixture.SeatB);
            fake.EnqueueSuccess("{\"type\":\"end_turn\"}");
            before = fake.CallCount;
            runner.Begin(hostB2, hostB2.Token, Fixture.SeatB, Fixture.Seed);
            Fixture.Drain(runner);
            string seatBAgain = fake.CallCount > before ? fake.Requests[before].User : "";
            log.Check(seatBAgain.Contains("label=\"Research Agriculture to level 1 for "
                                          + levelOneCost + " research points\"")
                      && !seatBAgain.Contains("Research Agriculture to level 2"),
                "next season the opponent is still offered level 1, never the buyer's level 2",
                "level 2 present=" + seatBAgain.Contains("Research Agriculture to level 2"));

            // ---- feature off: no research block and no research candidate -------------------
            GameEngine plain = Fixture.World(researchPointsA: 100);
            var plainFake = new FakeLlmTransport();
            var plainRunner = new LlmTurnRunner(plainFake, Fixture.Config(research: false), null);
            var plainHost = new Fixture.Host(plain, Fixture.SeatA);
            plainFake.EnqueueSuccess("{\"type\":\"end_turn\"}");
            plainRunner.Begin(plainHost, plainHost.Token, Fixture.SeatA, Fixture.Seed);
            Fixture.Drain(plainRunner);
            string plainUser = plainFake.Requests.Count > 0 ? plainFake.Requests[0].User : "";
            log.Check(!plainFake.Requests[0].System.Contains("RESEARCH ORDERS")
                      && !plainUser.Contains("type=research"),
                "with research off the prompt offers no research line at all",
                "system block=" + plainFake.Requests[0].System.Contains("RESEARCH ORDERS"));
        }
    }
}
