using System.Collections.Generic;

namespace LuanShi.Engine
{
    /// <summary>
    /// Deterministic rule-based controller. It does not touch game state directly —
    /// it only *proposes* ActionCommands, which enter the engine through the same
    /// Submit() pipeline as human clicks and LLM JSON (CONTROLLER_PROTOCOL.md §2).
    /// Stands in for the AI factions in the MVP demo.
    /// </summary>
    public static class ScriptedController
    {
        public static List<ActionCommand> PlanTurn(GameState state, string seatId)
        {
            var plan = new List<ActionCommand>();
            var fac = state.FindFaction(seatId);
            if (fac == null) return plan;

            int cp = fac.CommandPoints;
            var cities = state.CitiesOf(seatId);
            var committed = new List<string>();

            // 外交 first: answering an offer is free (0 號令) and is also the most time-sensitive thing
            // a seat does, so it never competes with the CP budget.
            PlanDiplomacy(state, seatId, plan);

            // 野戰 / 攻城 first: an army in contact with the enemy is the urgent thing, and only
            // real opportunities are taken. The scripted seat is the no-diplomacy baseline, so it
            // never opens an undeclared war and never storms a city of a faction it is not already
            // at war with (ADR-010 #7 prices surprise attacks from state).
            foreach (var army in state.ArmiesOf(seatId))
            {
                if (army.Morale <= BalanceConfig.CombatRoutMoraleThreshold) continue;

                foreach (var enemy in state.Armies)
                {
                    if (enemy.OwnerSeatId == seatId) continue;
                    if (!state.Map.AreAdjacent(army.X, army.Z, enemy.X, enemy.Z)) continue;
                    int attackCost = BalanceConfig.CommandPointCost(ActionType.AttackArmy);
                    if (cp < attackCost) break;
                    if (army.Troops * 100 < enemy.Troops * (100 + BalanceConfig.ScriptedAttackTroopAdvantagePercent)) continue;
                    plan.Add(Order(ActionType.AttackArmy, seatId, army.Id, "決戰", enemy.X, enemy.Z));
                    cp -= attackCost;
                    committed.Add(army.Id);
                    break;
                }
                if (committed.Contains(army.Id)) continue;

                foreach (var city in state.Cities)
                {
                    if (city.IsNeutral || city.OwnerSeatId == seatId) continue;
                    if (!state.Diplomacy.IsAtWar(seatId, city.OwnerSeatId)) continue;
                    if (!state.Map.AreAdjacent(army.X, army.Z, city.X, city.Z)) continue;
                    int assaultCost = BalanceConfig.CommandPointCost(ActionType.AttackCity);
                    if (cp < assaultCost) break;
                    if (army.Troops * 100 < city.Garrison * BalanceConfig.AssaultTroopRatio
                                                            * (100 + BalanceConfig.ScriptedAttackTroopAdvantagePercent)) continue;
                    plan.Add(Order(ActionType.AttackCity, seatId, army.Id, "攻取城池", city.X, city.Z));
                    cp -= assaultCost;
                    committed.Add(army.Id);
                    break;
                }
            }

            // 徵稅: raise gold when the treasury cannot pay for troops and 民心 can take it.
            foreach (var city in cities)
            {
                if (cp <= 0) break;
                if (city.Gold >= BalanceConfig.RecruitGoldCost) continue;
                if (city.Morale - BalanceConfig.TaxLevyMoraleCost < BalanceConfig.MoraleYieldPenaltyThreshold) continue;
                plan.Add(Order(ActionType.LevyTax, seatId, city.Id, "國庫空虛"));
                cp--;
            }

            // 開墾: reclaim every city that still has land left to reclaim.
            foreach (var city in cities)
            {
                if (cp <= 0) break;
                if (city.ReclaimedThisSeason) continue;
                if (city.ReclaimPct >= BalanceConfig.ReclaimPctCap) continue;
                plan.Add(Order(ActionType.Reclaim, seatId, city.Id, "積穀防饑"));
                cp--;
            }

            // 募兵: reinforce the capital if the treasury allows.
            if (cp > 0 && cities.Count > 0)
            {
                var capital = cities[0];
                if (capital.Gold >= BalanceConfig.RecruitGoldCost
                    && capital.Population >= BalanceConfig.RecruitPopCost)
                {
                    plan.Add(Order(ActionType.Recruit, seatId, capital.Id, "擴軍備戰"));
                    cp--;
                }
            }

            // 賑災: 民心 under the yield threshold costs more than the food does.
            foreach (var city in cities)
            {
                if (cp <= 0) break;
                if (city.Morale >= BalanceConfig.MoraleYieldPenaltyThreshold) continue;
                if (city.Food < BalanceConfig.ReliefFoodCost) continue;
                plan.Add(Order(ActionType.Relief, seatId, city.Id, "安撫百姓"));
                cp--;
            }

            // 練兵 / 修城: spend whatever is left on permanent city strength.
            foreach (var city in cities)
            {
                if (cp <= 0) break;
                if (city.Training >= BalanceConfig.TrainingMax) continue;
                plan.Add(Order(ActionType.Train, seatId, city.Id, "練兵備戰"));
                cp--;
            }

            foreach (var city in cities)
            {
                if (cp <= 0) break;
                if (city.Defense >= BalanceConfig.DefenseMax) continue;
                plan.Add(Order(ActionType.Fortify, seatId, city.Id, "鞏固城防"));
                cp--;
            }

            // 行軍: send the strongest idle field army toward the nearest free neutral city.
            if (cp > 0)
            {
                ArmyState best = null;
                foreach (var a in state.ArmiesOf(seatId))
                    if (!a.MarchedThisSeason && !committed.Contains(a.Id) && (best == null || a.Troops > best.Troops))
                        best = a;

                if (best != null)
                {
                    CityState target = null;
                    int bestCost = int.MaxValue;
                    foreach (var c in state.Cities)
                    {
                        if (!c.IsNeutral) continue;
                        if (state.ArmyAt(c.X, c.Z) != null) continue;      // occupied hexes are refused by M1
                        int cost = state.Map.PathCost(best.X, best.Z, c.X, c.Z, BalanceConfig.ArmyMoveCostPerSeason);
                        if (cost >= 0 && cost < bestCost) { bestCost = cost; target = c; }
                    }
                    if (target != null)
                    {
                        plan.Add(Order(ActionType.March, seatId, best.Id, "開拓疆土",
                            target.X, target.Z));
                    }
                }
            }

            return plan;
        }

        /// <summary>A proposed command is only ever a proposal — it still goes through Submit().</summary>
        private static ActionCommand Order(string type, string seatId, string targetId, string reason,
            int paramA = 0, int paramB = 0, Dictionary<string, string> args = null)
            => new ActionCommand
            {
                Type = type,
                ActorSeatId = seatId,
                ControllerType = ControllerType.ScriptedAi,
                TargetId = targetId,
                ParamA = paramA,
                ParamB = paramB,
                Args = args,
                Reason = reason,
            };

        /// <summary>
        /// 外交 is free (0 號令), so the scripted seat always answers what is on the table and then keeps
        /// one offer in play. Every command mirrors an engine precondition, so a plan never bounces.
        /// </summary>
        private static void PlanDiplomacy(GameState state, string seatId, List<ActionCommand> plan)
        {
            var dip = state.Diplomacy;

            // Answer incoming offers: my own 信任 in the proposer decides, per design §7.1.
            foreach (var proposal in dip.Proposals.ToArray())
            {
                if (proposal.ToSeatId != seatId) continue;
                int trust = dip.GetTrust(seatId, proposal.FromSeatId);
                bool accept;
                switch (proposal.Type)
                {
                    case TreatyType.Nap: accept = trust >= BalanceConfig.ScriptedNapTrustThreshold; break;
                    case TreatyType.Alliance: accept = trust >= BalanceConfig.ScriptedAllianceTrustThreshold; break;
                    case TreatyType.Truce: accept = trust >= BalanceConfig.ScriptedTruceTrustThreshold; break;
                    default: accept = false; break;
                }
                plan.Add(Order(ActionType.RespondTreaty, seatId, proposal.FromSeatId,
                    accept ? "締約" : "婉拒",
                    args: new Dictionary<string, string> { { "response", accept ? "accept" : "reject" } }));
            }

            // One offer per turn: 求和 to anyone we are still fighting, otherwise 互不侵犯 to the
            // first neighbour we do not distrust and have nothing pending with.
            foreach (var other in state.Factions)
            {
                if (other.SeatId == seatId) continue;
                if (dip.FindProposal(seatId, other.SeatId) != null) continue;

                if (dip.IsAtWar(seatId, other.SeatId))
                {
                    if (dip.GetTrust(seatId, other.SeatId) < BalanceConfig.ScriptedTruceTrustThreshold) continue;
                    plan.Add(Order(ActionType.ProposeTreaty, seatId, other.SeatId, "求成",
                        args: new Dictionary<string, string> { { "treaty_type", TreatyType.Truce } }));
                    return;
                }

                if (dip.GetTrust(seatId, other.SeatId) < BalanceConfig.ScriptedNapTrustThreshold) continue;
                if (dip.HasTreaty(TreatyType.Nap, seatId, other.SeatId)) continue;
                plan.Add(Order(ActionType.ProposeTreaty, seatId, other.SeatId, "和好",
                    args: new Dictionary<string, string> { { "treaty_type", TreatyType.Nap } }));
                return;
            }
        }
    }
}
