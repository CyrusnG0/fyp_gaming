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

            // 徵稅: raise gold when the treasury cannot pay for troops and 民心 can take it.
            foreach (var city in cities)
            {
                if (cp <= 0) break;
                if (city.Gold >= BalanceConfig.RecruitGoldCost) continue;
                if (city.Morale - BalanceConfig.TaxLevyMoraleCost < BalanceConfig.MoraleYieldPenaltyThreshold) continue;
                plan.Add(Order(ActionType.LevyTax, seatId, city.Id, "國庫空虛"));
                cp--;
            }

            // 開墾: reclaim every city that hasn't reclaimed yet.
            foreach (var city in cities)
            {
                if (cp <= 0) break;
                if (city.ReclaimedThisSeason) continue;
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

            // 行軍: send the strongest idle field army toward the nearest neutral city.
            if (cp > 0)
            {
                ArmyState best = null;
                foreach (var a in state.ArmiesOf(seatId))
                    if (!a.MarchedThisSeason && (best == null || a.Troops > best.Troops)) best = a;

                if (best != null)
                {
                    CityState target = null;
                    int bestCost = int.MaxValue;
                    foreach (var c in state.Cities)
                    {
                        if (!c.IsNeutral) continue;
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
            int paramA = 0, int paramB = 0)
            => new ActionCommand
            {
                Type = type,
                ActorSeatId = seatId,
                ControllerType = ControllerType.ScriptedAi,
                TargetId = targetId,
                ParamA = paramA,
                ParamB = paramB,
                Reason = reason,
            };
    }
}
