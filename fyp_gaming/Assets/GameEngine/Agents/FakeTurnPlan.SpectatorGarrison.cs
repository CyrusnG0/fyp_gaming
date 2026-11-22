using System;
using System.Collections.Generic;

namespace LuanShi.Engine
{
    public static partial class FakeTurnPlan
    {
        /// <summary>Command points the three garrison steps need (recruit, convert_garrison, march).</summary>
        public static int GarrisonStepsCommandPoints
            => BalanceConfig.CommandPointCost(ActionType.Recruit)
             + BalanceConfig.CommandPointCost(ActionType.ConvertGarrison)
             + BalanceConfig.CommandPointCost(ActionType.March);

        /// <summary>
        /// Queues three dynamic FAKE replies for one spectator seat turn: recruit, convert_garrison, march.
        /// The spectator queues a whole run before it starts, so each reply is resolved when it is
        /// dispatched, from the seat's own filtered observation, never from raw state. A step whose
        /// precondition fails is replaced by a labelled levy_tax, never by end_turn: an early end_turn
        /// would leave the remaining queued replies to be consumed by the next seat's turn.
        /// </summary>
        public static void EnqueueSpectatorGarrisonSteps(FakeLlmTransport fake, GameEngine engine, string seatId,
            Func<MovementView> movement, int delayMs = 0)
        {
            if (fake == null) throw new ArgumentNullException(nameof(fake));
            if (engine == null) throw new ArgumentNullException(nameof(engine));
            var steps = new SpectatorGarrisonSteps(engine, seatId, movement);
            for (int i = 0; i < 3; i++)
                fake.EnqueueDynamic(_ => steps.NextReply(), delayMs, 120, 20);
        }

        private sealed class SpectatorGarrisonSteps
        {
            private readonly GameEngine _engine;
            private readonly string _seatId;
            private readonly Func<MovementView> _movement;
            private int _step;
            private bool _ready = true;
            private string _cityId;
            private int _cityX, _cityZ;

            public SpectatorGarrisonSteps(GameEngine engine, string seatId, Func<MovementView> movement)
            {
                _engine = engine;
                _seatId = seatId;
                _movement = movement;
            }

            public string NextReply()
            {
                Observation obs = _engine.Observe(_seatId);
                string reply = null;
                if (_ready)
                {
                    if (_step == 0) reply = Recruit(obs);
                    else if (_step == 1) reply = Convert(obs);
                    else reply = March(obs);
                }
                _step++;
                if (reply != null) return reply;
                _ready = false;
                return Fallback(obs);
            }

            private string Recruit(Observation obs)
            {
                if (obs == null || obs.CommandPoints < GarrisonStepsCommandPoints) return null;
                BalanceProfile balance = obs.Balance ?? BalanceProfile.Standard;
                int goldCost = balance.RecruitGoldCost(UnitTypes.Infantry, BalanceConfig.RecruitTroopGain);
                foreach (CityView city in obs.CitiesOf(_seatId))
                {
                    if (city == null || string.IsNullOrEmpty(city.Id)) continue;
                    if (city.Population < BalanceConfig.RecruitPopCost || city.Gold < goldCost) continue;
                    if (city.Garrison > 0 && UnitTypes.Normalize(city.GarrisonType) != UnitTypes.Infantry) continue;
                    ArmyView blocker = ArmyAt(obs, city.X, city.Z);
                    if (blocker != null && (blocker.OwnerSeatId != _seatId
                        || UnitTypes.Normalize(blocker.UnitType) != UnitTypes.Infantry)) continue;
                    _cityId = city.Id; _cityX = city.X; _cityZ = city.Z;
                    return "{\"type\":\"recruit\",\"target_id\":" + MiniJson.Str(city.Id)
                        + ",\"args\":{\"unit_type\":" + MiniJson.Str(UnitTypes.Infantry) + "}"
                        + ",\"summary\":\"" + MiniJson.Escape(Label + ": recruit infantry at " + city.Id) + "\"}";
                }
                return null;
            }

            private string Convert(Observation obs)
            {
                CityView city = obs == null || _cityId == null ? null : obs.FindCity(_cityId);
                if (city == null || city.OwnerSeatId != _seatId || city.Garrison <= 0) return null;
                int amount = Math.Min(BalanceConfig.RecruitTroopGain, city.Garrison);
                return "{\"type\":\"convert_garrison\",\"target_id\":" + MiniJson.Str(city.Id)
                    + ",\"param_a\":" + MiniJson.Num(amount)
                    + ",\"summary\":\"" + MiniJson.Escape(Label + ": convert " + amount + " garrison at " + city.Id)
                    + "\"}";
            }

            private string March(Observation obs)
            {
                if (obs == null || _movement == null) return null;
                ArmyView army = ArmyAt(obs, _cityX, _cityZ);
                if (army == null || army.OwnerSeatId != _seatId) return null;
                MovementView view = _movement();
                if (view == null || view.Armies == null) return null;
                foreach (ArmyMovePlan plan in view.Armies)
                {
                    if (plan == null || plan.ArmyId != army.Id || !plan.CanMarch || plan.Options == null) continue;
                    MoveOption pick = null;
                    foreach (MoveOption option in plan.Options)
                    {
                        if (option == null) continue;
                        if (pick == null) pick = option;
                        if (option.Kind == KindCaptureCity || option.Kind == KindCaptureTower) { pick = option; break; }
                    }
                    if (pick != null) return MarchJson(army.Id, pick.X, pick.Z);
                }
                return null;
            }

            private string Fallback(Observation obs)
            {
                string city = FirstOwnedCity(obs, _seatId);
                if (city == null) return EndTurnJson();
                return "{\"type\":\"levy_tax\",\"target_id\":" + MiniJson.Str(city)
                    + ",\"summary\":\"" + MiniJson.Escape(Label + ": garrison step unavailable, levy instead")
                    + "\"}";
            }

            private static ArmyView ArmyAt(Observation obs, int x, int z)
            {
                if (obs == null || obs.Armies == null) return null;
                foreach (ArmyView army in obs.Armies)
                    if (army != null && army.X == x && army.Z == z) return army;
                return null;
            }
        }
    }
}
