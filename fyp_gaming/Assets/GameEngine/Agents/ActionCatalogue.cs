using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace LuanShi.Engine
{
    /// <summary>One advertised candidate order. "Likely" is advisory only: the engine revalidates.</summary>
    public sealed class ActionOption
    {
        public string Type;
        public string TargetId;
        public string Label;
        public int CommandPointCost;
        public bool LikelyAffordable;
        public string Note;
    }

    /// <summary>
    /// Builds the seat-safe action list from the seat's detached <see cref="Observation"/> only —
    /// never from canonical <see cref="GameState"/>. Foreign and hidden cities therefore cannot
    /// leak into the catalogue even if they exist in the engine.
    /// </summary>
    public static class ActionCatalogue
    {
        private const string CandidateNote = "candidate, revalidated by the engine";

        private static readonly string[] DomesticTypes =
        {
            ActionType.LevyTax,
            ActionType.LightenLabor,
            ActionType.Reclaim,
            ActionType.Recruit,
            ActionType.Train,
            ActionType.Fortify,
            ActionType.Relief,
        };

        /// <summary>
        /// One block per owned city (observation order), seven domestic types in fixed order,
        /// then end_turn. The result is deterministic for the same observation.
        /// </summary>
        public static List<ActionOption> Build(Observation observation)
        {
            var options = new List<ActionOption>();
            if (observation == null) return options;

            // A seatless observation cannot own cities; never let neutral/foreign views become targets.
            bool hasSeat = !string.IsNullOrEmpty(observation.SeatId);
            if (hasSeat)
            {
                foreach (CityView city in observation.CitiesOf(observation.SeatId))
                {
                    foreach (string type in DomesticTypes)
                    {
                        int cost = BalanceConfig.CommandPointCost(type);
                        bool affordable;
                        string reason;
                        Evaluate(observation, city, type, cost, out affordable, out reason);
                        options.Add(new ActionOption
                        {
                            Type = type,
                            TargetId = city.Id,
                            Label = LabelFor(type, city),
                            CommandPointCost = cost,
                            LikelyAffordable = affordable,
                            Note = affordable ? CandidateNote : CandidateNote + "; " + reason,
                        });
                    }
                }
            }

            options.Add(new ActionOption
            {
                Type = "end_turn",
                TargetId = null,
                Label = "End turn",
                CommandPointCost = 0,
                LikelyAffordable = true,
                Note = CandidateNote,
            });
            return options;
        }

        /// <summary>Stable, line-oriented rendering for the user prompt. No hidden targets can appear.</summary>
        public static string ToPromptText(IReadOnlyList<ActionOption> options)
        {
            if (options == null || options.Count == 0) return "(no options)";
            var sb = new StringBuilder();
            for (int i = 0; i < options.Count; i++)
            {
                ActionOption option = options[i];
                if (i > 0) sb.Append('\n');
                sb.Append("- type=").Append(option.Type);
                if (!string.IsNullOrEmpty(option.TargetId))
                    sb.Append(" target_id=").Append(Sanitize(option.TargetId));
                sb.Append(" cost=").Append(option.CommandPointCost.ToString(CultureInfo.InvariantCulture));
                sb.Append(" likely_affordable=").Append(option.LikelyAffordable ? "yes" : "no");
                if (!string.IsNullOrEmpty(option.Label))
                    sb.Append(" label=\"").Append(Sanitize(option.Label)).Append('"');
                if (!string.IsNullOrEmpty(option.Note))
                    sb.Append(" note=\"").Append(Sanitize(option.Note)).Append('"');
            }
            return sb.ToString();
        }

        private static void Evaluate(Observation observation, CityView city, string type, int cost,
            out bool affordable, out string reason)
        {
            affordable = true;
            reason = null;

            if (observation.CommandPoints < cost)
            {
                affordable = false;
                reason = "not enough command points";
                return;
            }

            switch (type)
            {
                case ActionType.LightenLabor:
                    if (city.Gold < BalanceConfig.LightenLaborGoldCost)
                    {
                        affordable = false;
                        reason = "not enough gold";
                    }
                    break;

                case ActionType.Reclaim:
                    if (city.ReclaimedThisSeason)
                    {
                        affordable = false;
                        reason = "already reclaimed this season";
                    }
                    else if (city.ReclaimPct >= BalanceConfig.ReclaimPctCap)
                    {
                        affordable = false;
                        reason = "reclaim cap reached";
                    }
                    break;

                case ActionType.Recruit:
                {
                    int goldCost = (int)((long)BalanceConfig.RecruitGoldCostInfantry
                                         * BalanceConfig.RecruitTroopGain / 1000);
                    if (city.Gold < goldCost)
                    {
                        affordable = false;
                        reason = "not enough gold";
                    }
                    else if (city.Population < BalanceConfig.RecruitPopCost)
                    {
                        affordable = false;
                        reason = "not enough population";
                    }
                    else if (city.Garrison > 0 && !IsInfantryGarrison(city.GarrisonType))
                    {
                        affordable = false;
                        reason = "garrison type differs";
                    }
                    break;
                }

                case ActionType.Train:
                    if (city.Training >= BalanceConfig.TrainingCap(
                            observation.ResearchLevel(ResearchLine.Military)))
                    {
                        affordable = false;
                        reason = "training cap reached";
                    }
                    break;

                case ActionType.Fortify:
                    if (city.Defense >= BalanceConfig.DefenseMax)
                    {
                        affordable = false;
                        reason = "defense cap reached";
                    }
                    break;

                case ActionType.Relief:
                    if (city.Food < BalanceConfig.ReliefFoodCost)
                    {
                        affordable = false;
                        reason = "not enough food";
                    }
                    break;
            }
        }

        /// <summary>Mirrors the engine's unknown-type-reads-as-infantry rule without reading game state.</summary>
        private static bool IsInfantryGarrison(string type)
            => string.IsNullOrEmpty(type)
               || (type != "archer" && type != "cavalry" && type != "siege");

        private static string LabelFor(string type, CityView city)
        {
            string action;
            switch (type)
            {
                case ActionType.LevyTax: action = "Levy tax"; break;
                case ActionType.LightenLabor: action = "Lighten labor"; break;
                case ActionType.Reclaim: action = "Reclaim land"; break;
                case ActionType.Recruit: action = "Recruit infantry"; break;
                case ActionType.Train: action = "Train garrison"; break;
                case ActionType.Fortify: action = "Fortify city"; break;
                case ActionType.Relief: action = "Relief"; break;
                default: action = type; break;
            }
            string name = string.IsNullOrEmpty(city.Name) ? city.Id : city.Name;
            return action + " at " + name;
        }

        private static string Sanitize(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            var sb = new StringBuilder(value.Length);
            foreach (char c in value)
            {
                if (c == '"') sb.Append('\'');
                else if (char.IsControl(c)) sb.Append(' ');
                else sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
