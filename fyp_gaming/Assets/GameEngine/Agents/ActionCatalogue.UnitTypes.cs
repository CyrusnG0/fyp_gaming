using System;
using System.Collections.Generic;
using System.Globalization;

namespace LuanShi.Engine
{
    public static partial class ActionCatalogue
    {
        /// <summary>
        /// Opt-in (<see cref="LlmConfig.EnableUnitTypes"/>): replaces each own-city recruit entry with one
        /// entry per <see cref="UnitTypes.All"/>, in that order, each carrying <c>args.unit_type</c>.
        /// Affordability reads only the seat's own observation and the match profile: command points,
        /// the Military research gate, the garrison's single type, gold and population, the same
        /// checks, in the same order, as the engine's recruit validation. A gated or blocked type stays
        /// listed with its reason, so the model sees the rule instead of a silent omission. Every other
        /// entry and the relative order are untouched.
        /// </summary>
        public static List<ActionOption> ExpandRecruitTypes(List<ActionOption> options, Observation observation)
        {
            if (options == null || observation == null || string.IsNullOrEmpty(observation.SeatId)) return options;
            BalanceProfile balance = observation.Balance ?? BalanceProfile.Standard;
            int cost = BalanceConfig.CommandPointCost(ActionType.Recruit);
            var own = observation.CitiesOf(observation.SeatId);
            var result = new List<ActionOption>(options.Count + 3 * own.Count);
            foreach (ActionOption option in options)
            {
                CityView city = option.Type == ActionType.Recruit && option.TargetId != null
                    ? own.Find(c => c.Id == option.TargetId) : null;
                if (city == null) { result.Add(option); continue; }
                foreach (string type in UnitTypes.All)
                {
                    string reason = RecruitBlock(observation, city, type, cost, balance);
                    result.Add(new ActionOption
                    {
                        Type = ActionType.Recruit,
                        TargetId = city.Id,
                        Label = "Recruit " + type + " at " + (string.IsNullOrEmpty(city.Name) ? city.Id : city.Name),
                        CommandPointCost = cost,
                        LikelyAffordable = reason == null,
                        Note = reason == null ? CandidateNote : CandidateNote + "; " + reason,
                        Args = new Dictionary<string, string>(StringComparer.Ordinal) { { "unit_type", type } },
                    });
                }
            }
            return result;
        }

        private static string RecruitBlock(Observation observation, CityView city, string type, int cost,
            BalanceProfile balance)
        {
            if (observation.CommandPoints < cost) return "not enough command points";
            int gate = UnitTypes.RequiredMilitaryLevel(type);
            if (observation.ResearchLevel(ResearchLine.Military) < gate)
                return "needs Military research level " + gate.ToString(CultureInfo.InvariantCulture);
            if (city.Garrison > 0 && UnitTypes.Normalize(city.GarrisonType) != type)
                return "garrison type differs";
            if (city.Gold < balance.RecruitGoldCost(type, BalanceConfig.RecruitTroopGain)) return "not enough gold";
            if (city.Population < BalanceConfig.RecruitPopCost) return "not enough population";
            return null;
        }
    }
}
