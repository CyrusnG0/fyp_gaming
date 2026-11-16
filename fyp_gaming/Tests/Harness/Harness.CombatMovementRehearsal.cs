using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using LuanShi.Engine;

// Offline combat & movement rehearsal + spatial context inspection.
// Fake provider only: no network, no paid call. Scripted replies are plumbing evidence only.
internal static partial class Harness
{
    private static void RunCombatMovementRehearsal()
    {
        Section("[CMR-1] spatial context inspection (CompactContact, Standard 12-cap vs Rich)");
        InspectCompactContactOpeningSpatial();
        InspectCompactContactNearEnemySpatial();

        Section("[CMR-2] offline fake-provider combat & movement rehearsal (fresh state per turn)");
        RehearseVisibleRouteMarch();
        RehearseFieldAttack();
        RehearseCityAssaultCapture();
        RehearseFoggedMarchRejection();
        RehearseUnderStrengthAssaultRejection();
    }

    private static void CmrCheck(bool ok, string name) => Check(ok, "[CMR] " + name);

    private static LlmConfig CmrConfig(SpatialDetailLevel level, bool attack = true)
        => new LlmConfig
        {
            MaxRequestsPerTurn = 6,
            MaxActionsPerTurn = 4,
            MaxInvalidPerTurn = 2,
            MaxTotalRequests = 32,
            EnableAttack = attack,
            RequireVisibleMarchRoutes = true,
            SpatialContext = new SpatialContextOptions { Level = level },
        };

    private static void InspectCompactContactOpeningSpatial()
    {
        GameEngine engine = ScenarioEngine(CompactScenario.CompactContact, BalanceProfile.Standard, 31001);
        // Season 1 starts in Spring. Inspect Wei (seat-0) and Shu (seat-1) with Standard (12-cap) vs Rich.
        var stdOpts = new SpatialContextOptions { Level = SpatialDetailLevel.Standard };
        var richOpts = new SpatialContextOptions { Level = SpatialDetailLevel.Rich };

        MovementView weiStdMove = MovementPlanner.BuildDiversified(
            engine, "seat-0", MovementPlanner.DefaultMaxOptionsPerArmy, null, stdOpts);
        MovementView weiRichMove = MovementPlanner.BuildDiversified(
            engine, "seat-0", MovementPlanner.DefaultMaxOptionsPerArmy, null, richOpts);

        string weiStdPrompt = PromptBuilder.UserPromptWithSpatial(
            engine.Observe("seat-0"),
            ActionCatalogue.Build(engine.Observe("seat-0"), AttackPlanner.Build(engine, "seat-0"), false),
            Array.Empty<string>(), 6, 4, weiStdMove, weiStdMove.SpatialContext, stdOpts,
            out SpatialTruncationReport weiStdTrunc, PromptBuilder.MaxPromptChars,
            null, null, false, null, true, AttackPlanner.Build(engine, "seat-0"));

        string weiRichPrompt = PromptBuilder.UserPromptWithSpatial(
            engine.Observe("seat-0"),
            ActionCatalogue.Build(engine.Observe("seat-0"), AttackPlanner.Build(engine, "seat-0"), false),
            Array.Empty<string>(), 6, 4, weiRichMove, weiRichMove.SpatialContext, richOpts,
            out SpatialTruncationReport weiRichTrunc, PromptBuilder.MaxPromptChars,
            null, null, false, null, true, AttackPlanner.Build(engine, "seat-0"));

        ArmyMovePlan weiVanStd = weiStdMove.Armies.First(a => a.ArmyId == "army-wei-1");
        ArmyMovePlan weiVanRich = weiRichMove.Armies.First(a => a.ArmyId == "army-wei-1");
        ArmyMovePlan weiMainStd = weiStdMove.Armies.First(a => a.ArmyId == "army-wei-2");

        Console.WriteLine("  [CMR-1] Opening CompactContact (Season 1 Spring, seat-0 Wei):");
        Console.WriteLine("    spatial frontier: visible=" + weiStdMove.SpatialContext.Frontier.VisibleCellCount
            + "/" + weiStdMove.SpatialContext.Frontier.TotalCellCount
            + " unseen=" + weiStdMove.SpatialContext.Frontier.CurrentlyUnseenCellCount
            + " visible_others=" + weiStdMove.SpatialContext.VisibleOthers.Count
            + " objectives=" + weiStdMove.SpatialContext.ObjectiveBearings.Count);
        Console.WriteLine("    army-wei-1 (8,8) budget=" + weiVanStd.Budget
            + " total_reachable=" + weiVanStd.TotalReachable
            + " std_advertised=" + weiVanStd.Options.Count
            + " std_omitted=" + (weiVanStd.TotalReachable - weiVanStd.Options.Count)
            + " rich_advertised=" + weiVanRich.Options.Count);
        Console.WriteLine("    army-wei-2 (6,9) budget=" + weiMainStd.Budget
            + " total_reachable=" + weiMainStd.TotalReachable
            + " std_advertised=" + weiMainStd.Options.Count
            + " std_omitted=" + (weiMainStd.TotalReachable - weiMainStd.Options.Count));
        Console.WriteLine("    std_prompt_len=" + weiStdPrompt.Length
            + " rich_prompt_len=" + weiRichPrompt.Length
            + " rich_truncated=" + (weiRichTrunc != null && weiRichTrunc.Truncated));

        // Print the exact advertised options vs top omitted options for army-wei-1.
        Console.WriteLine("    army-wei-1 Standard advertised options:");
        foreach (MoveOption opt in weiVanStd.Options)
        {
            Console.WriteLine("      (" + opt.X + "," + opt.Z + ") cost=" + opt.PathCost
                + " dir=" + opt.Direction + " dist=" + opt.GridDistance
                + (opt.ExploresFrontier ? " [frontier]" : "")
                + (string.IsNullOrEmpty(opt.ObjectiveHint) ? "" : " goal=" + opt.ObjectiveHint));
        }

        var advertisedSet = new HashSet<string>(
            weiVanStd.Options.Select(o => o.X + "," + o.Z), StringComparer.Ordinal);
        var omitted = weiVanRich.Options.Where(o => !advertisedSet.Contains(o.X + "," + o.Z)).ToList();
        Console.WriteLine("    army-wei-1 omitted by 12-cap (" + omitted.Count + " total); first 8 omitted:");
        foreach (MoveOption opt in omitted.Take(8))
        {
            Console.WriteLine("      omitted (" + opt.X + "," + opt.Z + ") cost=" + opt.PathCost
                + " dir=" + opt.Direction + " dist=" + opt.GridDistance
                + (opt.ExploresFrontier ? " [frontier]" : ""));
        }

        CmrCheck(weiStdMove.SpatialContext.OwnEntities.Count == 4,
            "opening Standard sees all 4 own entities (2 cities + 2 armies)");
        CmrCheck(weiStdMove.SpatialContext.VisibleOthers.Count == 0,
            "opening Standard sees 0 visible_others (neutral city at 13,8 and Shu at 18,8 are outside sight)");
        CmrCheck(weiVanStd.Options.Count == 12 && weiVanStd.TotalReachable > 12,
            "opening Standard caps army-wei-1 at 12 options while omitting " + (weiVanStd.TotalReachable - 12));
        CmrCheck(weiVanStd.Options.Any(o => o.Direction == "E" && o.ExploresFrontier),
            "opening Standard advertises an eastbound [frontier] route toward map_center");
        CmrCheck(weiRichMove.SpatialContext.Frontier.VisibleRowRanges.Count > 0 && weiRichTrunc != null && !weiRichTrunc.Truncated,
            "opening Rich fits all " + weiVanRich.Options.Count + " visible legal moves without truncation");
    }

    private static void InspectCompactContactNearEnemySpatial()
    {
        // Fresh CompactContact state advanced to contact near Wancheng (13,8):
        // Wei Vanguard at (11,8), Shu Vanguard at (13,7) (within FogArmyRange=2), neutral Wancheng at (13,8), Watchtower at (12,8).
        GameEngine engine = ScenarioEngine(CompactScenario.CompactContact, BalanceProfile.Standard, 31002);
        engine.State.FindArmy("army-wei-1").X = 11;
        engine.State.FindArmy("army-wei-1").Z = 8;
        engine.State.FindArmy("army-shu-1").X = 13;
        engine.State.FindArmy("army-shu-1").Z = 7;

        var stdOpts = new SpatialContextOptions { Level = SpatialDetailLevel.Standard };
        MovementView weiMove = MovementPlanner.BuildDiversified(
            engine, "seat-0", MovementPlanner.DefaultMaxOptionsPerArmy, null, stdOpts);
        AttackView weiAtk = AttackPlanner.Build(engine, "seat-0");

        string weiPrompt = PromptBuilder.UserPromptWithSpatial(
            engine.Observe("seat-0"),
            ActionCatalogue.Build(engine.Observe("seat-0"), weiAtk, false),
            Array.Empty<string>(), 6, 4, weiMove, weiMove.SpatialContext, stdOpts,
            out _, PromptBuilder.MaxPromptChars, null, null, false, null, true, weiAtk);

        ArmyMovePlan vanPlan = weiMove.Armies.First(a => a.ArmyId == "army-wei-1");
        Console.WriteLine("  [CMR-1] Near-contact CompactContact (army-wei-1 at 11,8; army-shu-1 at 13,7):");
        Console.WriteLine("    visible_others=" + weiMove.SpatialContext.VisibleOthers.Count
            + " objective_bearings=" + weiMove.SpatialContext.ObjectiveBearings.Count);
        foreach (var v in weiMove.SpatialContext.VisibleOthers)
        {
            Console.WriteLine("      visible: " + v.Kind + " " + v.Id + " owner=" + v.OwnerSeatId
                + " allegiance=" + v.Allegiance + " pos=" + v.X + "," + v.Z
                + " strength=" + v.Strength + " from_own=" + v.RefId + ":" + v.RefDistance + "@" + v.RefDirection);
        }
        foreach (var b in weiMove.SpatialContext.ObjectiveBearings)
        {
            if (b.ArmyId != "army-wei-1") continue;
            Console.WriteLine("      bearing: " + b.ArmyId + " -> " + b.TargetKind + " " + b.TargetId
                + " at " + b.TargetX + "," + b.TargetZ + " dist=" + b.GridDistance + " dir=" + b.Direction);
        }
        Console.WriteLine("    army-wei-1 advertised (" + vanPlan.Options.Count + "/" + vanPlan.TotalReachable + "):");
        foreach (MoveOption opt in vanPlan.Options)
        {
            Console.WriteLine("      (" + opt.X + "," + opt.Z + ") kind=" + opt.Kind + " cost=" + opt.PathCost
                + " dir=" + opt.Direction + " dist=" + opt.GridDistance
                + (string.IsNullOrEmpty(opt.ObjectiveHint) ? "" : " goal=" + opt.ObjectiveHint));
        }

        CmrCheck(weiMove.SpatialContext.VisibleOthers.Any(v => v.Id == "army-shu-1" && v.Allegiance == "foreign"),
            "near-contact spatial context shows visible enemy army-shu-1 with bearing");
        CmrCheck(weiMove.SpatialContext.VisibleOthers.Any(v => v.Id == "city-neutral" && v.Allegiance == "neutral"),
            "near-contact spatial context shows visible neutral city-neutral");
        CmrCheck(weiMove.SpatialContext.VisibleOthers.Any(v => v.Id == "tower-mid" && v.Allegiance == "neutral"),
            "near-contact spatial context shows visible neutral tower-mid");
        CmrCheck(vanPlan.Options.Any(o => o.Kind == "capture_neutral_city" && o.X == 13 && o.Z == 8),
            "near-contact advertised options include capturing city-neutral at (13,8)");
        CmrCheck(vanPlan.Options.Any(o => o.Kind == "capture_tower" && o.X == 12 && o.Z == 8),
            "near-contact advertised options include capturing tower-mid at (12,8)");
        CmrCheck(vanPlan.Options.Any(o => o.ObjectiveHint != null && o.ObjectiveHint.Contains("army-shu-1")),
            "near-contact advertised options include a route marked toward visible enemy army-shu-1");
        CmrCheck(weiPrompt.Contains("visible_others (3):") && weiPrompt.Contains("objective_bearings ("),
            "dispatched prompt contains both visible_others and objective_bearings");
    }

    private static void RehearseVisibleRouteMarch()
    {
        GameEngine engine = ScenarioEngine(CompactScenario.CompactContact, BalanceProfile.Standard, 31011);
        var fake = new FakeLlmTransport();
        var config = CmrConfig(SpatialDetailLevel.Standard, attack: true);
        var runner = new LlmTurnRunner(fake, config, null);
        var session = new SpectatorSession(engine, runner, 1, 31011);

        // Inspect seat-0's legal visible-route eastbound option dynamically from the fresh turn state.
        MovementView seat0Move = MovementPlanner.BuildDiversified(
            engine, "seat-0", MovementPlanner.DefaultMaxOptionsPerArmy, null, config.SpatialContext);
        ArmyMovePlan van = seat0Move.Armies.First(a => a.ArmyId == "army-wei-1");
        MoveOption east = van.Options.First(o => o.Direction == "E");
        int beforeX = van.X, beforeZ = van.Z;

        string marchReply = "{\"type\":\"march\",\"target_id\":\"army-wei-1\",\"param_a\":"
            + east.X + ",\"param_b\":" + east.Z + ",\"summary\":\"REHEARSAL visible march\"}";
        string endReply = "{\"type\":\"end_turn\",\"summary\":\"REHEARSAL end\"}";
        fake.EnqueueSuccess(marchReply);
        fake.EnqueueSuccess(endReply);
        fake.EnqueueSuccess(endReply); // seat-1

        session.Start();
        for (int i = 0; i < 64 && session.State == SpectatorState.Running; i++) session.Tick();

        ArmyState after = engine.State.FindArmy("army-wei-1");
        CmrCheck(session.State == SpectatorState.Completed && fake.CallCount == 3,
            "rehearsal 1 (visible march): spectator turn completes cleanly (calls=" + fake.CallCount + ")");
        CmrCheck(beforeX == 8 && beforeZ == 8 && after.X == east.X && after.Z == east.Z,
            "rehearsal 1: army-wei-1 moved from (" + beforeX + "," + beforeZ + ") to (" + after.X + "," + after.Z + ")");
        CmrCheck(fake.Requests[0].User.Contains("SPATIAL CONTEXT")
                 && fake.Requests[1].User.Contains("march army-wei-1 -> ok"),
            "rehearsal 1: prompt carried SPATIAL CONTEXT and next prompt recorded march -> ok");
    }

    private static void RehearseFieldAttack()
    {
        // Fresh state with Wei Vanguard (11,8) adjacent to visible Shu Vanguard (12,8).
        GameEngine engine = ScenarioEngine(CompactScenario.CompactContact, BalanceProfile.Standard, 31012);
        ArmyState weiVan = engine.State.FindArmy("army-wei-1");
        ArmyState shuVan = engine.State.FindArmy("army-shu-1");
        weiVan.X = 11; weiVan.Z = 8;
        shuVan.X = 12; shuVan.Z = 8;

        int weiTroopsBefore = weiVan.Troops;
        int shuTroopsBefore = shuVan.Troops;

        var fake = new FakeLlmTransport();
        var config = CmrConfig(SpatialDetailLevel.Standard, attack: true);
        var runner = new LlmTurnRunner(fake, config, null);
        var session = new SpectatorSession(engine, runner, 1, 31012);

        string atkReply = "{\"type\":\"attack_army\",\"target_id\":\"army-wei-1\",\"param_a\":12,\"param_b\":8,\"summary\":\"REHEARSAL field attack\"}";
        string endReply = "{\"type\":\"end_turn\",\"summary\":\"REHEARSAL end\"}";
        fake.EnqueueSuccess(atkReply);
        fake.EnqueueSuccess(endReply);
        fake.EnqueueSuccess(endReply); // seat-1

        session.Start();
        for (int i = 0; i < 64 && session.State == SpectatorState.Running; i++) session.Tick();

        var fieldEvent = Find(engine.Log, "battle_field");
        CmrCheck(session.State == SpectatorState.Completed && fieldEvent != null,
            "rehearsal 2 (field attack): battle_field resolved via SpectatorSession");
        CmrCheck(fake.Requests[0].User.Contains("type=attack_army target_id=army-wei-1 param_a=12 param_b=8"),
            "rehearsal 2: first prompt advertised attack_army at (12,8)");
        CmrCheck(weiVan.Troops < weiTroopsBefore && shuVan.Troops < shuTroopsBefore,
            "rehearsal 2: troops changed after field battle (wei " + weiTroopsBefore + "->" + weiVan.Troops
            + ", shu " + shuTroopsBefore + "->" + shuVan.Troops + ")");
    }

    private static void RehearseCityAssaultCapture()
    {
        // Fresh state where army-wei-2 (2000 troops) is adjacent to Shu's Chengdu (20,8, garrison 500 -> needs 1500).
        GameEngine engine = ScenarioEngine(CompactScenario.CompactContact, BalanceProfile.Standard, 31013);
        ArmyState weiMain = engine.State.FindArmy("army-wei-2");
        weiMain.X = 19; weiMain.Z = 8;
        CityState chengdu = engine.State.FindCity("city-shu");
        string ownerBefore = chengdu.OwnerSeatId;
        int garrisonBefore = chengdu.Garrison;
        int troopsBefore = weiMain.Troops;

        var fake = new FakeLlmTransport();
        var config = CmrConfig(SpatialDetailLevel.Standard, attack: true);
        var runner = new LlmTurnRunner(fake, config, null);
        var session = new SpectatorSession(engine, runner, 1, 31013);

        string assaultReply = "{\"type\":\"attack_city\",\"target_id\":\"army-wei-2\",\"param_a\":20,\"param_b\":8,\"summary\":\"REHEARSAL city assault\"}";
        string endReply = "{\"type\":\"end_turn\",\"summary\":\"REHEARSAL end\"}";
        fake.EnqueueSuccess(assaultReply);
        fake.EnqueueSuccess(endReply);
        fake.EnqueueSuccess(endReply); // seat-1

        session.Start();
        for (int i = 0; i < 64 && session.State == SpectatorState.Running; i++) session.Tick();

        var siegeEvent = Find(engine.Log, "battle_siege");
        var captureEvent = Find(engine.Log, "city_captured");
        CmrCheck(session.State == SpectatorState.Completed && siegeEvent != null && captureEvent != null,
            "rehearsal 3 (city assault): battle_siege and city_captured recorded");
        CmrCheck(ownerBefore == "seat-1" && chengdu.OwnerSeatId == "seat-0",
            "rehearsal 3: Chengdu ownership changed from " + ownerBefore + " to " + chengdu.OwnerSeatId);
        CmrCheck(weiMain.Troops <= troopsBefore && chengdu.Garrison != garrisonBefore,
            "rehearsal 3: troops/garrison updated (army " + troopsBefore + "->" + weiMain.Troops
            + ", garrison " + garrisonBefore + "->" + chengdu.Garrison + ")");
    }

    private static void RehearseFoggedMarchRejection()
    {
        // Natural opening CompactContact (no staged positions): Wei (seat-0) has armies at (8,8) and (6,9)
        // and cities at (6,8) and (5,10). Destination (20,8) (Shu's Chengdu) is truly unseen under fog of war.
        GameEngine engine = ScenarioEngine(CompactScenario.CompactContact, BalanceProfile.Standard, 31014);
        Observation obsBefore = engine.Observe("seat-0");
        var richOpts = new SpatialContextOptions { Level = SpatialDetailLevel.Rich };
        MovementView moveBefore = MovementPlanner.BuildDiversified(
            engine, "seat-0", MovementPlanner.DefaultMaxOptionsPerArmy, null, richOpts);

        CmrCheck(!engine.InSight("seat-0", 20, 8),
            "rehearsal 4a precondition: engine.InSight(seat-0, 20, 8) is false");
        CmrCheck(!obsBefore.Cities.Any(c => c.X == 20 && c.Z == 8)
                 && !obsBefore.Armies.Any(a => a.X == 20 && a.Z == 8),
            "rehearsal 4a precondition: seat-0 observation lists no city or army at (20,8)");
        CmrCheck(moveBefore.SpatialContext.Frontier.VisibleMaxX < 20
                 && !moveBefore.Armies.Any(a => a.Options.Any(o => o.X == 20 && o.Z == 8)),
            "rehearsal 4a precondition: (20,8) is beyond seat-0 visible frontier (VisibleMaxX="
            + moveBefore.SpatialContext.Frontier.VisibleMaxX + ") and absent from movement options");

        var fake = new FakeLlmTransport();
        var config = CmrConfig(SpatialDetailLevel.Standard, attack: true);
        config.MaxInvalidPerTurn = 4;
        var runner = new LlmTurnRunner(fake, config, null);
        var session = new SpectatorSession(engine, runner, 1, 31014);

        string foggedMarch = "{\"type\":\"march\",\"target_id\":\"army-wei-1\",\"param_a\":20,\"param_b\":8,\"summary\":\"REHEARSAL fogged march\"}";
        string endReply = "{\"type\":\"end_turn\",\"summary\":\"REHEARSAL end\"}";
        fake.EnqueueSuccess(foggedMarch);
        fake.EnqueueSuccess(endReply);
        fake.EnqueueSuccess(endReply); // seat-1

        session.Start();
        for (int i = 0; i < 64 && session.State == SpectatorState.Running; i++) session.Tick();

        Observation obsAfter = engine.Observe("seat-0");
        string feedbackPrompt = fake.Requests[1].User;
        CmrCheck(session.State == SpectatorState.Completed && session.TotalRejected == 1 && session.TotalAccepted == 0,
            "rehearsal 4a (fogged march): rejected safely and turn completes (rejected=" + session.TotalRejected + ")");
        CmrCheck(engine.State.FindArmy("army-wei-1").X == 8 && engine.State.FindArmy("army-wei-1").Z == 8,
            "rehearsal 4a: army-wei-1 remains at (8,8)");
        CmrCheck(feedbackPrompt.Contains(FeedbackPolicy.DestinationNotReachable)
                 && !feedbackPrompt.Contains("city-shu")
                 && !feedbackPrompt.Contains("Chengdu")
                 && !feedbackPrompt.Contains("army-shu-"),
            "rehearsal 4a: safe feedback returned without leaking hidden Shu city/army at (20,8)");
        CmrCheck(!obsAfter.Cities.Any(c => c.X == 20 && c.Z == 8),
            "rehearsal 4a: post-rejection seat-0 observation still does not see (20,8)");
    }

    private static void RehearseUnderStrengthAssaultRejection()
    {
        // Staged state: army-wei-2 at (19,8) with 400 troops (< 3 * 500 = 1500 needed for Chengdu at (20,8)).
        GameEngine engine = ScenarioEngine(CompactScenario.CompactContact, BalanceProfile.Standard, 31015);
        ArmyState weiMain = engine.State.FindArmy("army-wei-2");
        weiMain.X = 19; weiMain.Z = 8;
        weiMain.Troops = 400;
        CityState chengdu = engine.State.FindCity("city-shu");

        var fake = new FakeLlmTransport();
        var config = CmrConfig(SpatialDetailLevel.Standard, attack: true);
        config.MaxInvalidPerTurn = 4;
        var runner = new LlmTurnRunner(fake, config, null);
        var session = new SpectatorSession(engine, runner, 1, 31015);

        string weakAssault = "{\"type\":\"attack_city\",\"target_id\":\"army-wei-2\",\"param_a\":20,\"param_b\":8,\"summary\":\"REHEARSAL understrength assault\"}";
        string endReply = "{\"type\":\"end_turn\",\"summary\":\"REHEARSAL end\"}";
        fake.EnqueueSuccess(weakAssault);
        fake.EnqueueSuccess(endReply);
        fake.EnqueueSuccess(endReply); // seat-1

        session.Start();
        for (int i = 0; i < 64 && session.State == SpectatorState.Running; i++) session.Tick();

        CmrCheck(session.State == SpectatorState.Completed && session.TotalRejected == 1 && session.TotalAccepted == 0,
            "rehearsal 4b (under-strength assault): rejected safely and turn completes (rejected=" + session.TotalRejected + ")");
        CmrCheck(chengdu.OwnerSeatId == "seat-1" && weiMain.Troops == 400,
            "rehearsal 4b: canonical state unchanged (Chengdu owner=seat-1, army-wei-2 troops=400)");
        CmrCheck(fake.Requests[1].User.Contains("attack_city army-wei-2 -> rejected: " + FeedbackPolicy.AssaultRatioTooLow),
            "rehearsal 4b: useful AssaultRatioTooLow feedback reached subsequent prompt");
    }
}
