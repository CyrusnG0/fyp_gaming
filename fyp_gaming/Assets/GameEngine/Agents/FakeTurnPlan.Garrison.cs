using System;
using System.Collections.Generic;

namespace LuanShi.Engine
{
    public static partial class FakeTurnPlan
    {
        /// <summary>
        /// Builds a self-contained, offline-only config clone for the test-only garrison rehearsal.
        /// Leaves the caller's config untouched, strips API keys/real endpoints, and forces
        /// provider="fake" plus EnableGarrisonConversion=true.
        /// </summary>
        public static LlmConfig GarrisonRehearsalConfig(LlmConfig source = null)
        {
            var copy = new LlmConfig
            {
                Provider = "fake",
                BaseUrl = string.Empty,
                Model = "fake-model",
                ApiKeyEnv = string.Empty,
                JsonMode = true,
                EnableGarrisonConversion = true,
                EnableAttack = source != null && source.EnableAttack,
                EnableResearch = source != null && source.EnableResearch,
                EnableDiplomacy = source != null && source.EnableDiplomacy,
                EnableManualLookup = source != null && source.EnableManualLookup,
                EnablePersonas = source != null && source.EnablePersonas,
                RequireVisibleMarchRoutes = source != null && source.RequireVisibleMarchRoutes,
                SpatialContext = source != null && source.SpatialContext != null
                    ? source.SpatialContext
                    : new SpatialContextOptions(),
                MaxRequestsPerTurn = Math.Max(source != null ? source.MaxRequestsPerTurn : 0, 6),
                MaxActionsPerTurn = Math.Max(source != null ? source.MaxActionsPerTurn : 0, 5),
                MaxInvalidPerTurn = Math.Max(source != null ? source.MaxInvalidPerTurn : 0, 2),
                MaxTurnSeconds = Math.Max(source != null ? source.MaxTurnSeconds : 0, 60),
                RequestTimeoutMs = Math.Max(source != null ? source.RequestTimeoutMs : 0, 25000),
                MaxOutputTokens = Math.Max(source != null ? source.MaxOutputTokens : 0, 400),
                MaxTotalRequests = Math.Max(source != null ? source.MaxTotalRequests : 0, 12),
            };
            if (source != null)
                foreach (var kv in source.PersonaOverrides)
                    copy.SetPersonaOverride(kv.Key, kv.Value);
            return copy;
        }

        /// <summary>
        /// Queues the four dynamic fake-provider steps for one test-only
        /// recruit -&gt; convert_garrison -&gt; march -&gt; end_turn rehearsal through <paramref name="host"/>.
        /// Never calls a real provider; each reply uses <see cref="Label"/> and resolves targets
        /// only from the seat-filtered <see cref="ITurnHost"/>.
        /// </summary>
        public static void EnqueueGarrisonRehearsal(FakeLlmTransport fake, ITurnHost host,
            string seatId, SpatialContextOptions spatialOptions = null, int delayMs = 0)
        {
            if (fake == null) throw new ArgumentNullException(nameof(fake));
            if (host == null) throw new ArgumentNullException(nameof(host));
            fake.Reset();
            var sequence = new GarrisonRehearsalSequence(host, seatId, spatialOptions);
            for (int i = 0; i < 4; i++)
                fake.EnqueueDynamic(_ => sequence.NextReply(), delayMs, 120, 20);
        }

        private sealed class GarrisonRehearsalSequence
        {
            private readonly ITurnHost _host;
            private readonly string _seatId;
            private readonly SpatialContextOptions _spatialOptions;
            private int _step;
            private string _cityId;
            private int _cityX;
            private int _cityZ;

            public GarrisonRehearsalSequence(ITurnHost host, string seatId,
                SpatialContextOptions spatialOptions)
            {
                _host = host;
                _seatId = seatId;
                _spatialOptions = spatialOptions;
            }

            public string NextReply()
            {
                switch (_step)
                {
                    case 0: return StepRecruit();
                    case 1: return StepConvertGarrison();
                    case 2: return StepMarch();
                    default: return EndTurnJson();
                }
            }

            private string StepRecruit()
            {
                Observation obs = _host.Observe();
                CityView city = FindEligibleCity(obs);
                if (city == null)
                {
                    _step = 3;
                    return EndTurnJson();
                }

                _cityId = city.Id;
                _cityX = city.X;
                _cityZ = city.Z;
                _step = 1;
                return "{\"type\":\"recruit\",\"target_id\":" + MiniJson.Str(city.Id)
                    + ",\"args\":{\"unit_type\":" + MiniJson.Str(UnitTypes.Infantry) + "}"
                    + ",\"summary\":\"" + MiniJson.Escape(Label + ": recruit infantry at " + city.Id) + "\"}";
            }

            private string StepConvertGarrison()
            {
                Observation obs = _host.Observe();
                CityView city = FindCity(obs, _cityId);
                int requiredCp = BalanceConfig.CommandPointCost(ActionType.ConvertGarrison)
                               + BalanceConfig.CommandPointCost(ActionType.March);
                if (obs == null || city == null || city.Garrison <= 0 || obs.CommandPoints < requiredCp)
                {
                    _step = 3;
                    return EndTurnJson();
                }

                int amount = Math.Min(BalanceConfig.RecruitTroopGain, city.Garrison);
                _step = 2;
                return "{\"type\":\"convert_garrison\",\"target_id\":" + MiniJson.Str(city.Id)
                    + ",\"param_a\":" + MiniJson.Num(amount)
                    + ",\"summary\":\"" + MiniJson.Escape(
                        Label + ": convert " + amount + " garrison at " + city.Id) + "\"}";
            }

            private string StepMarch()
            {
                _step = 3;
                Observation obs = _host.Observe();
                if (obs == null || string.IsNullOrEmpty(_seatId)
                    || obs.CommandPoints < BalanceConfig.CommandPointCost(ActionType.March))
                    return EndTurnJson();

                ArmyView cityArmy = FindOwnArmyAt(obs, _seatId, _cityX, _cityZ);
                if (cityArmy == null) return EndTurnJson();

                MovementView movement = PlanMovement();
                ArmyMovePlan plan = FindMovePlan(movement, cityArmy.Id);
                if (plan == null || !plan.CanMarch || plan.Options == null || plan.Options.Count == 0)
                    return EndTurnJson();

                MoveOption option = FirstPreferredOption(plan.Options);
                if (option == null) return EndTurnJson();

                return MarchJson(cityArmy.Id, option.X, option.Z);
            }

            private MovementView PlanMovement()
            {
                if (_host is ISpatialPlanningHost spatialHost)
                    return spatialHost.PlanMovement(_spatialOptions);
                if (_host is IPlanningHost planningHost)
                    return planningHost.PlanMovement();
                return null;
            }

            private CityView FindEligibleCity(Observation obs)
            {
                if (obs == null || string.IsNullOrEmpty(_seatId)) return null;
                int requiredCp = BalanceConfig.CommandPointCost(ActionType.Recruit)
                               + BalanceConfig.CommandPointCost(ActionType.ConvertGarrison)
                               + BalanceConfig.CommandPointCost(ActionType.March);
                if (obs.CommandPoints < requiredCp) return null;

                BalanceProfile balance = obs.Balance ?? BalanceProfile.Standard;
                int goldCost = balance.RecruitGoldCost(UnitTypes.Infantry, BalanceConfig.RecruitTroopGain);
                foreach (CityView city in obs.CitiesOf(_seatId))
                {
                    if (city == null || string.IsNullOrEmpty(city.Id)) continue;
                    if (city.Population < BalanceConfig.RecruitPopCost || city.Gold < goldCost) continue;
                    if (city.Garrison > 0 && UnitTypes.Normalize(city.GarrisonType) != UnitTypes.Infantry)
                        continue;

                    ArmyView blocker = FindAnyArmyAt(obs, city.X, city.Z);
                    if (blocker != null)
                    {
                        if (blocker.OwnerSeatId != _seatId) continue;
                        if (UnitTypes.Normalize(blocker.UnitType) != UnitTypes.Infantry) continue;
                        if (blocker.MarchCountThisSeason >= UnitTypes.MarchesPerSeason(blocker.UnitType))
                            continue;
                    }
                    return city;
                }
                return null;
            }

            private static CityView FindCity(Observation obs, string cityId)
            {
                if (obs == null || string.IsNullOrEmpty(cityId)) return null;
                CityView city = obs.FindCity(cityId);
                return city != null && city.OwnerSeatId == obs.SeatId ? city : null;
            }

            private static ArmyView FindAnyArmyAt(Observation obs, int x, int z)
            {
                if (obs == null || obs.Armies == null) return null;
                foreach (ArmyView army in obs.Armies)
                    if (army != null && army.X == x && army.Z == z) return army;
                return null;
            }

            private static ArmyView FindOwnArmyAt(Observation obs, string seatId, int x, int z)
            {
                if (obs == null || string.IsNullOrEmpty(seatId)) return null;
                foreach (ArmyView army in obs.ArmiesOf(seatId))
                    if (army != null && army.X == x && army.Z == z) return army;
                return null;
            }

            private static ArmyMovePlan FindMovePlan(MovementView movement, string armyId)
            {
                if (movement == null || movement.Armies == null || string.IsNullOrEmpty(armyId))
                    return null;
                foreach (ArmyMovePlan plan in movement.Armies)
                    if (plan != null && string.Equals(plan.ArmyId, armyId, StringComparison.Ordinal))
                        return plan;
                return null;
            }

            private static MoveOption FirstPreferredOption(IReadOnlyList<MoveOption> options)
            {
                MoveOption fallback = null;
                foreach (MoveOption option in options)
                {
                    if (option == null) continue;
                    if (fallback == null) fallback = option;
                    if (option.Kind == KindCaptureCity || option.Kind == KindCaptureTower)
                        return option;
                }
                return fallback;
            }
        }
    }
}
