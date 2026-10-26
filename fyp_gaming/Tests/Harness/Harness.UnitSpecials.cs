using System;
using System.Collections.Generic;
using LuanShi.Engine;

internal static partial class Harness
{
    private static void RunUnitSpecials()
    {
        // ------------------------------------------------ P15 unit specials (design §5.3)
        Section("P15 unit specials: 弓兵 range, 騎兵 double march + 平原衝鋒, 步兵 耐打, 器械 vs 城防, fog-edge march");

        // ---- 弓兵: attack an army 2 hexes away; 1 for everyone else, 3 is too far
        {
            var e = TwoSeatCorridor(501);
            var archer = AddArmy(e, "seat-a", 0, 1000);
            archer.UnitType = UnitTypes.Archer;
            AddEnemyArmy(e, "seat-b", 2, 500, UnitTypes.Infantry);
            var r = e.Submit(AttackOrder("seat-a", archer.Id, 2, 0));
            Check(r.Ok && Find(e.Log, "battle_field") != null,
                  $"an archer attacks an army 2 hexes away ({r.Error})");
        }
        {
            var e = TwoSeatCorridor(502);
            var infantry = AddArmy(e, "seat-a", 0, 1000);
            AddEnemyArmy(e, "seat-b", 2, 500, UnitTypes.Infantry);
            var r = e.Submit(AttackOrder("seat-a", infantry.Id, 2, 0));
            Check(!r.Ok && r.Error.Contains("out of range (max 1)"),
                  $"infantry cannot attack at range 2 ({r.Error})");
        }
        {
            var e = TwoSeatCorridor(503);
            var archer = AddArmy(e, "seat-a", 0, 1000);
            archer.UnitType = UnitTypes.Archer;
            AddEnemyArmy(e, "seat-b", 3, 500, UnitTypes.Infantry);
            var r = e.Submit(AttackOrder("seat-a", archer.Id, 3, 0));
            Check(!r.Ok && r.Error.Contains("out of range (max 2)"),
                  $"an archer cannot attack at range 3 ({r.Error})");
        }

        // ---- 騎兵: two marches a season, each with its own budget and 1 號令
        {
            var e = Build(504, 2, morale: 100, food: 5000);
            var cavalry = AddArmy(e, "seat-a", 0, 1000);
            cavalry.UnitType = UnitTypes.Cavalry;
            int cpBefore = e.State.FindFaction("seat-a").CommandPoints;
            var m1 = e.Submit(MarchOrder(e, "seat-a", cavalry.Id, 1, 0));
            var m2 = e.Submit(MarchOrder(e, "seat-a", cavalry.Id, 2, 0));
            var m3 = e.Submit(MarchOrder(e, "seat-a", cavalry.Id, 3, 0));
            Check(m1.Ok && m2.Ok && cavalry.X == 2,
                  $"cavalry marches twice in one season ({m1.Error} / {m2.Error})");
            Check(cavalry.MarchCountThisSeason == 2 && cavalry.MarchedThisSeason,
                  $"both marches are counted and MarchedThisSeason stays set ({cavalry.MarchCountThisSeason})");
            Check(e.State.FindFaction("seat-a").CommandPoints == cpBefore - 2,
                  "each cavalry march costs 1 號令");
            Check(!m3.Ok && m3.Error.Contains("already marched 2 times"),
                  $"a third cavalry march is refused ({m3.Error})");
        }
        {
            var e = Build(505, 2, morale: 100, food: 5000);
            var infantry = AddArmy(e, "seat-a", 0, 1000);
            var m1 = e.Submit(MarchOrder(e, "seat-a", infantry.Id, 1, 0));
            var m2 = e.Submit(MarchOrder(e, "seat-a", infantry.Id, 2, 0));
            Check(m1.Ok && !m2.Ok && m2.Error.Contains("already marched this season"),
                  $"non-cavalry still marches once ({m2.Error})");
        }
        {
            // ADR-018: a cavalry army that marched AT ALL is movement-spent for garrison purposes — the
            // re-formed army may not spend the second march either.
            var e = GarrisonScenario();
            var army = e.State.FindArmy("army-a");
            army.UnitType = UnitTypes.Cavalry;
            e.State.FindCity("city-b").GarrisonType = UnitTypes.Cavalry;
            var march = e.Submit(MarchOrder(e, "seat-a", army.Id, 2, 0));
            Check(march.Ok && army.MarchCountThisSeason == 1, $"the cavalry's first march counts ({march.Error})");
            e.Submit(GarrisonOrder(e, "seat-a", army.Id, 2000));
            e.Submit(ConvertOrder(e, "seat-a", "city-b", 2000));
            var evt = Find(e.Log, "garrison_converted");
            var rearmed = evt != null ? e.State.FindArmy(evt.Payload["army_id"]) : null;
            Check(rearmed != null && rearmed.MarchedThisSeason
               && rearmed.MarchCountThisSeason == UnitTypes.MarchesPerSeason(UnitTypes.Cavalry),
                  "a marched garrison exhausts the cavalry's second march too (ADR-018)");
            var again = rearmed != null
                ? e.Submit(MarchOrder(e, "seat-a", rearmed.Id, 3, 0))
                : default(ValidationResult);
            Check(rearmed != null && !again.Ok && again.Error.Contains("already marched"),
                  $"and the re-formed army cannot march ({again.Error})");
        }

        // ---- 騎兵 平原衝鋒: ×1.3 on plains only (forest is cost 2)
        {
            var plains = P15Battle(510, UnitTypes.Cavalry, UnitTypes.Infantry, defenderHexCost: 1);
            var forest = P15Battle(510, UnitTypes.Cavalry, UnitTypes.Infantry, defenderHexCost: 2);
            var plainsRound = FirstRound(plains);
            var forestRound = FirstRound(forest);
            Check(plains != null && plains.Payload["plains_charge"] == "true"
               && plains.Payload["defender_on_plains"] == "true",
                  "a cavalry attack on a plains hex records plains_charge=true");
            Check(forest != null && forest.Payload["plains_charge"] == "false",
                  "the same attack on a forest hex does not charge");
            Check(plains != null && plains.Payload["attacker_type_mult"] == "1.69"
               && forest != null && forest.Payload["attacker_type_mult"] == "1.3",
                  "type multipliers carry the charge (1.3 相剋 × 1.3 charge = 1.69)");
            Check(plainsRound.DefenderLoss == 91 && forestRound.DefenderLoss == 70,
                  $"exact first-round defender losses: plains {plainsRound.DefenderLoss}, forest {forestRound.DefenderLoss}");
            Check(CloseTo(plainsRound.DefenderLoss, forestRound.DefenderLoss, 1.3),
                  $"the plains charge adds 1.3x losses ({plainsRound.DefenderLoss}/{forestRound.DefenderLoss})");
        }

        // ---- 步兵 耐打: infantry takes ×0.85 losses. Both attackers face a 器械 defender, so the 相剋
        //      multipliers are identical (attacker ×1, defender ×0.5) and only the loss multiplier differs.
        {
            var infantry = P15Battle(511, UnitTypes.Infantry, UnitTypes.Siege, defenderHexCost: 2);
            var archer = P15Battle(511, UnitTypes.Archer, UnitTypes.Siege, defenderHexCost: 2);
            var infRound = FirstRound(infantry);
            var arcRound = FirstRound(archer);
            Check(infantry != null && infantry.Payload["attacker_loss_mult"] == "0.85"
               && archer != null && archer.Payload["attacker_loss_mult"] == "1",
                  "the event carries attacker_loss_mult (infantry 0.85, archer 1)");
            Check(infRound.AttackerLoss == 29 && arcRound.AttackerLoss == 34,
                  $"exact first-round attacker losses: infantry {infRound.AttackerLoss}, archer {arcRound.AttackerLoss}");
            Check(CloseTo(infRound.AttackerLoss, arcRound.AttackerLoss, 0.85),
                  $"infantry takes 0.85x losses ({infRound.AttackerLoss}/{arcRound.AttackerLoss})");
        }

        // ---- 器械 剋 城防 (OQ-29): ×1.3 attacker multiplier in a siege, stacking with the 3:1 waiver
        {
            var inf = P15Siege(512, UnitTypes.Infantry);
            var siege = P15Siege(512, UnitTypes.Siege);
            var infRound = FirstRound(inf);
            var siegeRound = FirstRound(siege);
            Check(siege != null && siege.Payload["siege_vs_city"] == "true"
               && siege.Payload["attacker_type_mult"] == "1.3",
                  "a siege army's battle_siege records siege_vs_city=true, type_mult 1.3");
            Check(inf != null && inf.Payload["siege_vs_city"] == "false"
               && inf.Payload["attacker_type_mult"] == "1",
                  "infantry keeps type_mult 1 vs the city");
            Check(siegeRound.DefenderLoss == 144 && infRound.DefenderLoss == 111,
                  $"exact first-round garrison losses: siege {siegeRound.DefenderLoss}, infantry {infRound.DefenderLoss}");
            Check(CloseTo(siegeRound.DefenderLoss, infRound.DefenderLoss, 1.3),
                  $"siege deals 1.3x to the city ({siegeRound.DefenderLoss}/{infRound.DefenderLoss})");
        }

        // ---- fog-edge march rule (user decision 2026-10-20): destination must be in sight
        {
            var e = Build(520, 2, morale: 100, food: 5000, sight: false);
            var army = AddArmy(e, "seat-a", 0, 1000);
            var near = e.Submit(MarchOrder(e, "seat-a", army.Id, 3, 0));   // city-a sees 3 hexes
            Check(near.Ok && army.X == 3, $"a march to an in-sight hex is accepted ({near.Error})");
        }
        {
            var e = Build(521, 2, morale: 100, food: 5000, sight: false);
            var army = AddArmy(e, "seat-a", 0, 1000);
            var far = e.Submit(MarchOrder(e, "seat-a", army.Id, 4, 0));
            Check(!far.Ok && far.Error.Contains("destination is beyond your sight"),
                  $"a march to an unseen hex is refused ({far.Error})");
            Check(army.X == 0 && army.MarchCountThisSeason == 0, "and the refused march changes nothing");
        }
        {
            var e = Build(522, 2, morale: 100, food: 5000, sight: false);
            e.State.Cities.Add(new CityState
            {
                Id = "city-b", Name = "City B", X = 6, Z = 0, OwnerSeatId = null,
                Population = 10000, Food = 500, Gold = 0, Garrison = 0,
            });
            AddArmy(e, "seat-a", 0, 3000);
            Check(!HasPlan(e, ActionType.March),
                  "the scripted seat never plans a march to an out-of-sight city");
        }

        // ---- determinism through the typed recruit/upkeep/march path
        {
            string a = UnitReplay(401), b = UnitReplay(401);
            Check(a == b && a.Length > 0, "typed recruit/march/upkeep replays byte-identically");
            Check(UnitReplay(401) != UnitReplay(402), "a different seed diverges");
        }
    }
}
