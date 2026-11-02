using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LuanShi.Engine;

// [LS015-W2] Contract B: march through the bounded runner on a planning host.
//
// A plain ITurnHost keeps the demo-slice-3 domestic behaviour. Every check below uses
// FakeLlmTransport only: no network, no paid provider.

internal static partial class Harness
{
    private static void RunLlmMovement()
    {
        Section("[LS015-W2] march through the bounded runner (IPlanningHost)");
        MovementRunnerAcceptedThenFreshObservation();
        MovementRunnerStaleResponseExecutesOnce();
        MovementRunnerPlainHostRejectsMarch();
        MovementRunnerRejectionsBeforeSubmit();
        MovementRunnerInsufficientOrders();
        MovementRunnerExhaustedMovement();
        MovementRunnerCavalrySecondMarch();
        MovementRunnerNeutralCaptureAndChat();
        MovementRunRecordVersionAndHash();
    }

    private static void W2Check(bool ok, string name) => Check(ok, "[LS015-W2] " + name);

    // ---- fixtures -----------------------------------------------------------

    /// <summary>Gate-aware planning host: the runner sees movement/terminal capabilities.</summary>
    private sealed class LlmPlanningHost : IPlanningHost
    {
        private readonly GameEngine _engine;
        private readonly string _seat;

        public int ObserveCalls;
        public int SubmitCalls;
        public int EndTurnCalls;
        public readonly int Token;
        public readonly List<ActionCommand> Commands = new List<ActionCommand>();

        public LlmPlanningHost(GameEngine engine, string seat)
        {
            _engine = engine;
            _seat = seat;
            Token = engine.BeginSeatTurn(seat);
        }

        public Observation Observe()
        {
            ObserveCalls++;
            return _engine.Observe(_seat);
        }

        public ValidationResult Submit(ActionCommand cmd)
        {
            SubmitCalls++;
            if (cmd == null) return ValidationResult.Fail("null command");
            Commands.Add(cmd);
            return _engine.SubmitAs(_seat, Token, cmd);
        }

        public void EndTurn()
        {
            EndTurnCalls++;
            _engine.EndSeatTurn(Token);
        }

        public MovementView PlanMovement() => MovementPlanner.Build(_engine, _seat);

        public bool IsGameOver => _engine.State.WinnerSeatId != null;
    }

    /// <summary>seat-a city + army at (1,1); a neutral city at (2,0) is in sight and reachable.</summary>
    private static GameEngine W2CaptureWorld(int seed = 9101)
    {
        var e = MovementFixture(seed, 2, UnitTypes.Infantry);
        e.State.Cities.Add(new CityState
        {
            Id = "city-n", Name = "Neutral N", X = 2, Z = 0, OwnerSeatId = null,
            Population = 1000, Food = 100, Gold = 0, Garrison = 0,
        });
        return e;
    }

    /// <summary>
    /// Victory fixture: seat-a owns the only city and an army adjacent to one neutral city;
    /// capturing it leaves every city owned by seat-a => unification victory.
    /// </summary>
    private static GameEngine W2VictoryWorld(int seed = 9102)
    {
        var e = new GameEngine(seed);
        e.State.Map = MovementGrid(5, 3);
        e.State.Factions.Add(new FactionState
        {
            SeatId = "seat-a", Name = "Wei", Controller = ControllerType.Llm,
        });
        e.State.Factions.Add(new FactionState
        {
            SeatId = "seat-b", Name = "Shu", Controller = ControllerType.Llm,
        });
        e.State.Cities.Add(new CityState
        {
            Id = "city-a", Name = "City A", X = 0, Z = 0, OwnerSeatId = "seat-a",
            Population = 10000, Food = 5000, Gold = 1000, Garrison = 0, Morale = 100,
        });
        e.State.Cities.Add(new CityState
        {
            Id = "city-n", Name = "Neutral N", X = 2, Z = 1, OwnerSeatId = null,
            Population = 1000, Food = 100, Gold = 0, Garrison = 0,
        });
        e.State.Armies.Add(new ArmyState
        {
            Id = "army-a-1", Name = "Army A", OwnerSeatId = "seat-a", Troops = 2000,
            UnitType = UnitTypes.Infantry, X = 1, Z = 1,
        });
        e.BeginSeason();
        e.State.Season = 1;
        e.State.Phase = "player_orders";
        return e;
    }

    private static string W2March(string armyId, int x, int z)
        => "{\"type\":\"march\",\"target_id\":\"" + armyId + "\",\"param_a\":" + x
         + ",\"param_b\":" + z + "}";

    private static string W2EndTurn() => "{\"type\":\"end_turn\"}";

    // ---- 1: accepted move -> fresh observation -> next decision ------------

    private static void MovementRunnerAcceptedThenFreshObservation()
    {
        var engine = W2CaptureWorld(9103);
        var host = new LlmPlanningHost(engine, "seat-a");
        var fake = new FakeLlmTransport();
        var runner = new LlmTurnRunner(fake,
            new LlmConfig { MaxRequestsPerTurn = 6, MaxActionsPerTurn = 4 }, null);
        fake.EnqueueSuccess(W2March("army-a-1", 2, 0));
        fake.EnqueueSuccess(W2EndTurn());

        runner.Begin(host, host.Token, "seat-a", 3);
        LlmDrain(runner);

        var army = engine.State.FindArmy("army-a-1");
        W2Check(runner.State == RunnerState.Finished && runner.EndReason == EndReason.ModelEndedTurn
           && fake.CallCount == 2 && host.SubmitCalls == 1,
            "an accepted march is followed by one fresh observation and the next decision");
        W2Check(army.X == 2 && army.Z == 0
           && string.Equals(engine.State.FindCity("city-n").OwnerSeatId, "seat-a", StringComparison.Ordinal),
            "the model-selected march moved the army and captured the neutral city");
        W2Check(fake.Requests[0].User.Contains("MOVEMENT OPTIONS")
           && fake.Requests[1].User.Contains("position=2,0")
           && runner.RecentHistory.Any(h => h.Contains("march army-a-1 -> ok")),
            "the next prompt carries the fresh movement view and the action feedback");
        W2Check(!fake.Requests[1].User.Contains("Accepted by engine.")
           && !fake.Requests[1].System.Contains("Captured Neutral N"),
            "observer chat text never enters a prompt");
    }

    // ---- 2: stale/duplicate response executes once -------------------------

    private static void MovementRunnerStaleResponseExecutesOnce()
    {
        var engine = MovementFixture(9104, 2, UnitTypes.Infantry);
        var host = new LlmPlanningHost(engine, "seat-a");
        var fake = new FakeLlmTransport();
        var deferred = fake.EnqueueDeferred();
        fake.EnqueueSuccess(W2EndTurn());
        var runner = new LlmTurnRunner(fake,
            new LlmConfig { MaxRequestsPerTurn = 6, MaxActionsPerTurn = 4 }, null);

        runner.Begin(host, host.Token, "seat-a", 3);
        deferred.SetResult(LlmResponse.Success(W2March("army-a-1", 2, 1), 0, 5, 2, "once"));
        System.Threading.Thread.Sleep(20);
        runner.Tick();
        runner.Tick();   // a second Tick on the same generation must be inert
        runner.Tick();

        var army = engine.State.FindArmy("army-a-1");
        W2Check(host.SubmitCalls == 1 && runner.ActionsThisTurn == 1
           && army.X == 2 && army.Z == 1,
            "duplicate ticks / a replayed response execute the march exactly once");
        W2Check(runner.State == RunnerState.Finished && fake.CallCount == 2,
            "the turn still advances to the next decision and ends normally");
    }

    // ---- 2b: a plain host is never offered march ---------------------------

    private static void MovementRunnerPlainHostRejectsMarch()
    {
        // LlmHarnessHost is a plain ITurnHost: the prompt is domestic-only, so a march is not
        // advertised and must be rejected with a safe message before host.Submit.
        var engine = LlmEngine();
        var host = new LlmHarnessHost(engine, "seat-a");
        var fake = new FakeLlmTransport();
        var runner = new LlmTurnRunner(fake, new LlmConfig { MaxInvalidPerTurn = 4 }, null);
        fake.EnqueueSuccess(W2March("army-a-1", 1, 0));
        fake.EnqueueSuccess(W2EndTurn());

        runner.Begin(host, host.Token, "seat-a", 3);
        LlmDrain(runner);

        W2Check(host.SubmitCalls == 0 && runner.InvalidThisTurn == 1
           && runner.RecentHistory.Any(h => h.Contains("rejected: unsupported action")),
            "a march on a non-planning host is rejected safely before Submit");
        W2Check(fake.Requests[0].System == PromptBuilder.DomesticSystemPrompt()
           && !fake.Requests[0].User.Contains("MOVEMENT OPTIONS"),
            "the plain host's request stays on the domestic demo-slice-3 prompt");
    }

    // ---- 3: malformed, spoofed and foreign marches never reach Submit -----

    private static void MovementRunnerRejectionsBeforeSubmit()
    {
        var engine = MovementFixture(9105, 2, UnitTypes.Infantry);
        engine.State.Armies.Add(new ArmyState
        {
            Id = "army-b-1", Name = "B Army", OwnerSeatId = "seat-b", Troops = 500,
            UnitType = UnitTypes.Infantry, X = 4, Z = 4,
        });
        var host = new LlmPlanningHost(engine, "seat-a");
        var fake = new FakeLlmTransport();
        var runner = new LlmTurnRunner(fake, new LlmConfig
        {
            MaxRequestsPerTurn = 10, MaxActionsPerTurn = 6, MaxInvalidPerTurn = 5,
        }, null);
        fake.EnqueueSuccess("{\"type\":\"march\",\"target_id\":\"army-a-1\"}");   // missing coordinates
        fake.EnqueueSuccess("{\"type\":\"march\",\"target_id\":\"army-a-1\",\"param_a\":300,\"param_b\":1}");
        fake.EnqueueSuccess("{\"type\":\"march\",\"target_id\":\"army-a-1\",\"param_a\":1,"
            + "\"param_b\":1,\"actor_seat_id\":\"seat-a\"}");
        fake.EnqueueSuccess(W2March("army-b-1", 1, 0));              // well-formed, foreign army
        fake.EnqueueSuccess(W2EndTurn());

        runner.Begin(host, host.Token, "seat-a", 3);
        LlmDrain(runner);

        var own = engine.State.FindArmy("army-a-1");
        var foreign = engine.State.FindArmy("army-b-1");
        W2Check(runner.InvalidThisTurn == 4 && host.SubmitCalls == 1,
            "malformed, oversized and spoofed marches are rejected before host.Submit");
        W2Check(runner.RecentHistory.Any(h => h.Contains("rejected: invalid target"))
           && own.X == 1 && own.Z == 1 && foreign.X == 4 && foreign.Z == 4,
            "a foreign army id maps to 'invalid target' and changes no army");
    }

    // ---- 4: orders and movement budgets ------------------------------------

    private static void MovementRunnerInsufficientOrders()
    {
        var engine = MovementFixture(9106, 2, UnitTypes.Infantry);
        engine.State.FindFaction("seat-a").CommandPoints = 0;
        var host = new LlmPlanningHost(engine, "seat-a");
        var fake = new FakeLlmTransport();
        var runner = new LlmTurnRunner(fake, new LlmConfig { MaxRequestsPerTurn = 6 }, null);
        fake.EnqueueSuccess(W2March("army-a-1", 2, 1));
        fake.EnqueueSuccess(W2EndTurn());

        runner.Begin(host, host.Token, "seat-a", 3);
        LlmDrain(runner);

        W2Check(runner.State == RunnerState.Finished && runner.InvalidThisTurn == 1
           && host.SubmitCalls == 1 && runner.EndReason == EndReason.ModelEndedTurn,
            "a rejected march still lets the model continue to its next decision");
        W2Check(runner.RecentHistory.Any(h => h.Contains("rejected: no orders left"))
           && fake.Requests[1].User.Contains("no orders left"),
            "insufficient orders map to the safe 'no orders left' category");
    }

    private static void MovementRunnerExhaustedMovement()
    {
        var engine = MovementFixture(9107, 2, UnitTypes.Infantry);
        var host = new LlmPlanningHost(engine, "seat-a");
        var fake = new FakeLlmTransport();
        var runner = new LlmTurnRunner(fake,
            new LlmConfig { MaxRequestsPerTurn = 8, MaxInvalidPerTurn = 3 }, null);
        fake.EnqueueSuccess(W2March("army-a-1", 2, 1));
        fake.EnqueueSuccess(W2March("army-a-1", 2, 0));
        fake.EnqueueSuccess(W2EndTurn());

        runner.Begin(host, host.Token, "seat-a", 3);
        LlmDrain(runner);

        var army = engine.State.FindArmy("army-a-1");
        W2Check(runner.ActionsThisTurn == 2 && host.SubmitCalls == 2
           && army.X == 2 && army.Z == 1,
            "the first march moves the infantry; the second is executed but rejected by the engine");
        W2Check(runner.RecentHistory.Any(h => h.Contains("rejected: already marched this season")),
            "an exhausted infantry march maps to 'already marched this season'");
    }

    private static void MovementRunnerCavalrySecondMarch()
    {
        var engine = MovementFixture(9108, 2, UnitTypes.Cavalry);
        var host = new LlmPlanningHost(engine, "seat-a");
        var fake = new FakeLlmTransport();
        var runner = new LlmTurnRunner(fake,
            new LlmConfig { MaxRequestsPerTurn = 8, MaxInvalidPerTurn = 3 }, null);
        fake.EnqueueSuccess(W2March("army-a-1", 2, 1));
        fake.EnqueueSuccess(W2March("army-a-1", 2, 2));
        fake.EnqueueSuccess(W2EndTurn());

        runner.Begin(host, host.Token, "seat-a", 3);
        LlmDrain(runner);

        var army = engine.State.FindArmy("army-a-1");
        W2Check(runner.InvalidThisTurn == 0 && runner.ActionsThisTurn == 2
           && army.X == 2 && army.Z == 2,
            "cavalry may march twice through the runner");
        W2Check(fake.Requests[1].User.Contains("marches_left=1"),
            "the second prompt advertises the cavalry's remaining march");
    }

    // ---- 5: capture + chat -------------------------------------------------

    private static void MovementRunnerNeutralCaptureAndChat()
    {
        var engine = W2CaptureWorld(9109);
        var host = new LlmPlanningHost(engine, "seat-a");
        var fake = new FakeLlmTransport();
        var runner = new LlmTurnRunner(fake,
            new LlmConfig { MaxRequestsPerTurn = 6, MaxActionsPerTurn = 4 }, null);
        fake.EnqueueSuccess(W2March("army-a-1", 2, 0));
        fake.EnqueueSuccess(W2EndTurn());

        runner.Begin(host, host.Token, "seat-a", 3);
        LlmDrain(runner);

        var activity = runner.Activity;
        W2Check(activity.Any(e => e.Kind == "command"
                  && e.Text == "march army-a-1 -> (2,0)"),
            "the command entry names the army and destination hex");
        W2Check(activity.Any(e => e.Kind == "result"
                  && e.Text == "Accepted by engine. Captured Neutral N."),
            "the result entry derives 'captured <city>' from the seat observation");
        W2Check(!fake.Requests.Any(r => r.User.Contains("Captured Neutral N")
                  || r.User.Contains("Accepted by engine.")),
            "observer chat text never reaches any prompt");
    }

    // ---- 6: run record carries the planning version/hash -------------------

    private static void MovementRunRecordVersionAndHash()
    {
        string dir = LlmTempDir("ls015-w2-record");
        try
        {
            var engine = MovementFixture(9110, 2, UnitTypes.Infantry);
            var host = new LlmPlanningHost(engine, "seat-a");
            var fake = new FakeLlmTransport();
            var recorder = new LlmRunRecorder(dir);
            var runner = new LlmTurnRunner(fake, new LlmConfig { MaxRequestsPerTurn = 4 }, recorder);
            fake.EnqueueSuccess(W2March("army-a-1", 2, 1));
            fake.EnqueueSuccess(W2EndTurn());
            runner.Begin(host, host.Token, "seat-a", 3);
            LlmDrain(runner);

            string[] files = Directory.GetFiles(dir, "*.jsonl");
            string first = files.Length == 1 ? File.ReadAllLines(files[0])[0] : "";
            bool parsed = StrictJson.TryParse(first, out JsonNode node, out _);
            var obj = node as JsonObject;
            W2Check(parsed && obj != null
               && LlmStr(obj, "prompt_version") == "demo-slice-4"
               && LlmStr(obj, "schema_hash") == PromptBuilder.SchemaHashFor(true),
                "run records carry the stable planning prompt version and schema hash");
        }
        finally { LlmDelete(dir); }
    }
}
