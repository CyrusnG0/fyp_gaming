using System;
using System.Collections.Generic;
using LuanShi.Engine;

internal static partial class Harness
{
    private static void RunUnitTypes()
    {
        // ------------------------------------------------ P14 unit types (兵種)
        Section("P14 unit types (design §5.4, ADR-019): recruit, upkeep, movement, 相剋, sieges");

        Check(BalanceConfig.MarchBudget(2, UnitTypes.Infantry) == 6
           && BalanceConfig.MarchBudget(2, UnitTypes.Archer) == 6
           && BalanceConfig.MarchBudget(2, UnitTypes.Cavalry) == 8
           && BalanceConfig.MarchBudget(2, UnitTypes.Siege) == 3,
              "summer march budgets: 步/弓 6, 騎 8, 械 3");
        Check(BalanceConfig.MarchBudget(4, UnitTypes.Infantry) == 4
           && BalanceConfig.MarchBudget(4, UnitTypes.Cavalry) == 5
           && BalanceConfig.MarchBudget(4, UnitTypes.Siege) == 2,
              "winter x0.7 on top: 步 4, 騎 5, 械 2");

        // ---- recruit: per-type gold, garrison type, event, backward compatibility
        {
            var e = Build(201, 1, morale: 100, food: 5000, garrison: 0);
            SetLevel(e, "seat-a", ResearchLine.Military, 3);
            var city = e.State.FindCity("city-a");
            city.Gold = 1000;
            var r = e.Submit(new ActionCommand { Type = ActionType.Recruit, ActorSeatId = "seat-a", TargetId = "city-a" });
            var evt = Find(e.Log, "recruit");
            Check(r.Ok && city.Gold == 900 && city.Garrison == 1000 && city.GarrisonType == UnitTypes.Infantry,
                  $"recruit without args.unit_type = infantry, 100 gold (gold {city.Gold}, type {city.GarrisonType})");
            Check(evt != null && evt.Payload["unit_type"] == "infantry", "recruit event carries unit_type");
        }
        foreach (var spec in new[] { (UnitTypes.Archer, 150), (UnitTypes.Cavalry, 300), (UnitTypes.Siege, 500) })
        {
            var e = Build(202, 1, morale: 100, food: 5000, garrison: 0);
            SetLevel(e, "seat-a", ResearchLine.Military, 3);
            var city = e.State.FindCity("city-a");
            city.Gold = 1000;
            var r = e.Submit(RecruitOrder(e, "seat-a", "city-a", spec.Item1));
            Check(r.Ok && city.Gold == 1000 - spec.Item2 && city.GarrisonType == spec.Item1,
                  $"{spec.Item1} costs {spec.Item2} gold (gold {city.Gold}, type {city.GarrisonType})");
        }
        {
            var e = Build(203, 1, morale: 100, food: 5000, garrison: 500);
            SetLevel(e, "seat-a", ResearchLine.Military, 3);
            var city = e.State.FindCity("city-a");
            city.GarrisonType = UnitTypes.Infantry;
            city.Gold = 1000;
            var r = e.Submit(RecruitOrder(e, "seat-a", "city-a", UnitTypes.Archer));
            Check(!r.Ok && r.Error.Contains("garrison is infantry"),
                  $"a non-empty garrison refuses another type ({r.Error})");
            Check(city.Garrison == 500 && city.Gold == 1000, "and the refusal is atomic");
        }
        {
            var e = Build(204, 1, morale: 100, food: 5000, garrison: 0);
            var city = e.State.FindCity("city-a");
            city.Gold = 2000;
            SetLevel(e, "seat-a", ResearchLine.Military, 1);
            var cav = e.Submit(RecruitOrder(e, "seat-a", "city-a", UnitTypes.Cavalry));
            Check(!cav.Ok && cav.Error.Contains("needs Military 2"), $"cavalry needs 兵法 2 ({cav.Error})");
            var siege = e.Submit(RecruitOrder(e, "seat-a", "city-a", UnitTypes.Siege));
            Check(!siege.Ok && siege.Error.Contains("needs Military 3"), $"siege needs 兵法 3 ({siege.Error})");
            SetLevel(e, "seat-a", ResearchLine.Military, 2);
            var cavOk = e.Submit(RecruitOrder(e, "seat-a", "city-a", UnitTypes.Cavalry));
            Check(cavOk.Ok && city.GarrisonType == UnitTypes.Cavalry, "兵法 2 unlocks cavalry");
        }
        {
            var e = Build(205, 1, morale: 100, food: 5000, garrison: 0);
            SetLevel(e, "seat-a", ResearchLine.Military, 3);
            e.State.FindCity("city-a").Gold = 2000;
            var r = e.Submit(RecruitOrder(e, "seat-a", "city-a", UnitTypes.Siege));
            Check(r.Ok && e.State.FindCity("city-a").GarrisonType == UnitTypes.Siege, "兵法 3 unlocks siege");
        }

        // ---- upkeep: per-type garrison feeding and field supply
        foreach (var spec in new[] { (UnitTypes.Infantry, 900), (UnitTypes.Archer, 900), (UnitTypes.Cavalry, 800), (UnitTypes.Siege, 950) })
        {
            var e = Build(206, 2, morale: 100, food: 1000, garrison: 1000);
            e.State.FindCity("city-a").GarrisonType = spec.Item1;
            e.EndSeason();
            Check(e.State.FindCity("city-a").Food == spec.Item2,
                  $"{spec.Item1} garrison upkeep leaves {spec.Item2} food (got {e.State.FindCity("city-a").Food})");
        }
        {
            var inf = Build(207, 1, morale: 100, food: 0, population: 1000);
            var infArmy = AddArmy(inf, "seat-a", 5, 1000);
            infArmy.UnitType = UnitTypes.Infantry;
            inf.EndSeason();
            Check(CountEvents(inf.Log, "desertion") == 0 && infArmy.Troops == 1000,
                  "infantry eats 100 from a 120-food pool");

            var cav = Build(207, 1, morale: 100, food: 0, population: 1000);
            var cavArmy = AddArmy(cav, "seat-a", 5, 1000);
            cavArmy.UnitType = UnitTypes.Cavalry;
            cav.EndSeason();
            var d = Find(cav.Log, "desertion");
            Check(d != null && cavArmy.Troops == 900 && d.Payload["cause"] == "no_food",
                  $"cavalry needs 200 from the same pool and deserts ({cavArmy.Troops})");
        }

        // ---- movement: type-aware budgets accepted by the validator
        {
            var e = Build(208, 2, morale: 100, food: 5000);
            var army = AddArmy(e, "seat-a", 0, 1000);
            army.UnitType = UnitTypes.Cavalry;
            var r = e.Submit(MarchOrder(e, "seat-a", army.Id, 8, 0));
            Check(r.Ok && army.X == 8, $"cavalry marches 8 in summer ({r.Error})");
        }
        {
            var e = Build(209, 2, morale: 100, food: 5000);
            var army = AddArmy(e, "seat-a", 0, 1000);
            army.UnitType = UnitTypes.Siege;
            var far = e.Submit(MarchOrder(e, "seat-a", army.Id, 4, 0));
            Check(!far.Ok && far.Error.Contains("max cost 3"), $"siege is limited to 3 ({far.Error})");
            var near = e.Submit(MarchOrder(e, "seat-a", army.Id, 3, 0));
            Check(near.Ok && army.X == 3, $"siege reaches 3 ({near.Error})");
        }
        {
            var e = Build(210, 4, morale: 100, food: 5000);
            var army = AddArmy(e, "seat-a", 0, 1000);
            army.UnitType = UnitTypes.Cavalry;
            var far = e.Submit(MarchOrder(e, "seat-a", army.Id, 6, 0));
            Check(!far.Ok && far.Error.Contains("max cost 5"), $"winter cavalry is limited to 5 ({far.Error})");
            var near = e.Submit(MarchOrder(e, "seat-a", army.Id, 5, 0));
            Check(near.Ok && army.X == 5, $"winter cavalry reaches 5 ({near.Error})");
        }

        // ---- 相剋: first-round loss ratios in field battles
        {
            var baseline = FieldRoundOne(301, UnitTypes.Infantry, UnitTypes.Infantry);
            var infVsArcher = FieldRoundOne(301, UnitTypes.Infantry, UnitTypes.Archer);
            var cavVsInf = FieldRoundOne(301, UnitTypes.Cavalry, UnitTypes.Infantry);
            var archVsCav = FieldRoundOne(301, UnitTypes.Archer, UnitTypes.Cavalry);
            var siegeVsInf = FieldRoundOne(301, UnitTypes.Siege, UnitTypes.Infantry);
            var infVsSiege = FieldRoundOne(301, UnitTypes.Infantry, UnitTypes.Siege);

            Check(CloseTo(infVsArcher.AttackerLoss, baseline.AttackerLoss, 0.8),
                  $"infantry beats archer: the archer deals 0.8x first-round losses ({infVsArcher.AttackerLoss}/{baseline.AttackerLoss})");
            // 耐打 (P15): the baseline defender is infantry and takes ×0.85, so an archer defender's
            // 1.3x shows up as 1.3/0.85 against that baseline.
            Check(CloseTo(infVsArcher.DefenderLoss, baseline.DefenderLoss, 1.3 / 0.85),
                  $"infantry beats archer: the archer takes 1.3x (vs the 0.85 infantry baseline) ({infVsArcher.DefenderLoss}/{baseline.DefenderLoss})");
            Check(CloseTo(cavVsInf.AttackerLoss, baseline.AttackerLoss, 0.8 / 0.85)
               && CloseTo(cavVsInf.DefenderLoss, baseline.DefenderLoss, 1.3),
                  $"cavalry beats infantry (attacker {cavVsInf.AttackerLoss} vs baseline {baseline.AttackerLoss}, defender {cavVsInf.DefenderLoss} vs baseline {baseline.DefenderLoss})");
            Check(CloseTo(archVsCav.AttackerLoss, baseline.AttackerLoss, 0.8 / 0.85)
               && CloseTo(archVsCav.DefenderLoss, baseline.DefenderLoss, 1.3 / 0.85),
                  $"archer beats cavalry (attacker {archVsCav.AttackerLoss}, defender {archVsCav.DefenderLoss})");
            Check(CloseTo(siegeVsInf.DefenderLoss, baseline.DefenderLoss, 0.5)
               && CloseTo(siegeVsInf.AttackerLoss, baseline.AttackerLoss, 1.0 / 0.85),
                  $"siege deals 0.5x in the field and takes normal losses ({siegeVsInf.AttackerLoss}/{siegeVsInf.DefenderLoss})");
            Check(CloseTo(infVsSiege.AttackerLoss, baseline.AttackerLoss, 0.5),
                  $"a siege defender deals 0.5x ({infVsSiege.AttackerLoss}/{baseline.AttackerLoss})");
        }

        // ---- archer garrison defence + siege waiver
        {
            int infLosses = SiegeFirstRoundAttackerLosses(302, UnitTypes.Infantry);
            int archLosses = SiegeFirstRoundAttackerLosses(302, UnitTypes.Archer);
            Check(infLosses > 0 && CloseTo(archLosses, infLosses, 1.25),
                  $"an archer garrison raises first-round attacker losses by 1.25x ({infLosses} → {archLosses})");
        }
        {
            var e = SiegeEngine(defendingArmy: false);
            e.State.FindCity("city-b").Garrison = 5000;
            var army = e.State.FindArmy("army-a");
            army.Troops = 1000;
            army.UnitType = UnitTypes.Siege;
            var r = e.Submit(new ActionCommand { Type = ActionType.AttackCity, ActorSeatId = "seat-a", TargetId = army.Id, ParamA = 2, ParamB = 0 });
            Check(r.Ok && Find(e.Log, "battle_siege") != null, $"a siege army waives the 3:1 gate ({r.Error})");

            var e2 = SiegeEngine(defendingArmy: false);
            e2.State.FindCity("city-b").Garrison = 5000;
            var army2 = e2.State.FindArmy("army-a");
            army2.Troops = 1000;
            army2.UnitType = UnitTypes.Infantry;
            var r2 = e2.Submit(new ActionCommand { Type = ActionType.AttackCity, ActorSeatId = "seat-a", TargetId = army2.Id, ParamA = 2, ParamB = 0 });
            Check(!r2.Ok && r2.Error.Contains("3:1"), $"infantry still needs 3:1 ({r2.Error})");
        }
        {
            var state = SiegeScenario(defendingArmy: false);
            state.Cities.Find(c => c.Id == "city-b").Garrison = 5000;
            var army = state.FindArmy("army-a");
            army.Troops = 1000;
            army.UnitType = UnitTypes.Siege;
            Check(PlanContains(state, "seat-a", ActionType.AttackCity),
                  "the scripted seat plans a siege-army assault without 3:1");
            army.UnitType = UnitTypes.Infantry;
            Check(!PlanContains(state, "seat-a", ActionType.AttackCity),
                  "and refuses the same assault with infantry");
        }

        // ---- one type per army/garrison: transfers, formation, movement state
        {
            var e = GarrisonScenario();
            var army = e.State.FindArmy("army-a");
            army.X = 0; army.Z = 0; army.UnitType = UnitTypes.Cavalry;
            var r = e.Submit(ConvertOrder(e, "seat-a", "city-a", 500));
            Check(!r.Ok && r.Error.Contains("unit types differ"), $"convert merge requires the same type ({r.Error})");
            Check(e.State.FindCity("city-a").Garrison == 1000 && army.Troops == 2000, "and stays atomic");
        }
        {
            var e = GarrisonScenario();
            var army = e.State.FindArmy("army-a");
            army.X = 0; army.Z = 0; army.UnitType = UnitTypes.Cavalry;
            var r = e.Submit(GarrisonOrder(e, "seat-a", army.Id, 500));
            Check(!r.Ok && r.Error.Contains("unit types differ"), $"garrisoning requires the same type ({r.Error})");
            Check(army.Troops == 2000 && e.State.FindCity("city-a").Garrison == 1000, "and stays atomic");
        }
        {
            var e = GarrisonScenario();
            var city = e.State.FindCity("city-a");
            city.Garrison = 0;
            city.GarrisonType = UnitTypes.Archer;
            var army = e.State.FindArmy("army-a");
            army.X = 0; army.Z = 0; army.UnitType = UnitTypes.Cavalry;
            var r = e.Submit(GarrisonOrder(e, "seat-a", army.Id, 500));
            Check(r.Ok && city.GarrisonType == UnitTypes.Cavalry,
                  $"an empty garrison adopts the army's type ({city.GarrisonType})");
        }
        {
            var e = GarrisonScenario();
            e.State.FindCity("city-a").GarrisonType = UnitTypes.Cavalry;
            var r = e.Submit(ConvertOrder(e, "seat-a", "city-a", 500));
            var evt = Find(e.Log, "garrison_converted");
            var formed = evt != null ? e.State.FindArmy(evt.Payload["army_id"]) : null;
            Check(r.Ok && formed != null && formed.UnitType == UnitTypes.Cavalry
               && evt.Payload["unit_type"] == "cavalry",
                  $"a formed army inherits the garrison's type ({formed?.UnitType})");
        }
        {
            var e = GarrisonScenario();
            var army = e.State.FindArmy("army-a");
            army.UnitType = UnitTypes.Cavalry;
            e.State.FindCity("city-b").GarrisonType = UnitTypes.Cavalry;
            e.Submit(MarchOrder(e, "seat-a", army.Id, 2, 0));
            e.Submit(GarrisonOrder(e, "seat-a", army.Id, 2000));
            var cityB = e.State.FindCity("city-b");
            Check(cityB.GarrisonType == UnitTypes.Cavalry && cityB.GarrisonMarchedThisSeason,
                  "a cavalry round-trip keeps the type and movement state");
            var convert = e.Submit(ConvertOrder(e, "seat-a", "city-b", 2000));
            var evt = Find(e.Log, "garrison_converted");
            var rearmed = evt != null ? e.State.FindArmy(evt.Payload["army_id"]) : null;
            Check(convert.Ok && rearmed != null && rearmed.UnitType == UnitTypes.Cavalry
               && rearmed.MarchedThisSeason,
                  "the re-formed cavalry army is still movement-spent");
        }

        // ---- observation JSON
        {
            var e = Build(211, 1, morale: 100, food: 5000);
            e.State.FindCity("city-a").GarrisonType = UnitTypes.Archer;
            var army = AddArmy(e, "seat-a", 1, 1000);
            army.UnitType = UnitTypes.Cavalry;
            var json = e.Observe("seat-a").ToJson();
            Check(json.Contains("\"unit_type\":\"cavalry\""), "observation JSON carries an army's unit_type");
            Check(json.Contains("\"garrison_type\":\"archer\""), "observation JSON carries a city's garrison_type");

            e.State.Factions.Add(new FactionState { SeatId = "seat-b", Name = "Shu", Controller = ControllerType.ScriptedAi });
            e.State.Armies.Add(new ArmyState
            {
                Id = "army-b", Name = "Enemy", OwnerSeatId = "seat-b", Troops = 500,
                UnitType = UnitTypes.Siege, X = 2, Z = 0,
            });
            var foreign = e.Observe("seat-a");
            Check(foreign.FindArmy("army-b") != null && foreign.ToJson().Contains("\"unit_type\":\"siege\""),
                  "a foreign army in sight exposes its unit_type");
        }
    }

    private static ActionCommand RecruitOrder(GameEngine e, string seat, string cityId, string unitType)
        => new ActionCommand
        {
            Type = ActionType.Recruit, ActorSeatId = seat, TargetId = cityId,
            Args = new Dictionary<string, string> { { "unit_type", unitType } },
        };
}
