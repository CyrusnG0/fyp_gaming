using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using LuanShi.Engine;

// [ATK-3] The seat-wide cap on advertised attack options. The cap is global (all attackers together),
// nearest-first by each option's own distance, with a total deterministic tie-break, and it is
// stated to the model only when it dropped something. The engine validator stays authoritative.
//
// Prompt budget (stated, not inferred): the attack feature may add to a planning prompt at most
// the attack rules text + AtkLineBudget characters per advertised option x MaxAdvertisedOptions
// + the truncation line. The whole prompt must stay within PromptBuilder.MaxPromptChars (40,000),
// the same cap the movement-list truncation fallback enforces.

internal static partial class Harness
{
    private const int AtkLineBudget = 600;

    private static void AttackCapTests()
    {
        AttackCapOrdering();
        AttackCapForgedViewIsBounded();
        AttackCapPromptBudget();
        AttackCapRunnerFlow();
    }

    private static void CapCheck(bool ok, string name) => Check(ok, "[ATK-3] " + name);

    /// <summary>
    /// 12x10 map. Two archers (range 2): army-a-1 at (3,3) and army-a-2 at (8,6). Every free hex
    /// within two steps of either archer holds one enemy infantry army, so each attacker alone has
    /// more than ten legal targets and the pair has 24.
    /// </summary>
    private static GameEngine CrowdedWorld(int seed)
    {
        var e = MovementFixture(seed, 2, UnitTypes.Archer, 12, 10);
        var first = e.State.FindArmy("army-a-1");
        first.X = 3; first.Z = 3;
        e.State.Armies.Add(new ArmyState
        {
            Id = "army-a-2", Name = "Army A2", OwnerSeatId = "seat-a", Troops = 1500,
            UnitType = UnitTypes.Archer, X = 8, Z = 6,
        });
        var map = e.State.Map;
        foreach (var attacker in e.State.Armies.Where(a => a.OwnerSeatId == "seat-a").ToList())
        {
            for (int z = 0; z < map.Height; z++)
            {
                for (int x = 0; x < map.Width; x++)
                {
                    int d = map.HexDistance(attacker.X, attacker.Z, x, z, 2);
                    if (d < 1 || e.State.ArmyAt(x, z) != null || e.State.CityAt(x, z) != null) continue;
                    e.State.Armies.Add(new ArmyState
                    {
                        Id = "e-" + x + "-" + z, Name = "Raider " + x + "," + z, OwnerSeatId = "seat-b",
                        Troops = 100, UnitType = UnitTypes.Infantry, X = x, Z = z,
                    });
                }
            }
        }
        return e;
    }

    private static List<string> AtkExpectedNearest(GameEngine e, int take)
    {
        var map = e.State.Map;
        var all = new List<(int d, string army, int z, int x)>();
        foreach (var own in e.State.Armies.Where(a => a.OwnerSeatId == "seat-a"))
        {
            int range = UnitTypes.AttackRange(own.UnitType);
            foreach (var enemy in e.State.Armies.Where(a => a.OwnerSeatId != "seat-a"))
            {
                int d = map.HexDistance(own.X, own.Z, enemy.X, enemy.Z, range);
                if (d >= 1) all.Add((d, own.Id, enemy.Z, enemy.X));
            }
        }
        return all.OrderBy(t => t.d).ThenBy(t => t.army, StringComparer.Ordinal).ThenBy(t => t.z).ThenBy(t => t.x)
            .Take(take).Select(t => t.army + "|" + t.x + "|" + t.z).ToList();
    }

    private static int AtkTotalLegal(GameEngine e)
    {
        var map = e.State.Map;
        int n = 0;
        foreach (var own in e.State.Armies.Where(a => a.OwnerSeatId == "seat-a"))
            foreach (var enemy in e.State.Armies.Where(a => a.OwnerSeatId != "seat-a"))
                if (map.HexDistance(own.X, own.Z, enemy.X, enemy.Z, UnitTypes.AttackRange(own.UnitType)) >= 1) n++;
        return n;
    }

    // ---- (a) more than ten legal targets: exactly ten, in sight, nearest first --------------------

    private static void AttackCapOrdering()
    {
        var e = CrowdedWorld(9501);
        int legal = AtkTotalLegal(e);
        var view = AttackPlanner.Build(e, "seat-a");
        var obs = e.Observe("seat-a");
        var entries = ActionCatalogue.Build(obs, view)
            .Where(o => o.Type == ActionType.AttackArmy || o.Type == ActionType.AttackCity).ToList();

        CapCheck(legal > 20 && AttackPlanner.MaxAdvertisedOptions == 10,
            "the fixture really offers more than twenty legal targets across two attackers (" + legal + ")");
        CapCheck(view.OptionCount == 10 && entries.Count == 10 && view.TotalAvailable == legal && view.Truncated,
            "exactly ten options are advertised seat-wide, with the full legal count and the flag reported");
        CapCheck(view.Armies.Count(p => p.Options.Count > 0) == 2
           && view.Armies.All(p => p.TotalAvailable > p.Options.Count ? p.Truncated : !p.Truncated),
            "the ten are shared across both attackers (a global, not per-army, cap) and per-army flags agree");
        CapCheck(entries.All(o => GameEngine.InSight(e.State, "seat-a", o.ParamA.Value, o.ParamB.Value)),
            "every advertised target is inside the seat's sight");

        var map = e.State.Map;
        var distances = entries.Select(o =>
        {
            var own = e.State.FindArmy(o.TargetId);
            return map.HexDistance(own.X, own.Z, o.ParamA.Value, o.ParamB.Value, 2);
        }).ToList();
        bool nearestFirst = true;
        for (int i = 1; i < distances.Count; i++) if (distances[i] < distances[i - 1]) nearestFirst = false;
        CapCheck(nearestFirst && distances.All(d => d >= 1),
            "the catalogue lists the targets nearest first (distances recomputed independently)");

        var actual = entries.Select(o => o.TargetId + "|" + o.ParamA + "|" + o.ParamB).ToList();
        CapCheck(actual.SequenceEqual(AtkExpectedNearest(e, 10)),
            "the ten shown are exactly the ten nearest, ties broken by army id then z then x");

        var reordered = CrowdedWorld(9501);
        reordered.State.Armies.Reverse();
        CapCheck(AttackPlanner.Build(reordered, "seat-a").ToJson() == view.ToJson()
           && AttackPlanner.Build(e, "seat-a").ToJson() == view.ToJson(),
            "selection is deterministic and independent of the engine's army storage order");

        CapCheck(ActionCatalogue.AttackNotice(view) == "nearest 10 targets shown; other legal targets exist",
            "the truncation notice carries the exact required wording");
        var small = AttackPlanner.Build(AttackWorld(9502), "seat-a");
        CapCheck(!small.Truncated && ActionCatalogue.AttackNotice(small) == null
           && small.TotalAvailable == small.OptionCount,
            "a world within the cap states no truncation");

        int accepted = 0;
        for (int i = 0; i < entries.Count; i++)
        {
            var fresh = CrowdedWorld(9501);
            var o = entries[i];
            var r = fresh.Submit(new ActionCommand
            {
                Type = o.Type, ActorSeatId = "seat-a", TargetId = o.TargetId,
                ParamA = o.ParamA.Value, ParamB = o.ParamB.Value,
            });
            if (r.Ok) accepted++;
        }
        CapCheck(accepted == 10, "all ten advertised options are accepted by the normal validator");
    }

    private static void AttackCapForgedViewIsBounded()
    {
        var e = CrowdedWorld(9503);
        var obs = e.Observe("seat-a");
        var own = e.State.FindArmy("army-a-1");
        var map = e.State.Map;
        var forgedOptions = new List<AttackOption>();
        foreach (var enemy in obs.Armies.Where(r => r.OwnerSeatId != "seat-a"))
        {
            int d = map.HexDistance(own.X, own.Z, enemy.X, enemy.Z, 2);
            if (d < 1) continue;
            forgedOptions.Add(new AttackOption
            {
                ArmyId = own.Id, Kind = ActionType.AttackArmy, X = enemy.X, Z = enemy.Z, Distance = d,
                CommandPointCost = 1, TargetArmyId = enemy.Id,
            });
        }
        // Hostile ordering: farthest first. The catalogue must still keep the nearest ten.
        forgedOptions.Reverse();
        var forged = new AttackView
        {
            SeatId = "seat-a", Season = 2, CommandPoints = 5,
            Armies = new List<ArmyAttackPlan>
            {
                new ArmyAttackPlan
                {
                    ArmyId = own.Id, CanAttack = true, Options = forgedOptions,
                    TotalAvailable = forgedOptions.Count,
                },
            },
        };
        var entries = ActionCatalogue.Build(obs, forged).Where(o => o.Type == ActionType.AttackArmy).ToList();
        var dist = entries.Select(o => map.HexDistance(own.X, own.Z, o.ParamA.Value, o.ParamB.Value, 2)).ToList();
        var nearestTen = forgedOptions.Select(o => o.Distance).OrderBy(d => d).Take(10).ToList();
        CapCheck(forgedOptions.Count > 10 && entries.Count == 10 && dist.SequenceEqual(nearestTen)
           && ActionCatalogue.AttackNotice(forged) == AttackPlanner.TruncationNotice,
            "a hand-built oversized view is still cut to the ten nearest, and the notice is shown");
    }

    // ---- (b) crowded prompt stays inside the stated budget --------------------------------------

    private static MovementView CapCrowdedMovement()
    {
        var armies = new List<ArmyMovePlan>();
        for (int army = 0; army < 300; army++)
        {
            var planOptions = new List<MoveOption>();
            var plan = new ArmyMovePlan
            {
                ArmyId = "army-" + army, ArmyName = "A" + army, UnitType = UnitTypes.Infantry,
                X = army % 9, Z = army % 7, MarchesLeft = 1, Budget = 6,
                CanMarch = true, TotalReachable = 12, Truncated = true, Options = planOptions,
            };
            for (int i = 0; i < 12; i++)
                planOptions.Add(new MoveOption
                {
                    ArmyId = plan.ArmyId, X = i, Z = army % 7, PathCost = i,
                    Kind = i == 0 ? "capture_neutral_city" : "move",
                });
            armies.Add(plan);
        }
        return new MovementView { SeatId = "seat-a", Season = 2, CommandPoints = 5, MarchOrderCost = 1, Armies = armies };
    }

    private static void AttackCapPromptBudget()
    {
        var e = CrowdedWorld(9504);
        var obs = e.Observe("seat-a");
        var view = AttackPlanner.Build(e, "seat-a");
        var withAttack = ActionCatalogue.Build(obs, view);
        var withoutAttack = ActionCatalogue.Build(obs);
        var history = new List<string>();

        // Crowded: both attackers, 300 synthetic movement armies on top. Total is bounded.
        MovementView movement = CapCrowdedMovement();
        string crowded = PromptBuilder.UserPrompt(obs, withAttack, history, 4, 3, movement,
            null, null, false, null, true, view);
        int system = PromptBuilder.SystemPromptFor(true, false, null, true).Length;
        int total = system + crowded.Length;
        int attackLines = Regex.Matches(crowded, "(?m)^- type=attack_").Count;
        CapCheck(total <= PromptBuilder.MaxPromptChars && attackLines == 10
           && Regex.Matches(crowded, Regex.Escape("ATTACK TARGETS: nearest 10 targets shown; other legal targets exist")).Count == 1,
            "a crowded prompt (two attackers, 300 movement armies) stays under " + PromptBuilder.MaxPromptChars
            + " chars (" + total + ") with exactly ten attack lines and one notice");

        // Attack-only delta, measured without movement so the movement fallback cannot mask it.
        string off = PromptBuilder.UserPrompt(obs, withoutAttack, history, 4, 3, (MovementView)null);
        string on = PromptBuilder.UserPrompt(obs, withAttack, history, 4, 3, (MovementView)null,
            null, null, false, null, true, view);
        int delta = (PromptBuilder.SystemPromptFor(true, false, null, true).Length + on.Length)
                  - (PromptBuilder.SystemPromptFor(true).Length + off.Length);
        int budget = PromptBuilder.AttackAddendum().Length + 10 * AtkLineBudget
                   + AttackPlanner.TruncationNotice.Length + 64;
        int longest = on.Split('\n').Where(l => l.StartsWith("- type=attack_")).Max(l => l.Length);
        CapCheck(delta <= budget && longest <= AtkLineBudget,
            "the attack feature adds " + delta + " chars, within the stated " + budget + " (rules + 10 x "
            + AtkLineBudget + " + notice); longest option line " + longest);

        // Two attackers cannot double the bound: one attacker alone is also capped at ten.
        var solo = CrowdedWorld(9504);
        solo.State.Armies.RemoveAll(a => a.Id == "army-a-2");
        var soloView = AttackPlanner.Build(solo, "seat-a");
        CapCheck(soloView.OptionCount == 10 && view.OptionCount == 10,
            "one attacker and two attackers both advertise ten: the bound does not scale with army count");

        // Disabled: the view is ignored and the prompt is unchanged.
        string disabled = PromptBuilder.UserPrompt(obs, withoutAttack, history, 4, 3, movement,
            null, null, false, null, false, view);
        string plain = PromptBuilder.UserPrompt(obs, withoutAttack, history, 4, 3, movement);
        CapCheck(disabled == plain && !disabled.Contains("type=attack_") && !disabled.Contains("ATTACK TARGETS")
           && ActionCatalogue.ToPromptText(ActionCatalogue.Build(obs, null)) == ActionCatalogue.ToPromptText(withoutAttack),
            "with attack disabled the prompt and catalogue are byte-identical to the movement-only forms");
    }

    // ---- the same crowd through the runner and a fake provider ----------------------------------

    private static void AttackCapRunnerFlow()
    {
        var engine = CrowdedWorld(9505);
        var plan = AttackPlanner.Build(engine, "seat-a");
        var firstEntry = ActionCatalogue.Build(engine.Observe("seat-a"), plan)
            .First(o => o.Type == ActionType.AttackArmy);

        var host = new LlmAttackHost(engine, "seat-a");
        var fake = new FakeLlmTransport();
        fake.EnqueueSuccess(AtkJson("attack_army", firstEntry.TargetId, firstEntry.ParamA.Value, firstEntry.ParamB.Value));
        fake.EnqueueSuccess(W2EndTurn());
        var runner = new LlmTurnRunner(fake, AtkConfig(), null);
        runner.Begin(host, host.Token, "seat-a", 3);
        LlmDrain(runner);

        string user = fake.Requests[0].User;
        int size = fake.Requests[0].System.Length + user.Length;
        CapCheck(runner.EndReason == EndReason.ModelEndedTurn && size <= PromptBuilder.MaxPromptChars
           && Regex.Matches(user, "(?m)^- type=attack_").Count == 10
           && user.Contains("ATTACK TARGETS: nearest 10 targets shown; other legal targets exist"),
            "through the runner a crowded two-attacker turn sends ten options and a prompt of " + size + " chars");
        CapCheck(host.SubmitCalls == 1 && Find(engine.Log, "battle_field") != null,
            "choosing the first listed option reaches the validator and resolves a real battle");

        // Choosing a legal-looking target that is not among the ten shown is still decided by the engine.
        var engine2 = CrowdedWorld(9506);
        var view2 = AttackPlanner.Build(engine2, "seat-a");
        var shown = new HashSet<string>(view2.Armies.SelectMany(p => p.Options).Select(o => o.ArmyId + "|" + o.X + "|" + o.Z));
        var omitted = engine2.State.Armies.Where(a => a.OwnerSeatId == "seat-b" && a.Id.StartsWith("e-"))
            .Select(a => new { a.X, a.Z })
            .First(t => !shown.Contains("army-a-2|" + t.X + "|" + t.Z) && !shown.Contains("army-a-1|" + t.X + "|" + t.Z)
                && engine2.State.Map.HexDistance(8, 6, t.X, t.Z, 2) >= 1);
        var host2 = new LlmAttackHost(engine2, "seat-a");
        var fake2 = new FakeLlmTransport();
        fake2.EnqueueSuccess(AtkJson("attack_army", "army-a-2", omitted.X, omitted.Z));
        fake2.EnqueueSuccess(W2EndTurn());
        var runner2 = new LlmTurnRunner(fake2, AtkConfig(), null);
        runner2.Begin(host2, host2.Token, "seat-a", 3);
        LlmDrain(runner2);
        CapCheck(host2.SubmitCalls == 1 && Find(engine2.Log, "battle_field") != null,
            "a legal target omitted by the cap is still accepted: the validator, not the list, is authoritative");
    }
}
