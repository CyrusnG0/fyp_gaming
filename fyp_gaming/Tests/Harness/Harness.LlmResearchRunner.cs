using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using LuanShi.Engine;

// [RES-2] Research orders through the real LlmTurnRunner, its fake provider and the real GameEngine.
//
// The model chooses from engine-computed candidates and submits a normal command; the engine's one
// validator decides. Nothing below touches the network or a paid provider.
//
// The production config/runner switch belongs to Codex. These checks therefore read the switch by
// reflection: they compile before that wiring exists, and they FAIL (not skip silently) once this
// suite is registered in Program.cs without it.

internal static partial class Harness
{
    private static void RunLlmResearchRunner()
    {
        Section("[RES-2] research orders through the real runner (fake provider)");
        // The disabled contract needs no production switch, so it runs before the wiring gate.
        ResRunnerFeatureOff();
        if (!ResRunnerGate()) return;
        ResRunnerBuysLevel();
        ResRunnerRejections();
        ResRunnerPrivacy();
        ResRunnerComposition();
        ResRunnerFlagMatrix();
    }

    private static void ResRunnerCheck(bool ok, string name) => Check(ok, "[RES-2] " + name);

    // ---- wiring gate --------------------------------------------------------

    private static FieldInfo ResResearchSwitch =>
        typeof(LlmConfig).GetField("EnableResearch", BindingFlags.Public | BindingFlags.Instance);

    /// <summary>
    /// One explicit assertion, not a silent skip: registering this suite before the production switch
    /// exists must be visible as a failure.
    /// </summary>
    private static bool ResRunnerGate()
    {
        FieldInfo field = ResResearchSwitch;
        bool wired = field != null && field.FieldType == typeof(bool);
        ResRunnerCheck(wired, "LlmConfig exposes the opt-in EnableResearch switch (Codex wiring)");
        if (wired) return true;
        Console.WriteLine("  note: the remaining [RES-2] checks need the production config/runner "
            + "switch; see the handoff for the exact wiring.");
        return false;
    }

    private static void ResEnable(LlmConfig config, bool research)
    {
        if (!research) return;
        FieldInfo field = ResResearchSwitch;
        if (field != null && field.FieldType == typeof(bool)) field.SetValue(config, true);
    }

    private static LlmConfig ResRunnerConfig(bool research = true, bool attack = false,
        bool personas = false, bool deliberation = false)
    {
        var config = new LlmConfig
        {
            MaxRequestsPerTurn = 12,
            MaxActionsPerTurn = 6,
            MaxInvalidPerTurn = 6,
            MaxTotalRequests = 40,
            EnableAttack = attack,
            EnablePersonas = personas,
            EnableDeliberation = deliberation,
        };
        ResEnable(config, research);
        return config;
    }

    private static GameEngine ResRunnerWorld(int seed, int points = 100)
    {
        var engine = MovementFixture(seed, 2, UnitTypes.Infantry);
        engine.State.FindFaction("seat-a").ResearchPoints = points;
        return engine;
    }

    private sealed class ResCaptureRecorder : ILlmRunRecorder
    {
        public readonly List<string> Lines = new List<string>();
        public void Record(string runId, string jsonLine) => Lines.Add(jsonLine ?? "");
    }

    // ---- 1: a legal order buys one level ------------------------------------

    private static void ResRunnerBuysLevel()
    {
        var engine = ResRunnerWorld(9801, 100);
        var host = new LlmPlanningHost(engine, "seat-a");
        var fake = new FakeLlmTransport();
        fake.EnqueueSuccess(ResJson(ResearchLine.Agri));
        fake.EnqueueSuccess(W2EndTurn());
        var recorder = new ResCaptureRecorder();
        var runner = new LlmTurnRunner(fake, ResRunnerConfig(), recorder);
        runner.Begin(host, host.Token, "seat-a", 3);

        string first = fake.Requests[0].User;
        ResRunnerCheck(fake.Requests[0].System.Contains("RESEARCH ORDERS")
           && first.Contains("Prompt version: " + PromptBuilder.ResearchPromptVersion + "\n")
           && first.Contains("type=research args={\"line\":\"agri\"}")
           && first.Contains("label=\"Research Agriculture to level 1 for 20 research points\""),
            "the first request carries the research rules, the research identity and every line");

        LlmDrain(runner);
        var seat = engine.State.FindFaction("seat-a");
        var research = Find(engine.Log, "research");
        ResRunnerCheck(runner.State == RunnerState.Finished
           && runner.EndReason == EndReason.ModelEndedTurn
           && fake.CallCount == 2 && host.SubmitCalls == 1,
            "the turn continues to end_turn after exactly one submitted order");
        ResRunnerCheck(host.Commands[0].Type == ActionType.Research
           && host.Commands[0].TargetId == null && host.Commands[0].ParamA == 0
           && host.Commands[0].Args != null && host.Commands[0].Args["line"] == "agri",
            "the model's targetless research order reaches the normal validator unchanged");
        ResRunnerCheck(seat.ResearchLevel(ResearchLine.Agri) == 1 && seat.ResearchPoints == 80
           && seat.CommandPoints == 4,
            "the engine bought the level, deducted its points and charged one order");
        ResRunnerCheck(research != null && research.Visibility == "owner_only"
           && research.Payload["line"] == "agri" && research.Payload["level"] == "1",
            "the canonical research event stays owner-only with its line and level");
        ResRunnerCheck(runner.RecentHistory.Any(h => h == "research -> ok")
           && fake.Requests[1].User.Contains("research -> ok"),
            "the accepted order enters the seat history and the next request");
        ResRunnerCheck(recorder.Lines.Any(l => l.Contains("\"prompt_version\":\""
            + PromptBuilder.ResearchPromptVersion + "\"")),
            "the run record carries the research prompt version");
    }

    // ---- 2: every refusal is free and mutates nothing -----------------------

    private static void ResRunnerRejections()
    {
        // Unaffordable: below the level's price.
        var broke = ResRunnerWorld(9811, BalanceConfig.ResearchLevelCost(1) - 1);
        string unaffordable = ResRunnerReply(broke, 9811, ResJson(ResearchLine.Agri)).History
            .FirstOrDefault(h => h.StartsWith("research -> rejected", StringComparison.Ordinal));
        var brokeSeat = broke.State.FindFaction("seat-a");
        ResRunnerCheck(brokeSeat.ResearchLevel(ResearchLine.Agri) == 0
           && brokeSeat.ResearchPoints == BalanceConfig.ResearchLevelCost(1) - 1
           && brokeSeat.CommandPoints == BalanceConfig.CommandPointsPerSeason
           && Find(broke.Log, "research") == null,
            "an unaffordable research order is refused without touching points, level or orders");
        ResRunnerCheck(unaffordable == "research -> rejected: action rejected",
            "the model sees the generic category, not the engine's point arithmetic");

        // Maxed line: the price gate is above, the cap is the reason.
        var maxed = ResRunnerWorld(9812, 500);
        SetLevel(maxed, "seat-a", ResearchLine.Agri, BalanceConfig.ResearchMaxLevel);
        var maxedResult = ResRunnerReply(maxed, 9812, ResJson(ResearchLine.Agri));
        var maxedSeat = maxed.State.FindFaction("seat-a");
        ResRunnerCheck(maxedSeat.ResearchLevel(ResearchLine.Agri) == BalanceConfig.ResearchMaxLevel
           && maxedSeat.ResearchPoints == 500
           && maxedResult.History.Any(h => h.EndsWith("rejected: cap reached", StringComparison.Ordinal)),
            "a maxed line is refused with the typed cap category and no charge");

        // No command points left: the pipeline checks the price before the line.
        var noCp = ResRunnerWorld(9813, 100);
        noCp.State.FindFaction("seat-a").CommandPoints = 0;
        var noCpResult = ResRunnerReply(noCp, 9813, ResJson(ResearchLine.Governance));
        var noCpSeat = noCp.State.FindFaction("seat-a");
        ResRunnerCheck(noCpSeat.ResearchLevel(ResearchLine.Governance) == 0
           && noCpSeat.ResearchPoints == 100
           && noCpResult.History.Any(h => h.EndsWith("rejected: no orders left", StringComparison.Ordinal)),
            "with no command points the order is refused before any research is bought");

        // Schema violations never reach the engine at all.
        var badLine = ResRunnerWorld(9814, 100);
        var badLineResult = ResRunnerReply(badLine, 9814,
            "{\"type\":\"research\",\"args\":{\"line\":\"navy\"}}");
        var targeted = ResRunnerReply(ResRunnerWorld(9815, 100), 9815,
            "{\"type\":\"research\",\"target_id\":\"city-a\",\"args\":{\"line\":\"agri\"}}");
        var coordinated = ResRunnerReply(ResRunnerWorld(9816, 100), 9816,
            "{\"type\":\"research\",\"param_a\":1,\"param_b\":2,\"args\":{\"line\":\"agri\"}}");
        ResRunnerCheck(badLineResult.Submits == 0 && badLineResult.Fake.CallCount == 2
           && badLineResult.History.Contains("invalid model response -> unsupported research line"),
            "an unknown line is refused by the parser, not the engine");
        ResRunnerCheck(targeted.Submits == 0
           && targeted.History.Contains("invalid model response -> invalid field"),
            "a research order with a target_id never reaches the engine");
        ResRunnerCheck(coordinated.Submits == 0
           && coordinated.History.Contains("invalid model response -> invalid field"),
            "a research order with coordinates never reaches the engine");
        ResRunnerCheck(badLineResult.Engine.State.FindFaction("seat-a").ResearchLevel(ResearchLine.Agri) == 0
           && targeted.Engine.State.FindFaction("seat-a").ResearchPoints == 100
           && coordinated.Engine.State.FindFaction("seat-a").CommandPoints == 5,
            "no rejected schema violation changed any research state");
    }

    private sealed class ResReply
    {
        public FakeLlmTransport Fake;
        public int Submits;
        public IReadOnlyList<string> History;
        public GameEngine Engine;
    }

    /// <summary>One research-shaped reply followed by end_turn, through the real runner.</summary>
    private static ResReply ResRunnerReply(GameEngine engine, int seed, string reply)
    {
        var host = new LlmPlanningHost(engine, "seat-a");
        var fake = new FakeLlmTransport();
        fake.EnqueueSuccess(reply);
        fake.EnqueueSuccess(W2EndTurn());
        var runner = new LlmTurnRunner(fake, ResRunnerConfig(), null);
        runner.Begin(host, host.Token, "seat-a", seed);
        LlmDrain(runner);
        return new ResReply
        {
            Fake = fake, Submits = host.SubmitCalls,
            History = runner.RecentHistory.ToList(), Engine = engine,
        };
    }

    // ---- 3: a second seat cannot see the first seat's tech tree -------------

    private static void ResRunnerPrivacy()
    {
        var engine = ResRunnerWorld(9821, 987654);
        engine.State.FindFaction("seat-b").ResearchPoints = 100;
        var fake = new FakeLlmTransport();
        var runner = new LlmTurnRunner(fake, ResRunnerConfig(), null);

        var hostA = new LlmPlanningHost(engine, "seat-a");
        fake.EnqueueSuccess(ResJson(ResearchLine.Espionage));
        fake.EnqueueSuccess(W2EndTurn());
        runner.Begin(hostA, hostA.Token, "seat-a", 9);
        LlmDrain(runner);

        var hostB = new LlmPlanningHost(engine, "seat-b");
        fake.EnqueueSuccess(W2EndTurn());
        runner.Begin(hostB, hostB.Token, "seat-b", 9);
        LlmDrain(runner);

        string seatB = fake.Requests[fake.Requests.Count - 1].User;
        Observation theirs = engine.Observe("seat-b");
        var evt = Find(engine.Log, "research");
        ResRunnerCheck(!seatB.Contains("to level 2 for 40")
           && !seatB.Contains("987634") && !seatB.Contains("987654")
           && seatB.Contains("type=research args={\"line\":\"agri\"}"),
            "the other seat's prompt shows only its own levels, points and candidates");
        ResRunnerCheck(theirs.ResearchLevel(ResearchLine.Espionage) == 0
           && theirs.ResearchPoints != 987634,
            "the other seat's observation carries none of the researched seat's tech tree");
        ResRunnerCheck(engine.State.FindFaction("seat-a").ResearchLevel(ResearchLine.Espionage) == 1
           && evt != null && evt.Visibility == "owner_only",
            "the purchase still happened and stayed owner-only");
    }

    // ---- 4: composition with attacks, personas and deliberation -------------

    private static void ResRunnerComposition()
    {
        // (a) attacks + research
        var attackEngine = AttackWorld(9831);
        attackEngine.State.FindFaction("seat-a").ResearchPoints = 100;
        var attackHost = new LlmAttackHost(attackEngine, "seat-a");
        var attackFake = new FakeLlmTransport();
        attackFake.EnqueueSuccess(ResJson(ResearchLine.Military));
        attackFake.EnqueueSuccess(W2EndTurn());
        var attackRunner = new LlmTurnRunner(attackFake,
            ResRunnerConfig(research: true, attack: true), null);
        attackRunner.Begin(attackHost, attackHost.Token, "seat-a", 3);
        LlmDrain(attackRunner);
        ResRunnerCheck(attackFake.Requests[0].System.Contains("ATTACK AND CAPTURE")
           && attackFake.Requests[0].System.Contains("RESEARCH ORDERS")
           && attackFake.Requests[0].User.Contains("type=attack_army")
           && attackFake.Requests[0].User.Contains("type=research args={\"line\":\"military\"}")
           && attackFake.Requests[0].User.Contains("Prompt version: "
                + PromptBuilder.ResearchPromptVersion + "\n"),
            "attacks and research share one prompt, one identity and one candidate list");
        ResRunnerCheck(attackEngine.State.FindFaction("seat-a")
                .ResearchLevel(ResearchLine.Military) == 1,
            "the research order is bought while the attack interface is advertised");

        // (b) personas + research
        var personaEngine = ResRunnerWorld(9832, 100);
        var personaHost = new LlmPlanningHost(personaEngine, "seat-a");
        var personaFake = new FakeLlmTransport();
        personaFake.EnqueueSuccess(ResJson(ResearchLine.Governance));
        personaFake.EnqueueSuccess(W2EndTurn());
        var personaRunner = new LlmTurnRunner(personaFake,
            ResRunnerConfig(research: true, personas: true), null);
        personaRunner.Begin(personaHost, personaHost.Token, "seat-a", 3);
        LlmDrain(personaRunner);
        ResRunnerCheck(personaFake.Requests[0].System.Contains("FACTION PERSONA")
           && personaFake.Requests[0].System.Contains("RESEARCH ORDERS")
           && personaRunner.CurrentPersonaId == LlmPersona.WeiPersonaId
           && personaEngine.State.FindFaction("seat-a")
                .ResearchLevel(ResearchLine.Governance) == 1,
            "the persona block, the research block and a bought level coexist");

        // (c) deliberation + research, including an inquiry in the same turn
        var deliberationEngine = ResRunnerWorld(9833, 100);
        var deliberationHost = new LlmPlanningHost(deliberationEngine, "seat-a");
        var deliberationFake = new FakeLlmTransport();
        deliberationFake.EnqueueSuccess("{\"tool\":\"manual_lookup\",\"topic\":\"research\"}");
        deliberationFake.EnqueueSuccess(ResJson(ResearchLine.Agri));
        deliberationFake.EnqueueSuccess(W2EndTurn());
        var deliberationRunner = new LlmTurnRunner(deliberationFake,
            ResRunnerConfig(research: true, deliberation: true), null);
        deliberationRunner.Begin(deliberationHost, deliberationHost.Token, "seat-a", 3);
        LlmDrain(deliberationRunner);
        ResRunnerCheck(deliberationFake.Requests[0].System.Contains("OPTIONAL DELIBERATION")
           && deliberationFake.Requests[0].System.Contains("RESEARCH ORDERS")
           && deliberationFake.Requests[0].User.Contains("Prompt version: "
                + PromptBuilder.DeliberationResearchPromptVersion + "\n"),
            "deliberation plus research sends both blocks and its own version");
        ResRunnerCheck(deliberationFake.Requests[1].User.Contains("PREVIOUS INQUIRY RESULT")
           && deliberationFake.Requests[1].User.Contains("type=research")
           && deliberationEngine.State.FindFaction("seat-a")
                .ResearchLevel(ResearchLine.Agri) == 1,
            "an inquiry and a research purchase coexist in the deliberation turn");
        ResRunnerCheck(deliberationRunner.AcceptedActionsThisTurn == 1
           && deliberationRunner.DeliberationBudget != null,
            "the purchase is admitted and counted by the shared ledger");

        // (d) all four opt-ins at once
        var allEngine = AttackWorld(9834);
        allEngine.State.FindFaction("seat-a").ResearchPoints = 100;
        var allHost = new LlmAttackHost(allEngine, "seat-a");
        var allFake = new FakeLlmTransport();
        allFake.EnqueueSuccess(W2EndTurn());
        var allRunner = new LlmTurnRunner(allFake,
            ResRunnerConfig(research: true, attack: true, personas: true, deliberation: true), null);
        allRunner.Begin(allHost, allHost.Token, "seat-a", 3);
        LlmDrain(allRunner);
        string allSystem = allFake.Requests[0].System;
        ResRunnerCheck(allSystem.Contains("ATTACK AND CAPTURE")
           && allSystem.Contains("OPTIONAL DELIBERATION")
           && allSystem.Contains("FACTION PERSONA")
           && allSystem.Contains("RESEARCH ORDERS")
           && allFake.Requests[0].User.Contains("Prompt version: "
                + PromptBuilder.DeliberationResearchPromptVersion + "\n"),
            "all four opt-ins compose into one prompt and the deliberation+research identity");
    }

    // ---- 5: every deliberation/research combination is distinct -------------

    private static void ResRunnerFlagMatrix()
    {
        var versions = new Dictionary<string, string>(StringComparer.Ordinal);
        bool versionLineAgrees = true, hashAgrees = true, blocksAgree = true;

        foreach (bool deliberation in new[] { false, true })
        {
            foreach (bool research in new[] { false, true })
            {
                int seed = 9840 + versions.Count;
                var engine = ResRunnerWorld(seed, 100);
                var host = new LlmPlanningHost(engine, "seat-a");
                var fake = new FakeLlmTransport();
                fake.EnqueueSuccess(W2EndTurn());
                var recorder = new ResCaptureRecorder();
                var runner = new LlmTurnRunner(fake,
                    ResRunnerConfig(research: research, deliberation: deliberation), recorder);
                runner.Begin(host, host.Token, "seat-a", 3);
                LlmDrain(runner);

                string expectedVersion = PromptBuilder.VersionFor(true, false, (LlmPersona)null,
                    false, deliberation, research);
                string system = PromptBuilder.SystemPromptFor(true, false, null, false,
                    deliberation, research);
                string hash = PromptBuilder.SchemaHashFor(true, false, null, false, deliberation,
                    research);

                versionLineAgrees = versionLineAgrees
                    && fake.Requests[0].User.Contains("Prompt version: " + expectedVersion + "\n")
                    && fake.Requests[0].System == system;
                hashAgrees = hashAgrees && recorder.Lines.Count > 0
                    && recorder.Lines.All(l => l.Contains("\"schema_hash\":\"" + hash + "\"")
                        && l.Contains("\"prompt_version\":\"" + expectedVersion + "\""));
                blocksAgree = blocksAgree
                    && system.Contains("RESEARCH ORDERS") == research
                    && system.Contains("OPTIONAL DELIBERATION") == deliberation;
                versions[expectedVersion] = (deliberation ? "deliberation" : "plain")
                    + "+" + (research ? "research" : "no-research");
            }
        }

        ResRunnerCheck(versions.Count == 4,
            "the four flag combinations produced four distinct prompt versions");
        ResRunnerCheck(versionLineAgrees,
            "the user-prompt version line, the system prompt and the flag state agree");
        ResRunnerCheck(hashAgrees,
            "every run record carries the matching prompt version and schema hash");
        ResRunnerCheck(blocksAgree,
            "each system prompt contains exactly the blocks its flags enable");
    }
}
