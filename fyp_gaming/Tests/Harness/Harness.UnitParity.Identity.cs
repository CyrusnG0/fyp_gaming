using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using LuanShi.Engine;

// [UNITS] (continued) malformed orders, feature-off compatibility, prompt identity, replay and worst-case size.

internal static partial class Harness
{
    // ---- malformed orders and feature-off compatibility -------------------------------------------------
    private static void UnitParityMalformedAndFeatureOff()
    {
        foreach (string bad in new[] { "dragon", "Archer", "", "cavalry,siege" })
        {
            GameEngine engine = UtEngine(BalanceProfile.Standard);
            UtRunResult run = UtRun(engine, UtConfig(true), UtRecruit("city-wei-2", bad));
            UtCheck(UtEvents(engine, "recruit").Count == 0 && run.Runner.InvalidThisTurn >= 0
                    && engine.State.FindCity("city-wei-2").Garrison == 0
                    && UtEvents(engine, "action_rejected").Count == 0,
                "unit_type '" + bad + "' never reaches the engine and changes nothing");
            UtCheck(run.Fake.Requests.Count >= 2 && run.Fake.Requests[1].User.Contains("unsupported unit type"),
                "unit_type '" + bad + "': the model is told the reply was an unsupported unit type");
        }
        // Omitted unit_type keeps meaning infantry, exactly as before.
        GameEngine omitted = UtEngine(BalanceProfile.Standard);
        UtRun(omitted, UtConfig(true), UtRecruit("city-wei-2", null));
        UtCheck(UtEvents(omitted, "recruit").Count == 1 && UtEvents(omitted, "recruit")[0].Payload["unit_type"] == "infantry",
            "an order without unit_type recruits infantry");

        // Feature off (the default): archer is refused by the parser; infantry is unchanged.
        foreach (string type in new[] { "archer", "cavalry", "siege" })
        {
            GameEngine engine = UtEngine(BalanceProfile.Standard);
            UtRunResult run = UtRun(engine, UtConfig(false), UtRecruit("city-wei-2", type));
            UtCheck(UtEvents(engine, "recruit").Count == 0 && engine.State.FindCity("city-wei-2").Garrison == 0
                    && run.Fake.Requests.Count >= 2 && run.Fake.Requests[1].User.Contains("unsupported unit type"),
                "flag off: " + type + " is still refused as an unsupported unit type");
        }
        GameEngine offInfantry = UtEngine(BalanceProfile.Standard);
        UtRun(offInfantry, UtConfig(false), UtRecruit("city-wei-2", "infantry"));
        UtCheck(UtEvents(offInfantry, "recruit").Count == 1, "flag off: infantry recruitment is unchanged");

        // A non-planning host is not offered the feature even with the flag on.
        var fake = new FakeLlmTransport();
        fake.EnqueueSuccess(UtRecruit("city-a", "archer"), 0, 5, 1);
        fake.EnqueueSuccess("{\"type\":\"end_turn\"}", 0, 5, 1);
        var engineA = SpectatorEngine();
        var runner = SpectatorRunner(fake, new LlmConfig { EnableUnitTypes = true });
        var host = new LlmHarnessHost(engineA, "seat-a");
        runner.Begin(host, host.Token, "seat-a", 7);
        LlmDrain(runner);
        UtCheck(!fake.Requests[0].System.Contains("UNIT TYPES") && runner.InvalidThisTurn == 1,
            "a host without planning support gets no unit-type text and still refuses non-infantry");
    }

    // ---- what the model is told when the engine refuses a typed recruit -------------------------------
    private static void UnitParityFeedback()
    {
        string researchWord = FeedbackPolicy.UnitResearchLocked, garrisonWord = FeedbackPolicy.GarrisonTypeDiffers;
        GameEngine gated = UtEngine(BalanceProfile.Standard, 0);
        UtRunResult g = UtRun(gated, UtConfig(true), UtRecruit("city-wei-2", "cavalry"));
        UtCheck(g.Fake.Requests.Count >= 2 && g.Fake.Requests[1].User.Contains(researchWord),
            "flag on: a research-gated recruit tells the model its research is the reason");
        GameEngine conflict = UtEngine(BalanceProfile.Standard, 3, 600, "archer");
        UtRunResult c = UtRun(conflict, UtConfig(true), UtRecruit("city-wei-2", "infantry"));
        UtCheck(c.Fake.Requests.Count >= 2 && c.Fake.Requests[1].User.Contains(garrisonWord),
            "flag on: a garrison-type conflict tells the model the garrison holds a different type");
        GameEngine poor = UtEngine(BalanceProfile.Standard, 3, 0, "infantry", 10);
        UtRunResult p = UtRun(poor, UtConfig(true), UtRecruit("city-wei-2", "siege"));
        UtCheck(p.Fake.Requests.Count >= 2 && p.Fake.Requests[1].User.Contains(FeedbackPolicy.NotEnoughGold)
                && !p.Fake.Requests[1].User.Contains(researchWord),
            "flag on: an unaffordable recruit still reports the existing gold category");

        // Off: the same infantry conflict keeps today's generic answer; nothing new is shown.
        GameEngine off = UtEngine(BalanceProfile.Standard, 3, 600, "archer");
        UtRunResult o = UtRun(off, UtConfig(false), UtRecruit("city-wei-2", "infantry"));
        UtCheck(o.Fake.Requests.Count >= 2 && o.Fake.Requests[1].User.Contains(FeedbackPolicy.Generic)
                && !o.Fake.Requests[1].User.Contains(garrisonWord) && !o.Fake.Requests[1].User.Contains(researchWord),
            "flag off: the same refusal keeps the generic feedback");
        UtCheck(FeedbackPolicy.ForModel("garrison is archer; recruit the same type or empty the garrison first",
                    new ActionCommand { Type = ActionType.Recruit }, null) == FeedbackPolicy.Generic
                && FeedbackPolicy.ForModel("garrison is archer; recruit the same type or empty the garrison first",
                    new ActionCommand { Type = ActionType.LevyTax }, null, false, true) == FeedbackPolicy.Generic
                && FeedbackPolicy.ForModel("unknown city 'x'", new ActionCommand { Type = ActionType.Recruit }, null, false, true)
                    == FeedbackPolicy.InvalidTarget,
            "the new categories apply only to a recruit with the flag on; target existence answers are unchanged");
        UtCheck(FeedbackPolicy.IsSafeCategory(researchWord) && FeedbackPolicy.IsSafeCategory(garrisonWord)
                && !researchWord.Contains("level") && !researchWord.Any(char.IsDigit),
            "the new categories are on the allowlist and carry no number or opponent fact");
    }

    // ---- prompt text, version, hash and candidate list as dispatched -----------------------------------
    private static LlmRequest UtFirstRequest(LlmConfig config, BalanceProfile profile)
    {
        GameEngine engine = UtEngine(profile, 1, 200, "infantry", 5000);
        UtRunResult run = UtRun(engine, config);
        return run.Fake.Requests.Count > 0 ? run.Fake.Requests[0] : null;
    }

    private static string UtVersion(LlmRequest request)
        => UtValue(request.User, "Prompt version: ", "\n");

    private static void UnitParityPromptIdentity()
    {
        foreach (BalanceProfile profile in new[] { BalanceProfile.Standard, BalanceProfile.FoodEasy })
        {
            string tag = profile.Key + ": ";
            LlmRequest off = UtFirstRequest(UtConfig(false), profile);
            LlmRequest off2 = UtFirstRequest(UtConfig(false), profile);
            LlmRequest on = UtFirstRequest(UtConfig(true), profile);
            UtCheck(off != null && on != null, tag + "requests are dispatched with the flag off and on");
            if (off == null || on == null) continue;

            UtCheck(off.System == off2.System && off.User == off2.User, tag + "flag off is deterministic");
            UtCheck(!off.System.Contains("UNIT TYPES") && !off.User.Contains(PromptBuilder.UnitTypesVersionSuffix)
                    && off.System.Contains("recruit (args.unit_type must be \"infantry\")")
                    && off.User.Split('\n').Count(l => l.StartsWith("- type=recruit")) == 2,
                tag + "flag off: no unit-type text, infantry-only contract, one recruit candidate per city");
            string offBody = off.System.Substring(0, off.System.Length - PromptBuilder.GarrisonConversionAddendum.Length)
                .Replace(UtOrderOld, UtOrderNew).Replace(UtArgsOld, UtArgsNew);
            UtCheck(off.System.EndsWith(PromptBuilder.GarrisonConversionAddendum, StringComparison.Ordinal)
                    && on.System.StartsWith(offBody, StringComparison.Ordinal)
                    && on.System.Substring(offBody.Length).StartsWith("\n\nUNIT TYPES", StringComparison.Ordinal)
                    && on.System.EndsWith(PromptBuilder.GarrisonConversionAddendum, StringComparison.Ordinal),
                tag + "flag on: the existing prompt is unchanged; the block sits before the garrison addendum");
            string versionOff = UtVersion(off), versionOn = UtVersion(on);
            UtCheck(versionOn == versionOff.Replace(PromptBuilder.GarrisonConversionVersionSuffix,
                        PromptBuilder.UnitTypesVersionSuffix + PromptBuilder.GarrisonConversionVersionSuffix)
                    && PromptBuilder.IsUnitTypesPromptVersion(versionOn) && !PromptBuilder.IsUnitTypesPromptVersion(versionOff),
                tag + "the version gains +unit-types-2 before the garrison suffix (" + versionOn + ")");
            UtCheck(PromptBuilder.IsGarrisonConversionPromptVersion(versionOn) && PromptBuilder.IsAttackPromptVersion(versionOn),
                tag + "garrison and attack identity detection still work with the suffix");
            UtCheck(PromptBuilder.GarrisonConversionHash(on.System) != PromptBuilder.GarrisonConversionHash(off.System),
                tag + "the unit-type text changes the schema hash");
            int perCity = on.User.Split('\n').Count(l => l.StartsWith("- type=recruit target_id=city-wei-2"));
            UtCheck(perCity == 4 && on.User.Contains("label=\"Recruit cavalry at Xuchang\"")
                    && on.User.Contains("needs Military research level 2"),
                tag + "the dispatched candidate list has four recruit entries per city with the research reason");
            string recruitCost = profile.RecruitGoldCost("siege", BalanceConfig.RecruitTroopGain).ToString(CultureInfo.InvariantCulture);
            UtCheck(on.System.Contains("  - siege: " + recruitCost + " gold; needs Military research level 3"),
                tag + "the dispatched block carries the match profile's siege price (" + recruitCost + ")");
            UtCheck(!off.User.Contains("Recruit archer") && on.User.Contains("Recruit archer at Xuchang"),
                tag + "the archer candidate exists only with the flag on");
            // Seat filtering: Shu's prompt carries Shu's own cities only, and the block is identical for both seats.
            GameEngine shared = UtEngine(profile);
            UtRunResult weiRun = UtRun(shared, UtConfig(true));
            UtRunResult shuRun = UtRunSeat(shared, UtConfig(true), "seat-1");
            LlmRequest wei = weiRun.Fake.Requests[0], shu = shuRun.Fake.Requests.Count > 0 ? shuRun.Fake.Requests[0] : null;
            string blockWei = wei.System.Substring(wei.System.IndexOf("UNIT TYPES", StringComparison.Ordinal));
            string blockShu = shu == null ? null : shu.System.Substring(shu.System.IndexOf("UNIT TYPES", StringComparison.Ordinal));
            UtCheck(shu != null && blockWei == blockShu
                    && !shu.User.Contains("target_id=city-wei") && shu.User.Contains("target_id=city-shu"),
                tag + "both seats get the same fact block; each candidate list names only its own cities");
        }

        // Stacking with the action reference: the suffix order is stable and detection still works.
        LlmConfig stacked = UtConfig(true);
        stacked.EnableActionReference = true;
        LlmRequest both2 = UtFirstRequest(stacked, BalanceProfile.Standard);
        string v = both2 == null ? "" : UtVersion(both2);
        UtCheck(v.EndsWith(PromptBuilder.ActionReferenceVersionSuffix + PromptBuilder.AssaultTextVersionSuffix
                    + PromptBuilder.UnitTypesVersionSuffix + PromptBuilder.GarrisonConversionVersionSuffix, StringComparison.Ordinal)
                && PromptBuilder.IsActionReferencePromptVersion(v) && PromptBuilder.IsUnitTypesPromptVersion(v)
                && both2.System.Contains("ACTION REFERENCE") && both2.System.Contains("UNIT TYPES"),
            "with the action reference the order is actions, assault text, unit types, garrison (" + v + ")");
        string withRef = both2.System;
        UtCheck(withRef.IndexOf("ACTION REFERENCE", StringComparison.Ordinal) < withRef.IndexOf("UNIT TYPES (opt-in", StringComparison.Ordinal)
                && withRef.EndsWith(PromptBuilder.GarrisonConversionAddendum, StringComparison.Ordinal),
            "the action reference precedes the unit-type block and the garrison addendum stays last");

        // Historical 3:1 mode: the unit block follows the historical identity and states the siege waiver.
        GameEngine hist = UtEngine(BalanceProfile.Standard);
        hist.State.RequireAssaultTroopRatio = true;
        UtRunResult histRun = UtRun(hist, UtConfig(true));
        LlmRequest hr = histRun.Fake.Requests.Count > 0 ? histRun.Fake.Requests[0] : null;
        UtCheck(hr != null && hr.System.Contains("exempt from the minimum troop ratio")
                && PromptBuilder.IsUnitTypesPromptVersion(UtVersion(hr))
                && !UtVersion(hr).Contains(PromptBuilder.AssaultTextVersionSuffix),
            "historical 3:1 mode keeps its legacy attack identity and states the siege waiver");
    }

    // ---- replay: the recorded identity decides the vocabulary -------------------------------------------
    private static ReplayBundle UtReplayBundle(string promptVersion, string rawRecruit, bool parseOk, bool resultOk, string error)
    {
        ReplayBundle bundle = DipReplayBundle(promptVersion);
        bundle.Requests[0].RawModelText = rawRecruit;
        bundle.Requests[0].ParseOk = parseOk;
        bundle.Requests[0].ResultOk = resultOk;
        bundle.Requests[0].ResultError = error;
        return bundle;
    }

    private static void UnitParityReplay()
    {
        string versionOn = PromptBuilder.AttackPromptVersion + PromptBuilder.UnitTypesVersionSuffix;
        ReplayBundle recorded = UtReplayBundle(versionOn, UtRecruit("city-wei", "archer"), true, true, null);
        var on = new ReplaySession(recorded);
        on.RunToCompletion();
        UtCheck(on.IsCompleted && on.AcceptedActionsReplayed == 1 && on.UnexpectedRejections == 0
                && on.ActionOutcomeMismatches == 0
                && on.Engine.Log.Records.Any(r => r.EventType == "recruit" && r.Payload["unit_type"] == "archer"),
            "a +unit-types-2 recording replays its archer recruitment through the engine");

        // Historical recordings (no suffix) keep their recorded outcome: the old parser refused archer, so
        // a recording that says it was accepted can never be replayed as accepted. A recorded refusal stays refused.
        ReplayBundle old = UtReplayBundle(PromptBuilder.AttackPromptVersion, UtRecruit("city-wei", "archer"), false, false,
            "unsupported unit type");
        var oldSession = new ReplaySession(old);
        oldSession.RunToCompletion();
        UtCheck(oldSession.IsCompleted && oldSession.AcceptedActionsReplayed == 0
                && oldSession.InvalidResponsesReplayed == 1 && oldSession.UnexpectedAcceptances == 0
                && oldSession.Engine.Log.Records.All(r => r.EventType != "recruit"),
            "a recording without the suffix replays its recorded refusal of archer, never executing it");
        ReplayBundle contradictory = UtReplayBundle(PromptBuilder.AttackPromptVersion, UtRecruit("city-wei", "archer"), true, true, null);
        var contradictorySession = new ReplaySession(contradictory);
        contradictorySession.RunToCompletion();
        UtCheck(contradictorySession.Engine.Log.Records.All(r => r.EventType != "recruit")
                && contradictorySession.ActionOutcomeMismatches >= 1,
            "an old-identity record claiming archer was accepted fails closed and is reported as a mismatch");

        // The run record and summary carry the identity.
        string bootstrap = ReplayBootstrap.BuildJson(BootstrapEngine("ut-game"), 12, "run-ut", UtConfig(true), "rev", "spectator", 1);
        UtCheck(bootstrap.Contains("\"unit_types\":true")
                && !ReplayBootstrap.BuildJson(BootstrapEngine("ut-game"), 12, "run-ut", UtConfig(false), "rev", "spectator", 1)
                    .Contains("unit_types"),
            "the replay bootstrap records unit_types only when it was enabled");
        string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "luanshi-ut-rec-" + Guid.NewGuid().ToString("N"));
        try
        {
            var recorder = new LlmRunRecorder(root);
            var fake = new FakeLlmTransport();
            fake.EnqueueSuccess(UtRecruit("city-wei-2", "cavalry"), 0, 100, 10);
            for (int i = 0; i < 4; i++) fake.EnqueueSuccess("{\"type\":\"end_turn\"}", 0, 100, 10);
            GameEngine engine = UtEngine(BalanceProfile.Standard);
            var runner = new LlmTurnRunner(fake, UtConfig(true), recorder, null, new MemoryLlmStrategicNoteStore());
            var session = new SpectatorSession(engine, runner, 1, 777);
            session.Start();
            SpectatorDrain(session);
            string[] lines = System.IO.File.Exists(recorder.FilePath) ? System.IO.File.ReadAllLines(recorder.FilePath) : new string[0];
            UtCheck(lines.Length >= 3 && lines.All(l => l.Contains(PromptBuilder.UnitTypesVersionSuffix))
                    && lines[0].Contains("\\\"unit_type\\\":\\\"cavalry\\\""),
                "the run log records +unit-types-2 and the typed recruit on every request");
        }
        finally { try { System.IO.Directory.Delete(root, true); } catch (Exception) { } }
    }

    // ---- recruited types compose with the attack path: the unit rule decides, not the transport --------------
    private static int[] UtHexAtDistance(GameEngine engine, int fromX, int fromZ, int distance)
    {
        MapData map = engine.State.Map;
        var seen = new Dictionary<int, int> { { map.Index(fromX, fromZ), 0 } };
        var queue = new Queue<int>();
        queue.Enqueue(map.Index(fromX, fromZ));
        while (queue.Count > 0)
        {
            int at = queue.Dequeue();
            if (seen[at] == distance && map.Walkable[at])
                return new[] { at % map.Width, at / map.Width };
            if (seen[at] >= distance) continue;
            foreach (int next in map.Neighbors[at])
                if (!seen.ContainsKey(next)) { seen[next] = seen[at] + 1; queue.Enqueue(next); }
        }
        return null;
    }

    private static void UnitParityAttackComposition()
    {
        foreach (string type in new[] { "infantry", "archer" })
        {
            GameEngine engine = UtEngine(BalanceProfile.Standard, 0);
            CityState city = engine.State.FindCity("city-wei-2");
            int[] target = UtHexAtDistance(engine, city.X, city.Z, 2);
            ArmyState enemy = engine.State.FindArmy("army-shu-1");
            enemy.X = target[0]; enemy.Z = target[1];
            enemy.UnitType = "infantry";
            var fake = new FakeLlmTransport();
            fake.EnqueueSuccess(UtRecruit("city-wei-2", type), 0, 100, 10);
            fake.EnqueueSuccess("{\"type\":\"convert_garrison\",\"target_id\":\"city-wei-2\",\"param_a\":1000,\"param_b\":0}", 0, 100, 10);
            fake.EnqueueDynamic(req =>
            {
                ArmyState formed = engine.State.Armies.Find(a => a.OwnerSeatId == "seat-0" && a.X == city.X && a.Z == city.Z);
                return "{\"type\":\"attack_army\",\"target_id\":\"" + (formed == null ? "none" : formed.Id)
                    + "\",\"param_a\":" + target[0] + ",\"param_b\":" + target[1] + "}";
            }, 0, 100, 10);
            fake.EnqueueSuccess("{\"type\":\"end_turn\"}", 0, 100, 10);
            fake.EnqueueSuccess("{\"type\":\"end_turn\"}", 0, 100, 10);
            var runner = new LlmTurnRunner(fake, UtConfig(true), null, null, new MemoryLlmStrategicNoteStore());
            int token = engine.BeginSeatTurn("seat-0");
            var host = new SingleLlmTurnHost(engine, UtConfig(true), "seat-0", () => engine.Observe("seat-0"),
                cmd => engine.SubmitAs("seat-0", token, cmd), () => { });
            runner.Begin(host, token, "seat-0", 777);
            for (int i = 0; i < 2000 && (runner.State == RunnerState.Requesting || runner.State == RunnerState.Executing); i++)
            { runner.Tick(); System.Threading.Thread.Sleep(1); }
            engine.EndSeatTurn(token);
            List<EventRecord> battles = UtEvents(engine, "battle_field");
            if (type == "archer")
                UtCheck(battles.Count == 1 && battles[0].Payload["attacker_unit_type"] == "archer"
                        && battles[0].ControllerType == ControllerType.Llm && UtEvents(engine, "action_rejected").Count == 0,
                    "an LLM-recruited archer army attacks an enemy army two hexes away through the attack path");
            else
                UtCheck(battles.Count == 0 && UtEvents(engine, "action_rejected").Count == 1,
                    "the same order from an infantry army two hexes away is refused by the validator (range rule)");
        }
    }

    // ---- worst-case prompt size: every optional section on, unit types included ---------------------------
    private static void UnitParityCrowdedPrompt()
    {
        GameEngine engine = BuildCrowdedRichOverflowWorld();
        engine.State.FindCity("city-shu").Garrison = 1000;
        engine.State.FindCity("city-shu").GarrisonType = UnitTypes.Infantry;
        LlmConfig config = ActRefConfig(true);
        config.EnableUnitTypes = true;
        var fake = new FakeLlmTransport();
        fake.EnqueueSuccess("{\"type\":\"end_turn\"}", 0, 100, 10);
        fake.EnqueueSuccess("{\"type\":\"end_turn\"}", 0, 100, 10);
        var runner = new LlmTurnRunner(fake, config, null, null, new MemoryLlmStrategicNoteStore());
        var session = new SpectatorSession(engine, runner, 1, 909);
        session.Start();
        SpectatorDrain(session);
        bool dispatched = fake.Requests.Count > 0;
        int total = dispatched ? fake.Requests[0].System.Length + fake.Requests[0].User.Length : -1;
        UtCheck(dispatched && total <= 40000 && fake.Requests[0].System.Contains("UNIT TYPES")
                && fake.Requests[0].System.Contains("ACTION REFERENCE")
                && !runner.EndReasonText.ToLowerInvariant().Contains("prompt too large"),
            "the crowded worst-case world with every option on still dispatches within 40,000 characters (" + total + ")");
    }
}
