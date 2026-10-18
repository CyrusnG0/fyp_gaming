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
                    plan.Add(Order(ActionType.AttackArmy, seatId, army.Id, "Fight decisive battle", enemy.X, enemy.Z));
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
                    // the engine's 3:1 check counts a same-owner army defending the city as garrison, so
                    // the baseline must see the same effective garrison (with its own safety margin on top)
                    var defender = state.ArmyAt(city.X, city.Z);
                    int effectiveGarrison = city.Garrison
                        + (defender != null && defender.OwnerSeatId == city.OwnerSeatId ? defender.Troops : 0);
                    if (army.Troops * 100 < effectiveGarrison * BalanceConfig.AssaultTroopRatio
                                                            * (100 + BalanceConfig.ScriptedAttackTroopAdvantagePercent)) continue;
                    plan.Add(Order(ActionType.AttackCity, seatId, army.Id, "Capture city", city.X, city.Z));
                    cp -= assaultCost;
                    committed.Add(army.Id);
                    break;
                }
            }

            // 賑災 (revolt-risk first): a city under the 民變 threshold can produce nothing next season
            // (design §4.1), so feeding it outranks 徵稅/開墾/募兵 spending. The ordinary 民心 recovery
            // pass below still catches everything between this threshold and the yield threshold.
            foreach (var city in cities)
            {
                if (cp <= 0) break;
                if (city.Morale >= BalanceConfig.RevoltRiskMoraleThreshold) continue;
                if (city.Food < BalanceConfig.ReliefFoodCost) continue;
                plan.Add(Order(ActionType.Relief, seatId, city.Id, "Stop the revolt risk"));
                cp--;
            }

            // 徵稅: raise gold when the treasury cannot pay for troops and 民心 can take it.
            foreach (var city in cities)
            {
                if (cp <= 0) break;
                if (city.Gold >= BalanceConfig.RecruitGoldCost) continue;
                if (city.Morale - BalanceConfig.TaxLevyMoraleCost < BalanceConfig.MoraleYieldPenaltyThreshold) continue;
                plan.Add(Order(ActionType.LevyTax, seatId, city.Id, "Treasury is empty"));
                cp--;
            }

            // 開墾: reclaim every city that still has land left to reclaim.
            foreach (var city in cities)
            {
                if (cp <= 0) break;
                if (city.ReclaimedThisSeason) continue;
                if (city.ReclaimPct >= BalanceConfig.ReclaimPctCap) continue;
                plan.Add(Order(ActionType.Reclaim, seatId, city.Id, "Build food reserves"));
                cp--;
            }

            // 募兵: reinforce the capital if the treasury allows.
            if (cp > 0 && cities.Count > 0)
            {
                var capital = cities[0];
                if (capital.Gold >= BalanceConfig.RecruitGoldCost
                    && capital.Population >= BalanceConfig.RecruitPopCost)
                {
                    plan.Add(Order(ActionType.Recruit, seatId, capital.Id, "Expand and prepare the army"));
                    cp--;
                }
            }

            // 賑災: 民心 under the yield threshold costs more than the food does.
            foreach (var city in cities)
            {
                if (cp <= 0) break;
                if (city.Morale >= BalanceConfig.MoraleYieldPenaltyThreshold) continue;
                if (city.Food < BalanceConfig.ReliefFoodCost) continue;
                plan.Add(Order(ActionType.Relief, seatId, city.Id, "Reassure the people"));
                cp--;
            }

            // 練兵 / 修城: spend whatever is left on permanent city strength.
            foreach (var city in cities)
            {
                if (cp <= 0) break;
                if (city.Training >= BalanceConfig.TrainingCap(fac.ResearchLevel(ResearchLine.Military))) continue;
                plan.Add(Order(ActionType.Train, seatId, city.Id, "Train for war"));
                cp--;
            }

            foreach (var city in cities)
            {
                if (cp <= 0) break;
                if (city.Defense >= BalanceConfig.DefenseMax) continue;
                plan.Add(Order(ActionType.Fortify, seatId, city.Id, "Strengthen city defenses"));
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
                    int budget = BalanceConfig.MarchBudget(state.Season);   // 冬 行軍 ×0.7 (design §10.1)
                    foreach (var c in state.Cities)
                    {
                        if (!c.IsNeutral) continue;
                        if (state.ArmyAt(c.X, c.Z) != null) continue;      // occupied hexes are refused by M1
                        int cost = state.Map.PathCost(best.X, best.Z, c.X, c.Z, budget);
                        if (cost >= 0 && cost < bestCost) { bestCost = cost; target = c; }
                    }
                    if (target != null)
                    {
                        plan.Add(Order(ActionType.March, seatId, best.Id, "Expand territory",
                            target.X, target.Z));
                    }
                }
            }

            // 研究 (design §9): with a 號令 still unspent after every other priority, the scripted baseline
            // buys the next 農政 level — the line its food-first play style is bottlenecked by. At most one
            // purchase a season: the engine deducts the points on submit, so one order per plan.
            if (cp > 0)
            {
                int level = fac.ResearchLevel(ResearchLine.Agri);
                if (level < BalanceConfig.ResearchMaxLevel
                    && fac.ResearchPoints >= BalanceConfig.ResearchLevelCost(level + 1))
                {
                    plan.Add(Order(ActionType.Research, seatId, null, "Improve the fields",
                        args: new Dictionary<string, string> { { "line", ResearchLine.Agri } }));
                }
            }

            // 諜報 (design §8.2, P8): the baseline is deliberately un-clever — when it is already at war, the
            // treasury is comfortable and 諜報 L1 is researched, it spreads rumours in the enemy's least
            // loyal city. Every third season (TBD), so it never floods the log; it never sows distrust.
            if (cp > 0 && state.Season % BalanceConfig.ScriptedSpyRumorEveryNthSeason == 0)
            {
                int treasury = 0;
                foreach (var c in cities) treasury += c.Gold;
                if (treasury > BalanceConfig.ScriptedSpyGoldThreshold
                    && fac.ResearchLevel(ResearchLine.Espionage) >= BalanceConfig.RumorResearchLevel)
                {
                    CityState weakest = null;
                    foreach (var c in state.Cities)
                    {
                        if (c.IsNeutral || c.OwnerSeatId == seatId) continue;
                        if (!state.Diplomacy.IsAtWar(seatId, c.OwnerSeatId)) continue;
                        if (weakest == null || c.Morale < weakest.Morale) weakest = c;
                    }
                    if (weakest != null)
                        plan.Add(Order(ActionType.Rumor, seatId, weakest.Id, "Undermine the enemy's resolve"));
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
                    accept ? "Sign treaty" : "Decline treaty",
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
                    plan.Add(Order(ActionType.ProposeTreaty, seatId, other.SeatId, "Seek peace",
                        args: new Dictionary<string, string> { { "treaty_type", TreatyType.Truce } }));
                    return;
                }

                if (dip.GetTrust(seatId, other.SeatId) < BalanceConfig.ScriptedNapTrustThreshold) continue;
                if (dip.HasTreaty(TreatyType.Nap, seatId, other.SeatId)) continue;
                plan.Add(Order(ActionType.ProposeTreaty, seatId, other.SeatId, "Make peace",
                    args: new Dictionary<string, string> { { "treaty_type", TreatyType.Nap } }));
                return;
            }
        }
    }
}
