using System.Collections.Generic;

namespace LuanShi.Engine
{
    public static partial class ActionCatalogue
    {
        public static void AppendGarrisonConversions(List<ActionOption> options, Observation observation)
        {
            if (options == null || observation == null || string.IsNullOrEmpty(observation.SeatId)) return;
            int cost = BalanceConfig.CommandPointCost(ActionType.ConvertGarrison);
            foreach (CityView city in observation.CitiesOf(observation.SeatId))
            {
                if (city.Garrison <= 0) continue;
                bool blocked = false;
                foreach (ArmyView army in observation.Armies)
                    if (army.X == city.X && army.Z == city.Z
                        && (army.OwnerSeatId != observation.SeatId
                            || UnitTypes.Normalize(army.UnitType) != UnitTypes.Normalize(city.GarrisonType)))
                        blocked = true;
                if (blocked) continue;
                options.Add(new ActionOption
                {
                    Type = ActionType.ConvertGarrison, TargetId = city.Id,
                    Label = "Form or reinforce an army; choose param_a from 1 to " + city.Garrison,
                    CommandPointCost = cost, LikelyAffordable = observation.CommandPoints >= cost,
                    Note = "candidate, revalidated by the engine",
                });
            }
        }
    }
}
