using System;
using System.Collections.Generic;
using LuanShi.Engine;

internal static partial class Harness
{
    // ---- P14 helpers --------------------------------------------------------

    /// <summary>Two seats on a plain corridor with no cities — pure combat fixtures.</summary>
    private static GameEngine TwoSeatCorridor(int seed)
    {
        var e = new GameEngine(seed);
        e.State.Map = Corridor(10);
        e.State.Factions.Add(new FactionState { SeatId = "seat-a", Name = "Wei", Controller = ControllerType.Human });
        e.State.Factions.Add(new FactionState { SeatId = "seat-b", Name = "Shu", Controller = ControllerType.ScriptedAi });
        e.BeginSeason();
        e.State.Phase = "player_orders";
        return e;
    }

    private static ArmyState AddEnemyArmy(GameEngine e, string seat, int x, int troops, string unitType)
    {
        var army = new ArmyState
        {
            Id = "army-" + seat + "-1", Name = "Enemy", OwnerSeatId = seat,
            Troops = troops, UnitType = unitType, X = x, Z = 0,
        };
        e.State.Armies.Add(army);
        return army;
    }

    private static ActionCommand AttackOrder(string seat, string armyId, int x, int z)
        => new ActionCommand
        {
            Type = ActionType.AttackArmy, ActorSeatId = seat, TargetId = armyId, ParamA = x, ParamB = z,
        };

    /// <summary>P15 field fixture: 1,000 vs 1,000, defender at (1,0) with the given terrain cost.</summary>
    private static EventRecord P15Battle(int seed, string attackerType, string defenderType, int defenderHexCost)
    {
        var e = TwoSeatCorridor(seed);
        e.State.Map.Cost[1] = defenderHexCost;
        var attacker = new ArmyState
        {
            Id = "army-a", Name = "A", OwnerSeatId = "seat-a", Troops = 1000,
            UnitType = attackerType, X = 0, Z = 0,
        };
        var defender = new ArmyState
        {
            Id = "army-b", Name = "B", OwnerSeatId = "seat-b", Troops = 1000,
            UnitType = defenderType, X = 1, Z = 0,
        };
        e.State.Armies.Add(attacker);
        e.State.Armies.Add(defender);
        e.Submit(AttackOrder("seat-a", attacker.Id, 1, 0));
        return Find(e.Log, "battle_field");
    }

    /// <summary>P15 siege fixture: 2,000 attackers vs a 500-strong infantry garrison (4:1, legal for both types).</summary>
    private static EventRecord P15Siege(int seed, string attackerType)
    {
        var e = TwoSeatCorridor(seed);
        e.State.Cities.Add(new CityState
        {
            Id = "city-b", Name = "B", X = 1, Z = 0, OwnerSeatId = "seat-b",
            Population = 10000, Food = 5000, Gold = 1000, Garrison = 500, Morale = 100,
        });
        var attacker = new ArmyState
        {
            Id = "army-a", Name = "A", OwnerSeatId = "seat-a", Troops = 2000,
            UnitType = attackerType, X = 0, Z = 0,
        };
        e.State.Armies.Add(attacker);
        e.Submit(new ActionCommand
        {
            Type = ActionType.AttackCity, ActorSeatId = "seat-a", TargetId = attacker.Id, ParamA = 1, ParamB = 0,
        });
        return Find(e.Log, "battle_siege");
    }

    /// <summary>First-round losses of a 5,000-vs-5,000 field battle; same seed keeps the RNG aligned.
    /// The defender stands on a forest hex (cost 2) so P15's 平原衝鋒 does not colour the 相剋 ratios.</summary>
    private static (int AttackerLoss, int DefenderLoss) FieldRoundOne(int seed, string attackerType, string defenderType)
    {
        var e = new GameEngine(seed);
        e.State.Map = Corridor(10);
        e.State.Map.Cost[1] = 2;   // defender's hex: forest, not plains
        e.State.Factions.Add(new FactionState { SeatId = "seat-a", Name = "Wei", Controller = ControllerType.Human });
        e.State.Factions.Add(new FactionState { SeatId = "seat-b", Name = "Shu", Controller = ControllerType.ScriptedAi });
        e.State.Armies.Add(new ArmyState
        {
            Id = "army-a", Name = "A", OwnerSeatId = "seat-a", Troops = 5000, UnitType = attackerType, X = 0, Z = 0,
        });
        e.State.Armies.Add(new ArmyState
        {
            Id = "army-b", Name = "B", OwnerSeatId = "seat-b", Troops = 5000, UnitType = defenderType, X = 1, Z = 0,
        });
        e.BeginSeason();
        e.State.Phase = "player_orders";
        e.Submit(new ActionCommand
        {
            Type = ActionType.AttackArmy, ActorSeatId = "seat-a", TargetId = "army-a", ParamA = 1, ParamB = 0,
        });
        return FirstRound(Find(e.Log, "battle_field"));
    }

    /// <summary>First-round attacker losses of a siege against a 1,000-strong garrison of the given type.</summary>
    private static int SiegeFirstRoundAttackerLosses(int seed, string garrisonType)
    {
        var e = new GameEngine(seed);
        e.State.Map = Corridor(10);
        e.State.Factions.Add(new FactionState { SeatId = "seat-a", Name = "Wei", Controller = ControllerType.Human });
        e.State.Factions.Add(new FactionState { SeatId = "seat-b", Name = "Shu", Controller = ControllerType.ScriptedAi });
        e.State.Cities.Add(new CityState
        {
            Id = "city-b", Name = "B", X = 1, Z = 0, OwnerSeatId = "seat-b",
            Population = 10000, Food = 5000, Gold = 1000, Garrison = 1000, GarrisonType = garrisonType, Morale = 100,
        });
        e.State.Armies.Add(new ArmyState
        {
            Id = "army-a", Name = "A", OwnerSeatId = "seat-a", Troops = 5000,
            UnitType = UnitTypes.Infantry, X = 0, Z = 0,
        });
        e.BeginSeason();
        e.State.Phase = "player_orders";
        e.State.Diplomacy.DeclareWar("seat-a", "seat-b");
        var r = e.Submit(new ActionCommand
        {
            Type = ActionType.AttackCity, ActorSeatId = "seat-a", TargetId = "army-a", ParamA = 1, ParamB = 0,
        });
        if (!r.Ok) return -1;
        return FirstRound(Find(e.Log, "battle_siege")).AttackerLoss;
    }

    private static (int AttackerLoss, int DefenderLoss) FirstRound(EventRecord rec)
    {
        if (rec == null) return (-1, -1);
        string[] sides = rec.Payload["round_log"].Split(';')[0].Split('/');
        if (sides.Length != 2) return (-1, -1);
        int.TryParse(sides[0].Substring(2), out int attacker);
        int.TryParse(sides[1].Substring(2), out int defender);
        return (attacker, defender);
    }

    private static bool CloseTo(int value, int baseline, double expectedRatio)
        => baseline > 0 && Math.Abs(value / (double)baseline - expectedRatio) < 0.01;

    /// <summary>Five seasons of typed recruit/march/upkeep on one seed; returns the full event log.</summary>
    private static string UnitReplay(int seed)
    {
        var e = Build(seed, 1, morale: 100, food: 5000, garrison: 0);
        e.State.FindCity("city-a").Gold = 5000;
        SetLevel(e, "seat-a", ResearchLine.Military, 3);
        var army = AddArmy(e, "seat-a", 0, 1000);
        army.UnitType = UnitTypes.Cavalry;
        for (int season = 0; season < 5; season++)
        {
            e.Submit(RecruitOrder(e, "seat-a", "city-a", UnitTypes.Cavalry));
            e.Submit(MarchOrder(e, "seat-a", army.Id, 1, 0));
            e.EndSeason();
            e.BeginSeason();
        }
        return e.Log.ToJsonLines();
    }
}
