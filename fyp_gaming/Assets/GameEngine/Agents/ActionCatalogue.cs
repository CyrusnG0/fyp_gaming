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

        /// <summary>Target hex for coordinate orders (attack_army, attack_city); null otherwise.</summary>
        public int? ParamA;
        public int? ParamB;
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
            => Build(observation, null);

        /// <summary>
        /// As <see cref="Build(Observation)"/>, plus the seat-safe attack and assault candidates from
        /// <paramref name="attacks"/> (opt-in; null or empty leaves the list byte-identical). Attack
        /// entries sit after the domestic blocks and before end_turn. They carry only values the
        /// observation already shows and are never authorization: the engine revalidates the order.
        /// </summary>
        public static List<ActionOption> Build(Observation observation, AttackView attacks)
        {
            var options = new List<ActionOption>();
            if (observation == null) return options;

            // A seatless observation cannot own cities; never let neutral/foreign views become targets.
            bool hasSeat = !string.IsNullOrEmpty(observation.SeatId);
            if (hasSeat)
            {
                // The match's profile, so an opt-in reduced match advertises the gold it will charge.
                BalanceProfile balance = observation.Balance ?? BalanceProfile.Standard;
                foreach (CityView city in observation.CitiesOf(observation.SeatId))
                {
                    foreach (string type in DomesticTypes)
                    {
                        int cost = BalanceConfig.CommandPointCost(type);
                        bool affordable;
                        string reason;
                        Evaluate(observation, city, type, cost, balance, out affordable, out reason);
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

            if (hasSeat) AppendAttacks(options, observation, attacks);

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
                if (option.ParamA.HasValue && option.ParamB.HasValue)
                    sb.Append(" param_a=").Append(option.ParamA.Value.ToString(CultureInfo.InvariantCulture))
                      .Append(" param_b=").Append(option.ParamB.Value.ToString(CultureInfo.InvariantCulture));
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
            BalanceProfile balance, out bool affordable, out string reason)
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
                    if (city.Gold < balance.LightenLaborGoldCost)
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
                    int goldCost = balance.RecruitGoldCost(UnitTypes.Infantry, BalanceConfig.RecruitTroopGain);
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

        /// <summary>
        /// Attack and assault candidates. Fail closed against the seat's own observation: the view must
        /// be for this seat, the acting army must be one of its armies and the target army or city must
        /// appear in that same observation — otherwise the entry is dropped, so a miscomputed view can
        /// never advertise something the seat could not see.
        /// </summary>
        private static void AppendAttacks(List<ActionOption> options, Observation observation,
            AttackView attacks)
        {
            if (attacks == null || attacks.Armies == null) return;
            if (!string.Equals(attacks.SeatId, observation.SeatId, StringComparison.Ordinal)) return;

            // Entries that survive the observation cross-check compete for the seat-wide cap in the
            // planner's nearest-first order, so even a hand-built oversized view cannot enlarge the prompt.
            var built = new List<KeyValuePair<AttackOption, ActionOption>>();
            foreach (AttackArmyPair pair in AttackPairs(observation, attacks))
            {
                AttackOption a = pair.Option;
                string label;
                if (a.Kind == AttackPlanner.KindCity)
                {
                    CityView city = observation.Cities.Find(c => c != null && c.Id == a.TargetCityId
                        && c.X == a.X && c.Z == a.Z && !c.IsNeutral && c.OwnerSeatId != observation.SeatId);
                    if (city == null) continue;
                    string need = a.AssaultRatioWaived
                        ? "siege army waives the troop ratio"
                        : "needs at least " + a.RequiredTroops.ToString(CultureInfo.InvariantCulture) + " troops";
                    string defender = a.DefendingArmyTroops > 0
                        ? ", defending army " + a.DefendingArmyTroops.ToString(CultureInfo.InvariantCulture)
                        : "";
                    label = "Assault " + NameOf(city.Name, city.Id) + " owned by " + city.OwnerSeatId
                        + " (garrison " + city.Garrison.ToString(CultureInfo.InvariantCulture) + defender
                        + "; " + need + ") with " + NameOf(pair.Army.Name, pair.Army.Id);
                }
                else
                {
                    ArmyView enemy = observation.Armies.Find(r => r != null && r.Id == a.TargetArmyId
                        && r.X == a.X && r.Z == a.Z && r.OwnerSeatId != observation.SeatId);
                    if (enemy == null) continue;
                    label = "Attack " + enemy.OwnerSeatId + " " + enemy.UnitType + " army "
                        + NameOf(enemy.Name, enemy.Id) + " (troops "
                        + enemy.Troops.ToString(CultureInfo.InvariantCulture) + ", morale "
                        + enemy.Morale.ToString(CultureInfo.InvariantCulture) + ") with "
                        + NameOf(pair.Army.Name, pair.Army.Id);
                }

                built.Add(new KeyValuePair<AttackOption, ActionOption>(a, new ActionOption
                {
                    Type = a.Kind,
                    TargetId = a.ArmyId,
                    Label = label,
                    CommandPointCost = a.CommandPointCost,
                    LikelyAffordable = observation.CommandPoints >= a.CommandPointCost,
                    Note = string.IsNullOrEmpty(a.Note) ? CandidateNote : CandidateNote + "; " + a.Note,
                    ParamA = a.X,
                    ParamB = a.Z,
                }));
            }
            built.Sort((l, r) => AttackPlanner.CompareOptions(l.Key, r.Key));
            int take = Math.Min(built.Count, AttackPlanner.MaxAdvertisedOptions);
            for (int i = 0; i < take; i++) options.Add(built[i].Value);
        }

        /// <summary>
        /// The truncation line for the prompt, or null when every legal target is already listed.
        /// Depends only on the seat's own attack view.
        /// </summary>
        public static string AttackNotice(AttackView attacks)
        {
            if (attacks == null) return null;
            return attacks.Truncated || attacks.OptionCount > AttackPlanner.MaxAdvertisedOptions
                ? AttackPlanner.TruncationNotice : null;
        }

        private struct AttackArmyPair
        {
            public ArmyView Army;
            public AttackOption Option;
        }

        private static IEnumerable<AttackArmyPair> AttackPairs(Observation observation, AttackView attacks)
        {
            foreach (ArmyAttackPlan plan in attacks.Armies)
            {
                if (plan == null || !plan.CanAttack || plan.Options == null) continue;
                ArmyView own = observation.Armies.Find(r => r != null && r.Id == plan.ArmyId
                    && r.OwnerSeatId == observation.SeatId);
                if (own == null) continue;
                foreach (AttackOption option in plan.Options)
                    if (option != null && option.ArmyId == plan.ArmyId)
                        yield return new AttackArmyPair { Army = own, Option = option };
            }
        }

        private static string NameOf(string name, string id) => string.IsNullOrEmpty(name) ? id : name;

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
