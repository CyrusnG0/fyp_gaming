using System;
using System.Collections.Generic;
using System.Text;

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

            // 士氣 regrows between campaigns up to its start value (design §5.1: 糧足/訓練 restore it;
            // the design gives no other army-morale recovery, and 威望 has a comparable slow regression).
            foreach (var army in State.Armies)
                if (army.Morale < BalanceConfig.ArmyMoraleStart)
                    army.Morale = Math.Min(BalanceConfig.ArmyMoraleStart,
                                           army.Morale + BalanceConfig.ArmyMoraleRegainPerSeason);

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

            // Cost is checked up front and only charged on success: a rejected order stays free.
            int cpCost = BalanceConfig.CommandPointCost(cmd.Type);
            if (fac.CommandPoints < cpCost) return Reject(cmd, "no command points left");

            ValidationResult r;
            switch (cmd.Type)
            {
                case ActionType.LevyTax: r = DoLevyTax(fac, cmd); break;
                case ActionType.LightenLabor: r = DoLightenLabor(fac, cmd); break;
                case ActionType.Reclaim: r = DoReclaim(fac, cmd); break;
                case ActionType.Recruit: r = DoRecruit(fac, cmd); break;
                case ActionType.Train: r = DoTrain(fac, cmd); break;
                case ActionType.Fortify: r = DoFortify(fac, cmd); break;
                case ActionType.Relief: r = DoRelief(fac, cmd); break;
                case ActionType.March: r = DoMarch(fac, cmd); break;
                case ActionType.AttackArmy: r = DoAttackArmy(fac, cmd); break;
                case ActionType.AttackCity: r = DoAttackCity(fac, cmd); break;
                default: r = ValidationResult.Fail($"unknown action type '{cmd.Type}'"); break;
            }

            if (!r.Ok) return Reject(cmd, r.Error);

            fac.CommandPoints -= cpCost;
            Emit(fac.SeatId, fac.Controller, "action", "public",
                P("action", cmd.Type, "target_id", cmd.TargetId ?? "",
                  "param_a", cmd.ParamA.ToString(), "param_b", cmd.ParamB.ToString(),
                  "reason", cmd.Reason ?? ""), cmd.ActionId, cmd.Args, cmd.Text);
            return r;
        }

        // ---- 內政 D1-D7 (ACTIONS.md §3, all target_id = own city) ---------------

        /// <summary>D1 徵稅: gold now, 民心 pays for it.</summary>
        private ValidationResult DoLevyTax(FactionState fac, ActionCommand cmd)
        {
            ValidationResult check = OwnCity(fac, cmd, out CityState city);
            if (!check.Ok) return check;

            city.Gold += BalanceConfig.TaxLevyAmount;
            AddMorale(city, -BalanceConfig.TaxLevyMoraleCost);
            Emit(fac.SeatId, fac.Controller, "tax_levied", "public",
                P("city_id", city.Id, "gold_gained", BalanceConfig.TaxLevyAmount.ToString(),
                  "morale", city.Morale.ToString()), cmd.ActionId);
            return ValidationResult.Success;
        }

        /// <summary>D2 輕徭: gold buys 民心.</summary>
        private ValidationResult DoLightenLabor(FactionState fac, ActionCommand cmd)
        {
            ValidationResult check = OwnCity(fac, cmd, out CityState city);
            if (!check.Ok) return check;
            if (city.Gold < BalanceConfig.LightenLaborGoldCost)
                return ValidationResult.Fail($"not enough gold (needs {BalanceConfig.LightenLaborGoldCost})");

            city.Gold -= BalanceConfig.LightenLaborGoldCost;
            AddMorale(city, BalanceConfig.LightenLaborMoraleGain);
            Emit(fac.SeatId, fac.Controller, "labor_lightened", "owner_and_observers",
                P("city_id", city.Id, "gold_spent", BalanceConfig.LightenLaborGoldCost.ToString(),
                  "morale", city.Morale.ToString()), cmd.ActionId);
            return ValidationResult.Success;
        }

        /// <summary>D3 開墾: +ReclaimPctPerUse food yield, cumulative up to ReclaimPctCap.</summary>
        private ValidationResult DoReclaim(FactionState fac, ActionCommand cmd)
        {
            ValidationResult check = OwnCity(fac, cmd, out CityState city);
            if (!check.Ok) return check;
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

        /// <summary>D4 募兵: gold + population become garrison.</summary>
        private ValidationResult DoRecruit(FactionState fac, ActionCommand cmd)
        {
            ValidationResult check = OwnCity(fac, cmd, out CityState city);
            if (!check.Ok) return check;
            if (city.Gold < BalanceConfig.RecruitGoldCost)
                return ValidationResult.Fail($"not enough gold (needs {BalanceConfig.RecruitGoldCost})");
            if (city.Population < BalanceConfig.RecruitPopCost)
                return ValidationResult.Fail($"not enough population (needs {BalanceConfig.RecruitPopCost})");

            city.Gold -= BalanceConfig.RecruitGoldCost;
            city.Population -= BalanceConfig.RecruitPopCost;
            city.Garrison += BalanceConfig.RecruitTroopGain;
            Emit(fac.SeatId, fac.Controller, "recruit", "owner_and_observers",
                P("city_id", city.Id, "troops", BalanceConfig.RecruitTroopGain.ToString()), cmd.ActionId);
            return ValidationResult.Success;
        }

        /// <summary>D5 練兵: garrison training +1 (the §6.1 combat bonus lands with M2/M3).</summary>
        private ValidationResult DoTrain(FactionState fac, ActionCommand cmd)
        {
            ValidationResult check = OwnCity(fac, cmd, out CityState city);
            if (!check.Ok) return check;
            if (city.Training >= BalanceConfig.TrainingMax)
                return ValidationResult.Fail($"training already at max ({BalanceConfig.TrainingMax})");

            city.Training++;
            Emit(fac.SeatId, fac.Controller, "troops_trained", "owner_and_observers",
                P("city_id", city.Id, "training", city.Training.ToString()), cmd.ActionId);
            return ValidationResult.Success;
        }

        /// <summary>D6 修城: 城防 +1 (the §4.1 siege effect lands with M3).</summary>
        private ValidationResult DoFortify(FactionState fac, ActionCommand cmd)
        {
            ValidationResult check = OwnCity(fac, cmd, out CityState city);
            if (!check.Ok) return check;
            if (city.Defense >= BalanceConfig.DefenseMax)
                return ValidationResult.Fail($"defense already at max ({BalanceConfig.DefenseMax})");

            city.Defense++;
            Emit(fac.SeatId, fac.Controller, "city_fortified", "owner_and_observers",
                P("city_id", city.Id, "defense", city.Defense.ToString()), cmd.ActionId);
            return ValidationResult.Success;
        }

        /// <summary>D7 賑災: food buys 民心 back.</summary>
        private ValidationResult DoRelief(FactionState fac, ActionCommand cmd)
        {
            ValidationResult check = OwnCity(fac, cmd, out CityState city);
            if (!check.Ok) return check;
            if (city.Food < BalanceConfig.ReliefFoodCost)
                return ValidationResult.Fail($"not enough food (needs {BalanceConfig.ReliefFoodCost})");

            city.Food -= BalanceConfig.ReliefFoodCost;
            AddMorale(city, BalanceConfig.ReliefMoraleGain);
            Emit(fac.SeatId, fac.Controller, "disaster_relieved", "owner_and_observers",
                P("city_id", city.Id, "food_spent", BalanceConfig.ReliefFoodCost.ToString(),
                  "morale", city.Morale.ToString()), cmd.ActionId);
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

        /// <summary>Resolves target_id to one of the acting faction's own cities.</summary>
        private ValidationResult OwnCity(FactionState fac, ActionCommand cmd, out CityState city)
        {
            city = State.FindCity(cmd.TargetId);
            if (city == null) return ValidationResult.Fail($"unknown city '{cmd.TargetId}'");
            if (city.OwnerSeatId != fac.SeatId) return ValidationResult.Fail("not your city");
            return ValidationResult.Success;
        }

        /// <summary>民心 changes always clamp to the configured range.</summary>
        private static void AddMorale(CityState city, int delta)
            => city.Morale = Clamp(city.Morale + delta, BalanceConfig.MoraleMin, BalanceConfig.MoraleMax);

        private static int Clamp(int value, int min, int max)
            => value < min ? min : value > max ? max : value;

        // ---- 野戰 / 攻城 M2-M3 (ACTIONS.md §4, design §6) -----------------------

        /// <summary>M2 野戰: field battle against an adjacent enemy army (param_a/b = its hex).</summary>
        private ValidationResult DoAttackArmy(FactionState fac, ActionCommand cmd)
        {
            var army = State.FindArmy(cmd.TargetId);
            if (army == null) return ValidationResult.Fail($"unknown army '{cmd.TargetId}'");
            if (army.OwnerSeatId != fac.SeatId) return ValidationResult.Fail("not your army");
            if (army.Morale <= BalanceConfig.CombatRoutMoraleThreshold)
                return ValidationResult.Fail("army morale is broken — it cannot attack");

            var defender = State.ArmyAt(cmd.ParamA, cmd.ParamB);
            if (defender == null) return ValidationResult.Fail($"no army at [{cmd.ParamA},{cmd.ParamB}]");
            if (defender.OwnerSeatId == fac.SeatId) return ValidationResult.Fail("that is your own army");
            if (!State.Map.AreAdjacent(army.X, army.Z, defender.X, defender.Z))
                return ValidationResult.Fail("target army is not adjacent");

            string legitimacy = ApplyAttackReputation(fac, defender.OwnerSeatId, cmd);

            int attackerTraining = TrainingOf(fac.SeatId), defenderTraining = TrainingOf(defender.OwnerSeatId);
            int attackerBefore = army.Troops, defenderBefore = defender.Troops;
            var roundLog = new StringBuilder();
            int round = 0;
            while (round < BalanceConfig.CombatRounds
                   && army.Troops > 0 && defender.Troops > 0
                   && army.Morale > BalanceConfig.CombatRoutMoraleThreshold
                   && defender.Morale > BalanceConfig.CombatRoutMoraleThreshold)
            {
                round++;
                int attackerStart = army.Troops, defenderStart = defender.Troops;

                // Design §6.2: each side takes enemy 戰力 × CombatLossPerRound that round.
                int toAttacker = CapLoss(Vary(Strength(defender.Troops, defenderTraining, defender.Morale)
                                              * BalanceConfig.CombatLossPerRound), army.Troops);
                int toDefender = CapLoss(Vary(Strength(army.Troops, attackerTraining, army.Morale)
                                              * BalanceConfig.CombatLossPerRound), defender.Troops);

                army.Troops -= toAttacker;
                defender.Troops -= toDefender;
                army.Morale = Clamp(army.Morale - MoraleLoss(toAttacker, attackerStart),
                                    BalanceConfig.MoraleMin, BalanceConfig.MoraleMax);
                defender.Morale = Clamp(defender.Morale - MoraleLoss(toDefender, defenderStart),
                                        BalanceConfig.MoraleMin, BalanceConfig.MoraleMax);

                if (round > 1) roundLog.Append(';');
                roundLog.Append($"a-{toAttacker}/d-{toDefender}");
            }

            bool attackerBroken = army.Morale <= BalanceConfig.CombatRoutMoraleThreshold;
            bool defenderBroken = defender.Morale <= BalanceConfig.CombatRoutMoraleThreshold;
            string outcome;
            if (army.Troops <= 0 && defender.Troops <= 0) outcome = "mutual_destruction";
            else if (defender.Troops <= 0) outcome = "defender_destroyed";
            else if (army.Troops <= 0) outcome = "attacker_destroyed";
            else if (attackerBroken && defenderBroken) outcome = "mutual_rout";
            else if (attackerBroken) outcome = "attacker_routed";
            else if (defenderBroken) outcome = "defender_routed";
            else outcome = "defender_holds";     // 5 rounds, nobody broken → 守方留場 (design §6.2)

            string winnerSeatId =
                outcome == "defender_destroyed" || outcome == "defender_routed" ? fac.SeatId
                : outcome == "attacker_destroyed" || outcome == "attacker_routed" ? defender.OwnerSeatId
                : outcome == "defender_holds" ? defender.OwnerSeatId
                : null;                          // mutual destruction / mutual rout: nobody wins

            Emit(fac.SeatId, fac.Controller, "battle_field", "public",
                P("attacker_army_id", army.Id, "defender_army_id", defender.Id,
                  "attacker_seat_id", fac.SeatId, "defender_seat_id", defender.OwnerSeatId,
                  "attacker_troops_before", attackerBefore.ToString(), "defender_troops_before", defenderBefore.ToString(),
                  "attacker_troops_after", army.Troops.ToString(), "defender_troops_after", defender.Troops.ToString(),
                  "attacker_losses", (attackerBefore - army.Troops).ToString(),
                  "defender_losses", (defenderBefore - defender.Troops).ToString(),
                  "rounds", round.ToString(), "round_log", roundLog.ToString(),
                  "attacker_morale", army.Morale.ToString(), "defender_morale", defender.Morale.ToString(),
                  "attacker_training", attackerTraining.ToString(), "defender_training", defenderTraining.ToString(),
                  "outcome", outcome, "winner_seat_id", winnerSeatId ?? "",
                  "attack_legitimacy", legitimacy), cmd.ActionId);

            ApplyBattleConsequences(fac, army, defender, outcome, winnerSeatId, cmd);
            return ValidationResult.Success;
        }

        /// <summary>M3 攻城: assault-only, resolved once in the season it is launched (ADR-010 #4).</summary>
        private ValidationResult DoAttackCity(FactionState fac, ActionCommand cmd)
        {
            var army = State.FindArmy(cmd.TargetId);
            if (army == null) return ValidationResult.Fail($"unknown army '{cmd.TargetId}'");
            if (army.OwnerSeatId != fac.SeatId) return ValidationResult.Fail("not your army");
            if (army.Morale <= BalanceConfig.CombatRoutMoraleThreshold)
                return ValidationResult.Fail("army morale is broken — it cannot attack");

            var city = State.CityAt(cmd.ParamA, cmd.ParamB);
            if (city == null) return ValidationResult.Fail($"no city at [{cmd.ParamA},{cmd.ParamB}]");
            if (city.IsNeutral) return ValidationResult.Fail("neutral cities are taken by marching into them");
            if (city.OwnerSeatId == fac.SeatId) return ValidationResult.Fail("that is your own city");
            if (!State.Map.AreAdjacent(army.X, army.Z, city.X, city.Z))
                return ValidationResult.Fail("target city is not adjacent");
            if (army.Troops < BalanceConfig.AssaultTroopRatio * city.Garrison)
                return ValidationResult.Fail(
                    $"assault needs {BalanceConfig.AssaultTroopRatio}:1 troops (have {army.Troops}, garrison {city.Garrison})");

            string legitimacy = ApplyAttackReputation(fac, city.OwnerSeatId, cmd);

            int training = TrainingOf(fac.SeatId);
            int troopsBefore = army.Troops, garrisonBefore = city.Garrison;
            int garrisonTraining = city.Training, defense = city.Defense;
            var roundLog = new StringBuilder();
            int round = 0;
            while (round < BalanceConfig.CombatRounds
                   && city.Garrison > 0 && army.Troops > 0
                   && army.Morale > BalanceConfig.CombatRoutMoraleThreshold)
            {
                round++;
                int troopsStart = army.Troops;

                int toArmy = CapLoss(Vary(GarrisonStrength(city) * BalanceConfig.CombatLossPerRound), army.Troops);
                int toGarrison = CapLoss(Vary(Strength(army.Troops, training, army.Morale)
                                              * BalanceConfig.CombatLossPerRound), city.Garrison);

                army.Troops -= toArmy;
                city.Garrison -= toGarrison;
                army.Morale = Clamp(army.Morale - MoraleLoss(toArmy, troopsStart),
                                    BalanceConfig.MoraleMin, BalanceConfig.MoraleMax);

                if (round > 1) roundLog.Append(';');
                roundLog.Append($"a-{toArmy}/g-{toGarrison}");
            }

            string outcome;
            if (city.Garrison <= 0) outcome = "captured";
            else if (army.Troops <= 0) outcome = "assault_failed";
            else if (army.Morale <= BalanceConfig.CombatRoutMoraleThreshold) outcome = "attacker_routed";
            else outcome = "city_held";         // 5 rounds without a breach → 守方留場 (design §6.2)

            Emit(fac.SeatId, fac.Controller, "battle_siege", "public",
                P("city_id", city.Id, "army_id", army.Id,
                  "attacker_seat_id", fac.SeatId, "defender_seat_id", city.OwnerSeatId,
                  "garrison_before", garrisonBefore.ToString(), "garrison_after", city.Garrison.ToString(),
                  "troops_before", troopsBefore.ToString(), "troops_after", army.Troops.ToString(),
                  "army_losses", (troopsBefore - army.Troops).ToString(),
                  "garrison_losses", (garrisonBefore - city.Garrison).ToString(),
                  "rounds", round.ToString(), "round_log", roundLog.ToString(),
                  "attacker_morale", army.Morale.ToString(),
                  "defense", defense.ToString(), "city_training", garrisonTraining.ToString(),
                  "outcome", outcome, "attack_legitimacy", legitimacy), cmd.ActionId);

            if (outcome == "captured")
            {
                city.OwnerSeatId = fac.SeatId;
                city.Garrison = 0;
                city.Morale = BalanceConfig.MoraleMin;    // 佔領後民心歸零 (design §4.3)
                if (State.ArmyAt(city.X, city.Z) == null) { army.X = city.X; army.Z = city.Z; }   // occupy

                AddPrestige(fac, BalanceConfig.PrestigeCityCaptured, "city_captured", cmd);
                Emit(fac.SeatId, fac.Controller, "city_captured", "public",
                    P("city_id", city.Id, "army_id", army.Id), cmd.ActionId);
                if (army.Troops <= 0) DestroyArmy(army, "assault_pyrrhic", cmd);
            }
            else if (outcome == "attacker_routed") RoutArmy(army, "assault", cmd);
            else if (outcome == "assault_failed") DestroyArmy(army, "assault", cmd);

            return ValidationResult.Success;
        }

        /// <summary>Applies routs, destructions and the winner's spoils after a field battle.</summary>
        private void ApplyBattleConsequences(FactionState fac, ArmyState attacker, ArmyState defender,
            string outcome, string winnerSeatId, ActionCommand cmd)
        {
            if (outcome == "attacker_routed" || outcome == "mutual_rout") RoutArmy(attacker, "field_battle", cmd);
            if (outcome == "defender_routed" || outcome == "mutual_rout") RoutArmy(defender, "field_battle", cmd);
            if (outcome == "attacker_destroyed" || outcome == "mutual_destruction") DestroyArmy(attacker, "battle", cmd);
            if (outcome == "defender_destroyed" || outcome == "mutual_destruction") DestroyArmy(defender, "battle", cmd);

            if (winnerSeatId == null) return;

            var winner = State.FindFaction(winnerSeatId);
            var survivor = winnerSeatId == fac.SeatId ? attacker : defender;
            if (State.FindArmy(survivor.Id) != null)
                survivor.Morale = Clamp(survivor.Morale + BalanceConfig.CombatVictoryMoraleGain,
                                        BalanceConfig.MoraleMin, BalanceConfig.MoraleMax);
            if (winner != null) AddPrestige(winner, BalanceConfig.PrestigeBattleVictory, "battle_won", cmd);
        }

        /// <summary>
        /// Locked decision #7: aggression is priced from diplomacy state at execution time, never by a
        /// separate action — attacking an ally (背盟) costs most, breaking a truce less, an undeclared
        /// attack (偷襲) least-but-still-public. Any of them starts the war.
        /// </summary>
        private string ApplyAttackReputation(FactionState attacker, string victimSeatId, ActionCommand cmd)
        {
            var dip = State.Diplomacy;
            var alliance = dip.FindTreaty(TreatyType.Alliance, attacker.SeatId, victimSeatId);
            var nap = dip.FindTreaty(TreatyType.Nap, attacker.SeatId, victimSeatId);
            var truce = dip.FindTreaty(TreatyType.Truce, attacker.SeatId, victimSeatId);

            string legitimacy;
            int trustPenalty, prestigePenalty, thirdPartyPenalty = 0;

            if (alliance != null || nap != null)
            {
                legitimacy = alliance != null ? "betrayed_alliance" : "broke_non_aggression";
                trustPenalty = BalanceConfig.BetrayalTrustPenalty;
                prestigePenalty = BalanceConfig.BetrayalPrestigePenalty;
                thirdPartyPenalty = BalanceConfig.BetrayalThirdPartyTrustPenalty;
                dip.BreakTreaty(alliance ?? nap);
            }
            else if (truce != null)
            {
                legitimacy = "broke_truce";
                trustPenalty = BalanceConfig.TruceBreakTrustPenalty;
                prestigePenalty = BalanceConfig.TruceBreakPrestigePenalty;
                dip.BreakTreaty(truce);
            }
            else if (dip.IsAtWar(attacker.SeatId, victimSeatId))
            {
                legitimacy = "declared_war";
                trustPenalty = 0;
                prestigePenalty = 0;
            }
            else
            {
                legitimacy = "sneak_attack";
                trustPenalty = BalanceConfig.SurpriseAttackTrustPenalty;
                prestigePenalty = BalanceConfig.SurpriseAttackPrestigePenalty;
            }

            // 信任 is directional and reads "the victim's (and bystanders') view of the attacker".
            if (trustPenalty != 0)
                AddTrustEvent(victimSeatId, attacker.SeatId, -trustPenalty, legitimacy, cmd);
            foreach (var other in State.Factions)
                if (thirdPartyPenalty != 0 && other.SeatId != attacker.SeatId && other.SeatId != victimSeatId)
                    AddTrustEvent(other.SeatId, attacker.SeatId, -thirdPartyPenalty, "bystander_" + legitimacy, cmd);
            if (prestigePenalty != 0) AddPrestige(attacker, -prestigePenalty, legitimacy, cmd);

            if (!dip.IsAtWar(attacker.SeatId, victimSeatId))
            {
                dip.DeclareWar(attacker.SeatId, victimSeatId);
                Emit(attacker.SeatId, attacker.Controller, "war_declared", "public",
                    P("attacker_seat_id", attacker.SeatId, "defender_seat_id", victimSeatId,
                      "cause", legitimacy), cmd.ActionId);
            }
            return legitimacy;
        }

        private void AddTrustEvent(string fromSeatId, string toSeatId, int delta, string reason, ActionCommand cmd)
        {
            State.Diplomacy.AddTrust(fromSeatId, toSeatId, delta);
            Emit(fromSeatId, State.FindFaction(fromSeatId)?.Controller, "trust_changed", "owner_and_observers",
                P("from_seat_id", fromSeatId, "to_seat_id", toSeatId, "delta", delta.ToString(),
                  "trust", State.Diplomacy.GetTrust(fromSeatId, toSeatId).ToString(), "reason", reason),
                cmd.ActionId);
        }

        private void AddPrestige(FactionState fac, int delta, string reason, ActionCommand cmd)
        {
            fac.Prestige = Clamp(fac.Prestige + delta, BalanceConfig.PrestigeMin, BalanceConfig.PrestigeMax);
            Emit(fac.SeatId, fac.Controller, "prestige_changed", "public",
                P("seat_id", fac.SeatId, "delta", delta.ToString(),
                  "prestige", fac.Prestige.ToString(), "reason", reason), cmd.ActionId);
        }

        /// <summary>Design §6.1 戰力, reduced to the fields the MVP has: troops, 訓練 and 士氣.</summary>
        private static int Strength(int troops, int training, int morale)
            => (int)(troops
                   * (1f + training * BalanceConfig.CombatTrainingBonusPerLevel)
                   * (BalanceConfig.CombatMoraleFloor + morale / BalanceConfig.CombatMoraleScale));

        /// <summary>A city's garrison strength: 訓練 plus the 城防 multiplier (design §4.1/§6.1).</summary>
        private static int GarrisonStrength(CityState city)
            => (int)(city.Garrison
                   * (1f + city.Training * BalanceConfig.CombatTrainingBonusPerLevel)
                   * (1f + city.Defense * BalanceConfig.CombatDefenseBonusPerLevel));

        /// <summary>An army drills at home: the best 訓練 level among its faction's cities (§5.1).</summary>
        private int TrainingOf(string seatId)
        {
            int best = 0;
            foreach (var city in State.Cities)
                if (city.OwnerSeatId == seatId && city.Training > best) best = city.Training;
            return best;
        }

        /// <summary>Seeded per-round spread; the design's formula alone would make every matchup
        /// deterministic before a soldier is counted.</summary>
        private int Vary(float baseLoss)
            => Math.Max(0, (int)Math.Round(baseLoss
               * (1.0 + (rng.NextDouble() * 2 - 1) * BalanceConfig.CombatVariance)));

        /// <summary>Design §6.2: 士氣 falls with the losses — one point per percent lost that round.</summary>
        private static int MoraleLoss(int loss, int troopsAtRoundStart)
            => troopsAtRoundStart <= 0 ? 0
               : (int)(100f * loss / troopsAtRoundStart * BalanceConfig.CombatMoraleLossPerLossPercent);

        private static int CapLoss(int loss, int troops) => loss > troops ? troops : loss;

        /// <summary>潰散: extra losses, then fall back to the nearest own city — or die trying.</summary>
        private void RoutArmy(ArmyState army, string cause, ActionCommand cmd)
        {
            int extra = (int)(army.Troops * BalanceConfig.CombatRoutExtraLoss);
            army.Troops -= extra;
            if (army.Troops <= 0) { DestroyArmy(army, cause + "_rout", cmd); return; }

            var home = NearestFreeOwnCity(army.OwnerSeatId, army.X, army.Z);
            if (home == null) { DestroyArmy(army, cause + "_no_retreat", cmd); return; }

            int fromX = army.X, fromZ = army.Z;
            army.X = home.X;
            army.Z = home.Z;
            Emit(army.OwnerSeatId, State.FindFaction(army.OwnerSeatId)?.Controller, "army_routed",
                "owner_and_observers",
                P("army_id", army.Id, "cause", cause,
                  "from", $"[{fromX},{fromZ}]", "to", $"[{home.X},{home.Z}]", "city_id", home.Id,
                  "rout_loss", extra.ToString(), "troops", army.Troops.ToString(),
                  "morale", army.Morale.ToString()), cmd.ActionId);
        }

        /// <summary>Nearest own city with a free hex to regroup on (null = encircled, army is lost).</summary>
        private CityState NearestFreeOwnCity(string seatId, int x, int z)
        {
            CityState best = null;
            int bestDistance = int.MaxValue;
            foreach (var city in State.Cities)
            {
                if (city.OwnerSeatId != seatId) continue;
                var occupant = State.ArmyAt(city.X, city.Z);
                if (occupant != null) continue;
                int distance = Math.Abs(city.X - x) + Math.Abs(city.Z - z);
                if (distance < bestDistance) { bestDistance = distance; best = city; }
            }
            return best;
        }

        private void DestroyArmy(ArmyState army, string cause, ActionCommand cmd)
        {
            State.Armies.Remove(army);
            Emit(army.OwnerSeatId, State.FindFaction(army.OwnerSeatId)?.Controller, "army_destroyed", "public",
                P("army_id", army.Id, "cause", cause), cmd.ActionId);
        }

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
