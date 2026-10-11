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
            foreach (var f in State.Factions)
            {
                f.CommandPoints = BalanceConfig.CommandPointsPerSeason;
                // 研究 income (design §9): ~15 points/season (TBD), ×1.25 with 吏治 L4 學堂. Income and the
                // derived 吏治 L3 multiplier settle here at rollover, so a level bought this season pays
                // out — and 律法 takes hold — from the next one (ADR-014; cf. treaties, ADR-010 #5).
                f.ResearchPoints += BalanceConfig.ResearchIncome(f.ResearchLevel(ResearchLine.Governance));
                f.SyncResearchDerived();
                // 偵察 reports lapse here: a report covers the season it was taken and the next one
                // (design §8.2, window TBD), so nothing stale survives a rollover.
                f.ScoutedCities.RemoveAll(r => r.UntilSeason < State.Season);
            }
            foreach (var c in State.Cities) c.ReclaimedThisSeason = false;
            foreach (var a in State.Armies) a.MarchedThisSeason = false;
            Emit(null, null, "season_start", "public",
                P("season", State.Season.ToString(),
                  "season_name", BalanceConfig.SeasonName(State.Season)), null);

            // Rollover is when diplomacy moves: offers lapse or are ratified into treaties, and
            // treaties that have run their term expire (ADR-010 #5: treaties take effect next season).
            ResolveDiplomacyRollover();
        }

        /// <summary>Resolution: revolts, taxes, harvests, consumption, growth, army supply, starvation.</summary>
        public SeasonReport EndSeason()
        {
            State.Phase = "resolution";
            var report = new SeasonReport
            {
                Season = State.Season,
                SeasonName = BalanceConfig.SeasonName(State.Season),
            };
            float seasonFood = BalanceConfig.SeasonFoodYieldMult(State.Season);   // 天時 (design §3)

            foreach (var city in State.Cities)
            {
                if (city.IsNeutral) continue;
                var fac = State.FindFaction(city.OwnerSeatId);

                // 民變 (design §4.1 民心 <25 + §10.2 產出停擺): rolled before the yields so that one
                // tick zeroes both harvest and tax for a city that has risen.
                bool revolted = RollRevolt(city, fac);

                // 民心 below the threshold halves both yields (ADR-010, LOCKED).
                bool disheartened = city.Morale < BalanceConfig.MoraleYieldPenaltyThreshold;
                float yieldFactor = disheartened ? 1f - BalanceConfig.MoraleYieldPenalty : 1f;

                // 研究 (design §9), read at the point of use — no cached derived state (ADR-014):
                // 農政 L1/L5 scale this city's harvest, 吏治 L1 scales its tax.
                int agriLevel = fac?.ResearchLevel(ResearchLine.Agri) ?? 0;
                int govLevel = fac?.ResearchLevel(ResearchLine.Governance) ?? 0;

                int taxes = revolted ? 0
                          : (int)(city.Population * BalanceConfig.TaxGoldPerPop * yieldFactor
                                  * BalanceConfig.ResearchTaxMult(govLevel));
                city.Gold += taxes;

                int harvest = revolted ? 0
                            : (int)(BalanceConfig.CityBaseFoodYield
                                  * (1f + city.ReclaimPct / 100f) * yieldFactor * seasonFood
                                  * BalanceConfig.ResearchFoodYieldMult(agriLevel));
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
                    AddNote(ref note, $"Famine! Population reduced by {lost}");
                    Emit(city.OwnerSeatId, fac?.Controller, "starvation", "public",
                        P("city_id", city.Id, "pop_lost", lost.ToString()), null);
                }
                else
                {
                    float rate = BalanceConfig.PopGrowthRate
                               + (float)(rng.NextDouble() * 2 - 1) * BalanceConfig.PopGrowthVariance;
                    city.Population += (int)(city.Population * rate);
                }

                if (revolted)
                {
                    int garrisonLost = (int)(city.Garrison * BalanceConfig.RevoltGarrisonLossPct);
                    city.Garrison -= garrisonLost;
                    AddMorale(city, BalanceConfig.RevoltMoraleGainAfterVenting);
                    AddNote(ref note, $"Revolt! output halted this season, garrison −{garrisonLost}");
                    Emit(city.OwnerSeatId, fac?.Controller, "revolt", "public",
                        P("city_id", city.Id, "garrison_lost", garrisonLost.ToString(),
                          "morale", city.Morale.ToString()), null);
                }

                // 吏治 L2 察舉 (design §9): 民心 +5/季 — a resolution-time grant, so it lands in this
                // season's report line (unlike the 吏治 L3 multiplier, which is synced at rollover).
                if (govLevel >= 2)
                {
                    AddMorale(city, BalanceConfig.ResearchGovMoralePerSeason);
                    AddNote(ref note, $"Administration 2: morale +{BalanceConfig.ResearchGovMoralePerSeason}");
                }

                string line = $"{city.Name}: population {city.Population}, food {city.Food}, gold {city.Gold}, garrison {city.Garrison}, morale {city.Morale}"
                            + (disheartened ? $" (morale < {BalanceConfig.MoraleYieldPenaltyThreshold}, yields halved)" : "")
                            + (note != null ? $"; {note}" : "");
                report.Lines.Add(line);
                report.CityLines[city.Id] = line;      // keyed so a seat can read only the cities it can see
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

            // 逃兵 (design §5.1 補給 / §6.3 斷補 / §10.1 嚴冬): an unfed army loses men. Runs after the
            // morale regrowth above so this season's −15 士氣 is not quietly refunded by it.
            foreach (var army in State.Armies) ResolveArmySupply(army);

            Emit(null, null, "season_end", "public", P("season", State.Season.ToString()), null);
            return report;
        }

        // ---- 民變 / 逃兵 / 一統天下: the pressure systems (P6) -------------------

        /// <summary>
        /// 民變 roll (design §4.1): chance = (25 − 民心) × RevoltChancePerMoralePoint, scaled by the
        /// faction's `RevoltChanceMult` (the P7 吏治 hook). Seeded, so a fixed seed replays identically.
        /// </summary>
        private bool RollRevolt(CityState city, FactionState fac)
        {
            if (city.Morale >= BalanceConfig.RevoltRiskMoraleThreshold) return false;
            float chance = (BalanceConfig.RevoltRiskMoraleThreshold - city.Morale)
                         * BalanceConfig.RevoltChancePerMoralePoint
                         * (fac?.RevoltChanceMult ?? 1f);
            return rng.NextDouble() < chance;
        }

        /// <summary>
        /// One army's seasonal ration (design §5.1 補給 down → desert, §6.3 斷補 10%/季).
        /// Simplification (documented in ADR-013): 補給 0–100 and distance-from-home are not modelled.
        /// The army eats Troops × FoodPerGarrisonTroop; the city it stands on pays first, the faction's
        /// other cities cover the rest in proportion to their stores. A faction pool that cannot cover
        /// the whole ration feeds nobody — the army deserts instead of eating a fraction.
        /// </summary>
        private void ResolveArmySupply(ArmyState army)
        {
            int need = (int)(army.Troops * BalanceConfig.FoodPerGarrisonTroop);
            if (need <= 0) return;

            if (FoodPoolOf(army.OwnerSeatId) < need)
            {
                Desert(army, "no_food");
                return;
            }

            var here = State.CityAt(army.X, army.Z);
            if (here != null && here.OwnerSeatId == army.OwnerSeatId)
            {
                int taken = Math.Min(here.Food, need);
                here.Food -= taken;
                need -= taken;
            }
            if (need > 0) PayFromFoodPool(army.OwnerSeatId, need);
        }

        /// <summary>逃兵 (design §6.3 10%/季, §10.1 嚴冬 +5%): men leave and 士氣 falls with them.</summary>
        private void Desert(ArmyState army, string cause)
        {
            float pct = BalanceConfig.DesertionTroopLossPct
                      + (BalanceConfig.IsWinter(State.Season) ? BalanceConfig.WinterDesertionExtraTroopLoss : 0f);
            int lost = (int)(army.Troops * pct);
            army.Troops -= lost;
            army.Morale = Clamp(army.Morale - BalanceConfig.DesertionMoraleLoss,
                                BalanceConfig.MoraleMin, BalanceConfig.MoraleMax);
            Emit(army.OwnerSeatId, State.FindFaction(army.OwnerSeatId)?.Controller, "desertion", "owner_only",
                P("army_id", army.Id, "cause", cause, "troops_lost", lost.ToString(),
                  "troops", army.Troops.ToString(), "morale", army.Morale.ToString()), null);
        }

        /// <summary>Total food of a faction's cities — food is national in the design, per-city in the model.</summary>
        private int FoodPoolOf(string seatId)
        {
            int total = 0;
            foreach (var city in State.Cities) if (city.OwnerSeatId == seatId) total += city.Food;
            return total;
        }

        /// <summary>
        /// Draws `amount` from a faction's cities in proportion to each city's store. Integer shares leave
        /// a few units over, handed to the cities with food left in state order. Pays nothing when short.
        /// </summary>
        private bool PayFromFoodPool(string seatId, int amount)
        {
            if (amount <= 0) return true;
            var cities = State.CitiesOf(seatId);
            int total = 0;
            foreach (var city in cities) total += city.Food;
            if (total < amount) return false;

            int left = amount;
            foreach (var city in cities)
            {
                int share = (int)((long)amount * city.Food / total);
                if (share > city.Food) share = city.Food;
                city.Food -= share;
                left -= share;
            }
            foreach (var city in cities)
            {
                if (left <= 0) break;
                int take = Math.Min(city.Food, left);
                city.Food -= take;
                left -= take;
            }
            return true;
        }

        /// <summary>
        /// 一統天下 (design §13): one seat owning every city ends the game. Neutral cities count — they
        /// are territory nobody has taken, so holding "all but the neutral ones" is not unification.
        /// </summary>
        private void CheckVictory()
        {
            if (State.WinnerSeatId != null || State.Cities.Count == 0) return;
            string owner = State.Cities[0].OwnerSeatId;
            if (string.IsNullOrEmpty(owner)) return;
            foreach (var city in State.Cities)
                if (city.OwnerSeatId != owner) return;
            var fac = State.FindFaction(owner);
            if (fac == null) return;

            State.WinnerSeatId = owner;
            Emit(owner, fac.Controller, "game_over", "public",
                P("winner_seat_id", owner, "reason", "unification"), null);
        }

        // ---- command pipeline --------------------------------------------------

        /// <summary>The one entry point for every controller. Validates, executes, logs.</summary>
        public ValidationResult Submit(ActionCommand cmd)
        {
            if (cmd == null) return Reject(cmd, "null command");
            cmd.ActionId = "a-" + nextActionId++;
            if (State.WinnerSeatId != null) return Reject(cmd, "game over");

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
                case ActionType.SendMessage: r = DoSendMessage(fac, cmd); break;
                case ActionType.ProposeTreaty: r = DoProposeTreaty(fac, cmd); break;
                case ActionType.RespondTreaty: r = DoRespondTreaty(fac, cmd); break;
                case ActionType.Gift: r = DoGift(fac, cmd); break;
                case ActionType.DeclareWar: r = DoDeclareWar(fac, cmd); break;
                case ActionType.BreakTreaty: r = DoBreakTreaty(fac, cmd); break;
                case ActionType.Research: r = DoResearch(fac, cmd); break;
                case ActionType.Scout: r = DoScout(fac, cmd); break;
                case ActionType.Rumor: r = DoRumor(fac, cmd); break;
                case ActionType.SowDistrust: r = DoSowDistrust(fac, cmd); break;
                default: r = ValidationResult.Fail($"unknown action type '{cmd.Type}'"); break;
            }

            if (!r.Ok) return Reject(cmd, r.Error);

            fac.CommandPoints -= cpCost;
            // A covert order is logged for its author only (ADR-015): espionage is worth nothing if the
            // target can read who sent the agent. A failed mission becomes public on its own via
            // `spy_exposed`; a successful one stays deniable.
            Emit(fac.SeatId, fac.Controller, "action",
                ActionType.IsCovert(cmd.Type) ? "owner_only" : "public",
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

        /// <summary>D5 練兵: garrison training +1, up to the faction's cap (兵法 L1 raises it, design §9).</summary>
        private ValidationResult DoTrain(FactionState fac, ActionCommand cmd)
        {
            ValidationResult check = OwnCity(fac, cmd, out CityState city);
            if (!check.Ok) return check;
            int cap = BalanceConfig.TrainingCap(fac.ResearchLevel(ResearchLine.Military));
            if (city.Training >= cap)
                return ValidationResult.Fail($"training already at max ({cap})");

            city.Training++;
            Emit(fac.SeatId, fac.Controller, "troops_trained", "owner_and_observers",
                P("city_id", city.Id, "training", city.Training.ToString(), "training_cap", cap.ToString()),
                cmd.ActionId);
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

        // ---- 研究 R1 (design §9, ACTIONS.md §7) ---------------------------------

        /// <summary>
        /// 研究: buy the next level of one of the four §9 lines. Costs 1 號令 (TBD — the design's chapter-11
        /// table does not price it) plus the level's point cost, paid immediately on success.
        /// The event is **owner_only**: your tech tree is your secret (a locked choice, ADR-014).
        /// </summary>
        private ValidationResult DoResearch(FactionState fac, ActionCommand cmd)
        {
            string line = Arg(cmd, "line");
            if (string.IsNullOrEmpty(line))
                return ValidationResult.Fail("args.line is required (agri|military|espionage|governance)");
            if (!ResearchLine.IsValid(line))
                return ValidationResult.Fail($"unknown research line '{line}'");

            int level = fac.ResearchLevel(line);
            if (level >= BalanceConfig.ResearchMaxLevel)
                return ValidationResult.Fail($"{line} research is already at max level ({BalanceConfig.ResearchMaxLevel})");

            int cost = BalanceConfig.ResearchLevelCost(level + 1);
            if (fac.ResearchPoints < cost)
                return ValidationResult.Fail(
                    $"not enough research points for {line} {level + 1} (needs {cost}, have {fac.ResearchPoints})");

            fac.ResearchPoints -= cost;
            fac.Research[line] = level + 1;

            // 吏治 L5 九品 (design §9): a one-time 威望 +10 the moment the level is bought.
            if (line == ResearchLine.Governance && level + 1 >= BalanceConfig.ResearchMaxLevel)
                AddPrestige(fac, BalanceConfig.ResearchGovPrestigeL5, "research_governance_l5", cmd);

            Emit(fac.SeatId, fac.Controller, "research", "owner_only",
                P("line", line, "level", (level + 1).ToString(), "cost", cost.ToString(),
                  "research_points", fac.ResearchPoints.ToString()), cmd.ActionId);
            return ValidationResult.Success;
        }

        // ---- 諜報 E1-E3 (design §8.2/§8.3, ACTIONS.md §6, ADR-015) ---------------

        /// <summary>
        /// E1 偵察 (design §8.2, 200 金): learn a foreign or neutral city's 兵力/糧草 — as a 少/中/多 band at
        /// 諜報 L1-2, exact numbers at L3+. The report stays in the caller's observation for this season and
        /// the next (ADR-015), which is what makes an agent worth 200 gold once your army marches away.
        /// </summary>
        private ValidationResult DoScout(FactionState fac, ActionCommand cmd)
        {
            var city = State.FindCity(cmd.TargetId);
            if (city == null) return ValidationResult.Fail($"unknown city '{cmd.TargetId}'");
            if (city.OwnerSeatId == fac.SeatId) return ValidationResult.Fail("that is your own city");

            ValidationResult gate = SpyGate(fac, BalanceConfig.ScoutResearchLevel, BalanceConfig.ScoutGoldCost);
            if (!gate.Ok) return gate;

            if (!SpyRoll(fac, ActionType.Scout, city.OwnerSeatId, null, cmd))
                return ValidationResult.Success;      // exposed instead — gold is already gone

            int level = fac.ResearchLevel(ResearchLine.Espionage);
            int until = State.Season + BalanceConfig.ScoutReportExtraSeasons;
            fac.AddScoutReport(city.Id, until);
            Emit(fac.SeatId, fac.Controller, "scout_report", "owner_only",
                P("city_id", city.Id, "accuracy", BalanceConfig.ScoutAccuracy(level),
                  "scouted_until", until.ToString(), "espionage_level", level.ToString()), cmd.ActionId);
            return ValidationResult.Success;
        }

        /// <summary>
        /// E2 散播謠言 (design §8.2, 500 金): 目標城民心 −10. A success is deniable — the log record names no
        /// actor, and only the city's owner is told that rumours are circulating (ADR-015).
        /// </summary>
        private ValidationResult DoRumor(FactionState fac, ActionCommand cmd)
        {
            var city = State.FindCity(cmd.TargetId);
            if (city == null) return ValidationResult.Fail($"unknown city '{cmd.TargetId}'");
            if (city.OwnerSeatId == fac.SeatId) return ValidationResult.Fail("that is your own city");
            if (city.IsNeutral) return ValidationResult.Fail("a neutral city has no faction to undermine");

            ValidationResult gate = SpyGate(fac, BalanceConfig.RumorResearchLevel, BalanceConfig.RumorGoldCost);
            if (!gate.Ok) return gate;

            if (!SpyRoll(fac, ActionType.Rumor, city.OwnerSeatId, null, cmd))
                return ValidationResult.Success;

            AddMorale(city, -BalanceConfig.RumorMoraleLoss);
            Emit(null, null, "rumor", "owner_only",
                P("to_seat_id", city.OwnerSeatId, "city_id", city.Id,
                  "morale_lost", BalanceConfig.RumorMoraleLoss.ToString(),
                  "morale", city.Morale.ToString()), cmd.ActionId);
            return ValidationResult.Success;
        }

        /// <summary>
        /// E3 挑撥離間 (design §8.2, 800 金): 目標兩國之間的信任 −10 in both directions. The two victims see
        /// their own trust move (and the `sow_distrust` note); nobody sees who paid for it (ADR-015).
        /// </summary>
        private ValidationResult DoSowDistrust(FactionState fac, ActionCommand cmd)
        {
            var targetA = State.FindFaction(cmd.TargetId);
            if (targetA == null) return ValidationResult.Fail($"unknown seat '{cmd.TargetId}'");
            if (targetA.SeatId == fac.SeatId) return ValidationResult.Fail("pick two other seats");

            string targetBId = Arg(cmd, "target_b");
            if (string.IsNullOrEmpty(targetBId))
                return ValidationResult.Fail("args.target_b is required (the second seat)");
            var targetB = State.FindFaction(targetBId);
            if (targetB == null) return ValidationResult.Fail($"unknown seat '{targetBId}'");
            if (targetB.SeatId == fac.SeatId) return ValidationResult.Fail("pick two other seats");
            if (targetB.SeatId == targetA.SeatId)
                return ValidationResult.Fail("the two targets must be different seats");

            ValidationResult gate = SpyGate(fac, BalanceConfig.SowDistrustResearchLevel,
                                            BalanceConfig.SowDistrustGoldCost);
            if (!gate.Ok) return gate;

            if (!SpyRoll(fac, ActionType.SowDistrust, targetA.SeatId, targetB.SeatId, cmd))
                return ValidationResult.Success;

            AddTrustEvent(targetA.SeatId, targetB.SeatId,
                          -BalanceConfig.SowDistrustTrustPenalty, "sow_distrust", cmd);
            AddTrustEvent(targetB.SeatId, targetA.SeatId,
                          -BalanceConfig.SowDistrustTrustPenalty, "sow_distrust", cmd);
            Emit(null, null, "sow_distrust", "owner_only",
                P("from_seat_id", targetA.SeatId, "to_seat_id", targetB.SeatId,
                  "delta", (-BalanceConfig.SowDistrustTrustPenalty).ToString()), cmd.ActionId);
            return ValidationResult.Success;
        }

        /// <summary>Shared mission preconditions: the 諜報 level, then the gold — which is spent either way.</summary>
        private ValidationResult SpyGate(FactionState fac, int requiredLevel, int goldCost)
        {
            int level = fac.ResearchLevel(ResearchLine.Espionage);
            if (level < requiredLevel)
                return ValidationResult.Fail($"needs Espionage {requiredLevel} research (have {level})");
            if (!PayFromTreasury(fac, goldCost))
                return ValidationResult.Fail($"not enough gold (needs {goldCost})");
            return ValidationResult.Success;
        }

        /// <summary>
        /// The single seeded roll behind every mission (design §8.3, MVP form — no 嫌疑, no counter-intel):
        /// success chance = `BalanceConfig.SpySuccessChance(諜報 level)`. A failure exposes the attacker.
        /// </summary>
        private bool SpyRoll(FactionState fac, string mission, string victimSeatId, string secondVictimSeatId,
            ActionCommand cmd)
        {
            float chance = BalanceConfig.SpySuccessChance(fac.ResearchLevel(ResearchLine.Espionage));
            if (rng.NextDouble() < chance) return true;
            ExposeSpy(fac, mission, victimSeatId, secondVictimSeatId, cmd);
            return false;
        }

        /// <summary>
        /// §8.3 公開處決 path: the mission fails, the agent is caught, and the world is told — a public
        /// `spy_exposed` naming the attacker and the mission, 威望 −8 (LOCKED, design §7.3 table) and the
        /// victim's 信任 −10 (TBD). The design's paired 對方威望 +5 for choosing to execute is not paid out.
        /// </summary>
        private void ExposeSpy(FactionState fac, string mission, string victimSeatId, string secondVictimSeatId,
            ActionCommand cmd)
        {
            var victim = victimSeatId != null ? State.FindFaction(victimSeatId) : null;
            Emit(fac.SeatId, fac.Controller, "spy_exposed", "public",
                P("attacker_seat_id", fac.SeatId, "mission", mission,
                  "detected_by_seat_id", victimSeatId ?? "",
                  "prestige_lost", BalanceConfig.SpyExposedPrestigePenalty.ToString()), cmd.ActionId);
            AddPrestige(fac, -BalanceConfig.SpyExposedPrestigePenalty, "spy_exposed", cmd);
            if (victim != null)
                AddTrustEvent(victim.SeatId, fac.SeatId, -BalanceConfig.SpyExposedTrustPenalty,
                              "spy_exposed", cmd);
            var secondVictim = secondVictimSeatId != null ? State.FindFaction(secondVictimSeatId) : null;
            if (secondVictim != null)
                AddTrustEvent(secondVictim.SeatId, fac.SeatId, -BalanceConfig.SpyExposedTrustPenalty,
                              "spy_exposed", cmd);
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

            int budget = BalanceConfig.MarchBudget(State.Season);   // 冬 行軍 ×0.7 (design §3/§10.1 嚴冬)
            int cost = State.Map.PathCost(army.X, army.Z, tx, tz, budget);
            if (cost < 0) return ValidationResult.Fail(
                $"destination out of reach this season (max cost {budget})");

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
                CheckVictory();
            }
            var towerAtDest = State.WatchTowerAt(tx, tz);
            if (towerAtDest != null && towerAtDest.IsNeutral)
            {
                towerAtDest.OwnerSeatId = fac.SeatId;
                Emit(fac.SeatId, fac.Controller, "watch_tower_captured", "public",
                    P("watch_tower_id", towerAtDest.Id, "army_id", army.Id), cmd.ActionId);
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

        /// <summary>Joins the notes on a city's report line, so a season can report several events at once.</summary>
        private static void AddNote(ref string note, string extra)
            => note = note == null ? extra : note + "; " + extra;

        private static int Clamp(int value, int min, int max)
            => value < min ? min : value > max ? max : value;

        // ---- observations: fog of war (AGENTS.md #3, ACTIONS.md §9) -------------

        /// <summary>
        /// The filtered observation for one seat — the only world a controller may act on. Own cities and
        /// armies are complete; foreign ones appear only inside the §9 detection ranges and are absent
        /// (never redacted in place) outside them; the log is filtered by event visibility.
        /// </summary>
        public Observation Observe(string seatId)
        {
            var fac = State.FindFaction(seatId);
            if (fac == null) return null;

            var obs = new Observation
            {
                SeatId = fac.SeatId,
                Season = State.Season,
                SeasonName = BalanceConfig.SeasonName(State.Season),
                Phase = State.Phase,
                CommandPoints = fac.CommandPoints,
                Prestige = fac.Prestige,
                ResearchPoints = fac.ResearchPoints,     // 研究 is private to the seat (ADR-014)
                WinnerSeatId = State.WinnerSeatId,
            };
            foreach (string line in ResearchLine.All) obs.Research[line] = fac.ResearchLevel(line);

            foreach (var city in State.Cities)
                if (InSight(seatId, city.X, city.Z)) obs.Cities.Add(city);

            // 偵察 reports (design §8.2, ADR-015): a scouted city is in the observation even when nothing of
            // ours is near it — that is the whole point of paying for an agent. Cities already in sight stay
            // in `Cities` at full detail and are not duplicated here; the report carries its own accuracy.
            string scoutAccuracy = BalanceConfig.ScoutAccuracy(fac.ResearchLevel(ResearchLine.Espionage));
            foreach (var report in fac.ScoutedCities)
            {
                if (report.UntilSeason < State.Season) continue;
                var scouted = State.FindCity(report.CityId);
                if (scouted == null || InSight(seatId, scouted.X, scouted.Z)) continue;
                obs.ScoutedCities.Add(ScoutedCityView.From(scouted, scoutAccuracy, report.UntilSeason));
            }
            foreach (var army in State.Armies)
                if (InSight(seatId, army.X, army.Z)) obs.Armies.Add(army);
            foreach (var tower in State.WatchTowers)
                if (InSight(seatId, tower.X, tower.Z)) obs.WatchTowers.Add(tower);

            foreach (var other in State.Factions)
            {
                obs.Factions.Add(new FactionView
                {
                    SeatId = other.SeatId,
                    Name = other.Name,
                    Controller = other.Controller,
                    Prestige = other.Prestige,        // 威望 is the world's public opinion (design §7.3)
                });
                if (other.SeatId == seatId) continue;
                obs.Relations.Add(new RelationView
                {
                    SeatId = other.SeatId,
                    MyTrust = State.Diplomacy.GetTrust(seatId, other.SeatId),
                    TheirTrust = State.Diplomacy.GetTrust(other.SeatId, seatId),
                    AtWar = State.Diplomacy.IsAtWar(seatId, other.SeatId),
                });
            }

            foreach (var treaty in State.Diplomacy.Treaties)
                if (treaty.FactionA == seatId || treaty.FactionB == seatId) obs.Treaties.Add(treaty);
            foreach (var proposal in State.Diplomacy.Proposals)
                if (proposal.FromSeatId == seatId || proposal.ToSeatId == seatId) obs.Proposals.Add(proposal);
            foreach (var rec in Log.Records)
                if (SeesEvent(seatId, rec)) obs.Events.Add(rec);

            return obs;
        }

        /// <summary>
        /// Fog ranges (ACTIONS.md §9): a hex within FogCityRange of an own city, FogArmyRange of an own
        /// army, or FogWatchTowerRange of an own watchtower is seen regardless of who stands there. Terrain
        /// does not block sight (a radius, not a line).
        /// </summary>
        public bool InSight(string seatId, int x, int z)
        {
            foreach (var city in State.Cities)
                if (city.OwnerSeatId == seatId
                    && State.Map.HexDistance(city.X, city.Z, x, z, BalanceConfig.FogCityRange) >= 0) return true;
            foreach (var army in State.Armies)
                if (army.OwnerSeatId == seatId
                    && State.Map.HexDistance(army.X, army.Z, x, z, BalanceConfig.FogArmyRange) >= 0) return true;
            foreach (var tower in State.WatchTowers)
                if (tower.OwnerSeatId == seatId
                    && State.Map.HexDistance(tower.X, tower.Z, x, z, BalanceConfig.FogWatchTowerRange) >= 0) return true;
            return false;
        }

        /// <summary>
        /// Log filter (CONTROLLER_PROTOCOL §5): a seat sees public events, whatever it did itself, whatever
        /// names it as a party, and `owner_and_observers` events whose subject it can currently see. An
        /// `owner_and_observers` event with no in-sight subject reaches only its actor and its named parties.
        /// </summary>
        public bool SeesEvent(string seatId, EventRecord rec)
        {
            if (rec == null) return false;
            if (rec.Visibility == "public") return true;
            if (rec.ActorSeatId == seatId) return true;
            if (EventField(rec, "to_seat_id") == seatId) return true;
            if (EventField(rec, "from_seat_id") == seatId) return true;
            if (EventField(rec, "attacker_seat_id") == seatId) return true;
            if (EventField(rec, "defender_seat_id") == seatId) return true;
            if (rec.Visibility != "owner_and_observers") return false;

            string cityId = EventField(rec, "city_id");
            if (cityId != null)
            {
                var city = State.FindCity(cityId);
                if (city != null && InSight(seatId, city.X, city.Z)) return true;
            }
            string armyId = EventField(rec, "army_id") ?? EventField(rec, "attacker_army_id")
                         ?? EventField(rec, "defender_army_id");
            if (armyId != null)
            {
                var army = State.FindArmy(armyId);
                if (army != null && InSight(seatId, army.X, army.Z)) return true;
            }
            return false;
        }

        private static string EventField(EventRecord rec, string key)
            => rec.Payload != null && rec.Payload.TryGetValue(key, out string value) ? value : null;

        // ---- 外交 P1-P6 (ACTIONS.md §5) ----------------------------------------
        // Command shapes (the UI and the LLM adapter must both produce exactly these):
        //   send_message    target_id = recipient seat · text = 訊息
        //   propose_treaty  target_id = recipient seat · args { treaty_type ∈ nap|alliance|truce,
        //                                                        duration = seasons, optional }
        //   respond_treaty  target_id = proposer seat  · args { response ∈ accept|reject|counter }
        //                                                 (+ treaty_type/duration when countering)
        //   gift            target_id = recipient seat · param_a = gold amount
        //   declare_war     target_id = seat
        //   break_treaty    target_id = seat          · args { treaty_type }

        /// <summary>P1 遣使: a message is free and changes nothing (design §7.1: 說話不會改狀態).</summary>
        private ValidationResult DoSendMessage(FactionState fac, ActionCommand cmd)
        {
            ValidationResult check = OtherFaction(fac, cmd, out FactionState other);
            if (!check.Ok) return check;
            if (string.IsNullOrEmpty(cmd.Text)) return ValidationResult.Fail("a message needs text");

            Emit(fac.SeatId, fac.Controller, "message_sent", "public",
                P("to_seat_id", other.SeatId, "text", cmd.Text), cmd.ActionId);
            return ValidationResult.Success;
        }

        /// <summary>P2 提出條約: creates a pending offer; it takes effect next season once accepted.</summary>
        private ValidationResult DoProposeTreaty(FactionState fac, ActionCommand cmd)
        {
            ValidationResult check = OtherFaction(fac, cmd, out FactionState other);
            if (!check.Ok) return check;

            string type = Arg(cmd, "treaty_type");
            if (!TreatyType.IsValid(type))
                return ValidationResult.Fail($"unknown treaty_type '{type}' (use nap, alliance or truce)");
            if (State.Diplomacy.HasTreaty(type, fac.SeatId, other.SeatId))
                return ValidationResult.Fail($"a {type} treaty with that seat is already in force");
            if (State.Diplomacy.FindProposal(fac.SeatId, other.SeatId) != null)
                return ValidationResult.Fail("an offer is already pending with that seat");

            ValidationResult durationCheck = ProposalDuration(cmd, out int seasons);
            if (!durationCheck.Ok) return durationCheck;

            var proposal = State.Diplomacy.AddProposal(fac.SeatId, other.SeatId, type, seasons,
                State.Season, cmd.Text);
            Emit(fac.SeatId, fac.Controller, "treaty_proposed", "owner_and_observers",
                P("proposal_id", proposal.ProposalId, "from_seat_id", fac.SeatId, "to_seat_id", other.SeatId,
                  "treaty_type", type, "duration", seasons.ToString(), "text", cmd.Text ?? ""), cmd.ActionId);
            return ValidationResult.Success;
        }

        /// <summary>P3 接受/拒絕/修改: an answer is free; acceptance is ratified at the rollover.</summary>
        private ValidationResult DoRespondTreaty(FactionState fac, ActionCommand cmd)
        {
            ValidationResult check = OtherFaction(fac, cmd, out FactionState proposer);
            if (!check.Ok) return check;

            string response = Arg(cmd, "response");
            if (response != "accept" && response != "reject" && response != "counter")
                return ValidationResult.Fail($"unknown response '{response}' (use accept, reject or counter)");

            var proposal = State.Diplomacy.FindProposal(proposer.SeatId, fac.SeatId);
            if (proposal == null || proposal.ToSeatId != fac.SeatId)
                return ValidationResult.Fail("no offer from that seat is pending");

            if (response == "reject")
            {
                State.Diplomacy.Proposals.Remove(proposal);
                Emit(fac.SeatId, fac.Controller, "treaty_responded", "owner_and_observers",
                    P("proposal_id", proposal.ProposalId, "from_seat_id", proposal.FromSeatId,
                      "to_seat_id", proposal.ToSeatId, "treaty_type", proposal.Type, "response", "reject"),
                    cmd.ActionId);
                return ValidationResult.Success;
            }

            if (response == "counter")
            {
                string type = Arg(cmd, "treaty_type");
                if (!TreatyType.IsValid(type))
                    return ValidationResult.Fail($"unknown treaty_type '{type}' (use nap, alliance or truce)");
                ValidationResult durationCheck = ProposalDuration(cmd, out int seasons);
                if (!durationCheck.Ok) return durationCheck;

                State.Diplomacy.Proposals.Remove(proposal);
                var counter = State.Diplomacy.AddProposal(fac.SeatId, proposal.FromSeatId, type, seasons,
                    State.Season, cmd.Text);
                Emit(fac.SeatId, fac.Controller, "treaty_responded", "owner_and_observers",
                    P("proposal_id", proposal.ProposalId, "from_seat_id", proposal.FromSeatId,
                      "to_seat_id", proposal.ToSeatId, "treaty_type", proposal.Type, "response", "counter",
                      "counter_proposal_id", counter.ProposalId, "counter_treaty_type", type,
                      "duration", seasons.ToString()), cmd.ActionId);
                Emit(fac.SeatId, fac.Controller, "treaty_proposed", "owner_and_observers",
                    P("proposal_id", counter.ProposalId, "from_seat_id", counter.FromSeatId,
                      "to_seat_id", counter.ToSeatId, "treaty_type", type, "duration", seasons.ToString(),
                      "text", counter.Text ?? ""), cmd.ActionId);
                return ValidationResult.Success;
            }

            proposal.Accepted = true;        // ratified at the next rollover, not now (ADR-010 #5)
            Emit(fac.SeatId, fac.Controller, "treaty_responded", "owner_and_observers",
                P("proposal_id", proposal.ProposalId, "from_seat_id", proposal.FromSeatId,
                  "to_seat_id", proposal.ToSeatId, "treaty_type", proposal.Type, "response", "accept",
                  "duration", proposal.DurationSeasons.ToString()), cmd.ActionId);
            return ValidationResult.Success;
        }

        /// <summary>P4 贈禮: gold buys 信任 for the giver and 威望 (design §7.3: per 1,000 金).</summary>
        private ValidationResult DoGift(FactionState fac, ActionCommand cmd)
        {
            ValidationResult check = OtherFaction(fac, cmd, out FactionState other);
            if (!check.Ok) return check;

            int gold = cmd.ParamA;
            if (gold < BalanceConfig.GiftGoldPerTrustUnit)
                return ValidationResult.Fail($"a gift must be at least {BalanceConfig.GiftGoldPerTrustUnit} gold");
            if (!PayFromTreasury(fac, gold))
                return ValidationResult.Fail($"not enough gold (need {gold})");

            int units = gold / BalanceConfig.GiftGoldPerTrustUnit;
            int trustGain = units * BalanceConfig.GiftTrustPerUnit;
            int prestigeGain = units * BalanceConfig.GiftPrestigePerUnit;
            AddTrustEvent(other.SeatId, fac.SeatId, trustGain, "gift", cmd);        // 對方對你的信任 +
            AddPrestige(fac, prestigeGain, "gift", cmd);
            Emit(fac.SeatId, fac.Controller, "gift_sent", "owner_and_observers",
                P("from_seat_id", fac.SeatId, "to_seat_id", other.SeatId, "gold", gold.ToString(),
                  "trust_gain", trustGain.ToString(), "prestige_gain", prestigeGain.ToString(),
                  "trust", State.Diplomacy.GetTrust(other.SeatId, fac.SeatId).ToString()), cmd.ActionId);
            return ValidationResult.Success;
        }

        /// <summary>P5 宣戰: formal war, cheaper in 威望 than a 偷襲 (design §11 #18).</summary>
        private ValidationResult DoDeclareWar(FactionState fac, ActionCommand cmd)
        {
            ValidationResult check = OtherFaction(fac, cmd, out FactionState other);
            if (!check.Ok) return check;
            if (State.Diplomacy.IsAtWar(fac.SeatId, other.SeatId))
                return ValidationResult.Fail("already at war with that seat");

            State.Diplomacy.DeclareWar(fac.SeatId, other.SeatId);
            AddPrestige(fac, -BalanceConfig.DeclareWarPrestigeCost, "declared_war", cmd);
            Emit(fac.SeatId, fac.Controller, "war_declared", "public",
                P("attacker_seat_id", fac.SeatId, "defender_seat_id", other.SeatId, "cause", "declared"),
                cmd.ActionId);
            return ValidationResult.Success;
        }

        /// <summary>P6 背盟/毀約: renounce a treaty by act of state (design §7.3 penalties).</summary>
        private ValidationResult DoBreakTreaty(FactionState fac, ActionCommand cmd)
        {
            ValidationResult check = OtherFaction(fac, cmd, out FactionState other);
            if (!check.Ok) return check;

            string type = Arg(cmd, "treaty_type");
            if (!TreatyType.IsValid(type))
                return ValidationResult.Fail($"unknown treaty_type '{type}' (use nap, alliance or truce)");
            var treaty = State.Diplomacy.FindTreaty(type, fac.SeatId, other.SeatId);
            if (treaty == null)
                return ValidationResult.Fail($"no {type} treaty with that seat is in force");

            bool truce = type == TreatyType.Truce;
            string reason = "broke_" + type;
            State.Diplomacy.BreakTreaty(treaty);
            AddTrustEvent(other.SeatId, fac.SeatId,
                -(truce ? BalanceConfig.TruceBreakTrustPenalty : BalanceConfig.BetrayalTrustPenalty), reason, cmd);
            AddPrestige(fac, -(truce ? BalanceConfig.TruceBreakPrestigePenalty
                                     : BalanceConfig.BetrayalPrestigePenalty), reason, cmd);
            if (!truce)
                foreach (var third in State.Factions)
                    if (third.SeatId != fac.SeatId && third.SeatId != other.SeatId)
                        AddTrustEvent(third.SeatId, fac.SeatId, -BalanceConfig.BetrayalThirdPartyTrustPenalty,
                            "bystander_" + reason, cmd);

            Emit(fac.SeatId, fac.Controller, "treaty_broken", "public",
                P("breaker_seat_id", fac.SeatId, "counterpart_seat_id", other.SeatId, "treaty_type", type),
                cmd.ActionId);
            return ValidationResult.Success;
        }

        /// <summary>
        /// Rollover (ADR-010 #5): unanswered offers lapse, accepted ones are ratified into treaties that
        /// run to their ExpirySeason, and treaties whose term is over expire. A ratified 停戰 ends the war.
        /// </summary>
        private void ResolveDiplomacyRollover()
        {
            var dip = State.Diplomacy;

            foreach (var proposal in dip.Proposals.ToArray())
            {
                dip.Proposals.Remove(proposal);
                if (!proposal.Accepted)
                {
                    Emit(null, null, "treaty_lapsed", "owner_and_observers",
                        P("proposal_id", proposal.ProposalId, "from_seat_id", proposal.FromSeatId,
                          "to_seat_id", proposal.ToSeatId, "treaty_type", proposal.Type,
                          "proposed_season", proposal.ProposedSeason.ToString()), null);
                    continue;
                }

                int expiry = State.Season + proposal.DurationSeasons - 1;
                dip.Treaties.Add(new TreatyRecord
                {
                    Type = proposal.Type,
                    FactionA = proposal.FromSeatId,
                    FactionB = proposal.ToSeatId,
                    ExpirySeason = expiry,
                });
                bool endedWar = proposal.Type == TreatyType.Truce
                             && dip.IsAtWar(proposal.FromSeatId, proposal.ToSeatId);
                if (endedWar) dip.MakePeace(proposal.FromSeatId, proposal.ToSeatId);

                Emit(null, null, "treaty_ratified", "public",
                    P("proposal_id", proposal.ProposalId, "from_seat_id", proposal.FromSeatId,
                      "to_seat_id", proposal.ToSeatId, "treaty_type", proposal.Type,
                      "expiry_season", expiry.ToString(), "war_ended", endedWar ? "true" : "false"), null);
            }

            foreach (var treaty in dip.Treaties.ToArray())
            {
                if (treaty.ExpirySeason >= State.Season) continue;
                dip.Treaties.Remove(treaty);
                Emit(null, null, "treaty_expired", "public",
                    P("treaty_type", treaty.Type, "faction_a", treaty.FactionA, "faction_b", treaty.FactionB,
                      "expiry_season", treaty.ExpirySeason.ToString()), null);
            }
        }

        /// <summary>Resolves target_id to another faction's seat — diplomacy targets seats, not hexes.</summary>
        private ValidationResult OtherFaction(FactionState fac, ActionCommand cmd, out FactionState other)
        {
            other = State.FindFaction(cmd.TargetId);
            if (other == null) return ValidationResult.Fail($"unknown seat '{cmd.TargetId}'");
            if (other.SeatId == fac.SeatId) return ValidationResult.Fail("that is your own seat");
            return ValidationResult.Success;
        }

        /// <summary>Schema-v2 `args` reader: null when the controller sent no such key.</summary>
        private static string Arg(ActionCommand cmd, string key)
            => cmd.Args != null && cmd.Args.TryGetValue(key, out string value) ? value : null;

        /// <summary>Optional args.duration in seasons, defaulted and clamped to the configured range.</summary>
        private ValidationResult ProposalDuration(ActionCommand cmd, out int seasons)
        {
            seasons = BalanceConfig.TreatyDurationSeasons;
            string raw = Arg(cmd, "duration");
            if (string.IsNullOrEmpty(raw)) return ValidationResult.Success;
            if (!int.TryParse(raw, out int parsed))
                return ValidationResult.Fail($"duration must be a number of seasons, not '{raw}'");

            if (parsed < BalanceConfig.TreatyDurationMinSeasons) parsed = BalanceConfig.TreatyDurationMinSeasons;
            if (parsed > BalanceConfig.TreatyDurationMaxSeasons) parsed = BalanceConfig.TreatyDurationMaxSeasons;
            seasons = parsed;
            return ValidationResult.Success;
        }

        /// <summary>Gold is a national resource in the design but a per-city field in the model, so a
        /// treasury payment draws on the faction's cities in state order — and pays nothing on failure.</summary>
        private bool PayFromTreasury(FactionState fac, int amount)
        {
            var cities = State.CitiesOf(fac.SeatId);
            int total = 0;
            foreach (var city in cities) total += city.Gold;
            if (total < amount) return false;

            int left = amount;
            foreach (var city in cities)
            {
                int take = Math.Min(city.Gold, left);
                city.Gold -= take;
                left -= take;
                if (left == 0) break;
            }
            return true;
        }

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
            float attackerResearch = CombatResearchMult(fac.SeatId);
            float defenderResearch = CombatResearchMult(defender.OwnerSeatId);
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
                int toAttacker = CapLoss(Vary(Strength(defender.Troops, defenderTraining, defender.Morale,
                                                       defenderResearch)
                                              * BalanceConfig.CombatLossPerRound), army.Troops);
                int toDefender = CapLoss(Vary(Strength(army.Troops, attackerTraining, army.Morale,
                                                       attackerResearch)
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
            float attackerResearch = CombatResearchMult(fac.SeatId);
            float garrisonResearch = CombatResearchMult(city.OwnerSeatId);
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

                int toArmy = CapLoss(Vary(GarrisonStrength(city, garrisonResearch)
                                          * BalanceConfig.CombatLossPerRound), army.Troops);
                int toGarrison = CapLoss(Vary(Strength(army.Troops, training, army.Morale, attackerResearch)
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
                CheckVictory();
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

        /// <summary>
        /// Design §6.1 戰力, reduced to the fields the MVP has: troops, 訓練 and 士氣, times the acting
        /// faction's research multiplier (兵法 L4 兵法 +15%, design §9 — a faction-side bonus, so the
        /// opposite side of one battle can be fighting at a different multiplier).
        /// </summary>
        private static int Strength(int troops, int training, int morale, float researchMult)
            => (int)(troops
                   * (1f + training * BalanceConfig.CombatTrainingBonusPerLevel)
                   * (BalanceConfig.CombatMoraleFloor + morale / BalanceConfig.CombatMoraleScale)
                   * researchMult);

        /// <summary>A city's garrison strength: 訓練 plus the 城防 multiplier (design §4.1/§6.1),
        /// times its owner's 兵法 L4 research multiplier.</summary>
        private static int GarrisonStrength(CityState city, float researchMult)
            => (int)(city.Garrison
                   * (1f + city.Training * BalanceConfig.CombatTrainingBonusPerLevel)
                   * (1f + city.Defense * BalanceConfig.CombatDefenseBonusPerLevel)
                   * researchMult);

        /// <summary>The 兵法 L4 戰力 multiplier of one seat (1.0 without the research).</summary>
        private float CombatResearchMult(string seatId)
            => BalanceConfig.ResearchCombatStrengthMult(
                   State.FindFaction(seatId)?.ResearchLevel(ResearchLine.Military) ?? 0);

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
        public string SeasonName;                 // 春/夏/秋/冬 (design §3) — shown in the report header
        public readonly List<string> Lines = new List<string>();
        public readonly Dictionary<string, string> CityLines = new Dictionary<string, string>();  // city id → line
    }
}
