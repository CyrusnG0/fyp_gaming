using System;
using System.Collections.Generic;
using LuanShi.Engine;

namespace LuanShi.Playground
{
    /// <summary>Review regressions: march destinations beyond the display cap, and match-scoped token ceilings.</summary>
    internal static partial class PlaygroundSelfTests
    {
        private static GameEngine SecondMatchEngine()
        {
            GameEngine engine = PlaygroundScenario.Create(ProdSeed, "review-match-2");
            engine.BeginSeason();
            return engine;
        }

        private static MapData OpenGrid(int width, int height)
        {
            var map = new MapData
            {
                Width = width, Height = height, Walkable = new bool[width * height],
                Cost = new int[width * height], Neighbors = new int[width * height][],
            };
            for (int z = 0; z < height; z++)
                for (int x = 0; x < width; x++)
                {
                    int i = map.Index(x, z);
                    map.Walkable[i] = true;
                    map.Cost[i] = 1;
                    var n = new List<int>();
                    if (x > 0) n.Add(map.Index(x - 1, z));
                    if (x < width - 1) n.Add(map.Index(x + 1, z));
                    if (z > 0) n.Add(map.Index(x, z - 1));
                    if (z < height - 1) n.Add(map.Index(x, z + 1));
                    map.Neighbors[i] = n.ToArray();
                }
            return map;
        }

        private static ArmyMovePlan FindPlan(MovementView view, string armyId)
        {
            foreach (ArmyMovePlan plan in view.Armies) if (plan.ArmyId == armyId) return plan;
            return null;
        }

        private static bool HasDestination(ArmyMovePlan plan, MoveOption candidate)
        {
            foreach (MoveOption o in plan.Options) if (o.X == candidate.X && o.Z == candidate.Z) return true;
            return false;
        }

        private static void ExperimentOmittedDestinationIsAccepted(TestLog log)
        {
            // The corridor has too few destinations to reach the display cap, so this test widens only
            // the terrain into an open grid; the runner, prompt builder and host are the production ones.
            GameEngine engine = ProductionEngine();
            engine.State.Map = OpenGrid(13, 9);
            ArmyState vanguard = engine.State.FindArmy("army-a-1");
            vanguard.X = 3; vanguard.Z = 4;   // open ground, well inside the grid
            // Extra own armies widen the seat's own sight, so more of the grid is legally visible.
            for (int k = 0; k < 4; k++)
                engine.State.Armies.Add(new ArmyState
                {
                    Id = "army-a-scout" + k, Name = "Wei Scout " + k, OwnerSeatId = "seat-a", Troops = 500,
                    UnitType = UnitTypes.Infantry, X = 5 + (k % 2) * 3, Z = 2 + (k / 2) * 4,
                    Morale = BalanceConfig.ArmyMoraleStart,
                });
            MovementView shownView = MovementPlanner.BuildDiversified(engine, "seat-a");
            MovementView fullView = MovementPlanner.BuildDiversified(engine, "seat-a", 40);
            string armyId = null;
            MoveOption omitted = null;
            foreach (ArmyMovePlan shown in shownView.Armies)
            {
                ArmyMovePlan full = FindPlan(fullView, shown.ArmyId);
                if (full == null || full.Options == null) continue;
                foreach (MoveOption candidate in full.Options)
                {
                    bool listed = HasDestination(shown, candidate);
                    var probe = new ActionCommand
                    {
                        Type = ActionType.March, TargetId = shown.ArmyId, ParamA = candidate.X, ParamB = candidate.Z,
                    };
                    if (!listed && candidate.Kind == "move" && MovementPlanner.AllowsVisibleRoute(engine, "seat-a", probe))
                    { armyId = shown.ArmyId; omitted = candidate; break; }
                }
                if (omitted != null) break;
            }
            log.Check(omitted != null, "omitted destination: the scenario has a legal visible destination the display cap leaves out");
            if (omitted == null) return;

            LlmConfig config = RichConfig();
            config.RequireVisibleMarchRoutes = true;
            var fake = new FakeLlmTransport();
            fake.EnqueueSuccess("{\"type\":\"march\",\"target_id\":\"" + armyId + "\",\"param_a\":" + omitted.X
                + ",\"param_b\":" + omitted.Z + "}", 0, 100, 10);
            fake.EnqueueSuccess("{\"type\":\"end_turn\"}", 0, 100, 10);
            fake.EnqueueSuccess("{\"type\":\"end_turn\"}", 0, 100, 10);
            var runner = new LlmTurnRunner(fake, config, null, null, new MemoryLlmStrategicNoteStore());
            var session = new SpectatorSession(engine, runner, 1, ProdSeed);
            session.Start();
            for (int i = 0; i < 200 && session.State == SpectatorState.Running; i++) session.Tick();

            ArmyState army = engine.State.FindArmy(armyId);
            string first = UserOfCall(fake, 0);
            log.Check(army != null && army.X == omitted.X && army.Z == omitted.Z && session.TotalAccepted >= 1,
                "omitted destination: the production host accepts a legal visible destination the list omitted",
                army == null ? "no army" : "army at " + army.X + "," + army.Z + " accepted=" + session.TotalAccepted);
            log.Check(first.Contains("march destinations across your armies") && first.Contains("(INCOMPLETE)")
                      && first.Contains("not listed by any tool") && !first.Contains("cannot be ordered"),
                "omitted destination: the prompt reports the cut and no longer says omitted destinations cannot be ordered");
        }

        private static void ExperimentTokenCeilingsAreMatchScoped(TestLog log)
        {
            string inquiry = "{\"tool\":\"manual_lookup\",\"topic\":\"research\"}";

            // Match 1 reports no usage and is stopped; match 2 on the same runner has known usage.
            LlmConfig c = RichConfig();
            var fake = new FakeLlmTransport();
            fake.EnqueueSuccess(inquiry);   // no usage reported
            var runner = new LlmTurnRunner(fake, c, null, null, new MemoryLlmStrategicNoteStore());
            var host1 = new ProductionHost(ProductionEngine(), "seat-a");
            runner.Begin(host1, host1.Token, "seat-a", ProdSeed);
            DrainProduction(runner);
            log.Check(runner.StopCeiling == "session_usage_unknown" && fake.CallCount == 1
                      && runner.TotalInputTokens == null,
                "match scope: match one with missing usage stops on session_usage_unknown");

            fake.EnqueueSuccess(inquiry, 0, 100, 10);
            fake.EnqueueSuccess("{\"type\":\"end_turn\"}", 0, 100, 10);
            var host2 = new ProductionHost(SecondMatchEngine(), "seat-a");
            runner.Begin(host2, host2.Token, "seat-a", ProdSeed);
            DrainProduction(runner);
            log.Check(runner.StopCeiling == null && runner.EndReason == EndReason.ModelEndedTurn && host2.EndTurnCalls == 1,
                "match scope: match two starts with its own known usage and runs to its own end_turn",
                runner.StopCeiling + " " + runner.EndReason + " calls=" + fake.CallCount);
            log.Check(runner.TotalInputTokens == null && runner.TotalOutputTokens == null,
                "match scope: lifetime reporting is unchanged and still honest about the earlier missing usage");

            // Match 1 exhausts the input ceiling; match 2 gets a full allowance again.
            c = RichConfig(); c.MaxSessionInputTokens = 1000;
            fake = new FakeLlmTransport();
            fake.EnqueueSuccess(inquiry, 0, 1200, 20);
            runner = new LlmTurnRunner(fake, c, null, null, new MemoryLlmStrategicNoteStore());
            host1 = new ProductionHost(ProductionEngine(), "seat-a");
            runner.Begin(host1, host1.Token, "seat-a", ProdSeed);
            DrainProduction(runner);
            log.Check(runner.StopCeiling == "session_input_tokens" && fake.CallCount == 1,
                "match scope: match one exhausts its input ceiling");
            fake.EnqueueSuccess("{\"type\":\"end_turn\"}", 0, 900, 20);
            host2 = new ProductionHost(SecondMatchEngine(), "seat-a");
            runner.Begin(host2, host2.Token, "seat-a", ProdSeed);
            DrainProduction(runner);
            log.Check(runner.StopCeiling == null && fake.CallCount == 2 && host2.EndTurnCalls == 1
                      && runner.TotalInputTokens == 2100,
                "match scope: match two starts with a full input allowance; lifetime total still sums both matches",
                runner.StopCeiling + " calls=" + fake.CallCount + " total=" + runner.TotalInputTokens);

            // Output ceiling the same way.
            c = RichConfig(); c.MaxSessionOutputTokens = 100; c.MaxOutputTokens = 60;
            fake = new FakeLlmTransport();
            fake.EnqueueSuccess(inquiry, 0, 500, 50);
            runner = new LlmTurnRunner(fake, c, null, null, new MemoryLlmStrategicNoteStore());
            host1 = new ProductionHost(ProductionEngine(), "seat-a");
            runner.Begin(host1, host1.Token, "seat-a", ProdSeed);
            DrainProduction(runner);
            bool stopped = runner.StopCeiling == "session_output_tokens";
            fake.EnqueueSuccess("{\"type\":\"end_turn\"}", 0, 500, 50);
            host2 = new ProductionHost(SecondMatchEngine(), "seat-a");
            runner.Begin(host2, host2.Token, "seat-a", ProdSeed);
            DrainProduction(runner);
            log.Check(stopped && runner.StopCeiling == null && host2.EndTurnCalls == 1,
                "match scope: the output ceiling is per match as well");
        }
    }
}
