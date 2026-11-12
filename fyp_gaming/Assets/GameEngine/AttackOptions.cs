using System;
using System.Collections.Generic;
using System.Text;

namespace LuanShi.Engine
{
    /// <summary>
    /// One advertised attack or assault for one own army. Options are candidates only — never
    /// authorization: the model must still submit a normal <c>attack_army</c> / <c>attack_city</c>
    /// command and the engine revalidates it. Every field is copied from what the seat's own
    /// observation already carries for an in-sight enemy army or city; nothing else is read.
    /// No battle odds or predicted outcome are advertised (combat is resolved by the engine only).
    /// </summary>
    public sealed class AttackOption
    {
        public string ArmyId;
        public string Kind;                 // "attack_army" | "attack_city"
        public int X, Z;                    // target hex: param_a / param_b of the command
        public int Distance;                // hex steps from the attacker (1 = adjacent); the ordering key
        public int CommandPointCost;

        public string TargetArmyId;         // attack_army only (the army is in sight)
        public string TargetCityId;         // attack_city only (the city is in sight)
        public string TargetName;
        public string TargetOwnerSeatId;
        public string TargetUnitType;       // enemy army type, or the city's garrison type
        public int TargetTroops;            // enemy army troops, or the city garrison
        public int TargetMorale;            // enemy army only
        public int DefendingArmyTroops;     // attack_city: an enemy-owned army standing on the city hex
        public int RequiredTroops;          // attack_city: AssaultTroopRatio x effective garrison (0 if waived)
        public bool AssaultRatioWaived;     // attack_city by a siege army
        public string Note;                 // short typed warning (treaty in force) or null
    }

    /// <summary>
    /// The bounded attack plan for one own army. When <see cref="CanAttack"/> is false no options
    /// are advertised and <see cref="BlockedReason"/> carries a safe typed category.
    /// </summary>
    public sealed class ArmyAttackPlan
    {
        public string ArmyId, ArmyName, UnitType;
        public int X, Z;
        public int Troops, Morale;
        public int AttackRange;
        public bool CanAttack;              // morale not broken && orders >= the cheapest attack
        public string BlockedReason;        // "army morale is broken" | "no orders left" | null
        public int TotalAvailable;          // all valid candidates before the cap
        public bool Truncated;
        public IReadOnlyList<AttackOption> Options;   // this army's share of the seat-wide advertised set, nearest first
    }

    /// <summary>
    /// The seat-safe attack query result. Read-only by construction: built from the same predicates
    /// the attack validators use, it never mutates state, spends 號令, rolls RNG or emits.
    /// </summary>
    public sealed class AttackView
    {
        public string SeatId;
        public int Season;
        public int CommandPoints;
        public IReadOnlyList<ArmyAttackPlan> Armies;   // own armies only, ordered by army id
        public int TotalAvailable;                     // all legal candidates across every army, before the cap
        public bool Truncated;                         // true when the seat-wide cap dropped legal candidates

        /// <summary>Total advertised options across all armies.</summary>
        public int OptionCount
        {
            get
            {
                int n = 0;
                if (Armies != null)
                    foreach (var plan in Armies)
                        if (plan != null && plan.Options != null) n += plan.Options.Count;
                return n;
            }
        }

        /// <summary>Deterministic serialisation used by the harness for hidden-world equality checks.</summary>
        public string ToJson()
        {
            var armies = new List<string>();
            if (Armies != null)
                foreach (var plan in Armies)
                    if (plan != null) armies.Add(PlanJson(plan));

            var sb = new StringBuilder(512);
            sb.Append('{');
            sb.Append("\"seat_id\":").Append(MiniJson.Str(SeatId));
            sb.Append(",\"season\":").Append(MiniJson.Num(Season));
            sb.Append(",\"command_points\":").Append(MiniJson.Num(CommandPoints));
            sb.Append(",\"total_available\":").Append(MiniJson.Num(TotalAvailable));
            sb.Append(",\"truncated\":").Append(Truncated ? "true" : "false");
            sb.Append(",\"armies\":").Append(MiniJson.Array(armies));
            sb.Append('}');
            return sb.ToString();
        }

        private static string PlanJson(ArmyAttackPlan plan)
        {
            var options = new List<string>();
            if (plan.Options != null)
                foreach (var option in plan.Options)
                    if (option != null) options.Add(OptionJson(option));

            var sb = new StringBuilder(256);
            sb.Append('{');
            sb.Append("\"army_id\":").Append(MiniJson.Str(plan.ArmyId));
            sb.Append(",\"army_name\":").Append(MiniJson.Str(plan.ArmyName));
            sb.Append(",\"unit_type\":").Append(MiniJson.Str(plan.UnitType));
            sb.Append(",\"x\":").Append(MiniJson.Num(plan.X));
            sb.Append(",\"z\":").Append(MiniJson.Num(plan.Z));
            sb.Append(",\"troops\":").Append(MiniJson.Num(plan.Troops));
            sb.Append(",\"morale\":").Append(MiniJson.Num(plan.Morale));
            sb.Append(",\"attack_range\":").Append(MiniJson.Num(plan.AttackRange));
            sb.Append(",\"can_attack\":").Append(plan.CanAttack ? "true" : "false");
            sb.Append(",\"blocked_reason\":").Append(MiniJson.Str(plan.BlockedReason));
            sb.Append(",\"total_available\":").Append(MiniJson.Num(plan.TotalAvailable));
            sb.Append(",\"truncated\":").Append(plan.Truncated ? "true" : "false");
            sb.Append(",\"options\":").Append(MiniJson.Array(options));
            sb.Append('}');
            return sb.ToString();
        }

        private static string OptionJson(AttackOption option)
        {
            var sb = new StringBuilder(256);
            sb.Append('{');
            sb.Append("\"army_id\":").Append(MiniJson.Str(option.ArmyId));
            sb.Append(",\"kind\":").Append(MiniJson.Str(option.Kind));
            sb.Append(",\"x\":").Append(MiniJson.Num(option.X));
            sb.Append(",\"z\":").Append(MiniJson.Num(option.Z));
            sb.Append(",\"distance\":").Append(MiniJson.Num(option.Distance));
            sb.Append(",\"cost\":").Append(MiniJson.Num(option.CommandPointCost));
            sb.Append(",\"target_army_id\":").Append(MiniJson.Str(option.TargetArmyId));
            sb.Append(",\"target_city_id\":").Append(MiniJson.Str(option.TargetCityId));
            sb.Append(",\"target_name\":").Append(MiniJson.Str(option.TargetName));
            sb.Append(",\"target_owner\":").Append(MiniJson.Str(option.TargetOwnerSeatId));
            sb.Append(",\"target_unit_type\":").Append(MiniJson.Str(option.TargetUnitType));
            sb.Append(",\"target_troops\":").Append(MiniJson.Num(option.TargetTroops));
            sb.Append(",\"target_morale\":").Append(MiniJson.Num(option.TargetMorale));
            sb.Append(",\"defending_army_troops\":").Append(MiniJson.Num(option.DefendingArmyTroops));
            sb.Append(",\"required_troops\":").Append(MiniJson.Num(option.RequiredTroops));
            sb.Append(",\"ratio_waived\":").Append(option.AssaultRatioWaived ? "true" : "false");
            sb.Append(",\"note\":").Append(MiniJson.Str(option.Note));
            sb.Append('}');
            return sb.ToString();
        }
    }

    /// <summary>
    /// Seat-safe candidate query for attack and capture. Calls the predicates
    /// <c>GameEngine.DoAttackArmy</c> / <c>DoAttackCity</c> call before any combat roll, in the same
    /// order, and performs no validation side effects. A target hex is only read after the
    /// <c>InSight</c> check, so a hidden army or city can never influence the result. (Both attack
    /// ranges are inside an own army's sight radius, so the check never removes a legal target; it
    /// is there so the guarantee does not depend on balance constants.)
    ///
    /// "Capture" is two engine paths: marching onto a neutral city or watch tower (already offered by
    /// <see cref="MovementPlanner"/>) and assaulting an enemy city with <c>attack_city</c> (offered here).
    /// </summary>
    public static class AttackPlanner
    {
        /// <summary>
        /// Seat-wide bound on advertised options (all attackers together, not per army): the nearest
        /// N by distance from each option's own attacker. Callers may ask for fewer, never more.
        /// </summary>
        public const int MaxAdvertisedOptions = 10;

        /// <summary>The line shown to the model when the cap dropped legal candidates.</summary>
        public static readonly string TruncationNotice = "nearest "
            + MaxAdvertisedOptions.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + " targets shown; other legal targets exist";

        public const string KindArmy = ActionType.AttackArmy;
        public const string KindCity = ActionType.AttackCity;

        public static AttackView Build(GameEngine engine, string seatId,
            int maxOptions = MaxAdvertisedOptions)
        {
            int cap = ClampCap(maxOptions);
            var view = new AttackView
            {
                SeatId = null,
                Season = engine != null && engine.State != null ? engine.State.Season : 0,
                CommandPoints = 0,
                Armies = new List<ArmyAttackPlan>(),
            };
            if (engine == null || engine.State == null) return view;

            var fac = engine.State.FindFaction(seatId);
            if (fac == null) return view;   // unknown/absent seat => empty view with SeatId null

            view.SeatId = fac.SeatId;
            view.CommandPoints = fac.CommandPoints;
            view.Season = engine.State.Season;

            var own = new List<ArmyState>();
            foreach (var army in engine.State.Armies)
                if (army != null && army.OwnerSeatId == fac.SeatId) own.Add(army);
            own.Sort((a, b) => string.CompareOrdinal(a.Id ?? string.Empty, b.Id ?? string.Empty));

            var plans = new List<ArmyAttackPlan>(own.Count);
            // Each plan first holds every legal candidate for its army; the seat-wide cap is then
            // applied once, over all armies, so the prompt bound does not scale with army count.
            var pool = new List<AttackOption>();
            foreach (var army in own)
            {
                var plan = BuildPlan(engine, fac, army);
                plans.Add(plan);
                pool.AddRange(plan.Options);
            }
            pool.Sort(CompareOptions);
            int take = Math.Min(cap, pool.Count);
            var kept = new HashSet<AttackOption>();
            for (int i = 0; i < take; i++) kept.Add(pool[i]);
            foreach (var plan in plans)
            {
                var shown = new List<AttackOption>();
                foreach (var option in plan.Options)
                    if (kept.Contains(option)) shown.Add(option);
                plan.Truncated = shown.Count < plan.TotalAvailable;
                plan.Options = shown;
            }
            view.Armies = plans;
            view.TotalAvailable = pool.Count;
            view.Truncated = pool.Count > take;
            return view;
        }

        private static ArmyAttackPlan BuildPlan(GameEngine engine, FactionState fac, ArmyState army)
        {
            int fieldCost = BalanceConfig.CommandPointCost(ActionType.AttackArmy);
            int cityCost = BalanceConfig.CommandPointCost(ActionType.AttackCity);
            bool moraleOk = army.Morale > BalanceConfig.CombatRoutMoraleThreshold;
            bool ordersOk = fac.CommandPoints >= Math.Min(fieldCost, cityCost);
            bool canAttack = moraleOk && ordersOk;

            var plan = new ArmyAttackPlan
            {
                ArmyId = army.Id,
                ArmyName = army.Name,
                UnitType = army.UnitType,
                X = army.X,
                Z = army.Z,
                Troops = army.Troops,
                Morale = army.Morale,
                AttackRange = UnitTypes.AttackRange(army.UnitType),
                CanAttack = canAttack,
                // Safe typed categories only; no hidden ids or occupancy facts.
                BlockedReason = canAttack ? null : (!moraleOk ? "army morale is broken" : "no orders left"),
                TotalAvailable = 0,
                Truncated = false,
                Options = new List<AttackOption>(),
            };
            if (!canAttack) return plan;
            var map = engine.State.Map;
            if (map == null) return plan;

            int range = plan.AttackRange;
            var all = new List<AttackOption>();
            for (int z = 0; z < map.Height; z++)
            {
                for (int x = 0; x < map.Width; x++)
                {
                    if (x == army.X && z == army.Z) continue;
                    // Fail closed: nothing outside the seat's sight is ever read or advertised.
                    if (!GameEngine.InSight(engine.State, fac.SeatId, x, z)) continue;

                    int distance = fac.CommandPoints >= fieldCost
                        ? map.HexDistance(army.X, army.Z, x, z, range) : -1;
                    if (distance >= 0)
                    {
                        var target = engine.State.ArmyAt(x, z);
                        if (target != null && target.OwnerSeatId != fac.SeatId)
                        {
                            var field = FieldOption(engine, fac, army, ArmyView.From(target), fieldCost);
                            field.Distance = distance;
                            all.Add(field);
                        }
                    }

                    if (fac.CommandPoints >= cityCost && map.AreAdjacent(army.X, army.Z, x, z))
                    {
                        var option = CityOption(engine, fac, army, x, z, cityCost);
                        if (option != null) { option.Distance = 1; all.Add(option); }
                    }
                }
            }

            all.Sort(CompareOptions);
            plan.TotalAvailable = all.Count;
            plan.Options = all;
            return plan;
        }

        private static AttackOption FieldOption(GameEngine engine, FactionState fac, ArmyState army,
            ArmyView target, int cost)
        {
            return new AttackOption
            {
                ArmyId = army.Id,
                Kind = KindArmy,
                X = target.X,
                Z = target.Z,
                CommandPointCost = cost,
                TargetArmyId = target.Id,
                TargetName = target.Name,
                TargetOwnerSeatId = target.OwnerSeatId,
                TargetUnitType = target.UnitType,
                TargetTroops = target.Troops,
                TargetMorale = target.Morale,
                Note = TreatyNote(engine, fac.SeatId, target.OwnerSeatId),
            };
        }

        /// <summary>Mirrors DoAttackCity's pre-combat checks; null when the engine would reject.</summary>
        private static AttackOption CityOption(GameEngine engine, FactionState fac, ArmyState army,
            int x, int z, int cost)
        {
            var city = engine.State.CityAt(x, z);
            if (city == null || city.IsNeutral || city.OwnerSeatId == fac.SeatId) return null;

            bool waived = UnitTypes.WaivesAssaultRatio(UnitTypes.Normalize(army.UnitType));
            var defending = engine.State.ArmyAt(city.X, city.Z);
            int defendingTroops = defending != null && defending.OwnerSeatId == city.OwnerSeatId
                ? defending.Troops : 0;
            int effectiveGarrison = city.Garrison + defendingTroops;
            int required = BalanceConfig.AssaultTroopRatio * effectiveGarrison;
            if (!waived && army.Troops < required) return null;

            CityView view = CityView.From(city);
            return new AttackOption
            {
                ArmyId = army.Id,
                Kind = KindCity,
                X = view.X,
                Z = view.Z,
                CommandPointCost = cost,
                TargetCityId = view.Id,
                TargetName = view.Name,
                TargetOwnerSeatId = view.OwnerSeatId,
                TargetUnitType = UnitTypes.Normalize(view.GarrisonType),
                TargetTroops = view.Garrison,
                DefendingArmyTroops = defendingTroops,
                RequiredTroops = waived ? 0 : required,
                AssaultRatioWaived = waived,
                Note = TreatyNote(engine, fac.SeatId, view.OwnerSeatId),
            };
        }

        /// <summary>
        /// Treaties involving the seat are part of its own observation. The wording names the treaty
        /// only; penalty sizes are balance values the engine applies, not advertised here.
        /// </summary>
        private static string TreatyNote(GameEngine engine, string seatId, string victimSeatId)
        {
            var dip = engine.State.Diplomacy;
            if (dip == null) return null;
            if (dip.FindTreaty(TreatyType.Alliance, seatId, victimSeatId) != null)
                return "alliance in force: attacking breaks it and costs trust and prestige";
            if (dip.FindTreaty(TreatyType.Nap, seatId, victimSeatId) != null)
                return "non-aggression pact in force: attacking breaks it and costs trust and prestige";
            if (dip.FindTreaty(TreatyType.Truce, seatId, victimSeatId) != null)
                return "truce in force: attacking breaks it and costs trust and prestige";
            return null;
        }

        /// <summary>
        /// Nearest first; ties go to assaults (captures), then army id, then z, then x. Total over
        /// distinct options (a hex holds at most one army and one city), so the cap is deterministic.
        /// </summary>
        public static int CompareOptions(AttackOption a, AttackOption b)
        {
            if (a.Distance != b.Distance) return a.Distance.CompareTo(b.Distance);
            bool aCity = a.Kind == KindCity, bCity = b.Kind == KindCity;
            if (aCity != bCity) return aCity ? -1 : 1;
            int byArmy = string.CompareOrdinal(a.ArmyId ?? string.Empty, b.ArmyId ?? string.Empty);
            if (byArmy != 0) return byArmy;
            if (a.Z != b.Z) return a.Z.CompareTo(b.Z);
            return a.X.CompareTo(b.X);
        }

        private static int ClampCap(int value)
        {
            if (value < 1) return 1;
            return value > MaxAdvertisedOptions ? MaxAdvertisedOptions : value;
        }
    }
}
