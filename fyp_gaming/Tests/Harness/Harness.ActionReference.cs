using System;
using System.Globalization;
using LuanShi.Engine;

// [ACTREF] Opt-in action reference (LlmConfig.EnableActionReference): rules, costs and consequences
// derived from the active balance profile and the enabled order flags. No paid or network calls; the
// runner checks use the in-memory fake transport.

internal static partial class Harness
{
    private static string ActRef(BalanceProfile p, bool attack, bool research, bool diplomacy, bool conversion)
        => PromptBuilder.ActionReferenceAddendum(p, attack, research, diplomacy, conversion);

    private static void RunActionReference()
    {
        Section("[ACTREF] action reference: profile-derived costs, flag composition, versions, dispatched prompt");

        // ---- costs come from the active profile --------------------------------------------------
        string std = ActRef(BalanceProfile.Standard, true, true, true, true);
        string easy = ActRef(BalanceProfile.FoodEasy, true, true, true, true);
        Check(std.Contains("balance profile \"standard\"") && easy.Contains("balance profile \"food-easy\""),
              "[ACTREF] the block names the active balance profile");
        Check(std.Contains("costs 100 gold and 1000 population") && std.Contains("adds 1000 troops"),
              "[ACTREF] standard recruit: 100 gold + 1000 population -> 1000 garrison troops");
        Check(easy.Contains("costs 50 gold and 1000 population") && easy.Contains("adds 1000 troops"),
              "[ACTREF] food-easy recruit: 50 gold + 1000 population -> 1000 garrison troops");
        Check(std.Contains("lighten_labor: that city's gold -100") && easy.Contains("lighten_labor: that city's gold -50"),
              "[ACTREF] lighten_labor gold follows the profile (100 vs 50)");
        Check(std.Contains("harvests 200 food") && easy.Contains("harvests 600 food"),
              "[ACTREF] base city harvest follows the profile (200 vs 600)");
        Check(std.Contains("1,000 infantry eat about 100 food") && easy.Contains("1,000 infantry eat about 25 food"),
              "[ACTREF] troop food upkeep follows the profile (100 vs 25 per 1,000 infantry)");
        Check(ActRef(null, false, false, false, false).Contains("balance profile \"standard\""),
              "[ACTREF] a missing profile means standard");
        Check(std.Contains("population is not returned") && std.Contains("tax gold (0.01 per person")
              && std.Contains("food eaten (0.02 per person)"),
              "[ACTREF] recruit consequences: population is not refunded and feeds tax, growth and food");
        int cp = BalanceConfig.CommandPointsPerSeason;
        Check(std.Contains("You receive " + cp.ToString(CultureInfo.InvariantCulture) + " command points each season"),
              "[ACTREF] command points per season come from the balance constant");

        // ---- recruit -> garrison -> convert_garrison -> field army -> march ---------------------
        Check(std.Contains("recruit adds troops to a city's garrison; convert_garrison")
              && std.Contains("field army standing on the city") && std.Contains("only a field army can march or attack")
              && std.Contains("MARCH (target_id = your own army id"),
              "[ACTREF] conversion on: the full chain is stated and convert_garrison is never called unavailable");
        string noConv = ActRef(BalanceProfile.Standard, true, true, true, false);
        Check(!noConv.Contains("convert_garrison") && noConv.Contains("no order that turns a garrison into a field army"),
              "[ACTREF] conversion off: no convert_garrison text, and the garrison limit is stated");

        // ---- every flag combination composes; an enabled order is never called unavailable -------
        bool composed = true, noFalseUnavailable = true, gated = true;
        for (int mask = 0; mask < 16; mask++)
        {
            bool a = (mask & 1) != 0, r = (mask & 2) != 0, d = (mask & 4) != 0, c = (mask & 8) != 0;
            string t = ActRef(BalanceProfile.FoodEasy, a, r, d, c);
            composed &= t.Contains("RECRUITING AND ARMIES:") && t.Contains("SEASON RESOLUTION (this profile):");
            string lower = t.ToLowerInvariant();
            noFalseUnavailable &= !lower.Contains("not available") && !lower.Contains("unavailable")
                                  && !lower.Contains("do not emit");
            gated &= t.Contains("COMBAT (") == a
                     && t.Contains("research costs") == r
                     && t.Contains("send_message costs") == d
                     && t.Contains("convert_garrison (") == c;
        }
        Check(composed, "[ACTREF] all 16 flag combinations render the domestic, recruit and season sections");
        Check(noFalseUnavailable, "[ACTREF] no combination says an order is unavailable");
        Check(gated, "[ACTREF] attack, research, diplomacy and conversion text appears exactly when its flag is on");

        // ---- assault wording follows the engine's rule, not the retained constant --------------
        string liveText = PromptBuilder.AssaultRequirementText(false);
        string oldText = PromptBuilder.AssaultRequirementText(true);
        string ratioText = BalanceConfig.AssaultTroopRatio.ToString(CultureInfo.InvariantCulture);
        Check(BalanceConfig.AssaultTroopRatio == 3 && liveText.Contains("no minimum troop ratio")
              && oldText.Contains("at least " + ratioText + " times"),
              "[ACTREF] the retained constant (" + ratioText + ") only supplies the historical multiple");
        string attackLive = PromptBuilder.AttackAddendum();
        string attackOld = PromptBuilder.AttackAddendum(true);
        Check(attackLive.Contains(liveText) && !attackLive.Contains("times the defending")
              && attackOld.Contains(oldText) && !attackOld.Contains("no minimum")
              && attackLive == PromptBuilder.AttackAddendum(false),
              "[ACTREF] the attack rules say no minimum by default and 3:1 only in explicit historical mode");
        string refLive = PromptBuilder.ActionReferenceAddendum(BalanceProfile.Standard, true, false, false, false);
        string refOld = PromptBuilder.ActionReferenceAddendum(BalanceProfile.Standard, true, false, false, false, true);
        Check(refLive.Contains(liveText) && !refLive.Contains("times the defending")
              && refOld.Contains(oldText) && !refOld.Contains("no minimum"),
              "[ACTREF] the reference states the same live or historical assault rule as the attack rules");
        Check(std.Contains(liveText) && !std.Contains("times the defending"),
              "[ACTREF] the default reference never infers the 3:1 rule from the retained constant");

        // ---- versions and hashes -----------------------------------------------------------------
        string v = PromptBuilder.DiplomacyPromptVersion + PromptBuilder.ActionReferenceVersionSuffix
                   + PromptBuilder.GarrisonConversionVersionSuffix;
        Check(PromptBuilder.IsGarrisonConversionPromptVersion(v) && PromptBuilder.IsDiplomacyPromptVersion(v)
              && PromptBuilder.IsActionReferencePromptVersion(v)
              && PromptBuilder.IsSpatialPromptVersion("demo-slice-7-spatial-1+actions-1")
              && PromptBuilder.IsSpatialPromptVersion("demo-slice-6-attack-1+spatial+diplomacy+actions-1"
                                                      + PromptBuilder.GarrisonConversionVersionSuffix)
              && !PromptBuilder.IsActionReferencePromptVersion(PromptBuilder.DiplomacyPromptVersion
                                                               + PromptBuilder.GarrisonConversionVersionSuffix),
              "[ACTREF] the suffix keeps the diplomacy, spatial and garrison feature detection intact");
        Check(PromptBuilder.ActionReferenceHash(std) != PromptBuilder.ActionReferenceHash(easy)
              && PromptBuilder.ActionReferenceHash(std).Length == 16,
              "[ACTREF] profile text changes the schema hash");

        // ---- config key and diagnostic -----------------------------------------------------------
        var cfg = new LlmConfig();
        Check(!cfg.EnableActionReference && !cfg.Redacted().Contains("action_reference"),
              "[ACTREF] default off: no diagnostic text");
        cfg.EnableActionReference = true;
        Check(cfg.Redacted().Contains("action_reference=true"),
              "[ACTREF] enabled flag appears in the diagnostic");

        RunActionReferenceDispatch(BalanceProfile.Standard);
        RunActionReferenceDispatch(BalanceProfile.FoodEasy);
        RunActionReferenceOffIsIdentical();
        RunActionReferenceCrowded();
        RunActionReferenceAssault();
    }

    private static LlmConfig ActRefConfig(bool reference)
    {
        LlmConfig config = LlmConfig.RichDeliberation();
        config.MaxPromptChars = 80000;
        config.EnableAttack = true;
        config.EnableResearch = true;
        config.EnableDiplomacy = true;
        config.EnableGarrisonConversion = true;
        config.RequireVisibleMarchRoutes = true;
        config.SpatialContext = new SpatialContextOptions
        {
            Level = SpatialDetailLevel.Rich,
            ProviderCharLimit = 40000,
        };
        config.EnableActionReference = reference;
        return config;
    }

    private static LlmRequest ActRefDispatch(BalanceProfile profile, bool reference, out string state, out LlmTurnRunner runnerOut)
    {
        GameEngine engine = ScenarioEngine(CompactScenario.CompactContact, profile);
        var fake = new FakeLlmTransport();
        fake.EnqueueSuccess("{\"type\":\"end_turn\"}", inputTokens: 100, outputTokens: 10);
        fake.EnqueueSuccess("{\"type\":\"end_turn\"}", inputTokens: 100, outputTokens: 10);
        var runner = new LlmTurnRunner(fake, ActRefConfig(reference), null, null, new MemoryLlmStrategicNoteStore());
        var session = new SpectatorSession(engine, runner, 1, 909);
        session.Start();
        SpectatorDrain(session);
        state = session.State + "/" + runner.EndReasonText;
        runnerOut = runner;
        return fake.Requests.Count > 0 ? fake.Requests[0] : null;
    }

    private static void RunActionReferenceDispatch(BalanceProfile profile)
    {
        string tag = "[ACTREF " + profile.Key + "] ";
        LlmRequest on = ActRefDispatch(profile, true, out string state, out LlmTurnRunner runner);
        LlmRequest off = ActRefDispatch(profile, false, out string offState, out LlmTurnRunner offRunner);
        Check(on != null && off != null, tag + "a request is dispatched with and without the reference (" + state + ")");
        if (on == null || off == null) return;

        string reference = PromptBuilder.ActionReferenceAddendum(profile, true, true, true, true);
        int at = on.System.IndexOf(reference, StringComparison.Ordinal);
        Check(at > 0 && on.System.StartsWith(off.System.Substring(0, Math.Min(2000, off.System.Length)), StringComparison.Ordinal),
              tag + "the dispatched system prompt carries the reference after the existing rules");
        Check(on.System.EndsWith(PromptBuilder.GarrisonConversionAddendum, StringComparison.Ordinal),
              tag + "the garrison-conversion addendum stays last");
        string versionLine = "Prompt version: ";
        int vi = on.User.IndexOf(versionLine, StringComparison.Ordinal);
        string version = vi < 0 ? "" : on.User.Substring(vi + versionLine.Length, on.User.IndexOf('\n', vi) - vi - versionLine.Length);
        Check(version.EndsWith(PromptBuilder.ActionReferenceVersionSuffix + PromptBuilder.AssaultTextVersionSuffix
                  + PromptBuilder.GarrisonConversionVersionSuffix, StringComparison.Ordinal),
              tag + "the dispatched version records +actions-1 and the live assault wording before the garrison suffix (" + version + ")");
        Check(PromptBuilder.IsDiplomacyPromptVersion(version) && PromptBuilder.IsSpatialPromptVersion(version),
              tag + "the dispatched version still reads as diplomacy and spatial");
        Check(!off.System.Contains("ACTION REFERENCE") && !off.User.Contains(PromptBuilder.ActionReferenceVersionSuffix),
              tag + "flag off dispatches no reference and no suffix");

        int limit = 40000;
        int total = on.System.Length + on.User.Length;
        Check(!runner.EndReasonText.ToLowerInvariant().Contains("prompt too large") && total <= limit,
              tag + "the full prompt fits the effective 40,000-character limit: system "
              + on.System.Length + " + user " + on.User.Length + " = " + total
              + " (reference " + reference.Length + " chars; off total "
              + (off.System.Length + off.User.Length) + ")");
        string cost = profile.RecruitGoldCost(UnitTypes.Infantry, BalanceConfig.RecruitTroopGain).ToString(CultureInfo.InvariantCulture);
        Check(on.System.Contains("costs " + cost + " gold and 1000 population"),
              tag + "the dispatched recruit cost matches the match profile (" + cost + " gold)");
        Check(on.System.Contains("convert_garrison") && on.System.Contains("recruit adds troops to a city's garrison; convert_garrison (")
              && !on.System.ToLowerInvariant().Contains("convert_garrison is not available"),
              tag + "the dispatched prompt describes the enabled recruit -> convert_garrison -> march chain");
        Check(!reference.Contains("city-") && !reference.Contains("seat-") && !reference.Contains("army-"),
              tag + "the reference adds no seat, city or army identifiers");
        Check(on.User.Contains("Match balance profile: ") == !profile.IsStandard,
              tag + "the existing profile line in the user prompt is unchanged");
    }

    private static void RunActionReferenceOffIsIdentical()
    {
        LlmRequest a = ActRefDispatch(BalanceProfile.FoodEasy, false, out _, out _);
        LlmRequest b = ActRefDispatch(BalanceProfile.FoodEasy, false, out _, out _);
        bool same = a != null && b != null && a.System == b.System && a.User == b.User;
        Check(same, "[ACTREF] flag off is deterministic and carries no action-reference text");
    }

    private static void RunActionReferenceCrowded()
    {
        // Worst-case context pressure: the crowded overflow world at the real 40,000-character limit
        // with every flag and the reference on. The reference must be paid for by trimming optional
        // context, never by an oversize or failed dispatch.
        GameEngine engine = BuildCrowdedRichOverflowWorld();
        engine.State.FindCity("city-shu").Garrison = 1000;
        engine.State.FindCity("city-shu").GarrisonType = UnitTypes.Infantry;
        LlmConfig config = ActRefConfig(true);
        var fake = new FakeLlmTransport();
        fake.EnqueueSuccess("{\"type\":\"end_turn\"}", inputTokens: 100, outputTokens: 10);
        fake.EnqueueSuccess("{\"type\":\"end_turn\"}", inputTokens: 100, outputTokens: 10);
        var runner = new LlmTurnRunner(fake, config, null, null, new MemoryLlmStrategicNoteStore());
        var session = new SpectatorSession(engine, runner, 1, 909);
        session.Start();
        SpectatorDrain(session);
        bool dispatched = fake.Requests.Count > 0;
        int total = dispatched ? fake.Requests[0].System.Length + fake.Requests[0].User.Length : -1;
        Check(dispatched && total <= 40000
              && fake.Requests[0].System.Contains("ACTION REFERENCE")
              && !runner.EndReasonText.ToLowerInvariant().Contains("prompt too large"),
              "[ACTREF] crowded worst-case world still dispatches within 40,000 characters (" + total + ")");
    }
}
