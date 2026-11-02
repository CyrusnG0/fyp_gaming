using System;
using System.Collections.Generic;
using System.Text;

namespace LuanShi.Engine
{
    /// <summary>
    /// One advertised march destination for one own army (LS-015, Contract A). Options are candidates
    /// only — never authorization: the model must still submit a normal <c>march</c> command and the
    /// engine revalidates it. Every field is observable to the seat (destination in sight, own army).
    /// </summary>
    public sealed class MoveOption
    {
        public string ArmyId;
        public int X, Z;
        public int PathCost;
        public string Kind;        // "move" | "capture_neutral_city" | "capture_tower"
        public string CityId;      // only for capture_neutral_city (the city is in sight => observable)
        public string TowerId;     // only for capture_tower
        public string Note;        // short, safe, terrain/visibility words only — never hidden facts
    }

    /// <summary>
    /// The bounded movement plan for one own army. <see cref="CanMarch"/> mirrors the preconditions a
    /// <c>march</c> command needs before destination rules apply (march count + 號令); when it is false
    /// no options are advertised and <see cref="BlockedReason"/> carries a safe typed category.
    /// </summary>
    public sealed class ArmyMovePlan
    {
        public string ArmyId, ArmyName, UnitType;
        public int X, Z;
        public int MarchesLeft;
        public int Budget;                 // BalanceConfig.MarchBudget(season, unitType)
        public bool CanMarch;              // marches left > 0 && orders >= CommandPointCost(March)
        public string BlockedReason;       // "no orders left" | "already marched this season" | null
        public int TotalReachable;         // all valid candidates before the cap; 0 when blocked
        public bool Truncated;             // TotalReachable exceeded the advertised cap
        public IReadOnlyList<MoveOption> Options;   // capped, stable order: captures, cost, z, x
    }

    /// <summary>
    /// The seat-safe movement query result. Read-only by construction: it is built from the same
    /// predicates the march validator uses and never mutates state, spends 號令, rolls RNG or emits.
    /// </summary>
    public sealed class MovementView
    {
        public string SeatId;
        public int Season;
        public int CommandPoints;
        public int MarchOrderCost;
        public IReadOnlyList<ArmyMovePlan> Armies;   // own armies only, ordered by army id

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
            sb.Append(",\"march_order_cost\":").Append(MiniJson.Num(MarchOrderCost));
            sb.Append(",\"armies\":").Append(MiniJson.Array(armies));
            sb.Append('}');
            return sb.ToString();
        }

        private static string PlanJson(ArmyMovePlan plan)
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
            sb.Append(",\"marches_left\":").Append(MiniJson.Num(plan.MarchesLeft));
            sb.Append(",\"budget\":").Append(MiniJson.Num(plan.Budget));
            sb.Append(",\"can_march\":").Append(plan.CanMarch ? "true" : "false");
            sb.Append(",\"blocked_reason\":").Append(MiniJson.Str(plan.BlockedReason));
            sb.Append(",\"total_reachable\":").Append(MiniJson.Num(plan.TotalReachable));
            sb.Append(",\"truncated\":").Append(plan.Truncated ? "true" : "false");
            sb.Append(",\"options\":").Append(MiniJson.Array(options));
            sb.Append('}');
            return sb.ToString();
        }

        private static string OptionJson(MoveOption option)
        {
            var sb = new StringBuilder(160);
            sb.Append('{');
            sb.Append("\"army_id\":").Append(MiniJson.Str(option.ArmyId));
            sb.Append(",\"x\":").Append(MiniJson.Num(option.X));
            sb.Append(",\"z\":").Append(MiniJson.Num(option.Z));
            sb.Append(",\"path_cost\":").Append(MiniJson.Num(option.PathCost));
            sb.Append(",\"kind\":").Append(MiniJson.Str(option.Kind));
            sb.Append(",\"city_id\":").Append(MiniJson.Str(option.CityId));
            sb.Append(",\"tower_id\":").Append(MiniJson.Str(option.TowerId));
            sb.Append(",\"note\":").Append(MiniJson.Str(option.Note));
            sb.Append('}');
            return sb.ToString();
        }
    }

    /// <summary>
    /// Seat-safe candidate query for LS-015. Calls exactly the predicates <c>GameEngine.DoMarch</c>
    /// calls, in the same order, and performs no validation side effects:
    /// <c>Map.IsWalkable</c> → own-cell → <c>InSight</c> → <c>ArmyAt</c> → <c>CityAt</c> → <c>Map.PathCost</c>.
    /// Destination facts (army/city/tower) are read only after the in-sight check, so hidden worlds
    /// cannot influence the result.
    /// </summary>
    public static class MovementPlanner
    {
        public const int DefaultMaxOptionsPerArmy = 12;

        private const int MaxOptionsCap = 40;
        private const string KindMove = "move";
        private const string KindCity = "capture_neutral_city";
        private const string KindTower = "capture_tower";

        public static MovementView Build(GameEngine engine, string seatId,
            int maxOptionsPerArmy = DefaultMaxOptionsPerArmy)
        {
            int cap = ClampCap(maxOptionsPerArmy);
            var view = new MovementView
            {
                SeatId = null,
                Season = engine != null && engine.State != null ? engine.State.Season : 0,
                CommandPoints = 0,
                MarchOrderCost = BalanceConfig.CommandPointCost(ActionType.March),
                Armies = new List<ArmyMovePlan>(),
            };
            if (engine == null || engine.State == null) return view;

            var fac = engine.State.FindFaction(seatId);
            if (fac == null) return view;   // unknown/absent seat => empty view with SeatId null

            view.SeatId = fac.SeatId;
            view.CommandPoints = fac.CommandPoints;
            view.Season = engine.State.Season;

            int marchCost = view.MarchOrderCost;
            var own = new List<ArmyState>();
            foreach (var army in engine.State.Armies)
                if (army != null && army.OwnerSeatId == fac.SeatId) own.Add(army);
            own.Sort((a, b) => string.CompareOrdinal(a.Id ?? string.Empty, b.Id ?? string.Empty));

            var plans = new List<ArmyMovePlan>(own.Count);
            foreach (var army in own)
                plans.Add(BuildPlan(engine, fac, army, cap, marchCost));
            view.Armies = plans;
            return view;
        }

        private static ArmyMovePlan BuildPlan(GameEngine engine, FactionState fac, ArmyState army,
            int cap, int marchCost)
        {
            int marches = UnitTypes.MarchesPerSeason(army.UnitType);
            int marchesLeft = Math.Max(0, marches - army.MarchCountThisSeason);
            bool ordersAvailable = fac.CommandPoints >= marchCost;
            bool canMarch = marchesLeft > 0 && ordersAvailable;

            var plan = new ArmyMovePlan
            {
                ArmyId = army.Id,
                ArmyName = army.Name,
                UnitType = army.UnitType,
                X = army.X,
                Z = army.Z,
                MarchesLeft = marchesLeft,
                Budget = BalanceConfig.MarchBudget(engine.State.Season, army.UnitType),
                CanMarch = canMarch,
                // Safe typed categories only; no hidden ids or occupancy facts.
                BlockedReason = canMarch ? null
                    : (marchesLeft <= 0 ? "already marched this season" : "no orders left"),
                TotalReachable = 0,
                Truncated = false,
                Options = new List<MoveOption>(),
            };

            // A blocked army can execute no march at all, so it advertises no reachable cells
            // (the brute-force equivalence test pins this: Submit rejects every cell).
            if (!canMarch) return plan;
            var map = engine.State.Map;
            if (map == null) return plan;

            var all = new List<MoveOption>();
            for (int z = 0; z < map.Height; z++)
            {
                for (int x = 0; x < map.Width; x++)
                {
                    // Same predicate order as DoMarch's destination checks.
                    if (!map.IsWalkable(x, z)) continue;
                    if (x == army.X && z == army.Z) continue;
                    if (!GameEngine.InSight(engine.State, fac.SeatId, x, z)) continue;

                    var blocker = engine.State.ArmyAt(x, z);
                    if (blocker != null && blocker.Id != army.Id) continue;

                    var cityAtDest = engine.State.CityAt(x, z);
                    if (cityAtDest != null && !cityAtDest.IsNeutral && cityAtDest.OwnerSeatId != fac.SeatId)
                        continue;

                    int cost = map.PathCost(army.X, army.Z, x, z, plan.Budget);
                    if (cost < 0) continue;

                    // Destination-only facts; every cell here is in sight, so this is observable.
                    var towerAtDest = engine.State.WatchTowerAt(x, z);
                    var option = new MoveOption
                    {
                        ArmyId = army.Id,
                        X = x,
                        Z = z,
                        PathCost = cost,
                        Kind = KindMove,
                        CityId = null,
                        TowerId = null,
                        Note = map.Cost[map.Index(x, z)] > 1 ? "terrain cost counted" : null,
                    };
                    if (cityAtDest != null && cityAtDest.IsNeutral)
                    {
                        option.Kind = KindCity;
                        option.CityId = cityAtDest.Id;   // in sight => observable
                        option.Note = "neutral city in sight; march captures on arrival";
                    }
                    else if (towerAtDest != null && towerAtDest.OwnerSeatId != fac.SeatId)
                    {
                        option.Kind = KindTower;
                        option.TowerId = towerAtDest.Id;   // in sight => observable
                        option.Note = "watch tower in sight; march captures on arrival";
                    }
                    all.Add(option);
                }
            }

            // Contract order: captures first, then PathCost, then Z, then X. Stable and total
            // (two options never share a cell), so truncation is deterministic.
            all.Sort(CompareOptions);
            plan.TotalReachable = all.Count;
            int take = Math.Min(cap, all.Count);
            var options = new List<MoveOption>(take);
            for (int i = 0; i < take; i++) options.Add(all[i]);
            plan.Options = options;
            plan.Truncated = all.Count > take;
            return plan;
        }

        private static int CompareOptions(MoveOption a, MoveOption b)
        {
            bool aCapture = a.Kind != KindMove;
            bool bCapture = b.Kind != KindMove;
            if (aCapture != bCapture) return aCapture ? -1 : 1;
            if (a.PathCost != b.PathCost) return a.PathCost.CompareTo(b.PathCost);
            if (a.Z != b.Z) return a.Z.CompareTo(b.Z);
            return a.X.CompareTo(b.X);
        }

        /// <summary>At least one option, at most the hard cap; odd input never throws.</summary>
        private static int ClampCap(int value)
        {
            if (value < 1) return 1;
            return value > MaxOptionsCap ? MaxOptionsCap : value;
        }
    }
}
