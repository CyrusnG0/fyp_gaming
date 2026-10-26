using System;
using System.Collections.Generic;

namespace LuanShi.Engine
{
    /// <summary>
    /// Offline rehearsal script (FAKE, no provider). Pure C# so the harness can assert exactly what
    /// the UI enqueues: the march coordinates are copied from the seat's advertised movement options
    /// and are never invented. Every action carries a "FAKE rehearsal" summary.
    /// </summary>
    public static class FakeTurnPlan
    {
        public const string Label = "FAKE rehearsal";

        private const string KindCaptureCity = "capture_neutral_city";
        private const string KindCaptureTower = "capture_tower";

        /// <summary>
        /// Builds one bounded fake turn: capture march (captures preferred, else the first option),
        /// then one affordable domestic order, then end_turn. Null/blocked movement still yields a
        /// valid domestic plan, so the editor rehearsal never stalls on a map-less fixture.
        /// </summary>
        public static IReadOnlyList<string> Build(MovementView movement, Observation observation,
            string seatId)
        {
            var actions = new List<string>();
            int ordersLeft = observation == null ? 0 : observation.CommandPoints;

            ArmyMovePlan marchArmy = null;
            MoveOption marchOption = FirstCaptureOption(movement, out marchArmy);
            if (marchOption == null) marchOption = FirstOption(movement, out marchArmy);
            if (marchArmy != null && marchOption != null)
            {
                int marchCost = BalanceConfig.CommandPointCost(ActionType.March);
                if (ordersLeft >= marchCost)
                {
                    actions.Add(MarchJson(marchArmy.ArmyId, marchOption.X, marchOption.Z));
                    ordersLeft -= marchCost;
                }
            }

            string cityId = FirstOwnedCity(observation, seatId);
            int levyCost = BalanceConfig.CommandPointCost(ActionType.LevyTax);
            if (!string.IsNullOrEmpty(cityId) && ordersLeft >= levyCost)
                actions.Add("{\"type\":\"levy_tax\",\"target_id\":" + MiniJson.Str(cityId)
                    + ",\"summary\":\"" + MiniJson.Escape(Label + ": levy tax") + "\"}");

            actions.Add("{\"type\":\"end_turn\",\"summary\":\""
                + MiniJson.Escape(Label + ": end turn") + "\"}");
            return actions;
        }

        private static MoveOption FirstCaptureOption(MovementView movement, out ArmyMovePlan army)
        {
            army = null;
            if (movement == null || movement.Armies == null) return null;
            foreach (ArmyMovePlan plan in movement.Armies)
            {
                if (plan == null || !plan.CanMarch || plan.Options == null) continue;
                foreach (MoveOption option in plan.Options)
                {
                    if (option == null) continue;
                    if (option.Kind == KindCaptureCity || option.Kind == KindCaptureTower)
                    {
                        army = plan;
                        return option;
                    }
                }
            }
            return null;
        }

        private static MoveOption FirstOption(MovementView movement, out ArmyMovePlan army)
        {
            army = null;
            if (movement == null || movement.Armies == null) return null;
            foreach (ArmyMovePlan plan in movement.Armies)
            {
                if (plan == null || !plan.CanMarch || plan.Options == null || plan.Options.Count == 0)
                    continue;
                foreach (MoveOption option in plan.Options)
                {
                    if (option == null) continue;
                    army = plan;
                    return option;
                }
            }
            return null;
        }

        private static string FirstOwnedCity(Observation observation, string seatId)
        {
            if (observation == null || observation.Cities == null || string.IsNullOrEmpty(seatId))
                return null;
            List<CityView> cities = observation.CitiesOf(seatId);
            foreach (CityView city in cities)
                if (city != null && !string.IsNullOrEmpty(city.Id)) return city.Id;
            return null;
        }

        private static string MarchJson(string armyId, int x, int z)
        {
            return "{\"type\":\"march\",\"target_id\":" + MiniJson.Str(armyId)
                + ",\"param_a\":" + MiniJson.Num(x)
                + ",\"param_b\":" + MiniJson.Num(z)
                + ",\"summary\":\"" + MiniJson.Escape(
                    Label + ": march " + (armyId ?? "?") + " to (" + x + "," + z + ")") + "\"}";
        }
    }
}
