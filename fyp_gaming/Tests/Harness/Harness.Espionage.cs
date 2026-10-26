using System;
using System.Collections.Generic;
using LuanShi.Engine;

internal static partial class Harness
{
    private static void RunEspionage()
    {
        // ------------------------------------------------ 諜報 espionage lite (P8)
        Section("espionage lite (design §8.2 missions, §8.3 exposure, §11 #30/#32)");

        Check(BalanceConfig.SpySuccessChance(0) == 0.70f && BalanceConfig.SpySuccessChance(1) == 0.75f
           && BalanceConfig.SpySuccessChance(2) == 0.80f && BalanceConfig.SpySuccessChance(5) == 0.95f,
              "success chance = 70% + 諜報 level × 5% (75/80/85/90/95)");
        Check(BalanceConfig.BandLabel(999, 1000, 3000) == "few"
           && BalanceConfig.BandLabel(1000, 1000, 3000) == "some"
           && BalanceConfig.BandLabel(2999, 1000, 3000) == "some"
           && BalanceConfig.BandLabel(3000, 1000, 3000) == "many",
              "侦察 bands: 少 <1000 ≤ 中 <3000 ≤ 多");

        // ---- gates: research level, gold, target ownership
        {
            var e = EspionageState(morale: 100, gold: 5000);
            var cityB = e.State.FindCity("city-b");


            var noResearch = e.Submit(Mission(e, "seat-a", ActionType.Scout, "city-b"));
            Check(!noResearch.Ok && noResearch.Error.Contains("needs Espionage 1"),
                  $"偵察 needs 諜報 L1 ({noResearch.Error})");
            SetLevel(e, "seat-a", ResearchLine.Espionage, 2);

            var ownCity = e.Submit(Mission(e, "seat-a", ActionType.Scout, "city-a"));
            Check(!ownCity.Ok && ownCity.Error.Contains("your own city"), $"cannot scout your own city ({ownCity.Error})");

            MakeCityKnown(e, "seat-a", "city-n");
            var neutral = e.Submit(Mission(e, "seat-a", ActionType.Rumor, "city-n"));
            Check(!neutral.Ok && neutral.Error.Contains("neutral city"), $"cannot spread rumours in a neutral city ({neutral.Error})");

            var poor = EspionageState(morale: 100, gold: 100);
            SetLevel(poor, "seat-a", ResearchLine.Espionage, 2);
            var noGold = poor.Submit(Mission(poor, "seat-a", ActionType.Scout, "city-b"));
            Check(!noGold.Ok && noGold.Error.Contains("not enough gold (needs 200)"),
                  $"偵察 needs 200 gold ({noGold.Error})");
            Check(poor.State.FindCity("city-a").Gold == 100, "a refused mission spends nothing");

            SetLevel(e, "seat-a", ResearchLine.Espionage, 1);
            var sow0 = e.Submit(SowDistrust(e, "seat-a", "seat-b", "seat-c"));
            Check(!sow0.Ok && sow0.Error.Contains("needs Espionage 2"), $"挑撥離間 needs 諜報 L2 ({sow0.Error})");
            SetLevel(e, "seat-a", ResearchLine.Espionage, 2);
            var self = e.Submit(SowDistrust(e, "seat-a", "seat-b", "seat-a"));
            Check(!self.Ok && self.Error.Contains("two other seats"), $"挑撥離間 refuses your own seat ({self.Error})");
            var same = e.Submit(SowDistrust(e, "seat-a", "seat-b", "seat-b"));
            Check(!same.Ok && same.Error.Contains("different seats"), $"挑撥離間 needs two distinct seats ({same.Error})");
            var noB = e.Submit(new ActionCommand { Type = ActionType.SowDistrust, ActorSeatId = "seat-a", TargetId = "seat-b" });
            Check(!noB.Ok && noB.Error.Contains("args.target_b"), $"args.target_b is required ({noB.Error})");
            Check(cityB.Garrison > 0, "no failed gate touched the target city");
        }

        // ---- 偵察: the report window, and band vs exact accuracy
        {
            int seed = FindSpySeed(ActionType.Scout, success: true, espionageLevel: 1);
            Check(seed > 0, $"a successful 偵察 is reachable by seed search (seed {seed})");

            var e = EspionageState(morale: 100, gold: 5000, seed: seed);
            SetLevel(e, "seat-a", ResearchLine.Espionage, 1);

            int garrisonBefore = e.State.FindCity("city-b").Garrison;
            int season = e.State.Season;
            var r = e.Submit(Mission(e, "seat-a", ActionType.Scout, "city-b"));
            var report = Find(e.Log, "scout_report");
            Check(r.Ok && report != null, "偵察 succeeds and logs a scout_report");
            Check(report != null && report.Visibility == "owner_only", "the scout report is the agent's own");

            var obs = e.Observe("seat-a");
            var view = obs.FindScoutedCity("city-b");
            Check(view != null, "the scouted city is in the observation without being in sight");
            Check(!e.InSight("seat-a", 8, 0), "and it really is outside the fog radius");
            Check(view != null && view.UntilSeason == season + 1, $"the report covers this season and the next (until {view.UntilSeason})");
            Check(view != null && view.Accuracy == "band", $"諜報 L1 reports a band, not numbers (accuracy {view?.Accuracy})");
            Check(view != null && view.GarrisonBand == BalanceConfig.BandLabel(garrisonBefore, 1000, 3000),
                  $"the band matches the real garrison ({garrisonBefore} → {view?.GarrisonBand})");
            string bandJson = view.ToJson();
            Check(bandJson.Contains("\"accuracy\":\"band\"") && bandJson.Contains("\"garrison_band\"")
               && !bandJson.Contains("\"garrison\""),
                  $"the banded report carries no exact garrison field ({bandJson})");
            Check(e.State.FindCity("city-b").Garrison == garrisonBefore, "and the canonical city is untouched");

            // the window survives one rollover and expires at the next
            e.EndSeason();
            e.BeginSeason();
            Check(e.State.Season == 2 && e.Observe("seat-a").FindScoutedCity("city-b") != null,
                  $"the report is still live in season {e.State.Season} (+{BalanceConfig.ScoutReportExtraSeasons})");
            e.EndSeason();
            e.BeginSeason();
            Check(e.State.Season == 3 && e.Observe("seat-a").FindScoutedCity("city-b") == null,
                  "and it has expired one season later");

            // L3+ buys exact numbers
            var exact = EspionageState(morale: 100, gold: 5000, seed: seed);
            SetLevel(exact, "seat-a", ResearchLine.Espionage, 3);
            exact.Submit(Mission(exact, "seat-a", ActionType.Scout, "city-b"));
            var exactView = exact.Observe("seat-a").FindScoutedCity("city-b");
            Check(exactView != null && exactView.Accuracy == "exact" && exactView.Garrison == garrisonBefore,
                  $"諜報 L3 reports exact numbers (accuracy {exactView?.Accuracy}, garrison {exactView?.Garrison})");
            Check(exact.Observe("seat-a").ToJson().Contains(",\"garrison\":"), "the exact JSON carries the real garrison");
            Check(CountEvents(exact.Log, "spy_exposed") == 0, "and no exposure happened on the success path");
        }

        // ---- 散播謠言: morale −10 on success, attacker deniable, victim told
        {
            int seed = FindSpySeed(ActionType.Rumor, success: true, espionageLevel: 1);
            var e = EspionageState(morale: 60, gold: 5000, seed: seed);
            SetLevel(e, "seat-a", ResearchLine.Espionage, 1);

            int goldBefore = TreasuryOf(e, "seat-a");
            var r = e.Submit(Mission(e, "seat-a", ActionType.Rumor, "city-b"));
            var rumor = Find(e.Log, "rumor");

            Check(r.Ok && rumor != null, "the rumour lands");
            Check(e.State.FindCity("city-b").Morale == 50, $"民心 −10 (morale {e.State.FindCity("city-b").Morale})");
            Check(rumor != null && rumor.ActorSeatId == null, "the record names no author (deniable on success)");
            Check(rumor != null && rumor.Visibility == "owner_only", "the rumour event is owner-only");
            Check(e.SeesEvent("seat-b", rumor), "the city's owner is told that rumours are circulating");
            Check(!e.SeesEvent("seat-c", rumor), "a third seat is not");
            Check(TreasuryOf(e, "seat-a") == goldBefore - 500, $"500 gold is gone ({goldBefore} → {TreasuryOf(e, "seat-a")})");
            Check(e.State.FindFaction("seat-a").Prestige == 50, "a successful mission costs no prestige");

            // the covert order itself is not public either
            var action = Find(e.Log, "action");
            Check(action != null && action.Visibility == "owner_only" && action.Payload["action"] == "rumor",
                  "the covert order is logged for its author only");
            Check(!e.SeesEvent("seat-c", action) && !e.SeesEvent("seat-b", action),
                  "neither the target nor a bystander sees who ordered it");
            Check(e.SeesEvent("seat-a", action), "the author keeps their own record");

            // clamp, not underflow
            var weak = EspionageState(morale: 5, gold: 5000, seed: seed);
            SetLevel(weak, "seat-a", ResearchLine.Espionage, 1);
            weak.Submit(Mission(weak, "seat-a", ActionType.Rumor, "city-b"));
            Check(weak.State.FindCity("city-b").Morale == 0, $"民心 clamps at 0 (morale {weak.State.FindCity("city-b").Morale})");
        }

        // ---- 挑撥離間: trust −10 both directions, both victims told, nobody else
        {
            int seed = FindSpySeed(ActionType.SowDistrust, success: true, espionageLevel: 2);
            var e = EspionageState(morale: 100, gold: 5000, seed: seed);
            SetLevel(e, "seat-a", ResearchLine.Espionage, 2);
            e.State.Diplomacy.SetTrust("seat-b", "seat-c", 40);
            e.State.Diplomacy.SetTrust("seat-c", "seat-b", 40);

            var r = e.Submit(SowDistrust(e, "seat-a", "seat-b", "seat-c"));
            var evt = Find(e.Log, "sow_distrust");
            Check(r.Ok && evt != null, $"挑撥離間 lands ({r.Error})");
            Check(e.State.Diplomacy.GetTrust("seat-b", "seat-c") == 30
               && e.State.Diplomacy.GetTrust("seat-c", "seat-b") == 30,
                  $"信任 −10 in both directions (B→C {e.State.Diplomacy.GetTrust("seat-b", "seat-c")}, C→B {e.State.Diplomacy.GetTrust("seat-c", "seat-b")})");
            Check(evt != null && evt.ActorSeatId == null, "the record names no author");
            Check(e.SeesEvent("seat-b", evt) && e.SeesEvent("seat-c", evt), "both affected seats see it");
            Check(!e.SeesEvent("seat-d", evt), "an uninvolved seat does not");
            Check(TreasuryOf(e, "seat-a") == 5000 - 800, "800 gold is gone");
        }

        // ---- the failure path: exposure
        {
            int seed = FindSpySeed(ActionType.Rumor, success: false, espionageLevel: 1);
            Check(seed > 0, $"a failing 散播謠言 is reachable by seed search (seed {seed})");
            var e = EspionageState(morale: 60, gold: 5000, seed: seed);
            SetLevel(e, "seat-a", ResearchLine.Espionage, 1);

            e.State.Diplomacy.SetTrust("seat-b", "seat-a", 20);
            int goldBefore = TreasuryOf(e, "seat-a");
            int moraleBefore = e.State.FindCity("city-b").Morale;

            var r = e.Submit(Mission(e, "seat-a", ActionType.Rumor, "city-b"));
            var exposed = Find(e.Log, "spy_exposed");
            Check(r.Ok && exposed != null, "the order is accepted and the agent is caught");
            Check(e.State.FindCity("city-b").Morale == moraleBefore, $"a failed mission does nothing (morale {e.State.FindCity("city-b").Morale})");
            Check(exposed != null && exposed.Visibility == "public", "the exposure is public");
            Check(exposed != null && exposed.Payload["attacker_seat_id"] == "seat-a"
               && exposed.Payload["mission"] == "rumor", "it names the attacker and the mission");
            Check(e.SeesEvent("seat-c", exposed), "the whole world sees it");
            Check(e.State.FindFaction("seat-a").Prestige == 50 - 8, $"威望 −8 ({e.State.FindFaction("seat-a").Prestige})");
            Check(e.State.Diplomacy.GetTrust("seat-b", "seat-a") == 10,
                  $"the victim's 信任 −10 ({e.State.Diplomacy.GetTrust("seat-b", "seat-a")})");
            Check(TreasuryOf(e, "seat-a") == goldBefore - 500, $"the gold was spent anyway ({TreasuryOf(e, "seat-a")})");
            Check(CountEvents(e.Log, "rumor") == 0, "and no rumour event was logged");

            // 偵察 failure: nothing learned
            int scoutFailSeed = FindSpySeed(ActionType.Scout, success: false, espionageLevel: 1);
            var sf = EspionageState(morale: 100, gold: 5000, seed: scoutFailSeed);
            SetLevel(sf, "seat-a", ResearchLine.Espionage, 1);
            // Sight rather than a scout report: the failure must not leave a report in the observation.
            sf.State.WatchTowers.Add(new WatchTowerState
            {
                Id = "tower-esp-fail", Name = "Tower", X = 7, Z = 0, OwnerSeatId = "seat-a",
            });
            sf.Submit(Mission(sf, "seat-a", ActionType.Scout, "city-b"));
            Check(sf.Observe("seat-a").FindScoutedCity("city-b") == null, "a failed 偵察 reports nothing");
            Check(Find(sf.Log, "spy_exposed") != null, "and exposes the scout");
            Check(Find(sf.Log, "spy_exposed").Payload["detected_by_seat_id"] == "seat-b",
                  "the exposure names the city's owner as the detector");
        }

        // ---- gold is drawn from the faction treasury (state order, like 贈禮/ADR-012)
        {
            int seed = FindSpySeed(ActionType.Rumor, success: true, espionageLevel: 1);
            var e = EspionageState(morale: 60, gold: 1000, seed: seed);
            SetLevel(e, "seat-a", ResearchLine.Espionage, 1);

            e.State.FindCity("city-a").Gold = 100;      // short in the capital
            e.State.FindCity("city-a2").Gold = 1000;    // the rest sits in the second city
            var r = e.Submit(Mission(e, "seat-a", ActionType.Rumor, "city-b"));
            Check(r.Ok, $"a mission pays out of the treasury, not one city ({r.Error})");
            Check(e.State.FindCity("city-a").Gold == 0 && e.State.FindCity("city-a2").Gold == 600,
                  $"500 spread over the cities in state order ({e.State.FindCity("city-a").Gold}/{e.State.FindCity("city-a2").Gold})");
        }

        // ---- ScriptedController: an occasional rumour, never 挑撥離間
        {
            var state = ScriptedEspionageState(season: 3, atWar: true, espionageLevel: 1, gold: 2000);
            var plan = ScriptedController.PlanTurn(state, "seat-a");
            int rumors = 0;
            string rumorTarget = null;
            foreach (var cmd in plan)
            {
                if (cmd.Type == ActionType.Rumor) { rumors++; rumorTarget = cmd.TargetId; }
                if (cmd.Type == ActionType.SowDistrust) Check(false, "the baseline must never sow distrust");
            }
            Check(rumors == 1, $"at war, rich and researched → one rumour a season ({rumors})");
            Check(rumorTarget == "city-weak", $"aimed at the enemy's least loyal city ({rumorTarget})");

            var peace = ScriptedEspionageState(season: 3, atWar: false, espionageLevel: 1, gold: 2000);
            bool anyRumor = false;
            foreach (var cmd in ScriptedController.PlanTurn(peace, "seat-a"))
                if (cmd.Type == ActionType.Rumor) anyRumor = true;
            Check(!anyRumor, "no mission without a war");

            var unpicked = ScriptedEspionageState(season: 4, atWar: true, espionageLevel: 1, gold: 2000);
            anyRumor = false;
            foreach (var cmd in ScriptedController.PlanTurn(unpicked, "seat-a"))
                if (cmd.Type == ActionType.Rumor) anyRumor = true;
            Check(!anyRumor, "and not every season (the baseline stays occasional)");
        }

        // ---- determinism through the espionage path
        {
            string runA = SpyReplay(4242), runB = SpyReplay(4242);
            Check(runA == runB && runA.Length > 0, "two identical seeds replay byte-identical espionage logs");
            Check(SpyReplay(4242) != SpyReplay(77), "a different seed diverges");
        }
    }

    // ---- P8 helpers ---------------------------------------------------------

    /// <summary>Makes a city nameable by a seat without giving it sight: a live 偵察 report is one of
    /// the two sources <see cref="GameEngine.IsCityKnown"/> uses for safe error detail. This is a test
    /// fixture for the known-neutral specific rejection, not an engine targeting requirement.</summary>
    private static void MakeCityKnown(GameEngine e, string seatId, string cityId)
    {
        var fac = e.State.FindFaction(seatId);
        if (fac != null) fac.AddScoutReport(cityId, e.State.Season);
    }

    /// <summary>Three seats: A (the agent), B (the mark, city-b), C (a bystander); plus a neutral city.</summary>
    private static GameEngine EspionageState(int morale, int gold, int seed = 21)
    {
        var e = new GameEngine(seed);
        e.State.Map = Corridor(12);
        e.State.Factions.Add(new FactionState { SeatId = "seat-a", Name = "Wei", Controller = ControllerType.Human });
        e.State.Factions.Add(new FactionState { SeatId = "seat-b", Name = "Shu", Controller = ControllerType.ScriptedAi });
        e.State.Factions.Add(new FactionState { SeatId = "seat-c", Name = "Wu", Controller = ControllerType.ScriptedAi });
        e.State.Cities.Add(new CityState
        {
            Id = "city-a", Name = "City A", X = 0, Z = 0, OwnerSeatId = "seat-a",
            Population = 10000, Food = 5000, Gold = gold, Garrison = 500, Morale = 100,
        });
        e.State.Cities.Add(new CityState
        {
            Id = "city-a2", Name = "City A2", X = 1, Z = 0, OwnerSeatId = "seat-a",
            Population = 10000, Food = 5000, Gold = 0, Garrison = 500, Morale = 100,
        });
        e.State.Cities.Add(new CityState
        {
            Id = "city-b", Name = "City B", X = 8, Z = 0, OwnerSeatId = "seat-b",
            Population = 10000, Food = 2000, Gold = 300, Garrison = 3200, Morale = morale, Defense = 1,
        });
        e.State.Cities.Add(new CityState
        {
            Id = "city-n", Name = "Neutral", X = 10, Z = 0, OwnerSeatId = null,
            Population = 10000, Food = 200, Gold = 0, Garrison = 100, Morale = 70,
        });
        e.State.Cities.Add(new CityState
        {
            Id = "city-weak", Name = "Weak City", X = 9, Z = 0, OwnerSeatId = "seat-b",
            Population = 10000, Food = 300, Gold = 0, Garrison = 100, Morale = 15,
        });
        e.State.Factions.Add(new FactionState { SeatId = "seat-d", Name = "Han", Controller = ControllerType.ScriptedAi });
        e.BeginSeason();
        e.State.Season = 1;
        e.State.Phase = "player_orders";
        return e;
    }

    private static ActionCommand Mission(GameEngine e, string seat, string type, string cityId)
        => new ActionCommand { Type = type, ActorSeatId = seat, TargetId = cityId };

    private static ActionCommand SowDistrust(GameEngine e, string seat, string seatB, string seatC)
        => new ActionCommand
        {
            Type = ActionType.SowDistrust, ActorSeatId = seat, TargetId = seatB,
            Args = new Dictionary<string, string> { { "target_b", seatC } },
        };

    private static int TreasuryOf(GameEngine e, string seat)
    {
        int gold = 0;
        foreach (var city in e.State.CitiesOf(seat)) gold += city.Gold;
        return gold;
    }

    /// <summary>
    /// The deterministic way to hit either branch of the §8.3 roll: scan seeds for the first engine whose
    /// first mission roll lands on the wanted side. No test hook is wired into the engine itself.
    /// </summary>
    private static int FindSpySeed(string mission, bool success, int espionageLevel)
    {
        for (int seed = 1; seed <= 300; seed++)
        {
            var e = EspionageState(morale: 60, gold: 5000, seed: seed);
            e.State.FindFaction("seat-a").Research[ResearchLine.Espionage] = espionageLevel;

            if (mission == ActionType.SowDistrust)
                e.Submit(SowDistrust(e, "seat-a", "seat-b", "seat-c"));
            else
                e.Submit(Mission(e, "seat-a", mission, "city-b"));

            bool exposed = Find(e.Log, "spy_exposed") != null;
            if (exposed == !success) return seed;
        }
        return -1;
    }

    /// <summary>A scripted seat at war with seat-b, with weak and strong enemy cities to choose between.
    /// Two cities that need nothing else, so a 號令 is still free by the time the spy block runs.</summary>
    private static GameState ScriptedEspionageState(int season, bool atWar, int espionageLevel, int gold)
    {
        var e = EspionageState(morale: 60, gold: gold, seed: 5);
        e.State.Season = season;
        e.State.FindFaction("seat-a").Research[ResearchLine.Espionage] = espionageLevel;
        foreach (var city in e.State.CitiesOf("seat-a"))
        {
            city.ReclaimPct = BalanceConfig.ReclaimPctCap;      // no 開墾
            city.Training = BalanceConfig.TrainingMax;          // no 練兵
            city.Defense = BalanceConfig.DefenseMax;            // no 修城
            city.Population = BalanceConfig.RecruitPopCost / 2; // no 募兵
            city.Morale = 100;                                  // no 賑災
            int recruitFloor = UnitTypes.RecruitGoldCost(UnitTypes.Infantry, BalanceConfig.RecruitTroopGain);
            if (city.Gold < recruitFloor) city.Gold = recruitFloor;  // no 徵稅
        }
        if (atWar) e.State.Diplomacy.DeclareWar("seat-a", "seat-b");
        return e.State;
    }

    /// <summary>Six seasons of scripted play by a spy-capable seat, at war, with money to burn.</summary>
    private static string SpyReplay(int seed)
    {
        var e = EspionageState(morale: 60, gold: 4000, seed: seed);
        e.State.FindFaction("seat-a").Research[ResearchLine.Espionage] = 2;
        e.State.Diplomacy.DeclareWar("seat-a", "seat-b");
        for (int season = 0; season < 6; season++)
        {
            e.Submit(Mission(e, "seat-a", ActionType.Scout, "city-b"));
            e.Submit(Mission(e, "seat-a", ActionType.Rumor, "city-b"));
            e.Submit(SowDistrust(e, "seat-a", "seat-b", "seat-c"));
            foreach (var cmd in ScriptedController.PlanTurn(e.State, "seat-a")) e.Submit(cmd);
            e.EndSeason();
            e.BeginSeason();
        }
        return e.Log.ToJsonLines();
    }
}
