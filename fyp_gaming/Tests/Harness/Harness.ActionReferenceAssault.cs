using System;
using System.Linq;
using LuanShi.Engine;

// [ACTREF-ASSAULT] The assault rule in the dispatched prompt follows the engine's own rule
// (GameState.RequireAssaultTroopRatio), never the retained BalanceConfig.AssaultTroopRatio constant.
// Real LlmTurnRunner, real engine, in-memory transport: no paid or network calls.

internal static partial class Harness
{
    private static void ActAssaultCheck(bool ok, string name) => Check(ok, "[ACTREF-ASSAULT] " + name);

    private static LlmConfig ActAssaultConfig()
    {
        LlmConfig config = AtkConfig();
        config.EnableActionReference = true;
        return config;
    }

    // army-a-1 (400) against city-b (garrison 300): legal in every way except the old 3:1 ratio.
    private static GameEngine ActAssaultWorld(int seed, bool historicalRule)
    {
        GameEngine engine = AttackWorld(seed);
        engine.State.FindArmy("army-a-1").Troops = 400;
        engine.State.RequireAssaultTroopRatio = historicalRule;
        return engine;
    }

    private static void RunActionReferenceAssault()
    {
        // ---- a normal new game: the below-3:1 assault is offered, described and accepted ----------
        GameEngine live = ActAssaultWorld(9601, historicalRule: false);
        ActAssaultCheck(!new GameEngine(1).State.RequireAssaultTroopRatio,
            "a normal new game does not enforce the old ratio");
        var liveHost = new LlmAttackHost(live, "seat-a");
        var liveFake = new FakeLlmTransport();
        liveFake.EnqueueSuccess(AtkJson("attack_city", "army-a-1", 1, 2, "Assault with fewer than three to one."));
        liveFake.EnqueueSuccess(W2EndTurn());
        var liveRunner = new LlmTurnRunner(liveFake, ActAssaultConfig(), null);
        liveRunner.Begin(liveHost, liveHost.Token, "seat-a", 3);
        LlmDrain(liveRunner);

        LlmRequest first = liveFake.Requests[0];
        ActAssaultCheck(first.User.Contains("type=attack_city target_id=army-a-1 param_a=1 param_b=2")
            && !first.User.Contains("needs at least") && !first.User.Contains("waives the troop ratio"),
            "the below-3:1 assault is offered as a candidate and its label states no ratio");
        ActAssaultCheck(first.System.Contains("ATTACK AND CAPTURE") && first.System.Contains("ACTION REFERENCE"),
            "the dispatched system prompt carries the attack rules and the action reference");
        int minimum = CountOccurrences(first.System, "has no minimum troop ratio");
        ActAssaultCheck(minimum == 2 && !first.System.Contains("times the defending")
            && !first.System.Contains("assault ratio too low"),
            "both the attack rules and the reference say no minimum ratio (" + minimum + " statements)");
        ActAssaultCheck(first.User.Contains("Prompt version: demo-slice-6-attack-2" + PromptBuilder.ActionReferenceVersionSuffix
                + PromptBuilder.AssaultTextVersionSuffix + "\n"),
            "the dispatched identity is the post-removal attack version plus +actions-1 and the revised assault wording");
        string liveVersion = "demo-slice-6-attack-2" + PromptBuilder.ActionReferenceVersionSuffix + PromptBuilder.AssaultTextVersionSuffix;
        ActAssaultCheck(PromptBuilder.HasNoAssaultRatioPromptVersion(liveVersion) && !PromptBuilder.HasLegacyAssaultRatioPromptVersion(liveVersion)
            && PromptBuilder.IsAttackPromptVersion(liveVersion + PromptBuilder.GarrisonConversionVersionSuffix)
            && PromptBuilder.HasNoAssaultRatioPromptVersion(liveVersion + PromptBuilder.GarrisonConversionVersionSuffix)
            && PromptBuilder.IsActionReferencePromptVersion(liveVersion)
            && PromptBuilder.HasNoAssaultRatioPromptVersion("demo-slice-6-attack-2" + PromptBuilder.AssaultTextVersionSuffix)
            && PromptBuilder.HasNoAssaultRatioPromptVersion("demo-slice-6-attack-2"),
            "the revised-wording suffix resolves as the post-removal rule and earlier attack-2 identities stay valid");
        ActAssaultCheck(liveHost.Commands.Count == 1 && liveHost.Commands[0].Type == ActionType.AttackCity
            && liveRunner.RecentHistory.Any(h => h.Contains("attack_city army-a-1 -> ok")),
            "the engine accepts the below-3:1 assault the model chose");
        ActAssaultCheck(Find(live.Log, "battle_siege") != null,
            "the assault actually resolved in the engine");

        // ---- explicit historical-rule mode: 3:1 wording, legacy identity, engine still rejects ------
        GameEngine old = ActAssaultWorld(9602, historicalRule: true);
        var oldHost = new LlmAttackHost(old, "seat-a");
        var oldFake = new FakeLlmTransport();
        oldFake.EnqueueSuccess(AtkJson("attack_city", "army-a-1", 1, 2, "Try the assault."));
        oldFake.EnqueueSuccess(W2EndTurn());
        var oldRunner = new LlmTurnRunner(oldFake, ActAssaultConfig(), null);
        oldRunner.Begin(oldHost, oldHost.Token, "seat-a", 3);
        LlmDrain(oldRunner);

        LlmRequest histReq = oldFake.Requests[0];
        string requirement = PromptBuilder.AssaultRequirementText(true);
        ActAssaultCheck(CountOccurrences(histReq.System, requirement) == 2
            && !histReq.System.Contains("no minimum troop ratio"),
            "historical mode: attack rules and reference both state the 3:1 requirement");
        ActAssaultCheck(!histReq.User.Contains("type=attack_city target_id=army-a-1 param_a=1 param_b=2"),
            "historical mode: a below-3:1 assault is not even offered as a candidate");
        GameEngine strong = ActAssaultWorld(9603, historicalRule: true);
        strong.State.FindArmy("army-a-1").Troops = 2000;
        var strongHost = new LlmAttackHost(strong, "seat-a");
        var strongFake = new FakeLlmTransport();
        strongFake.EnqueueSuccess(W2EndTurn());
        var strongRunner = new LlmTurnRunner(strongFake, ActAssaultConfig(), null);
        strongRunner.Begin(strongHost, strongHost.Token, "seat-a", 3);
        LlmDrain(strongRunner);
        ActAssaultCheck(strongFake.Requests[0].User.Contains("needs at least 900 troops")
            && strongFake.Requests[0].User.Contains("type=attack_city target_id=army-a-1 param_a=1 param_b=2"),
            "historical mode: a 3:1 assault is offered with the engine-computed required troops");
        ActAssaultCheck(histReq.User.Contains("Prompt version: demo-slice-6-attack-1" + PromptBuilder.ActionReferenceVersionSuffix + "\n")
            && PromptBuilder.HasLegacyAssaultRatioPromptVersion("demo-slice-6-attack-1" + PromptBuilder.ActionReferenceVersionSuffix)
            && !PromptBuilder.HasNoAssaultRatioPromptVersion("demo-slice-6-attack-1" + PromptBuilder.ActionReferenceVersionSuffix),
            "historical mode: the dispatched identity is the legacy attack version, which replay reads as the old rule");
        ActAssaultCheck(!histReq.User.Contains(PromptBuilder.AssaultTextVersionSuffix),
            "historical mode never carries the revised-wording suffix");
        ActAssaultCheck(oldHost.Commands.Count == 1 && !oldRunner.RecentHistory.Any(h => h.Contains("attack_city army-a-1 -> ok"))
            && old.State.CityAt(1, 2).OwnerSeatId == "seat-b",
            "historical mode: the engine rejects the below-3:1 assault and the city is unchanged");
        ActAssaultCheck(histReq.System != first.System
            && PromptBuilder.ActionReferenceHash(histReq.System) != PromptBuilder.ActionReferenceHash(first.System),
            "the two rules produce different system prompts and hashes");

        // ---- the helper fails closed when the live block is not present exactly once --------------
        ActAssaultCheck(!PromptBuilder.TryApplyHistoricalAssaultRule("no attack rules here", "demo-slice-6-attack-2", out _, out _)
            && !PromptBuilder.TryApplyHistoricalAssaultRule(PromptBuilder.AttackAddendum(), "demo-slice-4", out _, out _)
            && !PromptBuilder.TryApplyHistoricalAssaultRule(PromptBuilder.AttackAddendum() + PromptBuilder.AttackAddendum(),
                "demo-slice-6-attack-2", out _, out _),
            "historical-rule swap fails closed on a missing, duplicated or unmapped identity");

        // ---- identities with +actions-1 resolve through replay's detectors -----------------------
        string suffixes = "+spatial+diplomacy" + PromptBuilder.ActionReferenceVersionSuffix
                          + PromptBuilder.GarrisonConversionVersionSuffix;
        bool ids = true;
        foreach (string baseVersion in new[]
                 {
                     PromptBuilder.AttackPromptVersion, PromptBuilder.ResearchPromptVersion,
                     PromptBuilder.RichResearchPromptVersion, PromptBuilder.DeliberationResearchPromptVersion,
                 })
        {
            foreach (string tail in new[] { "", PromptBuilder.ActionReferenceVersionSuffix, suffixes })
            {
                string v = baseVersion + tail;
                ids &= PromptBuilder.HasNoAssaultRatioPromptVersion(v) && !PromptBuilder.HasLegacyAssaultRatioPromptVersion(v);
            }
        }
        foreach (string baseVersion in new[]
                 {
                     PromptBuilder.LegacyAttackPromptVersion, PromptBuilder.LegacyResearchPromptVersion,
                     PromptBuilder.LegacyRichResearchPromptVersion, PromptBuilder.LegacyDeliberationResearchPromptVersion,
                 })
        {
            foreach (string tail in new[] { "", PromptBuilder.ActionReferenceVersionSuffix, suffixes })
            {
                string v = baseVersion + tail;
                ids &= PromptBuilder.HasLegacyAssaultRatioPromptVersion(v) && !PromptBuilder.HasNoAssaultRatioPromptVersion(v);
            }
        }
        ActAssaultCheck(ids, "every legacy and post-removal base version keeps its assault-rule reading with +actions-1");
        string attackWithRef = PromptBuilder.AttackPromptVersion + suffixes;
        string researchWithRef = PromptBuilder.RichResearchPromptVersion + suffixes;
        ActAssaultCheck(PromptBuilder.IsAttackPromptVersion(attackWithRef) && PromptBuilder.IsResearchPromptVersion(researchWithRef)
            && PromptBuilder.IsDiplomacyPromptVersion(attackWithRef) && PromptBuilder.IsSpatialPromptVersion(attackWithRef)
            && PromptBuilder.IsGarrisonConversionPromptVersion(attackWithRef)
            && PromptBuilder.IsAttackPromptVersion(PromptBuilder.AttackPromptVersion + PromptBuilder.ActionReferenceVersionSuffix),
            "attack, research, diplomacy, spatial and garrison features are all detected through +actions-1");
        ActAssaultCheck(!PromptBuilder.HasNoAssaultRatioPromptVersion("demo-slice-4+actions-1")
            && !PromptBuilder.HasLegacyAssaultRatioPromptVersion("demo-slice-4+actions-1"),
            "an identity with no assault wording claims neither rule (provenance stays unknown)");
    }

    private static int CountOccurrences(string text, string value)
    {
        int n = 0, at = 0;
        while ((at = text.IndexOf(value, at, StringComparison.Ordinal)) >= 0) { n++; at += value.Length; }
        return n;
    }
}
