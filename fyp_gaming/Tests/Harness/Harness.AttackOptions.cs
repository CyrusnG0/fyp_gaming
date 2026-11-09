using System;
using System.Collections.Generic;
using System.Linq;
using LuanShi.Engine;

// [ATK-1] Attack and capture candidates: the seat-safe planner, the catalogue entries, the opt-in
// parser vocabulary and the prompt identity. Offline only — FakeLlmTransport, no network, no paid call.
//
// The strongest check is brute force: on a fresh fixture per probed hex, Submit either accepts an
// attack_army / attack_city at that hex or it does not, and that answer must equal membership in the
// advertised (untruncated) option set. Hidden-world pairs pin that facts outside the seat's sight
// change neither the view, the catalogue text, the observation nor the rejection wording.

internal static partial class Harness
{
    private static void RunAttackOptions()
    {
        Section("[ATK-1] attack and capture candidates (planner, catalogue, parser, prompt)");
        AttackBasics();
        AttackBruteForceEquivalence();
        AttackBlockedAndCosts();
        AttackReadOnlyAndTruncation();
        AttackHiddenWorlds();
        AttackCatalogueEntries();
        AttackParserVocabulary();
        AttackConfigAndPromptIdentity();
    }

    private static void AtkCheck(bool ok, string name) => Check(ok, "[ATK-1] " + name);

    // ---- fixtures -----------------------------------------------------------

    /// <summary>
    /// seat-a: city-a (0,0), army-a-1 (1,1) 2000 troops. seat-b: army-b-1 (2,1) 500, army-b-2 (1,3) 100
    /// (two hexes away), city-b (1,2) garrison 300 (adjacent), city-far (7,5) hidden. city-n (0,1) is neutral.
    /// </summary>
    private static GameEngine AttackWorld(int seed, string unitType = UnitTypes.Infantry,
        int cityGarrison = 300, int hiddenGarrison = 0, bool hiddenArmy = false)
    {
        var e = MovementFixture(seed, 2, unitType);
        e.State.FindCity("city-hidden").Garrison = hiddenGarrison;
        e.State.Cities.Add(new CityState
        {
            Id = "city-b", Name = "City B", X = 1, Z = 2, OwnerSeatId = "seat-b",
            Population = 5000, Food = 500, Gold = 100, Garrison = cityGarrison, Morale = 100,
        });
        e.State.Cities.Add(new CityState
        {
            Id = "city-n", Name = "Neutral N", X = 0, Z = 1, OwnerSeatId = null,
            Population = 1000, Food = 100, Gold = 0, Garrison = 0,
        });
        e.State.Armies.Add(new ArmyState
        {
            Id = "army-b-1", Name = "Shu Vanguard", OwnerSeatId = "seat-b", Troops = 500,
            UnitType = UnitTypes.Infantry, X = 2, Z = 1,
        });
        e.State.Armies.Add(new ArmyState
        {
            Id = "army-b-2", Name = "Shu Reserve", OwnerSeatId = "seat-b", Troops = 100,
            UnitType = UnitTypes.Infantry, X = 1, Z = 3,
        });
        if (hiddenArmy)
            e.State.Armies.Add(new ArmyState
            {
                Id = "army-b-hidden", Name = "Ghost", OwnerSeatId = "seat-b", Troops = 4321,
                UnitType = UnitTypes.Cavalry, X = 6, Z = 4,
            });
        return e;
    }

    private static ActionCommand AtkCityOrder(string seat, string armyId, int x, int z)
        => new ActionCommand
        {
            Type = ActionType.AttackCity, ActorSeatId = seat, TargetId = armyId, ParamA = x, ParamB = z,
        };

    private static ArmyAttackPlan AtkPlan(AttackView view, string armyId)
        => view?.Armies?.FirstOrDefault(p => p != null && p.ArmyId == armyId);

    private static AttackOption AtkOption(ArmyAttackPlan plan, string kind, int x, int z)
        => plan?.Options?.FirstOrDefault(o => o != null && o.Kind == kind && o.X == x && o.Z == z);

    // ---- basics --------------------------------------------------------------

    private static void AttackBasics()
    {
        var e = AttackWorld(9301);
        var view = AttackPlanner.Build(e, "seat-a");
        var plan = AtkPlan(view, "army-a-1");
        AtkCheck(view.SeatId == "seat-a" && view.Season == 2 && view.CommandPoints == 5
           && view.Armies.Count == 1 && plan != null && plan.CanAttack && plan.BlockedReason == null
           && plan.AttackRange == 1 && plan.Troops == 2000,
            "the view carries the seat, orders and the single own army with its attack range");

        var field = AtkOption(plan, ActionType.AttackArmy, 2, 1);
        AtkCheck(field != null && field.TargetArmyId == "army-b-1" && field.TargetOwnerSeatId == "seat-b"
           && field.TargetTroops == 500 && field.TargetUnitType == UnitTypes.Infantry
           && field.CommandPointCost == 1 && field.Note == null,
            "an adjacent enemy army is a field-battle candidate carrying only its observable facts");
        var city = AtkOption(plan, ActionType.AttackCity, 1, 2);
        AtkCheck(city != null && city.TargetCityId == "city-b" && city.TargetTroops == 300
           && city.RequiredTroops == 900 && !city.AssaultRatioWaived && city.CommandPointCost == 2
           && city.DefendingArmyTroops == 0,
            "an adjacent enemy city is an assault candidate with the 3:1 troop requirement spelled out");
        AtkCheck(AtkOption(plan, ActionType.AttackArmy, 1, 3) == null,
            "an enemy army two hexes away is not a candidate for a range-1 army");
        AtkCheck(plan.Options.All(o => o.X != 0 || o.Z != 1),
            "a neutral city is never an attack target (march captures it)");
        AtkCheck(plan.Options.All(o => o.X != 0 || o.Z != 0) && plan.Options.All(o => o.X != 1 || o.Z != 1),
            "the seat's own city and its own army's hex are never targets");
        AtkCheck(plan.Options[0].Kind == ActionType.AttackCity && plan.TotalAvailable == 2 && !plan.Truncated,
            "assaults sort before field battles and the small fixture is untruncated");

        var archer = AttackPlanner.Build(AttackWorld(9302, UnitTypes.Archer), "seat-a");
        var archerPlan = AtkPlan(archer, "army-a-1");
        AtkCheck(archerPlan.AttackRange == 2 && AtkOption(archerPlan, ActionType.AttackArmy, 1, 3) != null
           && AtkOption(archerPlan, ActionType.AttackArmy, 1, 3).TargetTroops == 100,
            "an archer army also reaches the enemy army two hexes away");

        var empty = AttackPlanner.Build(e, "no-such-seat");
        AtkCheck(empty.SeatId == null && empty.Armies.Count == 0 && AttackPlanner.Build(null, "seat-a").Armies.Count == 0,
            "an unknown seat or a null engine yields an empty view and never throws");
        var b = AttackPlanner.Build(e, "seat-b");
        AtkCheck(b.SeatId == "seat-b" && b.Armies.All(p => p.ArmyId.StartsWith("army-b"))
           && b.Armies.SelectMany(p => p.Options).All(o => o.ArmyId.StartsWith("army-b")),
            "each seat's view contains only that seat's own armies");
    }

    // ---- brute force against the validator ---------------------------------

    private static void AttackBruteForceEquivalence()
    {
        CheckAttackEquivalence("infantry, weak city", () => AttackWorld(9311), "army-a-1");
        CheckAttackEquivalence("infantry, city too strong for 3:1",
            () => AttackWorld(9312, cityGarrison: 700), "army-a-1");
        CheckAttackEquivalence("archer range 2", () => AttackWorld(9313, UnitTypes.Archer), "army-a-1");
        CheckAttackEquivalence("siege waives the ratio",
            () => AttackWorld(9314, UnitTypes.Siege, cityGarrison: 5000), "army-a-1");
        CheckAttackEquivalence("cavalry with a defending army on the city hex", () =>
        {
            var w = AttackWorld(9315, UnitTypes.Cavalry);
            w.State.Armies.Add(new ArmyState
            {
                Id = "army-b-3", Name = "Gate", OwnerSeatId = "seat-b", Troops = 200,
                UnitType = UnitTypes.Infantry, X = 1, Z = 2,
            });
            return w;
        }, "army-a-1");
        CheckAttackEquivalence("only one order left (assault unaffordable)", () =>
        {
            var w = AttackWorld(9316);
            w.State.FindFaction("seat-a").CommandPoints = 1;
            return w;
        }, "army-a-1");
        CheckAttackEquivalence("allied owner still a legal (priced) target", () =>
        {
            var w = AttackWorld(9317);
            w.State.Diplomacy.Treaties.Add(new TreatyRecord
            {
                Type = TreatyType.Alliance, FactionA = "seat-a", FactionB = "seat-b", ExpirySeason = 9,
            });
            return w;
        }, "army-a-1");
    }

    /// <summary>For every hex, a fresh fixture: Submit(attack_*) accepted iff the hex is advertised.</summary>
    private static void CheckAttackEquivalence(string label, Func<GameEngine> make, string armyId)
    {
        var dims = make();
        int width = dims.State.Map.Width, height = dims.State.Map.Height;
        int fieldAccepted = 0, cityAccepted = 0, mismatches = 0;
        string first = null;
        for (int z = 0; z < height; z++)
        {
            for (int x = 0; x < width; x++)
            {
                var probe = make();
                var plan = AtkPlan(AttackPlanner.Build(probe, "seat-a", 20), armyId);
                bool listedField = AtkOption(plan, ActionType.AttackArmy, x, z) != null;
                bool listedCity = AtkOption(plan, ActionType.AttackCity, x, z) != null;
                bool truncated = plan.Truncated;

                var fieldWorld = make();
                bool fieldOk = fieldWorld.Submit(AttackOrder("seat-a", armyId, x, z)).Ok;
                var cityWorld = make();
                bool cityOk = cityWorld.Submit(AtkCityOrder("seat-a", armyId, x, z)).Ok;

                if (truncated || fieldOk != listedField || cityOk != listedCity)
                {
                    mismatches++;
                    if (first == null)
                        first = $"({x},{z}) field submit={fieldOk} listed={listedField}; city submit={cityOk} listed={listedCity}; truncated={truncated}";
                }
                if (fieldOk) fieldAccepted++;
                if (cityOk) cityAccepted++;
            }
        }
        AtkCheck(mismatches == 0,
            $"{label}: Submit accepts exactly the advertised hexes ({fieldAccepted} field, {cityAccepted} assault, {mismatches} mismatches"
            + (first == null ? ")" : $", first {first})"));
    }

    // ---- blocked states and costs ------------------------------------------

    private static void AttackBlockedAndCosts()
    {
        {
            var e = AttackWorld(9321);
            e.State.FindArmy("army-a-1").Morale = 0;
            var plan = AtkPlan(AttackPlanner.Build(e, "seat-a"), "army-a-1");
            var r = e.Submit(AttackOrder("seat-a", "army-a-1", 2, 1));
            AtkCheck(!plan.CanAttack && plan.Options.Count == 0 && plan.BlockedReason == "army morale is broken" && !r.Ok,
                "a broken-morale army advertises nothing and the engine agrees it cannot attack");
        }
        {
            var e = AttackWorld(9322);
            e.State.FindFaction("seat-a").CommandPoints = 0;
            var plan = AtkPlan(AttackPlanner.Build(e, "seat-a"), "army-a-1");
            var r = e.Submit(AttackOrder("seat-a", "army-a-1", 2, 1));
            AtkCheck(!plan.CanAttack && plan.Options.Count == 0 && plan.BlockedReason == "no orders left" && !r.Ok,
                "with no orders left nothing is advertised");
        }
        {
            var e = AttackWorld(9323);
            e.State.FindFaction("seat-a").CommandPoints = 1;
            var plan = AtkPlan(AttackPlanner.Build(e, "seat-a"), "army-a-1");
            AtkCheck(plan.CanAttack && AtkOption(plan, ActionType.AttackArmy, 2, 1) != null
               && AtkOption(plan, ActionType.AttackCity, 1, 2) == null,
                "one order left still offers the 1-order field battle but not the 2-order assault");
        }
        {
            var e = AttackWorld(9324);
            int before = e.State.FindFaction("seat-a").CommandPoints;
            e.Submit(AttackOrder("seat-a", "army-a-1", 2, 1));
            AtkCheck(before - e.State.FindFaction("seat-a").CommandPoints
                == BalanceConfig.CommandPointCost(ActionType.AttackArmy)
               && BalanceConfig.CommandPointCost(ActionType.AttackCity) == 2,
                "the advertised costs are the engine's own command-point prices");
        }
        {
            var e = AttackWorld(9325, UnitTypes.Siege, cityGarrison: 5000);
            var city = AtkOption(AtkPlan(AttackPlanner.Build(e, "seat-a"), "army-a-1"), ActionType.AttackCity, 1, 2);
            AtkCheck(city != null && city.AssaultRatioWaived && city.RequiredTroops == 0,
                "a siege army's assault is advertised with the ratio waived");
        }
        {
            var e = AttackWorld(9326);
            e.State.Diplomacy.Treaties.Add(new TreatyRecord
            {
                Type = TreatyType.Truce, FactionA = "seat-a", FactionB = "seat-b", ExpirySeason = 9,
            });
            var plan = AtkPlan(AttackPlanner.Build(e, "seat-a"), "army-a-1");
            var field = AtkOption(plan, ActionType.AttackArmy, 2, 1);
            AtkCheck(field != null && field.Note != null && field.Note.Contains("truce")
               && AtkOption(plan, ActionType.AttackCity, 1, 2).Note.Contains("truce"),
                "a truce the seat holds is named in the option note (typed warning, no numbers)");
        }
    }

    // ---- read-only, determinism, truncation --------------------------------

    private static void AttackReadOnlyAndTruncation()
    {
        var control = AttackWorld(9331);
        var probed = AttackWorld(9331);
        int logBefore = probed.Log.Records.Count;
        string json1 = AttackPlanner.Build(probed, "seat-a").ToJson();
        string json2 = AttackPlanner.Build(probed, "seat-a").ToJson();
        AtkCheck(json1 == json2 && probed.Log.Records.Count == logBefore,
            "building the view twice is deterministic and emits nothing");
        control.Submit(AttackOrder("seat-a", "army-a-1", 2, 1));
        probed.Submit(AttackOrder("seat-a", "army-a-1", 2, 1));
        AtkCheck(control.Log.ToJsonLines() == probed.Log.ToJsonLines()
           && control.State.FindFaction("seat-a").CommandPoints == probed.State.FindFaction("seat-a").CommandPoints,
            "planning never spends orders or rolls the RNG: the same battle resolves identically afterwards");

        var capped = AtkPlan(AttackPlanner.Build(AttackWorld(9332), "seat-a", 1), "army-a-1");
        AtkCheck(capped.Options.Count == 1 && capped.Truncated && capped.TotalAvailable == 2
           && capped.Options[0].Kind == ActionType.AttackCity,
            "a cap of one keeps the assault (stable prefix) and reports the truncation");
        AtkCheck(AttackPlanner.Build(AttackWorld(9333), "seat-a", -5).Armies[0].Options.Count == 1
           && AttackPlanner.Build(AttackWorld(9333), "seat-a", 9999).Armies[0].Options.Count == 2,
            "odd caps are clamped and never throw");
    }

    // ---- hidden information -------------------------------------------------

    private static void AttackHiddenWorlds()
    {
        var a = AttackWorld(9341, hiddenGarrison: 0, hiddenArmy: false);
        var b = AttackWorld(9341, hiddenGarrison: 9000, hiddenArmy: true);
        b.State.FindCity("city-hidden").Food = 12345;
        var viewA = AttackPlanner.Build(a, "seat-a");
        var viewB = AttackPlanner.Build(b, "seat-a");
        AtkCheck(viewA.ToJson() == viewB.ToJson(),
            "hidden cities and armies outside sight leave the attack view byte-identical");
        var obsA = a.Observe("seat-a");
        var obsB = b.Observe("seat-a");
        AtkCheck(ActionCatalogue.ToPromptText(ActionCatalogue.Build(obsA, viewA))
              == ActionCatalogue.ToPromptText(ActionCatalogue.Build(obsB, viewB)),
            "and the rendered candidate list is identical");
        AtkCheck(obsA.ToJson() == obsB.ToJson(), "and so is the observation itself");

        // Probing a hidden hex with a free rejected order must read the same in both worlds.
        string[] probes =
        {
            a.Submit(AttackOrder("seat-a", "army-a-1", 6, 4)).Error,
            a.Submit(AtkCityOrder("seat-a", "army-a-1", 7, 5)).Error,
        };
        string[] probesB =
        {
            b.Submit(AttackOrder("seat-a", "army-a-1", 6, 4)).Error,
            b.Submit(AtkCityOrder("seat-a", "army-a-1", 7, 5)).Error,
        };
        AtkCheck(probes[0] == probesB[0] && probes[1] == probesB[1],
            $"a rejected probe of a hidden army/city reads identically whether or not it exists ({probes[0]} / {probes[1]})");
        AtkCheck(probes.All(p => FeedbackPolicy.ForModel(p) == FeedbackPolicy.Generic),
            "and the model-facing feedback for both probes is the one generic category");

        // A tampered view naming a hidden target is dropped by the catalogue (fail closed).
        var forged = AttackPlanner.Build(a, "seat-a");
        var forgedPlan = AtkPlan(forged, "army-a-1");
        var list = forgedPlan.Options.ToList();
        list.Add(new AttackOption
        {
            ArmyId = "army-a-1", Kind = ActionType.AttackArmy, X = 6, Z = 4, CommandPointCost = 1,
            TargetArmyId = "army-b-hidden", TargetOwnerSeatId = "seat-b", TargetTroops = 4321,
            TargetUnitType = UnitTypes.Cavalry,
        });
        forgedPlan.Options = list;
        string text = ActionCatalogue.ToPromptText(ActionCatalogue.Build(b.Observe("seat-a"), forged));
        AtkCheck(!text.Contains("army-b-hidden") && !text.Contains("4321") && !text.Contains("param_a=6"),
            "an option for a target absent from the seat's observation never reaches the prompt text");
    }

    // ---- catalogue ------------------------------------------------------------

    private static void AttackCatalogueEntries()
    {
        var e = AttackWorld(9351);
        var obs = e.Observe("seat-a");
        var view = AttackPlanner.Build(e, "seat-a");
        var plain = ActionCatalogue.Build(obs);
        AtkCheck(ActionCatalogue.ToPromptText(ActionCatalogue.Build(obs, null)) == ActionCatalogue.ToPromptText(plain)
           && ActionCatalogue.ToPromptText(ActionCatalogue.Build(obs, new AttackView { SeatId = "seat-a", Armies = new List<ArmyAttackPlan>() }))
              == ActionCatalogue.ToPromptText(plain),
            "a null or empty attack view leaves the catalogue text byte-identical");
        AtkCheck(!ActionCatalogue.ToPromptText(plain).Contains("param_a"),
            "the default catalogue never prints coordinate fields");

        var withAttacks = ActionCatalogue.Build(obs, view);
        var atk = withAttacks.Where(o => o.Type == ActionType.AttackArmy || o.Type == ActionType.AttackCity).ToList();
        AtkCheck(atk.Count == 2 && withAttacks[withAttacks.Count - 1].Type == "end_turn"
           && withAttacks.IndexOf(atk[0]) > withAttacks.FindLastIndex(o => o.Type == ActionType.Relief),
            "attack entries follow the domestic blocks and end_turn stays last");
        var assault = atk.First(o => o.Type == ActionType.AttackCity);
        var battle = atk.First(o => o.Type == ActionType.AttackArmy);
        AtkCheck(assault.TargetId == "army-a-1" && assault.ParamA == 1 && assault.ParamB == 2
           && assault.CommandPointCost == 2 && assault.LikelyAffordable
           && assault.Label.Contains("City B") && assault.Label.Contains("garrison 300")
           && assault.Label.Contains("at least 900"),
            "the assault entry targets the own army, carries the hex and states the troop requirement");
        AtkCheck(battle.ParamA == 2 && battle.ParamB == 1 && battle.CommandPointCost == 1
           && battle.Label.Contains("troops 500") && battle.Label.Contains("Shu Vanguard"),
            "the field entry carries the observable enemy strength");
        string rendered = ActionCatalogue.ToPromptText(withAttacks);
        AtkCheck(rendered.Contains("type=attack_city target_id=army-a-1 param_a=1 param_b=2 cost=2")
           && rendered.Contains("type=attack_army target_id=army-a-1 param_a=2 param_b=1 cost=1"),
            "the rendered lines are directly copyable into an order");

        var other = AttackPlanner.Build(e, "seat-b");
        AtkCheck(ActionCatalogue.ToPromptText(ActionCatalogue.Build(obs, other)) == ActionCatalogue.ToPromptText(plain),
            "a view built for another seat is ignored entirely");
        AtkCheck(ActionCatalogue.Build(null, view).Count == 0, "a null observation still yields an empty list");
    }

    // ---- parser ----------------------------------------------------------------

    private static void AttackParserVocabulary()
    {
        const string atk = "{\"type\":\"attack_army\",\"target_id\":\"army-a-1\",\"param_a\":2,\"param_b\":1}";
        const string cty = "{\"type\":\"attack_city\",\"target_id\":\"army-a-1\",\"param_a\":1,\"param_b\":2,\"summary\":\"take it\"}";
        var off = LlmActionParser.Parse(atk);
        AtkCheck(!off.Ok && off.Error == "unsupported action type" && !LlmActionParser.Parse(cty).Ok,
            "by default the attack orders are an unsupported action type (vocabulary unchanged)");
        AtkCheck(!LlmActionParser.Parse(atk, false).Ok && LlmActionParser.SupportedTypes.Count == 9
           && !LlmActionParser.SupportedTypes.Contains(ActionType.AttackArmy)
           && !LlmActionParser.SupportedTypes.Contains(ActionType.AttackCity),
            "SupportedTypes still lists exactly the nine demo orders");

        var on = LlmActionParser.Parse(atk, true);
        AtkCheck(on.Ok && on.Action.Type == ActionType.AttackArmy && on.Action.TargetId == "army-a-1"
           && on.Action.ParamA == 2 && on.Action.ParamB == 1,
            "with the opt-in, attack_army parses to the same fixed shape as march");
        var city = LlmActionParser.Parse(cty, true);
        AtkCheck(city.Ok && city.Action.Type == ActionType.AttackCity && city.Action.Summary == "take it",
            "attack_city parses too");
        var cmd = LlmActionParser.ToCommand(on.Action);
        AtkCheck(cmd.Type == ActionType.AttackArmy && cmd.ActorSeatId == null && cmd.ActionId == null
           && cmd.ControllerType == null && cmd.ParamA == 2 && cmd.ParamB == 1,
            "the host-owned fields stay unset on the command");

        string[] bad =
        {
            "{\"type\":\"attack_army\",\"target_id\":\"army-a-1\",\"param_a\":2}",
            "{\"type\":\"attack_city\",\"param_a\":1,\"param_b\":2}",
            "{\"type\":\"attack_city\",\"target_id\":\"\",\"param_a\":1,\"param_b\":2}",
            "{\"type\":\"attack_army\",\"target_id\":\"army-a-1\",\"param_a\":-1,\"param_b\":2}",
            "{\"type\":\"attack_army\",\"target_id\":\"army-a-1\",\"param_a\":2,\"param_b\":256}",
            "{\"type\":\"attack_army\",\"target_id\":\"army-a-1\",\"param_a\":\"2\",\"param_b\":1}",
            "{\"type\":\"attack_army\",\"target_id\":\"army-a-1\",\"param_a\":2,\"param_b\":1,\"args\":{\"x\":\"1\"}}",
            "{\"type\":\"attack_army\",\"target_id\":\"army-a-1\",\"param_a\":2,\"param_b\":1,\"actor_seat_id\":\"seat-b\"}",
            "{\"type\":\"attack_army\",\"target_id\":\"army-a-1\",\"param_a\":2,\"param_b\":1,\"odds\":1}",
        };
        AtkCheck(bad.All(t => !LlmActionParser.Parse(t, true).Ok),
            "missing/non-integer/out-of-range coordinates, a missing target, args and host or unknown fields are all rejected");
        AtkCheck(LlmActionParser.Parse("{\"type\":\"attack_army\",\"target_id\":\"army-a-1\",\"param_a\":2,\"param_b\":1,\"actor_seat_id\":\"seat-b\"}", true).Error == "forbidden field",
            "a spoofed seat is named a forbidden field");
        AtkCheck(LlmActionParser.AttackTypes.Count == 2
           && LlmActionParser.Parse("{\"type\":\"declare_war\",\"target_id\":\"seat-b\"}", true).Error == "unsupported action type",
            "the opt-in widens the vocabulary by exactly the two attack orders");
    }

    // ---- config and prompt identity -------------------------------------------

    private static void AttackConfigAndPromptIdentity()
    {
        var def = new LlmConfig();
        string legacy = def.Redacted();
        AtkCheck(!def.EnableAttack && !legacy.Contains("attack"), "the attack flag defaults off and is absent from the diagnostic");
        string dir = LlmTempDir("atk-config");
        try
        {
            string path = System.IO.Path.Combine(dir, "llm.local.json");
            System.IO.File.WriteAllText(path, "{\"enable_attack\":true,\"api_key\":\"sk-secret-1234\"}");
            var cfg = LlmConfig.LoadFromFile(path);
            AtkCheck(cfg.EnableAttack && cfg.Redacted().EndsWith("; attack=true") && !cfg.Redacted().Contains("sk-secret"),
                "enable_attack loads from the config file and the redacted diagnostic never shows the key");
            System.IO.File.WriteAllText(path, "{\"enable_attack\":\"yes\"}");
            AtkCheck(!LlmConfig.LoadFromFile(path).EnableAttack, "a non-boolean value leaves the flag off");
        }
        finally { LlmDelete(dir); }

        PromptBuilder.ResetPromptVersion();
        AtkCheck(PromptBuilder.SystemPromptFor(true, false, null, false) == PromptBuilder.SystemPromptFor(true)
           && PromptBuilder.SystemPromptFor(true, true, null, false) == PromptBuilder.SystemPromptFor(true, true)
           && PromptBuilder.SchemaHashFor(true, false, null, false) == PromptBuilder.SchemaHash()
           && PromptBuilder.VersionFor(true, false, null, false) == PromptBuilder.PromptVersion,
            "with the flag off the system prompt, hash and version are byte-identical to before");
        string system = PromptBuilder.SystemPromptFor(true, false, null, true);
        AtkCheck(system.StartsWith(PromptBuilder.SystemPromptFor(true))
           && system.Contains("ATTACK AND CAPTURE") && system.Contains("at least 3 times the defending troops")
           && system.Contains("costs 2 command points") && system.Contains("Neutral cities and unowned watch towers")
           && PromptBuilder.VersionFor(true, false, null, true) == PromptBuilder.AttackPromptVersion
           && PromptBuilder.SchemaHashFor(true, false, null, true) != PromptBuilder.SchemaHash(),
            "enabling it appends the engine-derived attack rules and gets its own version and hash");
        AtkCheck(!system.Contains("odds") || system.Contains("not guaranteed"),
            "the rules promise no combat odds");
        AtkCheck(PromptBuilder.SystemPromptFor(false, false, null, true) == PromptBuilder.SystemPromptFor(false)
           && PromptBuilder.VersionFor(false, false, null, true) == PromptBuilder.DomesticPromptVersion,
            "a non-planning (domestic) prompt never gains attack text");
    }
}
