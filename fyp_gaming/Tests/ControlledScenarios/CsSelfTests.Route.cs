using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LuanShi.Engine;

namespace LuanShi.ControlledScenarios
{
    // Route-choice scenario: the engine's own validator must reject marches onto the river wall
    // and onto the granary (out of one march's budget), and must accept every destination the
    // prompt advertises. Each submit uses a fresh engine, so no order changes another's result.
    public static partial class CsSelfTests
    {
        private static readonly int[][] RouteWallCells =
        {
            new[] { 11, 6 }, new[] { 11, 7 }, new[] { 11, 8 }, new[] { 12, 9 },
            new[] { 13, 10 }, new[] { 14, 10 }, new[] { 13, 11 },
        };

        private static ValidationResult RouteMarch(CsScenarioSpec spec, int x, int z)
        {
            GameEngine engine = CsWorld.Create(spec, "route-check");
            return engine.Submit(new ActionCommand
            {
                Type = ActionType.March, ActorSeatId = spec.StudySeat,
                TargetId = CsScenarioFactory.OwnArmyId, ParamA = x, ParamB = z,
            });
        }

        private static void RouteChoiceEngineChecks(TestLog log)
        {
            CsScenarioSpec spec = Builtin(CsScenarios.RouteChoiceId, 5151);
            log.Check(spec.Goal == CsScenarios.RouteGoal && spec.EnableSpatial,
                "[CS-R1] route scenario carries the goal text and spatial context");

            foreach (int[] cell in RouteWallCells)
            {
                ValidationResult r = RouteMarch(spec, cell[0], cell[1]);
                log.Check(!r.Ok, "[CS-R1] engine rejects a march onto wall cell " + cell[0] + "," + cell[1],
                    "error=" + r.Error);
                Console.WriteLine("       wall " + cell[0] + "," + cell[1] + " -> " + (r.Ok ? "ACCEPTED" : r.Error));
            }

            ValidationResult ford = RouteMarch(spec, 14, 11);
            log.Check(!ford.Ok, "[CS-R1] the granary (14,11) cannot be reached in one march",
                "error=" + ford.Error);
            Console.WriteLine("       granary 14,11 -> " + (ford.Ok ? "ACCEPTED" : ford.Error));

            GameEngine view = CsWorld.Create(spec, "route-check");
            MovementView movement = MovementPlanner.BuildDiversified(view, spec.StudySeat,
                MovementPlanner.DefaultMaxOptionsPerArmy, null,
                new SpatialContextOptions { Level = SpatialDetailLevel.Rich });
            var options = new List<MoveOption>();
            foreach (ArmyMovePlan plan in movement.Armies) options.AddRange(plan.Options);
            int rejected = 0;
            bool anyWall = false, nearGranary = false;
            foreach (MoveOption o in options)
            {
                if (!RouteMarch(spec, o.X, o.Z).Ok) rejected++;
                foreach (int[] w in RouteWallCells) if (w[0] == o.X && w[1] == o.Z) anyWall = true;
                if (Math.Abs(o.X - 14) + Math.Abs(o.Z - 11) <= 2) nearGranary = true;
            }
            log.Check(options.Count >= 2 && rejected == 0,
                "[CS-R1] every advertised destination is accepted by the engine",
                "options=" + options.Count + " rejected=" + rejected);
            log.Check(!anyWall, "[CS-R1] no advertised destination is a wall cell");
            Console.WriteLine("       advertised=" + options.Count + " near_granary=" + nearGranary);
        }

        private sealed class RouteCapture : ILlmTransport
        {
            public readonly List<LlmRequest> Requests = new List<LlmRequest>();
            public string Provider => "test-live";
            public string Model => "test-live";
            public Task<LlmResponse> SendAsync(LlmRequest req, CancellationToken ct)
            {
                lock (Requests) Requests.Add(req);
                return Task.FromResult(EndTurnReply());
            }
        }

        /// <summary>Live-path plumbing with a capturing transport: limits, goal, and the wire body.</summary>
        private static void RouteChoiceLivePlumbing(TestLog log)
        {
            string outRoot = NewRunDir("csr2");
            try
            {
                using (new LiveEnvLease())
                {
                    SetLiveEnv();
                    var capture = new RouteCapture();
                    var provider = new TestLiveProvider(ctx => capture);
                    CsManifest manifest = LiveManifest(new[] { CsScenarios.RouteChoiceId }, 5151, c =>
                    {
                        c.MaxRunRequests = 20; c.MaxScenarioRequests = 20; c.MaxRunWallSeconds = 600;
                        c.MaxScenarioWallSeconds = 600; c.MaxOutputTokens = 8000; c.RequestTimeoutMs = 90000;
                    });
                    CsRunResult run = CsRunner.Run(manifest, outRoot, CsPaths.RepoRoot(), LiveRun(provider));
                    CsScenarioResult scenario = run.Scenarios[0];
                    JsonElement settings = ReadJsonFile(Path.Combine(scenario.Directory, "settings.json"));
                    JsonElement ceilings = Obj(settings, "ceilings");
                    log.Check(CsEvidenceRecorder.Int(ceilings, "max_requests") == 20
                            && CsEvidenceRecorder.Int(ceilings, "max_wall_seconds") == 600,
                        "[CS-R2] settings.json shows 20 requests and 600 seconds for the whole scenario");
                    List<LlmRequest> sent; lock (capture.Requests) sent = new List<LlmRequest>(capture.Requests);
                    bool allTokens = sent.Count > 0, goalSeat0 = false, noGoalSeat1 = true;
                    foreach (LlmRequest r in sent) if (r.MaxOutputTokens != 8000) allTokens = false;
                    log.Check(allTokens, "[CS-R2] every request reaching the transport carries MaxOutputTokens=8000",
                        "requests=" + sent.Count);
                    // The recorded prompts say which seat acted.
                    string[] lines = File.ReadAllLines(Path.Combine(scenario.Directory, "prompts.ndjson"));
                    int seat1 = 0;
                    foreach (string line in lines)
                    {
                        using (JsonDocument d = JsonDocument.Parse(line))
                        {
                            string seat = d.RootElement.GetProperty("seat").GetString();
                            bool hasGoal = d.RootElement.GetProperty("user").GetString()
                                .Contains(CsScenarios.RouteGoal);
                            if (seat == "seat-0" && hasGoal) goalSeat0 = true;
                            if (seat != "seat-0") { seat1++; if (hasGoal) noGoalSeat1 = false; }
                        }
                    }
                    log.Check(goalSeat0 && noGoalSeat1,
                        "[CS-R2] the saved final prompt holds the goal for seat-0 and not for the other seat",
                        "seat1_requests=" + seat1);
                    log.Check(seat1 > 0 && sent.Count == lines.Length,
                        "[CS-R2] both seats' requests reach the same transport and the same request count",
                        "sent=" + sent.Count + " saved=" + lines.Length + " seat1=" + seat1);

                    // The production transport turns that field into the wire body.
                    var cfg = new LlmConfig { Provider = "openai-compatible", Model = "m" };
                    var real = new OpenAiChatTransport(cfg);
                    MethodInfo build = typeof(OpenAiChatTransport).GetMethod("BuildBody",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                    string body = (string)build.Invoke(real, new object[] { sent[0], sent[0].MaxOutputTokens });
                    log.Check(body.Contains("\"max_tokens\":8000"),
                        "[CS-R2] the production OpenAiChatTransport body contains max_tokens:8000");
                }
            }
            finally { Cleanup(outRoot); }
        }
    }
}
