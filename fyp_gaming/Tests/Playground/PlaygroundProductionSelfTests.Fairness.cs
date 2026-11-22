using System.Collections.Generic;
using LuanShi.Engine;

namespace LuanShi.Playground
{
    internal static partial class PlaygroundSelfTests
    {
        private static void ProductionFairnessAndHandover(TestLog log)
        {
            HandoverAndCrossSeatPrivacy(log);
            StaleEnemyIntelAndRouteAsymmetry(log);
            ActionCatalogueAndRuleParityChecks(log);
            ReplayRuleConsistencyAndProvenanceTests(log);
        }

        private static void HandoverAndCrossSeatPrivacy(TestLog log)
        {
            GameEngine engine = PlaygroundScenario.Create(ProdSeed, ProdGame);
            engine.State.FindFaction(PlaygroundScenario.SeatA).Controller = ControllerType.Human;
            engine.BeginSeason();

            TurnFlowStep step1 = TurnFlowPlanner.First(engine.State);
            log.Check(step1.Action == TurnFlowAction.HumanSeat && step1.SeatId == PlaygroundScenario.SeatA,
                "fairness-handover: season 1 begins on human seat-a");

            int tokenA1 = engine.BeginSeatTurn(PlaygroundScenario.SeatA);
            ValidationResult taxRes = engine.SubmitAs(PlaygroundScenario.SeatA, tokenA1, new ActionCommand
            {
                Type = ActionType.LevyTax,
                ActorSeatId = PlaygroundScenario.SeatA,
                ControllerType = ControllerType.Human,
                TargetId = "city-a",
            });
            ValidationResult convRes = engine.SubmitAs(PlaygroundScenario.SeatA, tokenA1, new ActionCommand
            {
                Type = ActionType.ConvertGarrison,
                ActorSeatId = PlaygroundScenario.SeatA,
                ControllerType = ControllerType.Human,
                TargetId = "city-a",
                ParamA = 300,
            });
            engine.EndSeatTurn(tokenA1);
            log.Check(taxRes.Ok && convRes.Ok,
                "fairness-handover: human submits domestic and private garrison conversion");

            TurnFlowStep stepB1 = TurnFlowPlanner.NextAfter(engine.State, PlaygroundScenario.SeatA);
            log.Check(stepB1.Action == TurnFlowAction.LlmSeat && stepB1.SeatId == PlaygroundScenario.SeatB,
                "fairness-handover: turn flow advances to seat-b");

            engine.State.FindFaction(PlaygroundScenario.SeatB).ResearchPoints = BalanceConfig.ResearchLevelCosts[0];
            int tokenB1 = engine.BeginSeatTurn(PlaygroundScenario.SeatB);
            ValidationResult resB = engine.SubmitAs(PlaygroundScenario.SeatB, tokenB1, new ActionCommand
            {
                Type = ActionType.Research,
                ActorSeatId = PlaygroundScenario.SeatB,
                ControllerType = ControllerType.Llm,
                Args = new Dictionary<string, string> { { "line", ResearchLine.Military } },
            });
            ValidationResult marchB = engine.SubmitAs(PlaygroundScenario.SeatB, tokenB1, new ActionCommand
            {
                Type = ActionType.March,
                ActorSeatId = PlaygroundScenario.SeatB,
                ControllerType = ControllerType.Llm,
                TargetId = "army-b-1",
                ParamA = 10,
                ParamB = 0,
            });
            ValidationResult msgB = engine.SubmitAs(PlaygroundScenario.SeatB, tokenB1, new ActionCommand
            {
                Type = ActionType.SendMessage,
                ActorSeatId = PlaygroundScenario.SeatB,
                ControllerType = ControllerType.Llm,
                TargetId = PlaygroundScenario.SeatA,
                Text = "envoy-hello",
            });
            engine.EndSeatTurn(tokenB1);
            log.Check(resB.Ok && marchB.Ok && msgB.Ok,
                "fairness-handover: seat-b executes private research, unseen march, and envoy message");

            TurnFlowStep resolve1 = TurnFlowPlanner.NextAfter(engine.State, PlaygroundScenario.SeatB);
            log.Check(resolve1.Action == TurnFlowAction.ResolveSeason,
                "fairness-handover: season 1 ends at ResolveSeason");

            engine.EndSeason();
            engine.BeginSeason();

            // Handover same seat (seat-a) from Human to LLM for Season 2.
            engine.State.FindFaction(PlaygroundScenario.SeatA).Controller = ControllerType.Llm;
            TurnFlowStep stepA2 = TurnFlowPlanner.First(engine.State);
            log.Check(stepA2.Action == TurnFlowAction.LlmSeat && stepA2.SeatId == PlaygroundScenario.SeatA,
                "fairness-handover: seat-a hands over cleanly from Human to Llm on season 2");

            LlmConfig config = ProductionConfig();
            config.EnableDiplomacy = true;
            config.EnableGarrisonConversion = true;
            config.EnableResearch = true;
            config.EnableAttack = true;
            config.EnableManualLookup = true;
            config.SpatialContext = new SpatialContextOptions { Level = SpatialDetailLevel.Rich };

            var fake = new FakeLlmTransport();
            var memories = new InMemoryLlmMemoryStore();
            var notes = new MemoryLlmStrategicNoteStore();
            fake.EnqueueSuccess("{\"tool\":\"search_history\",\"limit\":10}");
            fake.EnqueueSuccess("{\"tool\":\"inspect_entity\",\"kind\":\"city\",\"id\":\"city-b\"}");
            fake.EnqueueSuccess("{\"tool\":\"manual_lookup\",\"topic\":\"recruit\"}");
            fake.EnqueueSuccess("{\"note\":\"private seat-a plan\"}");
            fake.EnqueueSuccess("{\"type\":\"levy_tax\",\"target_id\":\"city-a\",\"summary\":\"s2 llm tax\"}");
            fake.EnqueueSuccess("{\"type\":\"end_turn\",\"summary\":\"s2 done\"}");

            int tokenA2 = engine.BeginSeatTurn(PlaygroundScenario.SeatA);
            var hostA2 = new SingleLlmTurnHost(
                engine, config, PlaygroundScenario.SeatA,
                () => engine.Observe(PlaygroundScenario.SeatA),
                cmd => engine.SubmitAs(PlaygroundScenario.SeatA, tokenA2, cmd),
                () => engine.EndSeatTurn(tokenA2));
            var runner = new LlmTurnRunner(fake, config, null, memories, notes);
            runner.Begin(hostA2, tokenA2, PlaygroundScenario.SeatA, ProdSeed);
            DrainProduction(runner);

            log.Check(runner.State == RunnerState.Finished
                      && runner.EndReason == EndReason.ModelEndedTurn
                      && runner.AcceptedActionsThisTurn == 1,
                "fairness-handover: LLM seat completes deliberation and action after human turn",
                runner.FailureReason ?? runner.EndReasonText);

            string p0 = fake.Requests[0].User;
            string p1 = fake.Requests[1].User;
            string p2 = fake.Requests[2].User;
            string p3 = fake.Requests[3].User;
            log.Check(p0.Contains("envoy-hello")
                      && p0.Contains("garrison_converted")
                      && !p0.Contains("city-b")
                      && !p0.Contains("Shu Capital")
                      && !p0.Contains("army-b-1")
                      && !p0.Contains("research_purchased"),
                "fairness-handover: initial LLM prompt sees public/own season 1 events without opponent secrets",
                "envoy=" + p0.Contains("envoy-hello") + " conv=" + p0.Contains("garrison_converted")
                + " res=" + p0.Contains("research_purchased"));
            log.Check(p1.Contains("search_history")
                      && p1.Contains("garrison_converted")
                      && !p1.Contains("city-b")
                      && !p1.Contains("Shu Capital")
                      && !p1.Contains("army-b-1")
                      && !p1.Contains("research_purchased"),
                "fairness-handover: search_history respects fog of war and private research");
            log.Check(p2.Contains("inspect_entity")
                      && !p2.Contains("Shu Capital"),
                "fairness-handover: inspect_entity refuses to reveal unseen opponent city");
            log.Check(p3.Contains("manual_lookup") && p3.Contains("archer") && p3.Contains("cavalry"),
                "fairness-handover: manual_lookup(recruit) advertises unit types beyond infantry");

            // Check cross-seat isolation: seat-b cannot see seat-a's private garrison conversion action
            // record, private strategic note, or memory entries.
            Observation obsB = engine.Observe(PlaygroundScenario.SeatB);
            bool seatBLeakedConvert = false;
            foreach (EventView ev in obsB.Events)
            {
                if (ev.EventType == "action" && ev.ActorSeatId == PlaygroundScenario.SeatA
                    && ev.Payload != null && ev.Payload.TryGetValue("type", out string t)
                    && t == ActionType.ConvertGarrison)
                    seatBLeakedConvert = true;
            }
            string noteB = notes.Load(ProdGame, PlaygroundScenario.SeatB);
            IReadOnlyList<LlmMemory> memB = memories.Load(ProdGame, PlaygroundScenario.SeatB);
            log.Check(!seatBLeakedConvert && noteB == null && memB.Count == 0,
                "fairness-privacy: seat-b cannot observe seat-a private convert_garrison, note, or memory");

            // Complete seat-b S2 and hand seat-a back to Human for S3: history preserves distinct controller types.
            int tokenB2 = engine.BeginSeatTurn(PlaygroundScenario.SeatB);
            engine.EndSeatTurn(tokenB2);
            engine.EndSeason();
            engine.BeginSeason();

            engine.State.FindFaction(PlaygroundScenario.SeatA).Controller = ControllerType.Human;
            TurnFlowStep stepA3 = TurnFlowPlanner.First(engine.State);
            Observation obsA3 = engine.Observe(PlaygroundScenario.SeatA);
            bool sawHumanS1Tax = false, sawLlmS2Tax = false;
            foreach (EventView ev in obsA3.Events)
            {
                if (ev.EventType != "action" || ev.ActorSeatId != PlaygroundScenario.SeatA) continue;
                if (ev.Season == 1 && ev.ControllerType == ControllerType.Human) sawHumanS1Tax = true;
                if (ev.Season == 2 && ev.ControllerType == ControllerType.Llm) sawLlmS2Tax = true;
            }
            log.Check(stepA3.Action == TurnFlowAction.HumanSeat
                      && sawHumanS1Tax && sawLlmS2Tax,
                "fairness-handover: handed-back human seat sees both prior human and LLM actions");
        }

        private static void StaleEnemyIntelAndRouteAsymmetry(TestLog log)
        {
            // Corridor width 7: seat-a sees x=0..4 (city-a at x=0, army-a-1 at x=2), seat-b starts out of sight at x=5.
            // Also block x=1 with Cost=1 walkable? Wait: to test unseen-shortcut route asymmetry separately,
            // first test stale tracking on open corridor x=0..6.
            var engine = new GameEngine(ProdSeed + 1, "stale-intel-check", BalanceProfile.Standard);
            engine.State.Map = BuildCorridor(7);
            engine.State.Factions.Add(new FactionState
            {
                SeatId = PlaygroundScenario.SeatA, Name = "Wei", Controller = ControllerType.Llm,
            });
            engine.State.Factions.Add(new FactionState
            {
                SeatId = PlaygroundScenario.SeatB, Name = "Shu", Controller = ControllerType.Human,
            });
            engine.State.Cities.Add(new CityState
            {
                Id = "city-a", Name = "Wei Cap", X = 0, Z = 0, OwnerSeatId = PlaygroundScenario.SeatA,
                Population = 8000, Food = 500, Gold = 500, Garrison = 500,
            });
            engine.State.Cities.Add(new CityState
            {
                Id = "city-b", Name = "Shu Cap", X = 6, Z = 0, OwnerSeatId = PlaygroundScenario.SeatB,
                Population = 8000, Food = 500, Gold = 500, Garrison = 500,
            });
            engine.State.Armies.Add(new ArmyState
            {
                Id = "army-a-1", Name = "Wei Van", OwnerSeatId = PlaygroundScenario.SeatA,
                Troops = 1000, UnitType = UnitTypes.Infantry, X = 2, Z = 0,
            });
            engine.State.Armies.Add(new ArmyState
            {
                Id = "army-b-1", Name = "Shu Van", OwnerSeatId = PlaygroundScenario.SeatB,
                Troops = 1000, UnitType = UnitTypes.Infantry, X = 5, Z = 0,
            });

            engine.BeginSeason();
            // S1: seat-b marches into sight at x=4 -> seat-a observes army_moved to (4,0).
            int tA1 = engine.BeginSeatTurn(PlaygroundScenario.SeatA);
            engine.EndSeatTurn(tA1);
            int tB1 = engine.BeginSeatTurn(PlaygroundScenario.SeatB);
            ValidationResult inSightMove = engine.SubmitAs(PlaygroundScenario.SeatB, tB1, new ActionCommand
            {
                Type = ActionType.March, ActorSeatId = PlaygroundScenario.SeatB,
                ControllerType = ControllerType.Human, TargetId = "army-b-1", ParamA = 4, ParamB = 0,
            });
            engine.EndSeatTurn(tB1);
            engine.EndSeason();
            engine.BeginSeason();

            // S2: seat-b withdraws back to unseen (5,0). seat-a sees nothing at (5,0) and (4,0) is empty in sight.
            int tA2 = engine.BeginSeatTurn(PlaygroundScenario.SeatA);
            engine.EndSeatTurn(tA2);
            int tB2 = engine.BeginSeatTurn(PlaygroundScenario.SeatB);
            ValidationResult outSightMove = engine.SubmitAs(PlaygroundScenario.SeatB, tB2, new ActionCommand
            {
                Type = ActionType.March, ActorSeatId = PlaygroundScenario.SeatB,
                ControllerType = ControllerType.Human, TargetId = "army-b-1", ParamA = 5, ParamB = 0,
            });
            engine.EndSeatTurn(tB2);

            Observation obsA = engine.Observe(PlaygroundScenario.SeatA);
            SpatialContextView spatial = SpatialContextBuilder.Build(
                engine, PlaygroundScenario.SeatA, obsA);
            SpatialStaleRecord stale = null;
            foreach (SpatialStaleRecord rec in spatial.StaleIntel)
                if (rec.Kind == "army_last_seen" && rec.Id == "army-b-1") stale = rec;

            log.Check(inSightMove.Ok && outSightMove.Ok
                      && obsA.Armies.Count == 1
                      && stale != null && stale.Knowledge == "stale"
                      && stale.X == 4 && stale.Z == 0 && stale.RecordedSeason == 1
                      && stale.Summary.Contains("no longer visible there")
                      && !spatial.ToJson().Contains("\"x\":5"),
                "fairness-stale: last-seen enemy army stays anchored at (4,0) without leaking unseen retreat to (5,0)");

            // Route fairness check:
            // 1) Legacy/unshared host-only config (RequireVisibleMarchRoutes=false on engine, Rich Spatial on LLM)
            //    reproduces the historical asymmetry where host rejects LLM while engine accepts Human.
            // 2) Shared match rule (engine.State.RequireVisibleMarchRoutes = true) applies VisiblePathCost
            //    authoritatively in GameEngine.DoMarch for BOTH Human and LLM, gives matching feedback,
            //    accepts visible-route marches identically, and discloses zero hidden terrain across paired worlds.
            var routeConfig = new LlmConfig
            {
                SpatialContext = new SpatialContextOptions { Level = SpatialDetailLevel.Rich },
            };
            var marchCmd = new ActionCommand
            {
                Type = ActionType.March,
                ActorSeatId = PlaygroundScenario.SeatA,
                ControllerType = ControllerType.Llm,
                TargetId = "cav-1",
                ParamA = 5,
                ParamB = 0,
            };

            // (1) Reproduce historical unshared host-only asymmetry (human submits on pre-sync engine
            //     where RequireVisibleMarchRoutes=false, while SingleLlmTurnHost enforces visible routes
            //     for LLM AND syncs the engine rule on construction in the fixed build).
            var legacyHumanEngine = BuildCorridorRouteWorld(ProdSeed + 2, unseenBridgeWalkable: true, sharedVisibleRule: false);
            bool gapUnseenBefore = !legacyHumanEngine.InSight(PlaygroundScenario.SeatA, 4, 0)
                                   && legacyHumanEngine.InSight(PlaygroundScenario.SeatA, 5, 0);
            int legacyHumanTok = legacyHumanEngine.BeginSeatTurn(PlaygroundScenario.SeatA);
            ValidationResult legacyHuman = legacyHumanEngine.SubmitAs(PlaygroundScenario.SeatA, legacyHumanTok, new ActionCommand
            {
                Type = ActionType.March,
                ActorSeatId = PlaygroundScenario.SeatA,
                ControllerType = ControllerType.Human,
                TargetId = "cav-1",
                ParamA = 5,
                ParamB = 0,
            });
            legacyHumanEngine.EndSeatTurn(legacyHumanTok);

            var legacyLlmEngine = BuildCorridorRouteWorld(ProdSeed + 2, unseenBridgeWalkable: true, sharedVisibleRule: false);
            int legacyLlmTok = legacyLlmEngine.BeginSeatTurn(PlaygroundScenario.SeatA);
            var legacyHost = new SingleLlmTurnHost(
                legacyLlmEngine, routeConfig, PlaygroundScenario.SeatA,
                () => legacyLlmEngine.Observe(PlaygroundScenario.SeatA),
                cmd => legacyLlmEngine.SubmitAs(PlaygroundScenario.SeatA, legacyLlmTok, cmd),
                () => legacyLlmEngine.EndSeatTurn(legacyLlmTok));
            ValidationResult legacyLlm = legacyHost.Submit(marchCmd);
            legacyLlmEngine.EndSeatTurn(legacyLlmTok);

            log.Check(gapUnseenBefore
                      && !legacyLlm.Ok && legacyLlm.Error == FeedbackPolicy.DestinationNotReachable
                      && legacyHuman.Ok
                      && legacyLlmEngine.State.RequireVisibleMarchRoutes,
                "fairness-repro: unshared host-only config reproduced LLM rejection vs human acceptance across unseen hex (4,0)",
                $"gapBefore={gapUnseenBefore} llm={legacyLlm.Ok}/{legacyLlm.Error} human={legacyHuman.Ok}/{legacyHuman.Error}");

            // (2) Shared visible-route rule across paired worlds (bridge open vs blocked)
            string openHumanErr = null, blockedHumanErr = null;
            string openLlmErr = null, blockedLlmErr = null;
            bool bothAcceptedVisible = false;
            bool movementParityEveryCell = true;

            foreach (bool bridgeOpen in new[] { true, false })
            {
                var sharedEngine = BuildCorridorRouteWorld(ProdSeed + 3, unseenBridgeWalkable: bridgeOpen, sharedVisibleRule: true);
                int tok = sharedEngine.BeginSeatTurn(PlaygroundScenario.SeatA);
                var host = new SingleLlmTurnHost(
                    sharedEngine, routeConfig, PlaygroundScenario.SeatA,
                    () => sharedEngine.Observe(PlaygroundScenario.SeatA),
                    cmd => sharedEngine.SubmitAs(PlaygroundScenario.SeatA, tok, cmd),
                    () => sharedEngine.EndSeatTurn(tok));

                // Same visible input candidates for Human and LLM
                MovementView llmMoveView = host.PlanMovement(routeConfig.SpatialContext);
                MovementView rawMoveView = MovementPlanner.Build(sharedEngine, PlaygroundScenario.SeatA);
                if (HasOptionAtX(rawMoveView.Armies[0].Options, 5)
                    || HasOptionAtX(llmMoveView.Armies[0].Options, 5))
                    movementParityEveryCell = false;

                ValidationResult sharedLlm = host.Submit(marchCmd);
                ValidationResult sharedHuman = sharedEngine.SubmitAs(PlaygroundScenario.SeatA, tok, new ActionCommand
                {
                    Type = ActionType.March,
                    ActorSeatId = PlaygroundScenario.SeatA,
                    ControllerType = ControllerType.Human,
                    TargetId = "cav-1",
                    ParamA = 5,
                    ParamB = 0,
                });
                string humanMapped = FeedbackPolicy.ForModel(sharedHuman.Error);
                if (sharedLlm.Ok || sharedHuman.Ok || humanMapped != sharedLlm.Error)
                    movementParityEveryCell = false;

                if (bridgeOpen) { openLlmErr = sharedLlm.Error; openHumanErr = sharedHuman.Error; }
                else { blockedLlmErr = sharedLlm.Error; blockedHumanErr = sharedHuman.Error; }

                // Legal visible march (1,0) -> (3,0) must succeed identically for both controllers
                var visLlm = host.Submit(new ActionCommand
                {
                    Type = ActionType.March, ActorSeatId = PlaygroundScenario.SeatA, ControllerType = ControllerType.Llm,
                    TargetId = "cav-1", ParamA = 3, ParamB = 0,
                });
                // Cavalry gets 2 marches per season; second visible march (3,0) -> (2,0) for Human
                var visHuman = sharedEngine.SubmitAs(PlaygroundScenario.SeatA, tok, new ActionCommand
                {
                    Type = ActionType.March, ActorSeatId = PlaygroundScenario.SeatA, ControllerType = ControllerType.Human,
                    TargetId = "cav-1", ParamA = 2, ParamB = 0,
                });
                sharedEngine.EndSeatTurn(tok);
                if (bridgeOpen) bothAcceptedVisible = visLlm.Ok && visHuman.Ok && sharedEngine.State.FindArmy("cav-1").X == 2;
            }

            log.Check(movementParityEveryCell
                      && bothAcceptedVisible
                      && openHumanErr == blockedHumanErr
                      && openLlmErr == blockedLlmErr
                      && openLlmErr == FeedbackPolicy.DestinationNotReachable,
                "fairness-shared-route: shared RequireVisibleMarchRoutes gives identical candidates, legality, feedback, and zero hidden-terrain leak for human and LLM",
                $"parity={movementParityEveryCell} vis={bothAcceptedVisible} openH={openHumanErr} blockedH={blockedHumanErr} openL={openLlmErr}");
        }

        private static GameEngine BuildCorridorRouteWorld(int seed, bool unseenBridgeWalkable, bool sharedVisibleRule)
        {
            var routeEngine = new GameEngine(seed, "route-asymmetry-check", BalanceProfile.Standard);
            routeEngine.State.RequireVisibleMarchRoutes = sharedVisibleRule;
            routeEngine.State.Map = BuildCorridor(11);
            if (!unseenBridgeWalkable)
                routeEngine.State.Map.Walkable[4] = false;
            routeEngine.State.Factions.Add(new FactionState
            {
                SeatId = PlaygroundScenario.SeatA, Name = "Wei", Controller = ControllerType.Human,
            });
            routeEngine.State.Factions.Add(new FactionState
            {
                SeatId = PlaygroundScenario.SeatB, Name = "Shu", Controller = ControllerType.Llm,
            });
            routeEngine.State.Cities.Add(new CityState
            {
                Id = "city-w", Name = "West", X = 0, Z = 0, OwnerSeatId = PlaygroundScenario.SeatA,
                Population = 5000, Food = 400, Gold = 400, Garrison = 500,
            });
            routeEngine.State.Cities.Add(new CityState
            {
                Id = "city-e", Name = "East", X = 8, Z = 0, OwnerSeatId = PlaygroundScenario.SeatA,
                Population = 5000, Food = 400, Gold = 400, Garrison = 500,
            });
            routeEngine.State.Cities.Add(new CityState
            {
                Id = "city-b", Name = "Shu Cap", X = 10, Z = 0, OwnerSeatId = PlaygroundScenario.SeatB,
                Population = 5000, Food = 400, Gold = 400, Garrison = 500,
            });
            routeEngine.State.Armies.Add(new ArmyState
            {
                Id = "cav-1", Name = "Wei Cav", OwnerSeatId = PlaygroundScenario.SeatA,
                Troops = 1000, UnitType = UnitTypes.Cavalry, X = 1, Z = 0,
            });
            routeEngine.BeginSeason();
            return routeEngine;
        }

        private static void ActionCatalogueAndRuleParityChecks(TestLog log)
        {
            // Verify documented parser restrictions vs engine-accepted human actions on the same ruleset.
            string[] blockedLlmReplies =
            {
                "{\"type\":\"recruit\",\"target_id\":\"city-a\",\"args\":{\"unit_type\":\"archer\"}}",
                "{\"type\":\"recruit\",\"target_id\":\"city-a\",\"args\":{\"unit_type\":\"cavalry\"}}",
                "{\"type\":\"recruit\",\"target_id\":\"city-a\",\"args\":{\"unit_type\":\"siege\"}}",
                "{\"type\":\"garrison_army\",\"target_id\":\"army-a-1\",\"param_a\":500}",
                "{\"type\":\"gift\",\"target_id\":\"seat-b\",\"param_a\":1000}",
                "{\"type\":\"declare_war\",\"target_id\":\"seat-b\"}",
                "{\"type\":\"break_treaty\",\"target_id\":\"seat-b\",\"args\":{\"treaty_type\":\"nap\"}}",
                "{\"type\":\"scout\",\"target_id\":\"city-b\"}",
            };
            bool allBlockedForLlm = true;
            foreach (string reply in blockedLlmReplies)
                if (LlmActionParser.Parse(reply, true, true, true, true).Ok)
                    allBlockedForLlm = false;

            GameEngine engine = ProductionEngine();
            CityState cityA = engine.State.FindCity("city-a");
            cityA.Garrison = 0;
            cityA.Gold = 2000;
            int tokA = engine.BeginSeatTurn(PlaygroundScenario.SeatA);
            ValidationResult humanArcher = engine.SubmitAs(PlaygroundScenario.SeatA, tokA, new ActionCommand
            {
                Type = ActionType.Recruit,
                ActorSeatId = PlaygroundScenario.SeatA,
                ControllerType = ControllerType.Human,
                TargetId = "city-a",
                Args = new Dictionary<string, string> { { "unit_type", UnitTypes.Archer } },
            });
            ValidationResult humanGift = engine.SubmitAs(PlaygroundScenario.SeatA, tokA, new ActionCommand
            {
                Type = ActionType.Gift,
                ActorSeatId = PlaygroundScenario.SeatA,
                ControllerType = ControllerType.Human,
                TargetId = PlaygroundScenario.SeatB,
                ParamA = 1000,
            });
            engine.EndSeatTurn(tokA);

            var inv = new InvestigationService(engine.State.GameId, PlaygroundScenario.SeatA, engine.Observe(PlaygroundScenario.SeatA), null);
            ManualLookupResult moveManual = inv.ManualLookup("movement");
            bool manualCoversVisibleRule = moveManual != null && moveManual.Ok
                && moveManual.ToJson().Contains("visible-only march routing");

            // 8-seat layout and observation invariants (Diplomacy & Intel scalability)
            var eightEngine = new GameEngine(ProdSeed + 4, "eight-seat-check", BalanceProfile.Standard);
            eightEngine.State.Map = BuildCorridor(24);
            for (int s = 0; s < 8; s++)
            {
                eightEngine.State.Factions.Add(new FactionState
                {
                    SeatId = "seat-" + s, Name = "Faction-" + s,
                    Controller = s == 0 ? ControllerType.Human : ControllerType.Llm,
                });
                eightEngine.State.Cities.Add(new CityState
                {
                    Id = "city-" + s, Name = "City-" + s, X = s * 3, Z = 0, OwnerSeatId = "seat-" + s,
                    Population = 3000, Food = 300, Gold = 300, Garrison = 300,
                });
            }
            eightEngine.BeginSeason();
            Observation obs8 = eightEngine.Observe("seat-0");
            int pairCount = 0;
            for (int i = 0; i < obs8.Factions.Count; i++)
            {
                if (obs8.Factions[i].SeatId == "seat-0") continue;
                for (int j = i + 1; j < obs8.Factions.Count; j++)
                    if (obs8.Factions[j].SeatId != "seat-0") pairCount++;
            }
            UiLayoutMetrics layout100 = UiLayout.Compute(new UiViewport(1f, 1600f, 900f));
            UiLayoutMetrics layout120 = UiLayout.Compute(new UiViewport(1.2f, 1333.33f, 750f));
            bool layoutOk = layout100.TabPanelDockedWidth >= UiLayout.MinTabPanelWidth
                && layout120.TabPanelDockedWidth >= UiLayout.MinTabPanelWidth
                && layout120.SidePanelX > UiLayout.TabPanelDockedX + layout120.TabPanelDockedWidth;

            log.Check(allBlockedForLlm && humanArcher.Ok && humanGift.Ok
                      && manualCoversVisibleRule
                      && obs8.Factions.Count == 8 && pairCount == 21
                      && layoutOk,
                "fairness-catalogue: parity checks, manual movement rule, and 8-seat layout/observation scale cleanly",
                $"blocked={allBlockedForLlm} archer={humanArcher.Ok} gift={humanGift.Ok} manual={manualCoversVisibleRule} seats={obs8.Factions.Count} pairs={pairCount} layout={layoutOk}");
        }

        private static bool HasOptionAtX(IReadOnlyList<MoveOption> options, int x)
        {
            if (options == null) return false;
            for (int i = 0; i < options.Count; i++)
                if (options[i] != null && options[i].X == x) return true;
            return false;
        }

        private static MapData BuildCorridor(int width)
        {
            var map = new MapData
            {
                Width = width,
                Height = 1,
                Walkable = new bool[width],
                Cost = new int[width],
                Neighbors = new int[width][],
            };
            for (int x = 0; x < width; x++)
            {
                map.Walkable[x] = true;
                map.Cost[x] = 1;
                var neighbors = new List<int>();
                if (x > 0) neighbors.Add(x - 1);
                if (x < width - 1) neighbors.Add(x + 1);
                map.Neighbors[x] = neighbors.ToArray();
            }
            return map;
        }
    }
}
