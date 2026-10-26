using System;
using System.Collections.Generic;
using LuanShi.Engine;

internal static partial class Harness
{
    private static void RunGarrisonTransfers()
    {
        // ------------------------------------------------ PR3 garrison transfers / movement / fog / siege
        Section("PR3 garrison transfers: formation/merge, movement state, atomic rejects, fog, scripted siege");

        Check(BalanceConfig.FogWatchTowerRange == 5,
              $"FogWatchTowerRange restored to 5 ({BalanceConfig.FogWatchTowerRange})");
        Check(BalanceConfig.CityBaseFoodYield == 200,
              $"CityBaseFoodYield restored to 200 ({BalanceConfig.CityBaseFoodYield})");
        Check(BalanceConfig.StartFood == 500,
              $"StartFood restored to 500 ({BalanceConfig.StartFood})");
        Check(BalanceConfig.SpringFoodYieldMult == 0.7f,
              $"SpringFoodYieldMult restored to 0.7 ({BalanceConfig.SpringFoodYieldMult})");

        // ---- formation: a clean garrison forms a moveable army; the event says "formed"
        {
            var e = GarrisonScenario();
            var city = e.State.FindCity("city-a");
            var r = e.Submit(ConvertOrder(e, "seat-a", "city-a", 400));
            var evt = Find(e.Log, "garrison_converted");
            var formed = evt != null ? e.State.FindArmy(evt.Payload["army_id"]) : null;
            Check(r.Ok && formed != null && formed.Troops == 400, $"convert_garrison forms an army ({r.Error})");
            Check(evt != null && evt.Payload["mode"] == "formed", "the domain event records mode=formed");
            Check(formed != null && !formed.MarchedThisSeason, "an unmoved garrison forms a moveable army");
            Check(city.Garrison == 600, $"the garrison loses the converted troops ({city.Garrison})");
            var march = e.Submit(MarchOrder(e, "seat-a", formed.Id, 1, 0));
            Check(march.Ok && formed.X == 1, $"the new army may march ({march.Error})");
        }

        // ---- the reported exploit: marched -> full garrison -> convert -> second march
        {
            var e = GarrisonScenario();
            var army = e.State.FindArmy("army-a");
            var cityB = e.State.FindCity("city-b");
            var march = e.Submit(MarchOrder(e, "seat-a", army.Id, 2, 0));
            Check(march.Ok && army.MarchedThisSeason, $"the army marches to the second city ({march.Error})");
            var garrison = e.Submit(GarrisonOrder(e, "seat-a", army.Id, 2000));
            Check(garrison.Ok && e.State.FindArmy(army.Id) == null, "a full garrisoning removes the army");
            Check(cityB.Garrison == 2000, $"the garrison takes the troops ({cityB.Garrison})");
            Check(cityB.GarrisonMarchedThisSeason, "the city records that the garrison has already moved");

            int transfer = IndexOf(e, "army_garrisoned"), destroyed = IndexOf(e, "army_destroyed");
            Check(transfer >= 0 && destroyed > transfer,
                  $"army_garrisoned is logged before army_destroyed ({transfer} < {destroyed})");

            var convert = e.Submit(ConvertOrder(e, "seat-a", "city-b", 2000));
            var evt = Find(e.Log, "garrison_converted");
            var rearmed = evt != null ? e.State.FindArmy(evt.Payload["army_id"]) : null;
            Check(convert.Ok && rearmed != null && rearmed.Troops == 2000, $"the garrison converts back into an army ({convert.Error})");
            Check(rearmed != null && rearmed.MarchedThisSeason,
                  "the re-formed army keeps the movement state (no fresh mover)");
            int x = rearmed != null ? rearmed.X : -1;
            var again = e.Submit(MarchOrder(e, "seat-a", rearmed.Id, 3, 0));
            Check(!again.Ok && again.Error.Contains("already marched this season"),
                  $"the march-reset exploit is closed ({again.Error})");
            Check(rearmed != null && rearmed.X == x, "the rejected march did not move the army");
        }

        // ---- partial garrison then merge back: the moved army stays moved
        {
            var e = GarrisonScenario();
            var army = e.State.FindArmy("army-a");
            e.Submit(MarchOrder(e, "seat-a", army.Id, 2, 0));
            var cityB = e.State.FindCity("city-b");
            var partial = e.Submit(GarrisonOrder(e, "seat-a", army.Id, 500));
            Check(partial.Ok && army.Troops == 1500 && cityB.Garrison == 500,
                  $"a partial garrisoning leaves the army in the field ({army.Troops}/{cityB.Garrison})");
            Check(army.MarchedThisSeason && cityB.GarrisonMarchedThisSeason, "both halves remember the move");
            var merge = e.Submit(ConvertOrder(e, "seat-a", "city-b", 500));
            Check(merge.Ok && army.Troops == 2000, $"the garrison merges back ({army.Troops}, {merge.Error})");
            Check(army.MarchedThisSeason, "the merged army is still movement-spent");
            var again = e.Submit(MarchOrder(e, "seat-a", army.Id, 3, 0));
            Check(!again.Ok && again.Error.Contains("already marched this season"),
                  $"a merge cannot launder a move ({again.Error})");
        }

        // ---- merging a movement-spent garrison into an unmoved army locks it too
        {
            var e = GarrisonScenario();
            var army = e.State.FindArmy("army-a");
            e.Submit(MarchOrder(e, "seat-a", army.Id, 2, 0));
            e.Submit(GarrisonOrder(e, "seat-a", army.Id, 2000));          // full: city-b is now movement-spent
            var fresh = new ArmyState { Id = "army-fresh", Name = "Fresh", OwnerSeatId = "seat-a", Troops = 300, X = 2, Z = 0 };
            e.State.Armies.Add(fresh);
            var merge = e.Submit(ConvertOrder(e, "seat-a", "city-b", 1000));
            Check(merge.Ok && fresh.Troops == 1300, $"the conversion merges into the unmoved army ({fresh.Troops}, {merge.Error})");
            Check(fresh.MarchedThisSeason, "the merge adopts the garrison's movement state");
            var again = e.Submit(MarchOrder(e, "seat-a", fresh.Id, 3, 0));
            Check(!again.Ok && again.Error.Contains("already marched this season"),
                  $"the merged army cannot march ({again.Error})");
        }

        // ---- a clean merge stays moveable (the fix must not over-lock ordinary play)
        {
            var e = GarrisonScenario();
            var army = e.State.FindArmy("army-a");
            army.X = 2; army.Z = 0;                                        // on city-b, unmoved
            e.State.FindCity("city-b").Garrison = 500;
            var merge = e.Submit(ConvertOrder(e, "seat-a", "city-b", 500));
            Check(merge.Ok && army.Troops == 2500, $"a clean garrison merges into the army ({army.Troops}, {merge.Error})");
            Check(!army.MarchedThisSeason, "a clean merge does not lock the army");
            var march = e.Submit(MarchOrder(e, "seat-a", army.Id, 3, 0));
            Check(march.Ok, $"the clean merged army can still march ({march.Error})");
        }

        // ---- the rollover clears the movement state with the army flags
        {
            var e = GarrisonScenario();
            var army = e.State.FindArmy("army-a");
            e.Submit(MarchOrder(e, "seat-a", army.Id, 2, 0));
            e.Submit(GarrisonOrder(e, "seat-a", army.Id, 2000));
            Check(e.State.FindCity("city-b").GarrisonMarchedThisSeason, "the flag is set before the rollover");
            e.EndSeason();
            e.BeginSeason();
            Check(!e.State.FindCity("city-b").GarrisonMarchedThisSeason, "the rollover clears the garrison flag");
            var convert = e.Submit(ConvertOrder(e, "seat-a", "city-b", 2000));
            var evt = Find(e.Log, "garrison_converted");
            var rearmed = evt != null ? e.State.FindArmy(evt.Payload["army_id"]) : null;
            Check(convert.Ok && rearmed != null && !rearmed.MarchedThisSeason,
                  "the next season forms a fresh, moveable army");
            Check(e.Submit(MarchOrder(e, "seat-a", rearmed.Id, 3, 0)).Ok, "and it can march");
        }

        // ---- refused transfers are atomic (no mutate-then-rollback)
        {
            var e = GarrisonScenario();
            var city = e.State.FindCity("city-a");
            e.State.Armies.Add(new ArmyState { Id = "army-enemy", Name = "Enemy", OwnerSeatId = "seat-b", Troops = 400, X = 0, Z = 0 });
            int armies = e.State.Armies.Count;
            var r = e.Submit(ConvertOrder(e, "seat-a", "city-a", 300));
            Check(!r.Ok && r.Error.Contains("occupied by another faction"), $"an occupied city refuses conversion ({r.Error})");
            Check(city.Garrison == 1000, $"the refused conversion leaves the garrison untouched ({city.Garrison})");
            Check(e.State.Armies.Count == armies, "and forms no army");

            var tooMany = e.Submit(ConvertOrder(e, "seat-a", "city-a", 1001));
            Check(!tooMany.Ok && city.Garrison == 1000, "an over-large conversion is refused atomically");
        }
        {
            var e = GarrisonScenario();
            var army = e.State.FindArmy("army-a");
            army.X = 10; army.Z = 0;                                       // city-c, owned by seat-c
            var foreign = e.State.FindCity("city-c");
            int troops = army.Troops, garrison = foreign.Garrison;
            var r = e.Submit(GarrisonOrder(e, "seat-a", army.Id, 500));
            Check(!r.Ok && r.Error.Contains("one of your cities"), $"garrisoning requires an own city ({r.Error})");
            Check(!r.Error.Contains("not allied"), "the misleading 'not allied' wording is gone");
            Check(army.Troops == troops && foreign.Garrison == garrison, "the refused order is atomic");
        }
        {
            var e = GarrisonScenario();
            var army = e.State.FindArmy("army-a");
            army.X = 0; army.Z = 0;                                        // on city-a
            var city = e.State.FindCity("city-a");
            int garrison = city.Garrison;
            var r = e.Submit(GarrisonOrder(e, "seat-a", army.Id, army.Troops + 1));
            Check(!r.Ok && r.Error.Contains("between 1 and the army's troops"), $"over-garrisoning is refused ({r.Error})");
            Check(army.Troops == 2000 && city.Garrison == garrison, "and it changes nothing");
        }

        // ---- generic action records for garrison transfers are owner-only (fog privacy)
        {
            var e = GarrisonScenario();
            var convert = e.Submit(ConvertOrder(e, "seat-a", "city-a", 700));
            var action = LastAction(e, ActionType.ConvertGarrison);
            Check(convert.Ok, $"the conversion succeeds ({convert.Error})");
            Check(action != null && action.Visibility == "owner_only", "the convert_garrison action record is owner-only");
            Check(action != null && action.Payload["param_a"] == "700", "it still carries the troop count for the owner");
            Check(e.SeesEvent("seat-a", action), "the owner sees their own order");
            Check(!e.SeesEvent("seat-c", action), "a seat with no sight does not see the troop count");
            var domain = Find(e.Log, "garrison_converted");
            Check(domain != null && domain.Visibility == "owner_and_observers",
                  "the domain event keeps the city context at owner_and_observers");
            Check(!e.SeesEvent("seat-c", domain), "the out-of-sight seat does not see the domain event either");
            Check(e.SeesEvent("seat-a", domain), "the owner does");

            bool leaked = false;
            foreach (var rec in e.Observe("seat-c").Events)
                if (rec.EventType == "garrison_converted"
                    || (rec.EventType == "action" && rec.Payload["action"] == ActionType.ConvertGarrison))
                    leaked = true;
            Check(!leaked, "seat C's filtered log contains nothing about the transfer");
        }
        {
            var e = GarrisonScenario();
            var army = e.State.FindArmy("army-a");
            army.X = 0; army.Z = 0;                                        // on city-a
            var r = e.Submit(GarrisonOrder(e, "seat-a", army.Id, 500));
            var action = LastAction(e, ActionType.GarrisonArmy);
            Check(r.Ok, $"garrison_army succeeds ({r.Error})");
            Check(action != null && action.Visibility == "owner_only", "the garrison_army action record is owner-only");
            Check(!e.SeesEvent("seat-c", action), "a seat with no sight does not see it");
            var domain = Find(e.Log, "army_garrisoned");
            Check(domain != null && !e.SeesEvent("seat-c", domain), "the domain event stays out of sight too");
        }
        {
            var e = GarrisonScenario();
            var reclaim = e.Submit(new ActionCommand { Type = ActionType.Reclaim, ActorSeatId = "seat-a", TargetId = "city-a" });
            var action = LastAction(e, ActionType.Reclaim);
            Check(reclaim.Ok && action != null && action.Visibility == "public",
                  "ordinary action records remain public");
            Check(e.SeesEvent("seat-c", action), "and a third seat still sees them");
        }

        // ---- the scripted baseline judges a siege on the engine's effective garrison
        {
            var defended = SiegeScenario(defendingArmy: true);
            Check(!PlanContains(defended, "seat-a", ActionType.AttackCity),
                  "the baseline does not plan an assault the engine would refuse (defending army counted)");
            var bare = SiegeScenario(defendingArmy: false);
            Check(PlanContains(bare, "seat-a", ActionType.AttackCity),
                  "and it still plans the assault when the garrison really is small");
        }
    }

    // ---- PR3 helpers --------------------------------------------------------

    /// <summary>Three seats on a corridor: A owns city-a (garrison 1000) and city-b; army-a sits at (1,0).</summary>
    private static GameEngine GarrisonScenario()
    {
        var e = new GameEngine(71);
        e.State.Map = Corridor(12);
        e.State.Factions.Add(new FactionState { SeatId = "seat-a", Name = "Wei", Controller = ControllerType.Human });
        e.State.Factions.Add(new FactionState { SeatId = "seat-b", Name = "Shu", Controller = ControllerType.ScriptedAi });
        e.State.Factions.Add(new FactionState { SeatId = "seat-c", Name = "Wu", Controller = ControllerType.ScriptedAi });
        e.State.Cities.Add(new CityState
        {
            Id = "city-a", Name = "City A", X = 0, Z = 0, OwnerSeatId = "seat-a",
            Population = 10000, Food = 5000, Gold = 1000, Garrison = 1000, Morale = 100,
        });
        e.State.Cities.Add(new CityState
        {
            Id = "city-b", Name = "City B", X = 2, Z = 0, OwnerSeatId = "seat-a",
            Population = 10000, Food = 5000, Gold = 1000, Garrison = 0, Morale = 100,
        });
        e.State.Cities.Add(new CityState
        {
            Id = "city-c", Name = "City C", X = 10, Z = 0, OwnerSeatId = "seat-c",
            Population = 10000, Food = 5000, Gold = 1000, Garrison = 100, Morale = 100,
        });
        e.State.Armies.Add(new ArmyState
        {
            Id = "army-a", Name = "Army A", OwnerSeatId = "seat-a", Troops = 2000, X = 3, Z = 0,
        });
        e.BeginSeason();
        e.State.Phase = "player_orders";
        return e;
    }

    private static ActionCommand ConvertOrder(GameEngine e, string seat, string cityId, int troops)
        => new ActionCommand
        {
            Type = ActionType.ConvertGarrison, ActorSeatId = seat, TargetId = cityId, ParamA = troops,
        };

    private static ActionCommand GarrisonOrder(GameEngine e, string seat, string armyId, int troops)
        => new ActionCommand
        {
            Type = ActionType.GarrisonArmy, ActorSeatId = seat, TargetId = armyId, ParamA = troops,
        };

    private static ActionCommand MarchOrder(GameEngine e, string seat, string armyId, int x, int z)
        => new ActionCommand
        {
            Type = ActionType.March, ActorSeatId = seat, TargetId = armyId, ParamA = x, ParamB = z,
        };

    private static EventRecord LastAction(GameEngine e, string actionType)
    {
        EventRecord found = null;
        foreach (var rec in e.Log.Records)
            if (rec.EventType == "action" && rec.Payload["action"] == actionType) found = rec;
        return found;
    }

    private static int IndexOf(GameEngine e, string eventType)
    {
        var records = e.Log.Records;
        for (int i = 0; i < records.Count; i++)
            if (records[i].EventType == eventType) return i;
        return -1;
    }

    /// <summary>Scripted siege: 3000 attackers vs 500 garrison, optionally +2500 same-owner defenders.</summary>
    private static GameState SiegeScenario(bool defendingArmy) => SiegeEngine(defendingArmy).State;

    private static GameEngine SiegeEngine(bool defendingArmy)
    {
        var e = new GameEngine(81);
        e.State.Map = Corridor(12);
        e.State.Factions.Add(new FactionState { SeatId = "seat-a", Name = "Wei", Controller = ControllerType.ScriptedAi });
        e.State.Factions.Add(new FactionState { SeatId = "seat-b", Name = "Shu", Controller = ControllerType.ScriptedAi });
        e.State.Cities.Add(new CityState
        {
            Id = "city-a", Name = "City A", X = 0, Z = 0, OwnerSeatId = "seat-a",
            Population = 10000, Food = 5000, Gold = 1000, Garrison = 1000, Morale = 100,
        });
        e.State.Cities.Add(new CityState
        {
            Id = "city-b", Name = "City B", X = 2, Z = 0, OwnerSeatId = "seat-b",
            Population = 10000, Food = 5000, Gold = 1000, Garrison = 500, Morale = 100,
        });
        e.State.Armies.Add(new ArmyState
        {
            Id = "army-a", Name = "Army A", OwnerSeatId = "seat-a", Troops = 3000, X = 1, Z = 0,
        });
        if (defendingArmy)
            e.State.Armies.Add(new ArmyState
            {
                Id = "army-b", Name = "Army B", OwnerSeatId = "seat-b", Troops = 2500, X = 2, Z = 0,
            });
        e.BeginSeason();
        e.State.Phase = "player_orders";
        e.State.Diplomacy.DeclareWar("seat-a", "seat-b");
        return e;
    }

    private static bool PlanContains(GameState state, string seatId, string actionType)
    {
        foreach (var cmd in ScriptedController.PlanTurn(state, seatId))
            if (cmd.Type == actionType) return true;
        return false;
    }

}
