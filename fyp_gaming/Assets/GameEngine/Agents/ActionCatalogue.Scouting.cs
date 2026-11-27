using System;
using System.Collections.Generic;

namespace LuanShi.Engine
{
    public static partial class ActionCatalogue
    {
        /// <summary>
        /// Adds one scout candidate for every enemy or neutral city this seat already knows:
        /// currently visible cities plus live scout reports. Espionage 1 is required; the
        /// candidate is advisory only and the engine revalidates research, gold and command points.
        /// </summary>
        public static void AppendScoutMissions(List<ActionOption> options, Observation observation)
        {
            if (options == null || observation == null || string.IsNullOrEmpty(observation.SeatId)) return;
            if (observation.ResearchLevel(ResearchLine.Espionage) < BalanceConfig.ScoutResearchLevel) return;

            int cost = BalanceConfig.CommandPointCost(ActionType.Scout);
            int gold = 0;
            foreach (CityView city in observation.CitiesOf(observation.SeatId)) gold += city.Gold;
            bool affordable = observation.CommandPoints >= cost && gold >= BalanceConfig.ScoutGoldCost;
            string reason = observation.CommandPoints < cost
                ? "no orders left"
                : gold < BalanceConfig.ScoutGoldCost ? "needs " + BalanceConfig.ScoutGoldCost + " gold" : null;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var scouts = new List<ActionOption>();
            foreach (CityView city in observation.Cities)
            {
                if (city == null || city.OwnerSeatId == observation.SeatId) continue;
                if (!seen.Add(city.Id)) continue;
                scouts.Add(ScoutOption(city.Id, city.Name, cost, affordable, reason));
            }
            foreach (ScoutedCityView city in observation.ScoutedCities)
            {
                if (city == null || city.OwnerSeatId == observation.SeatId) continue;
                if (!seen.Add(city.CityId)) continue;
                scouts.Add(ScoutOption(city.CityId, city.Name, cost, affordable, reason));
            }
            scouts.Sort((a, b) => string.CompareOrdinal(a.TargetId ?? "", b.TargetId ?? ""));
            options.AddRange(scouts);
        }

        private static ActionOption ScoutOption(string cityId, string name, int cost, bool affordable,
            string reason)
        {
            return new ActionOption
            {
                Type = ActionType.Scout,
                TargetId = cityId,
                Label = "Scout " + (string.IsNullOrEmpty(name) ? cityId : name)
                    + " (" + BalanceConfig.ScoutGoldCost + " gold)",
                CommandPointCost = cost,
                LikelyAffordable = affordable,
                Note = affordable ? CandidateNote : CandidateNote + "; " + reason,
            };
        }
    }
}
