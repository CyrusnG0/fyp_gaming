using System;
using System.Collections.Generic;
using LuanShi.Engine;

// [new] Added during review: the historical harness only touched domestic economy indirectly (tax and
// harvest appeared inside revolt/research fixtures), so the D1–D3 actions themselves had no direct
// assertions. This is a smoke of 徵稅 / 輕徭 / 開墾 plus representative rejects; every assertion name
// carries the [new] prefix so recovered and new coverage stay distinguishable in the output.

internal static partial class Harness
{
    private static void RunEconomySmoke()
    {
        Section("[new] domestic economy: 徵稅 D1, 輕徭 D2, 開墾 D3 (design §4.1, §11)");

        // ---- 徵稅: gold in, 民心 out
        {
            var e = EconomyScenario();
            var city = e.State.FindCity("city-a");
            int goldBefore = city.Gold, moraleBefore = city.Morale;
            int cpBefore = e.State.FindFaction("seat-a").CommandPoints;
            var r = e.Submit(DomesticOrder("seat-a", ActionType.LevyTax, "city-a"));
            Check(r.Ok, $"[new] 徵稅 is accepted on an own city ({r.Error})");
            Check(city.Gold == goldBefore + BalanceConfig.TaxLevyAmount,
                  $"[new] 徵稅 adds {BalanceConfig.TaxLevyAmount} gold");
            Check(city.Morale == moraleBefore - BalanceConfig.TaxLevyMoraleCost,
                  $"[new] 徵稅 costs {BalanceConfig.TaxLevyMoraleCost} 民心");
            Check(e.State.FindFaction("seat-a").CommandPoints
                  == cpBefore - BalanceConfig.CommandPointsPerOrder,
                  "[new] 徵稅 costs one 號令");
            var levied = Find(e.Log, "tax_levied");
            Check(levied != null && levied.Visibility == "public"
               && levied.Payload["gold_gained"] == BalanceConfig.TaxLevyAmount.ToString(),
                  "[new] tax_levied is public and carries the gain");

            city.Morale = 2;
            e.Submit(DomesticOrder("seat-a", ActionType.LevyTax, "city-a"));
            Check(city.Morale == BalanceConfig.MoraleMin, "[new] 徵稅 clamps 民心 at 0, never below");
        }

        // ---- 徵稅 rejects: foreign and unknown target, with no mutation
        {
            var e = EconomyScenario();
            var foreign = e.State.FindCity("city-b");
            int goldBefore = foreign.Gold, moraleBefore = foreign.Morale;

            var foreignOrder = e.Submit(DomesticOrder("seat-a", ActionType.LevyTax, "city-b"));
            Check(!foreignOrder.Ok && foreignOrder.Error.Contains("not your city"),
                  $"[new] 徵稅 on a foreign city is refused ({foreignOrder.Error})");
            var unknownOrder = e.Submit(DomesticOrder("seat-a", ActionType.LevyTax, "city-x"));
            Check(!unknownOrder.Ok && unknownOrder.Error.Contains("unknown city"),
                  $"[new] 徵稅 on an unknown city is refused ({unknownOrder.Error})");
            Check(foreign.Gold == goldBefore && foreign.Morale == moraleBefore,
                  "[new] the refused taxes change nothing");
            Check(Find(e.Log, "tax_levied") == null, "[new] a refused tax logs no tax event");
        }

        // ---- 輕徭: gold buys 民心
        {
            var e = EconomyScenario();
            var city = e.State.FindCity("city-a");
            int goldBefore = city.Gold, moraleBefore = city.Morale;
            var r = e.Submit(DomesticOrder("seat-a", ActionType.LightenLabor, "city-a"));
            Check(r.Ok, $"[new] 輕徭 is accepted on an own city ({r.Error})");
            Check(city.Gold == goldBefore - BalanceConfig.LightenLaborGoldCost,
                  $"[new] 輕徭 spends {BalanceConfig.LightenLaborGoldCost} gold");
            Check(city.Morale == moraleBefore + BalanceConfig.LightenLaborMoraleGain,
                  $"[new] 輕徭 grants {BalanceConfig.LightenLaborMoraleGain} 民心");
            var lightened = Find(e.Log, "labor_lightened");
            Check(lightened != null && lightened.Visibility == "owner_and_observers"
               && lightened.Payload["gold_spent"] == BalanceConfig.LightenLaborGoldCost.ToString(),
                  "[new] labor_lightened carries the spend for owner and observers");

            city.Morale = BalanceConfig.MoraleMax - 2;
            e.Submit(DomesticOrder("seat-a", ActionType.LightenLabor, "city-a"));
            Check(city.Morale == BalanceConfig.MoraleMax, "[new] 輕徭 clamps 民心 at 100, never above");
        }

        // ---- 輕徭 gate: gold check is atomic
        {
            var e = EconomyScenario(gold: BalanceConfig.LightenLaborGoldCost - 1);
            var city = e.State.FindCity("city-a");
            int goldBefore = city.Gold, moraleBefore = city.Morale;
            var poor = e.Submit(DomesticOrder("seat-a", ActionType.LightenLabor, "city-a"));
            Check(!poor.Ok && poor.Error.Contains("not enough gold"),
                  $"[new] 輕徭 below {BalanceConfig.LightenLaborGoldCost} gold is refused ({poor.Error})");
            Check(city.Gold == goldBefore && city.Morale == moraleBefore,
                  "[new] the refused 輕徭 spends and grants nothing");
            Check(Find(e.Log, "labor_lightened") == null, "[new] a refused 輕徭 logs no event");

            var foreignOrder = e.Submit(DomesticOrder("seat-a", ActionType.LightenLabor, "city-b"));
            Check(!foreignOrder.Ok && foreignOrder.Error.Contains("not your city"),
                  $"[new] 輕徭 on a foreign city is refused ({foreignOrder.Error})");
        }

        // ---- 開墾: one use per season, cumulative to the cap
        {
            var e = EconomyScenario();
            var city = e.State.FindCity("city-a");
            var r = e.Submit(DomesticOrder("seat-a", ActionType.Reclaim, "city-a"));
            Check(r.Ok, $"[new] 開墾 is accepted on an own city ({r.Error})");
            Check(city.ReclaimPct == BalanceConfig.ReclaimPctStart + BalanceConfig.ReclaimPctPerUse,
                  $"[new] 開墾 adds {BalanceConfig.ReclaimPctPerUse}% food yield");
            Check(city.ReclaimedThisSeason, "[new] the city is marked reclaimed for this season");
            var reclaimed = Find(e.Log, "land_reclaimed");
            Check(reclaimed != null && reclaimed.Visibility == "owner_and_observers"
               && reclaimed.Payload["gain_pct"] == BalanceConfig.ReclaimPctPerUse.ToString(),
                  "[new] land_reclaimed carries the gain for owner and observers");

            var again = e.Submit(DomesticOrder("seat-a", ActionType.Reclaim, "city-a"));
            Check(!again.Ok && again.Error.Contains("already reclaimed this season"),
                  $"[new] a second 開墾 in the same season is refused ({again.Error})");
            Check(city.ReclaimPct == BalanceConfig.ReclaimPctStart + BalanceConfig.ReclaimPctPerUse,
                  "[new] the refused second 開墾 adds nothing");

            e.BeginSeason();
            var nextSeason = e.Submit(DomesticOrder("seat-a", ActionType.Reclaim, "city-a"));
            Check(nextSeason.Ok
               && city.ReclaimPct == BalanceConfig.ReclaimPctStart + 2 * BalanceConfig.ReclaimPctPerUse,
                  "[new] the rollover allows one more 開墾 next season");
        }

        // ---- 開墾 cap, including the partial last gain
        {
            var e = EconomyScenario();
            var city = e.State.FindCity("city-a");
            city.ReclaimPct = BalanceConfig.ReclaimPctCap - 5;
            var partial = e.Submit(DomesticOrder("seat-a", ActionType.Reclaim, "city-a"));
            Check(partial.Ok && city.ReclaimPct == BalanceConfig.ReclaimPctCap,
                  $"[new] the last 開墾 stops at the {BalanceConfig.ReclaimPctCap}% cap");
            var reclaimed = Find(e.Log, "land_reclaimed");
            Check(reclaimed != null && reclaimed.Payload["gain_pct"] == "5",
                  "[new] the clamped 開墾 records the partial gain (5)");

            e.BeginSeason();
            var atCap = e.Submit(DomesticOrder("seat-a", ActionType.Reclaim, "city-a"));
            Check(!atCap.Ok && atCap.Error.Contains("land fully reclaimed"),
                  $"[new] 開墾 on a fully reclaimed city is refused ({atCap.Error})");
            Check(city.ReclaimPct == BalanceConfig.ReclaimPctCap,
                  "[new] a fully reclaimed city stays at the cap");
        }

        // ---- 開墾 pays out in the harvest (design §4.1: 糧產 +15%)
        {
            var e = EconomyScenario();
            var reclaimed = e.State.FindCity("city-a");
            var control = e.State.FindCity("city-a2");
            Check(e.Submit(DomesticOrder("seat-a", ActionType.Reclaim, "city-a")).Ok,
                  "[new] 開墾 is accepted before the harvest comparison");
            e.EndSeason();

            int baseHarvest = BalanceConfig.CityBaseFoodYield;
            int reclaimedHarvest = (int)(BalanceConfig.CityBaseFoodYield
                                       * (1f + BalanceConfig.ReclaimPctPerUse / 100f));
            Check(reclaimed.Food - control.Food == reclaimedHarvest - baseHarvest,
                  $"[new] the +{BalanceConfig.ReclaimPctPerUse}% 開墾 raises this harvest by "
                  + $"{reclaimedHarvest - baseHarvest} food (delta {reclaimed.Food - control.Food})");
        }

        // ---- determinism of the domestic path
        Check(EconomyReplay() == EconomyReplay(), "[new] the domestic economy path replays byte-identically");
    }

    /// <summary>Two own cities (one harvested as a control) and one foreign city; Summer, so the
    /// seasonal food multiplier is 1 and the 開墾 delta is exact.</summary>
    private static GameEngine EconomyScenario(int gold = 1000)
    {
        var e = new GameEngine(17);
        e.State.Map = Corridor(12);
        e.State.Factions.Add(new FactionState { SeatId = "seat-a", Name = "Wei", Controller = ControllerType.Human });
        e.State.Factions.Add(new FactionState { SeatId = "seat-b", Name = "Shu", Controller = ControllerType.ScriptedAi });
        e.State.Cities.Add(new CityState
        {
            Id = "city-a", Name = "City A", X = 0, Z = 0, OwnerSeatId = "seat-a",
            Population = 10000, Food = 5000, Gold = gold, Garrison = 0, Morale = BalanceConfig.MoraleStart,
        });
        e.State.Cities.Add(new CityState
        {
            Id = "city-a2", Name = "City A2", X = 1, Z = 0, OwnerSeatId = "seat-a",
            Population = 10000, Food = 5000, Gold = 0, Garrison = 0, Morale = BalanceConfig.MoraleStart,
        });
        e.State.Cities.Add(new CityState
        {
            Id = "city-b", Name = "City B", X = 8, Z = 0, OwnerSeatId = "seat-b",
            Population = 10000, Food = 2000, Gold = 500, Garrison = 0, Morale = BalanceConfig.MoraleStart,
        });
        e.BeginSeason();
        e.State.Season = 2;   // Summer: SeasonFoodYieldMult == 1
        return e;
    }

    private static ActionCommand DomesticOrder(string seat, string type, string cityId)
        => new ActionCommand { Type = type, ActorSeatId = seat, TargetId = cityId };

    private static string EconomyReplay()
    {
        var e = EconomyScenario();
        e.Submit(DomesticOrder("seat-a", ActionType.LevyTax, "city-a"));
        e.Submit(DomesticOrder("seat-a", ActionType.LightenLabor, "city-a"));
        e.Submit(DomesticOrder("seat-a", ActionType.Reclaim, "city-a2"));
        e.EndSeason();
        return e.Log.ToJsonLines();
    }
}
