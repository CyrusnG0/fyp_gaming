using System;
using System.Collections.Generic;

namespace LuanShi.Engine
{
    /// <summary>
    /// The authoritative game core. Only this class mutates canonical state; every
    /// controller (human UI, LLM, scripted) submits ActionCommands through Submit(),
    /// which validates and executes them (CONTROLLER_PROTOCOL.md §2).
    ///
    /// Season flow: BeginSeason() → seats submit orders → EndSeason() resolves the world.
    /// Pure C#, no UnityEngine dependencies, deterministic under a fixed seed.
    /// </summary>
    public sealed class GameEngine
    {
        public readonly GameState State = new GameState();
        public readonly EventLog Log = new EventLog();

        private readonly Random rng;
        private int nextActionId = 1;

        public GameEngine(int seed, string gameId = "g-001")
        {
            rng = new Random(seed);
            State.GameId = gameId;
        }

        // ---- season lifecycle ------------------------------------------------

        public void BeginSeason()
        {
            State.Season++;
            State.Phase = "player_orders";
            foreach (var f in State.Factions) f.CommandPoints = BalanceConfig.CommandPointsPerSeason;
            foreach (var c in State.Cities) c.ReclaimedThisSeason = false;
            foreach (var a in State.Armies) a.MarchedThisSeason = false;
            Emit(null, null, "season_start", "public", P("season", State.Season.ToString()), null);
        }

        /// <summary>Resolution: taxes, harvests, consumption, growth, starvation.</summary>
        public SeasonReport EndSeason()
        {
            State.Phase = "resolution";
            var report = new SeasonReport { Season = State.Season };

            foreach (var city in State.Cities)
            {
                if (city.IsNeutral) continue;
                var fac = State.FindFaction(city.OwnerSeatId);

                // 民心 below the threshold halves both yields (ADR-010, LOCKED).
                bool disheartened = city.Morale < BalanceConfig.MoraleYieldPenaltyThreshold;
                float yieldFactor = disheartened ? 1f - BalanceConfig.MoraleYieldPenalty : 1f;

                int taxes = (int)(city.Population * BalanceConfig.TaxGoldPerPop * yieldFactor);
                city.Gold += taxes;

                int harvest = (int)(BalanceConfig.CityBaseFoodYield
                                  * (1f + city.ReclaimPct / 100f) * yieldFactor);
                city.Food += harvest;
                int eaten = (int)(city.Population * BalanceConfig.FoodPerPop
                                + city.Garrison * BalanceConfig.FoodPerGarrisonTroop);
                city.Food -= eaten;

                string note = null;
                if (city.Food < 0)
                {
                    int lost = (int)(city.Population * BalanceConfig.StarvationPopLoss);
                    city.Population -= lost;
                    city.Food = 0;
                    note = $"饑荒！人口減{lost}";
                    Emit(city.OwnerSeatId, fac?.Controller, "starvation", "public",
                        P("city_id", city.Id, "pop_lost", lost.ToString()), null);
                }
                else
                {
                    float rate = BalanceConfig.PopGrowthRate
                               + (float)(rng.NextDouble() * 2 - 1) * BalanceConfig.PopGrowthVariance;
                    city.Population += (int)(city.Population * rate);
                }

                report.Lines.Add($"{city.Name}：人口{city.Population}，糧{city.Food}，金{city.Gold}，駐軍{city.Garrison}，民心{city.Morale}"
                               + (disheartened ? $"（民心<{BalanceConfig.MoraleYieldPenaltyThreshold}，產出減半）" : "")
                               + (note != null ? $"。{note}" : ""));
                Emit(city.OwnerSeatId, fac?.Controller, "city_resolved", "owner_and_observers",
                    P("city_id", city.Id, "pop", city.Population.ToString(),
                      "food", city.Food.ToString(), "gold", city.Gold.ToString(),
                      "garrison", city.Garrison.ToString(),
                      "morale", city.Morale.ToString(),
                      "reclaim_pct", city.ReclaimPct.ToString()), null);
            }

            Emit(null, null, "season_end", "public", P("season", State.Season.ToString()), null);
            return report;
        }

        // ---- command pipeline --------------------------------------------------

        /// <summary>The one entry point for every controller. Validates, executes, logs.</summary>
        public ValidationResult Submit(ActionCommand cmd)
        {
            if (cmd == null) return Reject(cmd, "null command");
            cmd.ActionId = "a-" + nextActionId++;

            var fac = State.FindFaction(cmd.ActorSeatId);
            if (fac == null) return Reject(cmd, $"unknown seat '{cmd.ActorSeatId}'");
            cmd.ControllerType = fac.Controller;
            if (cmd.Type == ActionType.Farm) cmd.Type = ActionType.Reclaim;   // legacy alias
            if (State.Phase != "player_orders") return Reject(cmd, $"not in orders phase ({State.Phase})");
            if (fac.CommandPoints <= 0) return Reject(cmd, "no command points left");

            ValidationResult r;
            switch (cmd.Type)
            {
                case ActionType.Reclaim: r = DoReclaim(fac, cmd); break;
                case ActionType.Recruit: r = DoRecruit(fac, cmd); break;
                case ActionType.March: r = DoMarch(fac, cmd); break;
                default: r = ValidationResult.Fail($"unknown action type '{cmd.Type}'"); break;
            }

            if (!r.Ok) return Reject(cmd, r.Error);

            fac.CommandPoints--;
            Emit(fac.SeatId, fac.Controller, "action", "public",
                P("action", cmd.Type, "target_id", cmd.TargetId ?? "",
                  "param_a", cmd.ParamA.ToString(), "param_b", cmd.ParamB.ToString(),
                  "reason", cmd.Reason ?? ""), cmd.ActionId, cmd.Args, cmd.Text);
            return r;
        }

        /// <summary>D3 開墾: +ReclaimPctPerUse food yield, cumulative up to ReclaimPctCap.</summary>
        private ValidationResult DoReclaim(FactionState fac, ActionCommand cmd)
        {
            var city = State.FindCity(cmd.TargetId);
            if (city == null) return ValidationResult.Fail($"unknown city '{cmd.TargetId}'");
            if (city.OwnerSeatId != fac.SeatId) return ValidationResult.Fail("not your city");
            if (city.ReclaimedThisSeason) return ValidationResult.Fail("city already reclaimed this season");
            if (city.ReclaimPct >= BalanceConfig.ReclaimPctCap)
                return ValidationResult.Fail($"land fully reclaimed (cap {BalanceConfig.ReclaimPctCap}%)");

            int gained = Math.Min(BalanceConfig.ReclaimPctPerUse,
                                  BalanceConfig.ReclaimPctCap - city.ReclaimPct);
            city.ReclaimPct += gained;
            city.ReclaimedThisSeason = true;
            Emit(fac.SeatId, fac.Controller, "land_reclaimed", "owner_and_observers",
                P("city_id", city.Id, "gain_pct", gained.ToString(),
                  "reclaim_pct", city.ReclaimPct.ToString()), cmd.ActionId);
            return ValidationResult.Success;
        }

        private ValidationResult DoRecruit(FactionState fac, ActionCommand cmd)
        {
            var city = State.FindCity(cmd.TargetId);
            if (city == null) return ValidationResult.Fail($"unknown city '{cmd.TargetId}'");
            if (city.OwnerSeatId != fac.SeatId) return ValidationResult.Fail("not your city");
            if (city.Gold < BalanceConfig.RecruitGoldCost) return ValidationResult.Fail("not enough gold");
            if (city.Population < BalanceConfig.RecruitPopCost) return ValidationResult.Fail("not enough population");

            city.Gold -= BalanceConfig.RecruitGoldCost;
            city.Population -= BalanceConfig.RecruitPopCost;
            city.Garrison += BalanceConfig.RecruitTroopGain;
            Emit(fac.SeatId, fac.Controller, "recruit", "owner_and_observers",
                P("city_id", city.Id, "troops", BalanceConfig.RecruitTroopGain.ToString()), cmd.ActionId);
            return ValidationResult.Success;
        }

        private ValidationResult DoMarch(FactionState fac, ActionCommand cmd)
        {
            var army = State.FindArmy(cmd.TargetId);
            if (army == null) return ValidationResult.Fail($"unknown army '{cmd.TargetId}'");
            if (army.OwnerSeatId != fac.SeatId) return ValidationResult.Fail("not your army");
            if (army.MarchedThisSeason) return ValidationResult.Fail("army already marched this season");

            int tx = cmd.ParamA, tz = cmd.ParamB;
            if (!State.Map.IsWalkable(tx, tz)) return ValidationResult.Fail("destination not walkable");
            if (tx == army.X && tz == army.Z) return ValidationResult.Fail("already there");

            var blocker = State.ArmyAt(tx, tz);
            if (blocker != null && blocker.Id != army.Id) return ValidationResult.Fail("destination occupied by an army");

            var cityAtDest = State.CityAt(tx, tz);
            if (cityAtDest != null && !cityAtDest.IsNeutral && cityAtDest.OwnerSeatId != fac.SeatId)
                return ValidationResult.Fail("enemy city — battle is beyond this demo");

            int cost = State.Map.PathCost(army.X, army.Z, tx, tz, BalanceConfig.ArmyMoveCostPerSeason);
            if (cost < 0) return ValidationResult.Fail(
                $"destination out of reach this season (max cost {BalanceConfig.ArmyMoveCostPerSeason})");

            int fx = army.X, fz = army.Z;
            army.X = tx; army.Z = tz;
            army.MarchedThisSeason = true;
            Emit(fac.SeatId, fac.Controller, "army_moved", "owner_and_observers",
                P("army_id", army.Id, "from", $"[{fx},{fz}]", "to", $"[{tx},{tz}]", "cost", cost.ToString()),
                cmd.ActionId);

            if (cityAtDest != null && cityAtDest.IsNeutral)
            {
                cityAtDest.OwnerSeatId = fac.SeatId;
                Emit(fac.SeatId, fac.Controller, "city_captured", "public",
                    P("city_id", cityAtDest.Id, "army_id", army.Id), cmd.ActionId);
            }
            return ValidationResult.Success;
        }

        // ---- helpers -----------------------------------------------------------

        private ValidationResult Reject(ActionCommand cmd, string why)
        {
            Emit(cmd?.ActorSeatId, cmd?.ControllerType, "action_rejected", "owner_only",
                P("action", cmd?.Type ?? "", "reason", why), cmd?.ActionId, cmd?.Args, cmd?.Text);
            return ValidationResult.Fail(why);
        }

        private EventRecord Emit(string seatId, string controller, string type,
            string visibility, Dictionary<string, string> payload, string sourceActionId,
            Dictionary<string, string> args = null, string text = null)
            => Log.Emit(State.GameId, State.Season, State.Phase, seatId, controller, type,
                        visibility, payload, sourceActionId, args, text);

        private static Dictionary<string, string> P(params string[] kv)
        {
            var d = new Dictionary<string, string>();
            for (int i = 0; i + 1 < kv.Length; i += 2) d[kv[i]] = kv[i + 1];
            return d;
        }
    }

    public sealed class SeasonReport
    {
        public int Season;
        public readonly List<string> Lines = new List<string>();
    }
}
