using System;
using System.Collections.Generic;
using System.Linq;
using LuanShi.Engine;

// [ATK-2] Attack and capture through the bounded runner and the spectator host, fake provider only.
//
// The model chooses from engine-computed candidates and submits a normal command; the engine's one
// validator decides. Nothing below touches the network or a paid provider.

internal static partial class Harness
{
    private static void RunLlmAttack()
    {
        Section("[ATK-2] attack and capture through the runner (fake provider)");
        AttackRunnerFieldBattle();
        AttackRunnerAssaultWinsGame();
        AttackRunnerOptInGating();
        AttackRunnerIllegalOrders();
        AttackRunnerHiddenWorldEquivalence();
        AttackSpectatorFlow();
    }

    private static void RunnerAtkCheck(bool ok, string name) => Check(ok, "[ATK-2] " + name);

    private static LlmConfig AtkConfig(bool attack = true, int maxInvalid = 2, int maxActions = 5)
        => new LlmConfig
        {
            MaxRequestsPerTurn = 12, MaxActionsPerTurn = maxActions, MaxInvalidPerTurn = maxInvalid,
            MaxTotalRequests = 40, EnableAttack = attack,
        };

    /// <summary>Planning host that can also plan attacks: what a host opts in with.</summary>
    private sealed class LlmAttackHost : IAttackPlanningHost
    {
        private readonly GameEngine _engine;
        private readonly string _seat;

        public int SubmitCalls;
        public int PlanAttackCalls;
        public readonly int Token;
        public readonly List<ActionCommand> Commands = new List<ActionCommand>();

        public LlmAttackHost(GameEngine engine, string seat)
        {
            _engine = engine;
            _seat = seat;
            Token = engine.BeginSeatTurn(seat);
        }

        public Observation Observe() => _engine.Observe(_seat);

        public ValidationResult Submit(ActionCommand cmd)
        {
            SubmitCalls++;
            if (cmd == null) return ValidationResult.Fail("null command");
            Commands.Add(cmd);
            return _engine.SubmitAs(_seat, Token, cmd);
        }

        public void EndTurn() => _engine.EndSeatTurn(Token);

        public MovementView PlanMovement() => MovementPlanner.Build(_engine, _seat);

        public AttackView PlanAttacks()
        {
            PlanAttackCalls++;
            return AttackPlanner.Build(_engine, _seat);
        }

        public bool IsGameOver => _engine.State.WinnerSeatId != null;
    }

    private static string AtkJson(string type, string armyId, int x, int z, string summary = null)
        => "{\"type\":\"" + type + "\",\"target_id\":\"" + armyId + "\",\"param_a\":" + x
         + ",\"param_b\":" + z + (summary == null ? "" : ",\"summary\":\"" + summary + "\"") + "}";

    // ---- 1: a model-chosen field battle ------------------------------------

    private static void AttackRunnerFieldBattle()
    {
        var engine = AttackWorld(9401);
        var host = new LlmAttackHost(engine, "seat-a");
        var fake = new FakeLlmTransport();
        fake.EnqueueSuccess(AtkJson("attack_army", "army-a-1", 2, 1, "Strike their vanguard."));
        fake.EnqueueSuccess(W2EndTurn());
        var runner = new LlmTurnRunner(fake, AtkConfig(), null);
        runner.Begin(host, host.Token, "seat-a", 3);
        LlmDrain(runner);

        var battle = Find(engine.Log, "battle_field");
        RunnerAtkCheck(runner.State == RunnerState.Finished && runner.EndReason == EndReason.ModelEndedTurn
           && fake.CallCount == 2 && host.SubmitCalls == 1 && host.Commands[0].Type == ActionType.AttackArmy,
            "the model's attack_army reaches the normal validator once and the turn continues to end_turn");
        RunnerAtkCheck(battle != null && battle.Payload["attacker_army_id"] == "army-a-1"
           && battle.Payload["defender_army_id"] == "army-b-1",
            "the engine resolved a real field battle between the two named armies");
        RunnerAtkCheck(engine.State.FindFaction("seat-a").CommandPoints == 4,
            "the engine charged the one-order price");

        string first = fake.Requests[0].User;
        RunnerAtkCheck(first.Contains("Prompt version: " + PromptBuilder.AttackPromptVersion)
           && fake.Requests[0].System.Contains("ATTACK AND CAPTURE")
           && first.Contains("type=attack_army target_id=army-a-1 param_a=2 param_b=1 cost=1")
           && first.Contains("type=attack_city target_id=army-a-1 param_a=1 param_b=2 cost=2"),
            "the first prompt carries the attack rules, version and both engine-computed candidates");
        RunnerAtkCheck(runner.RecentHistory.Any(h => h.Contains("attack_army army-a-1 -> ok"))
           && fake.Requests[1].User.Contains("attack_army army-a-1 -> ok")
           && runner.Activity.Any(a => a.Kind == "command" && a.Text == "attack_army army-a-1 -> (2,1)"),
            "the next request and the activity log record the accepted order");
        RunnerAtkCheck(!first.Contains("army-b-hidden") && !first.Contains("city-hidden"),
            "no out-of-sight id appears in the prompt");
    }

    // ---- 2: an assault that takes the last enemy city ends the game ----------

    private static void AttackRunnerAssaultWinsGame()
    {
        var engine = W2VictoryWorld(9411);
        var enemy = engine.State.FindCity("city-n");
        enemy.OwnerSeatId = "seat-b";
        enemy.Name = "City B";
        enemy.Garrison = 100;
        enemy.Morale = 100;

        var host = new LlmAttackHost(engine, "seat-a");
        var fake = new FakeLlmTransport();
        fake.EnqueueSuccess(AtkJson("attack_city", "army-a-1", 2, 1));
        fake.EnqueueSuccess(W2EndTurn());
        var runner = new LlmTurnRunner(fake, AtkConfig(), null);
        runner.Begin(host, host.Token, "seat-a", 3);

        RunnerAtkCheck(fake.Requests[0].User.Contains("type=attack_city target_id=army-a-1 param_a=2 param_b=1 cost=2")
           && fake.Requests[0].User.Contains("garrison 100") && fake.Requests[0].User.Contains("at least 300 troops"),
            "the assault candidate states the garrison and the 3:1 requirement before the model chooses");
        LlmDrain(runner);

        RunnerAtkCheck(engine.State.FindCity("city-n").OwnerSeatId == "seat-a"
           && Find(engine.Log, "battle_siege") != null && Find(engine.Log, "battle_siege").Payload["outcome"] == "captured",
            "the engine resolved the siege and transferred the city");
        RunnerAtkCheck(string.Equals(engine.State.WinnerSeatId, "seat-a", StringComparison.Ordinal)
           && runner.EndReason == EndReason.GameOver && fake.CallCount == 1 && host.SubmitCalls == 1,
            "the winning assault stops the turn with no further provider request");
        RunnerAtkCheck(runner.Activity.Any(a => a.Kind == "result" && a.Text.Contains("Captured City B")),
            "the result line names the captured city from a fresh seat-filtered observation");
    }

    // ---- 3: opt-in gating --------------------------------------------------

    private static void AttackRunnerOptInGating()
    {
        string attackReply = AtkJson("attack_army", "army-a-1", 2, 1);

        // (a) capable host, flag off: the legacy march-only prompt, and an attack reply is refused.
        var engineA = AttackWorld(9421);
        var hostA = new LlmAttackHost(engineA, "seat-a");
        var fakeA = new FakeLlmTransport();
        fakeA.EnqueueSuccess(attackReply);
        fakeA.EnqueueSuccess(W2EndTurn());
        var runnerA = new LlmTurnRunner(fakeA, AtkConfig(attack: false), null);
        runnerA.Begin(hostA, hostA.Token, "seat-a", 3);
        LlmDrain(runnerA);
        RunnerAtkCheck(hostA.PlanAttackCalls == 0 && hostA.SubmitCalls == 0
           && !fakeA.Requests[0].User.Contains("attack_army") && !fakeA.Requests[0].System.Contains("ATTACK")
           && fakeA.Requests[0].User.Contains("Prompt version: " + PromptBuilder.PromptVersion)
           && engineA.Log.Records.All(r => r.EventType != "battle_field"),
            "with the flag off the host is never queried, the prompt has no attack text and nothing reaches the engine");
        RunnerAtkCheck(runnerA.RecentHistory.SequenceEqual(new[] { "invalid model response -> unsupported action type" })
           && runnerA.InvalidThisTurn == 1,
            "an attack reply is an unsupported action type when not advertised");

        // (b) flag on but a plain planning host: identical to the flag-off prompt bytes.
        var engineB = AttackWorld(9421);
        var hostB = new LlmPlanningHost(engineB, "seat-a");
        var fakeB = new FakeLlmTransport();
        fakeB.EnqueueSuccess(attackReply);
        fakeB.EnqueueSuccess(W2EndTurn());
        var runnerB = new LlmTurnRunner(fakeB, AtkConfig(attack: true), null);
        runnerB.Begin(hostB, hostB.Token, "seat-a", 3);
        LlmDrain(runnerB);
        RunnerAtkCheck(fakeB.Requests[0].System == fakeA.Requests[0].System
           && fakeB.Requests[0].User == fakeA.Requests[0].User && hostB.SubmitCalls == 0
           && runnerB.RecentHistory.Contains("invalid model response -> unsupported action type"),
            "the flag on a host that cannot plan attacks changes nothing: same prompt bytes, attack refused");

        // (c) the default config on the capable host matches a plain host byte for byte too.
        var engineC = AttackWorld(9421);
        var hostC = new LlmAttackHost(engineC, "seat-a");
        var fakeC = new FakeLlmTransport();
        fakeC.EnqueueSuccess(W2EndTurn());
        var runnerC = new LlmTurnRunner(fakeC, new LlmConfig { MaxRequestsPerTurn = 12, MaxActionsPerTurn = 5, MaxTotalRequests = 40 }, null);
        runnerC.Begin(hostC, hostC.Token, "seat-a", 3);
        RunnerAtkCheck(fakeC.Requests[0].User == fakeA.Requests[0].User
           && fakeC.Requests[0].System == PromptBuilder.SystemPromptFor(true),
            "a default LlmConfig on an attack-capable host sends the unchanged demo-slice-4 prompt");
    }

    // ---- 4: illegal orders cannot bypass the validator ---------------------

    private static void AttackRunnerIllegalOrders()
    {
        var engine = AttackWorld(9431, hiddenArmy: true);
        var host = new LlmAttackHost(engine, "seat-a");
        var fake = new FakeLlmTransport();
        fake.EnqueueSuccess(AtkJson("attack_army", "army-a-1", 6, 4));      // hidden enemy army, out of range
        fake.EnqueueSuccess(AtkJson("attack_army", "army-a-1", 1, 3));      // visible but two hexes away
        fake.EnqueueSuccess(AtkJson("attack_city", "army-a-1", 0, 1));      // neutral city: use march
        fake.EnqueueSuccess(AtkJson("attack_city", "army-a-1", 7, 5));      // hidden enemy city
        fake.EnqueueSuccess(AtkJson("attack_army", "army-b-1", 1, 1));      // someone else's army
        fake.EnqueueSuccess(AtkJson("attack_army", "army-zz", 2, 1));       // no such army
        fake.EnqueueSuccess(AtkJson("attack_army", "army-a-1", 1, 1));      // own hex
        fake.EnqueueSuccess(W2EndTurn());
        var runner = new LlmTurnRunner(fake, AtkConfig(maxInvalid: 20, maxActions: 20), null);
        runner.Begin(host, host.Token, "seat-a", 3);
        LlmDrain(runner);

        var seat = engine.State.FindFaction("seat-a");
        RunnerAtkCheck(host.SubmitCalls == 7 && runner.InvalidThisTurn == 7 && fake.CallCount == 8,
            "all seven illegal orders reached the validator and every one was rejected");
        RunnerAtkCheck(seat.CommandPoints == 5 && engine.State.FindArmy("army-a-1").Troops == 2000
           && engine.State.FindArmy("army-b-1").Troops == 500
           && engine.Log.Records.All(r => r.EventType != "battle_field" && r.EventType != "battle_siege"),
            "rejected orders cost nothing, fought nothing and changed no troop count");
        var rejections = runner.RecentHistory.Where(h => h.Contains("rejected")).ToList();
        RunnerAtkCheck(rejections.Count == 7
           && rejections.All(h => h.EndsWith("rejected: action rejected") || h.EndsWith("rejected: invalid target")),
            "model-facing feedback is only the typed allowlist, never raw engine text");
        RunnerAtkCheck(rejections[0] == "attack_army army-a-1 -> rejected: action rejected"
           && rejections[0] == rejections[1],
            "a hidden target and a visible out-of-range target read the same way");
        RunnerAtkCheck(fake.Requests.Skip(1).All(r => !r.User.Contains("army-b-hidden") && !r.User.Contains("4321")),
            "the rejection loop never leaks the hidden army to the model");

        // A model reply carrying spoofed host fields is refused before the engine is touched.
        var engine2 = AttackWorld(9432);
        var host2 = new LlmAttackHost(engine2, "seat-a");
        var fake2 = new FakeLlmTransport();
        fake2.EnqueueSuccess("{\"type\":\"attack_army\",\"target_id\":\"army-b-1\",\"param_a\":1,\"param_b\":1,\"actor_seat_id\":\"seat-b\"}");
        fake2.EnqueueSuccess(W2EndTurn());
        var runner2 = new LlmTurnRunner(fake2, AtkConfig(), null);
        runner2.Begin(host2, host2.Token, "seat-a", 3);
        LlmDrain(runner2);
        RunnerAtkCheck(host2.SubmitCalls == 0
           && runner2.RecentHistory.Contains("invalid model response -> forbidden field"),
            "a spoofed actor_seat_id on an attack never reaches the engine");
    }

    // ---- 5: hidden-world equivalence through the whole prompt -------------

    private static void AttackRunnerHiddenWorldEquivalence()
    {
        var a = AttackWorld(9441, hiddenGarrison: 0, hiddenArmy: false);
        var b = AttackWorld(9441, hiddenGarrison: 7777, hiddenArmy: true);
        var results = new List<List<string>>();
        var prompts = new List<string>();
        foreach (var engine in new[] { a, b })
        {
            var host = new LlmAttackHost(engine, "seat-a");
            var fake = new FakeLlmTransport();
            fake.EnqueueSuccess(AtkJson("attack_army", "army-a-1", 6, 4));
            fake.EnqueueSuccess(AtkJson("attack_city", "army-a-1", 7, 5));
            fake.EnqueueSuccess(AtkJson("attack_city", "army-a-1", 1, 2));
            fake.EnqueueSuccess(W2EndTurn());
            var runner = new LlmTurnRunner(fake, AtkConfig(maxInvalid: 20, maxActions: 20), null);
            runner.Begin(host, host.Token, "seat-a", 3);
            LlmDrain(runner);
            results.Add(runner.RecentHistory.ToList());
            prompts.Add(string.Join("\n----\n", fake.Requests.Take(3).Select(r => r.System + "\n" + r.User)));
        }
        RunnerAtkCheck(prompts[0] == prompts[1],
            "worlds differing only outside sight send byte-identical prompts for the first three requests");
        RunnerAtkCheck(results[0].SequenceEqual(results[1]) && results[0].Any(h => h.Contains("attack_city army-a-1 -> ok")),
            "and produce the same typed feedback for the hidden probes, then the same accepted assault");
    }

    // ---- 6: the real spectator host --------------------------------------------

    private static void AttackSpectatorFlow()
    {
        var engine = new GameEngine(9451, "g-atk");
        engine.State.Map = MovementGrid(8, 6);
        engine.State.Factions.Add(new FactionState { SeatId = "seat-a", Name = "Wei", Controller = ControllerType.Llm });
        engine.State.Factions.Add(new FactionState { SeatId = "seat-b", Name = "Shu", Controller = ControllerType.Llm });
        engine.State.Cities.Add(new CityState
        {
            Id = "city-a", Name = "City A", X = 0, Z = 0, OwnerSeatId = "seat-a",
            Population = 10000, Food = 5000, Gold = 1000, Garrison = 0, Morale = 100,
        });
        engine.State.Cities.Add(new CityState
        {
            Id = "city-b", Name = "City B", X = 7, Z = 5, OwnerSeatId = "seat-b",
            Population = 10000, Food = 5000, Gold = 1000, Garrison = 0, Morale = 100,
        });
        engine.State.Armies.Add(new ArmyState
        {
            Id = "army-a-1", Name = "Army A", OwnerSeatId = "seat-a", Troops = 2000,
            UnitType = UnitTypes.Infantry, X = 3, Z = 3,
        });
        engine.State.Armies.Add(new ArmyState
        {
            Id = "army-b-1", Name = "Army B", OwnerSeatId = "seat-b", Troops = 1500,
            UnitType = UnitTypes.Infantry, X = 4, Z = 3,
        });
        engine.BeginSeason();
        engine.State.Season = 2;
        engine.State.Phase = "player_orders";

        var fake = new FakeLlmTransport();
        fake.EnqueueSuccess(AtkJson("attack_army", "army-a-1", 4, 3, "Engage."));
        fake.EnqueueSuccess(W2EndTurn());
        fake.EnqueueSuccess(W2EndTurn());
        var runner = new LlmTurnRunner(fake, AtkConfig(), null);
        var session = new SpectatorSession(engine, runner, 1, 4242);
        session.Start();
        SpectatorDrain(session);

        RunnerAtkCheck(session.State == SpectatorState.Completed && session.TotalRequests == fake.CallCount
           && Find(engine.Log, "battle_field") != null
           && Find(engine.Log, "battle_field").Payload["attacker_seat_id"] == "seat-a",
            "the real spectator host offers attacks, runs the model's order and completes the season");
        string seatA = fake.Requests[0].User;
        string seatB = fake.Requests[fake.Requests.Count - 1].User;
        RunnerAtkCheck(seatA.Contains("type=attack_army target_id=army-a-1 param_a=4 param_b=3")
           && !seatA.Contains("target_id=army-b-1"),
            "seat-a's prompt lists only its own army as the attacker");
        RunnerAtkCheck(seatB.Contains("target_id=army-b-1 param_a=3 param_b=3")
           && !seatB.Contains("target_id=army-a-1 param_a"),
            "seat-b's prompt lists only seat-b's own army as the attacker");
        RunnerAtkCheck(session.TurnRecords.Count == 2 && session.TurnRecords.All(r => r.EndedBy == "end_turn"),
            "both seats' turns end normally after the battle");
    }
}
