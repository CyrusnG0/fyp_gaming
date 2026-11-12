using System;
using System.Collections.Generic;
using System.Linq;
using LuanShi.Engine;

// [ATK-4] Typed feedback for rejected model attack commands.
//
// Only two engine rejections are explained to the model: a broken-morale army (own state, checked
// before the target is read) and a failed assault ratio (only reachable for an adjacent enemy city,
// whose garrison the seat can already see). Everything else, and anything the seat's own observation
// does not back up, stays the generic category. The engine validator is untouched: these tests read
// what it says, they never change what it accepts.

internal static partial class Harness
{
    private static void RunAttackFeedback()
    {
        Section("[ATK-4] typed attack-rejection feedback (fake provider)");
        AttackFeedbackRatio();
        AttackFeedbackMorale();
        AttackFeedbackTreatyIsNotARejection();
        AttackFeedbackFogSafety();
        AttackFeedbackUnchangedElsewhere();
    }

    private static void AtkFb(bool ok, string name) => Check(ok, "[ATK-4] " + name);

    private static List<string> AtkFbRun(GameEngine engine, params string[] orders)
    {
        var host = new LlmAttackHost(engine, "seat-a");
        var fake = new FakeLlmTransport();
        foreach (string order in orders) fake.EnqueueSuccess(order);
        fake.EnqueueSuccess(W2EndTurn());
        var runner = new LlmTurnRunner(fake, AtkConfig(maxInvalid: 20, maxActions: 20), null);
        runner.Begin(host, host.Token, "seat-a", 3);
        LlmDrain(runner);
        return runner.RecentHistory.Where(h => h.Contains("rejected") || h.Contains("-> ok")).ToList();
    }

    // ---- assault ratio ---------------------------------------------------------

    private static void AttackFeedbackRatio()
    {
        // 2000 troops against a 1000 garrison: 3:1 needs 3000, so the engine rejects the assault.
        var engine = AttackWorld(9601, cityGarrison: 1000);
        var viewBefore = AttackPlanner.Build(engine, "seat-a");
        AtkFb(AtkOption(AtkPlan(viewBefore, "army-a-1"), ActionType.AttackCity, 1, 2) == null,
            "the planner does not advertise the failing assault (the engine would reject it)");

        string raw = engine.Submit(AtkCityOrder("seat-a", "army-a-1", 1, 2)).Error;
        AtkFb(raw != null && raw.StartsWith("assault needs"),
            $"the validator itself says the assault ratio failed ({raw})");

        var fresh = AttackWorld(9601, cityGarrison: 1000);
        var history = AtkFbRun(fresh, AtkJson("attack_city", "army-a-1", 1, 2));
        AtkFb(history.Count == 1 && history[0] == "attack_city army-a-1 -> rejected: assault ratio too low",
            "the model is told the assault ratio was too low");
        AtkFb(!history[0].Contains("1000") && !history[0].Contains("3000") && !history[0].Contains("2000"),
            "and no garrison, required or own troop figure is repeated");
        AtkFb(fresh.State.FindFaction("seat-a").CommandPoints == 5
           && fresh.Log.Records.All(r => r.EventType != "battle_siege"),
            "the rejected assault cost nothing and fought nothing");

        // The same garrison with a legal ratio is accepted: the feedback never invents a rejection.
        var ok = AttackWorld(9601, cityGarrison: 300);
        var okHistory = AtkFbRun(ok, AtkJson("attack_city", "army-a-1", 1, 2));
        AtkFb(okHistory.Count == 1 && okHistory[0] == "attack_city army-a-1 -> ok",
            "a legal assault is still accepted and reports ok");

        // A siege army waives the ratio, so the same garrison is accepted rather than explained.
        var siege = AttackWorld(9601, UnitTypes.Siege, cityGarrison: 5000);
        var siegeHistory = AtkFbRun(siege, AtkJson("attack_city", "army-a-1", 1, 2));
        AtkFb(siegeHistory.Count == 1 && siegeHistory[0].EndsWith("-> ok"),
            "a siege army's waived assault is accepted, so no ratio feedback appears");
    }

    // ---- broken morale -----------------------------------------------------------

    private static void AttackFeedbackMorale()
    {
        var engine = AttackWorld(9611);
        engine.State.FindArmy("army-a-1").Morale = BalanceConfig.CombatRoutMoraleThreshold;
        string rawField = engine.Submit(AttackOrder("seat-a", "army-a-1", 2, 1)).Error;
        string rawCity = engine.Submit(AtkCityOrder("seat-a", "army-a-1", 1, 2)).Error;
        AtkFb(rawField != null && rawField.StartsWith("army morale is broken")
           && rawField == rawCity,
            "the validator answers a broken army identically for a field order and an assault");

        var fresh = AttackWorld(9611);
        fresh.State.FindArmy("army-a-1").Morale = BalanceConfig.CombatRoutMoraleThreshold;
        var history = AtkFbRun(fresh,
            AtkJson("attack_army", "army-a-1", 2, 1),
            AtkJson("attack_city", "army-a-1", 1, 2));
        AtkFb(history.Count == 2 && history.All(h => h.EndsWith("-> rejected: army morale broken")),
            "both attack orders from a broken army read 'army morale broken'");
        AtkFb(fresh.State.FindArmy("army-b-1").Troops == 500
           && fresh.State.FindFaction("seat-a").CommandPoints == 5,
            "and nothing was fought or charged");

        // The reason is target-independent: a hidden hex and an empty hex give the same answer,
        // so it cannot be used to probe the fog.
        var probe = AttackWorld(9611, hiddenArmy: true);
        probe.State.FindArmy("army-a-1").Morale = BalanceConfig.CombatRoutMoraleThreshold;
        var probeHistory = AtkFbRun(probe,
            AtkJson("attack_army", "army-a-1", 6, 4),
            AtkJson("attack_army", "army-a-1", 0, 0),
            AtkJson("attack_city", "army-a-1", 7, 5));
        AtkFb(probeHistory.Count == 3 && probeHistory.All(h => h.EndsWith("-> rejected: army morale broken")),
            "a broken army's answer is the same for hidden, empty and visible targets");

        // An army that is merely hurt (above the threshold) is not described as broken.
        var hurt = AttackWorld(9611);
        hurt.State.FindArmy("army-a-1").Morale = BalanceConfig.CombatRoutMoraleThreshold + 1;
        var hurtHistory = AtkFbRun(hurt, AtkJson("attack_army", "army-a-1", 2, 1));
        AtkFb(hurtHistory.Count == 1 && hurtHistory[0].EndsWith("-> ok"),
            "an army just above the threshold still attacks, with no morale feedback");
    }

    // ---- treaty ------------------------------------------------------------------

    private static void AttackFeedbackTreatyIsNotARejection()
    {
        var engine = AttackWorld(9621);
        engine.State.Diplomacy.Treaties.Add(new TreatyRecord
        {
            Type = TreatyType.Alliance, FactionA = "seat-a", FactionB = "seat-b", ExpirySeason = 9,
        });
        var result = engine.Submit(AttackOrder("seat-a", "army-a-1", 2, 1));
        AtkFb(result.Ok && engine.Log.Records.Any(r => r.EventType == "battle_field"),
            "the engine does not reject an attack on an ally: it fights and prices the betrayal afterwards");
        AtkFb(FeedbackPolicy.Categories.All(c => !c.Contains("treaty") && !c.Contains("alliance")
              && !c.Contains("truce") && !c.Contains("pact") && !c.Contains("ally")),
            "so no treaty category exists to be reported as a refusal");

        var viaRunner = AttackWorld(9621);
        viaRunner.State.Diplomacy.Treaties.Add(new TreatyRecord
        {
            Type = TreatyType.Truce, FactionA = "seat-a", FactionB = "seat-b", ExpirySeason = 9,
        });
        var history = AtkFbRun(viaRunner, AtkJson("attack_army", "army-a-1", 2, 1));
        AtkFb(history.Count == 1 && history[0].EndsWith("-> ok"),
            "through the runner an attack on a truce partner is accepted, not reported as rejected");
    }

    // ---- fog safety --------------------------------------------------------------

    private static void AttackFeedbackFogSafety()
    {
        // Worlds that differ only outside the seat's sight must give byte-identical feedback.
        var histories = new List<List<string>>();
        foreach (var engine in new[]
        {
            AttackWorld(9631, cityGarrison: 1000, hiddenGarrison: 0, hiddenArmy: false),
            AttackWorld(9631, cityGarrison: 1000, hiddenGarrison: 9999, hiddenArmy: true),
        })
        {
            histories.Add(AtkFbRun(engine,
                AtkJson("attack_army", "army-a-1", 6, 4),      // hidden army hex
                AtkJson("attack_city", "army-a-1", 7, 5),      // hidden city hex
                AtkJson("attack_city", "army-a-1", 1, 2),      // visible enemy city, ratio fails
                AtkJson("attack_city", "army-a-1", 0, 1)));    // neutral city
        }
        AtkFb(histories[0].SequenceEqual(histories[1]),
            "worlds differing only outside sight give identical typed feedback for every probe");
        AtkFb(histories[0].Count == 4
           && histories[0][0].EndsWith("rejected: action rejected")
           && histories[0][1].EndsWith("rejected: action rejected")
           && histories[0][2].EndsWith("rejected: assault ratio too low")
           && histories[0][3].EndsWith("rejected: action rejected"),
            "hidden, out-of-range and neutral probes stay generic; only the visible failing assault is explained");

        // The upgrade needs the seat's own observation to back it. Without it the answer is generic.
        var world = AttackWorld(9632, cityGarrison: 1000);
        var obs = world.Observe("seat-a");
        string ratio = "assault needs 3:1 troops (have 2000, garrison 1000) or a siege army";
        string morale = "army morale is broken — it cannot attack";
        var visibleCity = AtkCityOrder("seat-a", "army-a-1", 1, 2);
        var hiddenCity = AtkCityOrder("seat-a", "army-a-1", 7, 5);
        var neutralCity = AtkCityOrder("seat-a", "army-a-1", 0, 1);
        var foreignArmy = AttackOrder("seat-a", "army-b-1", 2, 1);
        AtkFb(FeedbackPolicy.ForModel(ratio) == FeedbackPolicy.Generic
           && FeedbackPolicy.ForModel(morale) == FeedbackPolicy.Generic,
            "the plain string mapping never upgrades attack text by itself");
        AtkFb(FeedbackPolicy.ForModel(ratio, visibleCity, obs) == FeedbackPolicy.AssaultRatioTooLow
           && FeedbackPolicy.ForModel(ratio, visibleCity, null) == FeedbackPolicy.Generic
           && FeedbackPolicy.ForModel(ratio, hiddenCity, obs) == FeedbackPolicy.Generic
           && FeedbackPolicy.ForModel(ratio, neutralCity, obs) == FeedbackPolicy.Generic,
            "the ratio category needs an enemy city on that hex in the seat's own observation");
        AtkFb(FeedbackPolicy.ForModel(morale, visibleCity, obs) == FeedbackPolicy.ArmyMoraleBroken
           && FeedbackPolicy.ForModel(morale, visibleCity, null) == FeedbackPolicy.Generic
           && FeedbackPolicy.ForModel(morale, foreignArmy, obs) == FeedbackPolicy.Generic,
            "the morale category needs the order's army to be listed as the seat's own");
        AtkFb(FeedbackPolicy.ForModel(ratio, new ActionCommand
                { Type = ActionType.March, TargetId = "army-a-1", ParamA = 1, ParamB = 2 }, obs)
              == FeedbackPolicy.Generic
           && FeedbackPolicy.ForModel(ratio, AttackOrder("seat-a", "army-a-1", 1, 2), obs)
              == FeedbackPolicy.Generic,
            "and only on an attack order of the matching kind (a field order cannot name a ratio)");

        AtkFb(FeedbackPolicy.IsSafeCategory(FeedbackPolicy.ArmyMoraleBroken)
           && FeedbackPolicy.IsSafeCategory(FeedbackPolicy.AssaultRatioTooLow)
           && !FeedbackPolicy.ArmyMoraleBroken.Any(char.IsDigit)
           && !FeedbackPolicy.AssaultRatioTooLow.Any(char.IsDigit),
            "both new categories are in the published allowlist and carry no numbers");
    }

    // ---- unchanged elsewhere -------------------------------------------------------

    private static void AttackFeedbackUnchangedElsewhere()
    {
        var world = AttackWorld(9641);
        var obs = world.Observe("seat-a");
        string[] errors =
        {
            "target army is out of range (max 1)", "no army at [3,3]", "that is your own army",
            "target city is not adjacent", "no city at [3,3]", "neutral cities are taken by marching into them",
            "that is your own city", "not your army", "unknown army 'army-zz'", "no command points left",
            "not enough gold (needs 40)", "destination out of reach this season (max cost 6)",
            "army already marched this season", "something nobody anticipated", "", null,
        };
        var commands = new[]
        {
            AttackOrder("seat-a", "army-a-1", 2, 1), AtkCityOrder("seat-a", "army-a-1", 1, 2),
            MarchOrder(world, "seat-a", "army-a-1", 3, 1),
            new ActionCommand { Type = ActionType.Recruit, TargetId = "city-a", ParamA = 100 },
            null,
        };
        bool same = true;
        foreach (string error in errors)
            foreach (var command in commands)
                same &= FeedbackPolicy.ForModel(error, command, obs) == FeedbackPolicy.ForModel(error);
        AtkFb(same,
            "every other rejection, for attack and non-attack commands alike, maps exactly as before");

        // A domestic rejection through the runner keeps its existing typed answer with attack on.
        var host = new LlmAttackHost(AttackWorld(9641), "seat-a");
        var fake = new FakeLlmTransport();
        fake.EnqueueSuccess("{\"type\":\"recruit\",\"target_id\":\"city-zzz\",\"param_a\":100}");
        fake.EnqueueSuccess(W2EndTurn());
        var runner = new LlmTurnRunner(fake, AtkConfig(), null);
        runner.Begin(host, host.Token, "seat-a", 3);
        LlmDrain(runner);
        AtkFb(runner.RecentHistory.Any(h => h.EndsWith("rejected: invalid target")),
            "a non-attack rejection through the runner keeps its existing 'invalid target' answer");
    }
}
