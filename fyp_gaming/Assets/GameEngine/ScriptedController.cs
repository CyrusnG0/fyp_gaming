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

            // 開墾: reclaim every city that hasn't reclaimed yet.
            foreach (var city in cities)
            {
                if (cp <= 0) break;
                if (city.ReclaimedThisSeason) continue;
                plan.Add(new ActionCommand
                {
                    Type = ActionType.Reclaim,
                    ActorSeatId = seatId,
                    ControllerType = ControllerType.ScriptedAi,
                    TargetId = city.Id,
                    Reason = "積穀防饑",
                });
                cp--;
            }

            // 徵兵: reinforce the capital if the treasury allows.
            if (cp > 0 && cities.Count > 0)
            {
                var capital = cities[0];
                if (capital.Gold >= BalanceConfig.RecruitGoldCost
                    && capital.Population >= BalanceConfig.RecruitPopCost)
                {
                    plan.Add(new ActionCommand
                    {
                        Type = ActionType.Recruit,
                        ActorSeatId = seatId,
                        ControllerType = ControllerType.ScriptedAi,
                        TargetId = capital.Id,
                        Reason = "擴軍備戰",
                    });
                    cp--;
                }
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
                        plan.Add(new ActionCommand
                        {
                            Type = ActionType.March,
                            ActorSeatId = seatId,
                            ControllerType = ControllerType.ScriptedAi,
                            TargetId = best.Id,
                            ParamA = target.X,
                            ParamB = target.Z,
                            Reason = "開拓疆土",
                        });
                    }
                }
            }

            return plan;
        }
    }
}
