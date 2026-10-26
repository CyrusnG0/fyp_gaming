using System;
using System.Collections.Generic;
using LuanShi.Engine;

// [LS-002] Event privacy: public payload minimization, battles/sieges and the espionage oracle.

internal static partial class Harness
{
    // ---- public battle news with a minimal payload -------------------------

    private static void PublicBattlePayloads()
    {
        var e = new GameEngine(613);
        e.State.Map = Corridor(12);
        e.State.Factions.Add(new FactionState { SeatId = "seat-a", Name = "Wei", Controller = ControllerType.Human });
        e.State.Factions.Add(new FactionState { SeatId = "seat-b", Name = "Shu", Controller = ControllerType.ScriptedAi });
        e.State.Factions.Add(new FactionState { SeatId = "seat-c", Name = "Wu", Controller = ControllerType.ScriptedAi });
        e.State.Factions.Add(new FactionState { SeatId = "seat-d", Name = "Han", Controller = ControllerType.ScriptedAi });
        e.BeginSeason();
        var observer = AddArmy(e, "seat-a", 9, 999);
        var witness = AddArmy(e, "seat-d", 1, 111);
        var attacker = AddArmy(e, "seat-b", 2, 4321);
        attacker.UnitType = UnitTypes.Archer;
        var defender = AddEnemyArmy(e, "seat-c", 4, 876, UnitTypes.Infantry);
        var r = e.Submit(PrivacyOrder(ActionType.AttackArmy, "seat-b", attacker.Id, 4, 0));

        var canonical = Find(e.Log, "battle_field");
        var obsA = e.Observe("seat-a");
        var obsB = e.Observe("seat-b");
        var obsC = e.Observe("seat-c");
        var obsD = e.Observe("seat-d");
        var aEvent = ObsEvent(obsA, "battle_field");
        var bEvent = ObsEvent(obsB, "battle_field");
        var cEvent = ObsEvent(obsC, "battle_field");
        var dEvent = ObsEvent(obsD, "battle_field");
        string jsonA = obsA.ToJson();
        string jsonD = obsD.ToJson();

        Check(r.Ok && canonical != null, $"[LS-002] a field battle is resolved and logged ({r.Error})");
        Check(canonical != null && canonical.Visibility == "public",
              "[LS-002] battle_field remains explicitly public (ACTIONS.md M2)");
        Check(canonical != null && canonical.Payload["attacker_troops_before"] == "4321"
           && canonical.Payload.ContainsKey("round_log"),
              "[LS-002] the canonical battle report keeps the full order of battle");
        Check(aEvent != null && aEvent.Payload["attacker_troops_before"] == "?"
           && aEvent.Payload["defender_troops_before"] == "?"
           && !aEvent.Payload.ContainsKey("attacker_unit_type")
           && !aEvent.Payload.ContainsKey("defender_unit_type"),
              "[LS-002] a public bystander gets placeholders, not exact strengths/types");
        Check(aEvent != null && !aEvent.Payload.ContainsKey("round_log"),
              "[LS-002] the public bystander gets no round-by-round log");
        Check(aEvent != null && canonical != null && aEvent.Payload["outcome"] == canonical.Payload["outcome"]
           && aEvent.Payload["rounds"] == canonical.Payload["rounds"],
              "[LS-002] the public report still carries the outcome and round count");
        Check(bEvent != null && bEvent.Payload["attacker_troops_before"] == "4321"
           && bEvent.Payload.ContainsKey("round_log"),
              "[LS-002] the attacker keeps the full battle report");
        Check(cEvent != null && cEvent.Payload["defender_troops_before"] == "876",
              "[LS-002] the defender keeps the full battle report");
        Check(dEvent != null && dEvent.Payload["attacker_troops_before"] == "?"
           && dEvent.Payload["defender_troops_before"] == "?"
           && !dEvent.Payload.ContainsKey("round_log"),
              "[LS-002] a battle-location witness gets the limited report, not hidden strengths/logs");
        Check(obsD.FindArmy(attacker.Id) != null && obsD.FindArmy(defender.Id) == null,
              "[LS-002] the witness really does see only one side of the ranged battle");
        Check(!jsonD.Contains("876") && !jsonD.Contains("round_log"),
              "[LS-002] the unseen defender's strength and the round log stay out of the witness JSON");
        Check(!jsonA.Contains("4321") && !jsonA.Contains("876") && !jsonA.Contains("round_log"),
              "[LS-002] exact strengths and the round log do not appear in the bystander JSON");
        Check(obsA.FindArmy(attacker.Id) == null && obsA.FindArmy(defender.Id) == null,
              "[LS-002] the hidden battle participants stay absent from the bystander's map");
    }

    // ---- public siege + defensive army_garrisoned -------------------------

    private static void PublicSiegeAndDefensiveMergePayloads()
    {
        var e = new GameEngine(614);
        e.State.Map = Corridor(12);
        e.State.Factions.Add(new FactionState { SeatId = "seat-a", Name = "Wei", Controller = ControllerType.Human });
        e.State.Factions.Add(new FactionState { SeatId = "seat-b", Name = "Shu", Controller = ControllerType.ScriptedAi });
        e.State.Factions.Add(new FactionState { SeatId = "seat-c", Name = "Wu", Controller = ControllerType.ScriptedAi });
        e.State.Cities.Add(new CityState
        {
            Id = "city-a", Name = "City A", X = 9, Z = 0, OwnerSeatId = "seat-a",
            Population = 10000, Food = 5000, Gold = 1000, Garrison = 777, Morale = 100,
        });
        e.State.Cities.Add(new CityState
        {
            Id = "city-c", Name = "City C", X = 3, Z = 0, OwnerSeatId = "seat-c",
            Population = 10000, Food = 5000, Gold = 1000, Garrison = 1000, Morale = 100,
        });
        e.BeginSeason();
        var attacker = AddArmy(e, "seat-b", 2, 8000);
        AddEnemyArmy(e, "seat-c", 3, 1500, UnitTypes.Infantry);
        var r = e.Submit(PrivacyOrder(ActionType.AttackCity, "seat-b", attacker.Id, 3, 0));

        var canonicalMerge = Find(e.Log, "army_garrisoned");
        var canonicalSiege = Find(e.Log, "battle_siege");
        var obsA = e.Observe("seat-a");
        var obsC = e.Observe("seat-c");
        var mergeA = ObsEvent(obsA, "army_garrisoned");
        var mergeC = ObsEvent(obsC, "army_garrisoned");
        var siegeA = ObsEvent(obsA, "battle_siege");
        string jsonA = obsA.ToJson();

        Check(r.Ok && canonicalMerge != null && canonicalSiege != null,
              $"[LS-002] the defensive merge and siege are resolved ({r.Error})");
        Check(canonicalMerge != null && canonicalMerge.Visibility == "public"
           && canonicalMerge.Payload["troops"] == "1500"
           && canonicalMerge.Payload["mode"] == "defensive_assault",
              "[LS-002] the defensive army_garrisoned stays public and keeps its full payload");
        Check(mergeA != null && !mergeA.Payload.ContainsKey("troops")
           && !mergeA.Payload.ContainsKey("remaining_troops")
           && !mergeA.Payload.ContainsKey("army_id")
           && !mergeA.Payload.ContainsKey("city_id"),
              "[LS-002] a public bystander's defensive merge has no ids or exact troop count");
        Check(mergeC != null && mergeC.Payload["troops"] == "1500",
              "[LS-002] the defending city owner keeps the exact merged count");
        Check(canonicalSiege != null && canonicalSiege.Visibility == "public",
              "[LS-002] battle_siege remains explicitly public (ACTIONS.md M3)");
        Check(siegeA != null && siegeA.Payload["garrison_before"] == "?"
           && siegeA.Payload["troops_before"] == "?"
           && !siegeA.Payload.ContainsKey("attacker_unit_type")
           && !siegeA.Payload.ContainsKey("garrison_unit_type"),
              "[LS-002] the public siege report uses placeholders for strengths/types");
        Check(siegeA != null && !siegeA.Payload.ContainsKey("round_log"),
              "[LS-002] the public siege report has no round log");
        Check(!jsonA.Contains("1500") && !jsonA.Contains("8000") && !jsonA.Contains("round_log"),
              "[LS-002] exact siege strengths and the round log stay out of the bystander JSON");
        Check(obsA.FindCity("city-c") == null,
              "[LS-002] the hidden belligerent city stays absent from the bystander's map");
    }
    // ---- world events without a city in sight ------------------------------

    private static void HiddenWorldEventPrivacy()
    {
        var hidden = PrivacyWorld(617);
        hidden.State.FindCity("city-b").Food = 0;
        hidden.EndSeason();
        var canonical = Find(hidden.Log, "starvation");
        Check(canonical != null && canonical.Visibility == "owner_and_observers",
              "[LS-002] a famine is owner_and_observers, not a world-wide public event");
        Check(ObsEvent(hidden.Observe("seat-a"), "starvation") == null,
              "[LS-002] a hidden city's famine is not delivered to a seat that cannot see it");
        Check(ObsEvent(hidden.Observe("seat-b"), "starvation") != null,
              "[LS-002] the starving city's owner still gets the event and its pop_lost");

        var visible = PrivacyWorld(618, observerTower: true);
        visible.State.FindCity("city-b").Food = 0;
        visible.EndSeason();
        var seen = ObsEvent(visible.Observe("seat-a"), "starvation");
        Check(seen != null && seen.Payload["city_id"] == "city-b"
           && seen.Payload.ContainsKey("pop_lost"),
              "[LS-002] an in-sight witness gets the same famine detail as the owner");
        Check(ObsEvent(visible.Observe("seat-c"), "starvation") == null,
              "[LS-002] a seat with no sight of the city gets no famine event");
    }
    // ---- espionage target oracle: hidden targets share one answer -----------

    private static void EspionageTargetOracle()
    {
        // Two worlds with sufficient research and gold: one target id is unknown, the other is a
        // hidden neutral city. Both are invalid, so both must produce the same generic answer and
        // neither may spend gold.
        var w1 = PrivacyWorld(622);
        var w2 = PrivacyWorld(622);
        AddHiddenCity(w2, null);
        SetLevel(w1, "seat-a", ResearchLine.Espionage, 1);
        SetLevel(w2, "seat-a", ResearchLine.Espionage, 1);
        var rumor1 = w1.Submit(PrivacyOrder(ActionType.Rumor, "seat-a", "city-missing"));
        var rumor2 = w2.Submit(PrivacyOrder(ActionType.Rumor, "seat-a", "city-hidden"));
        Check(!rumor1.Ok && !rumor2.Ok && rumor1.Error == rumor2.Error
           && rumor1.Error.Contains("not available"),
              $"[LS-002] hidden unknown/neutral targets share one generic rejection ({rumor1.Error})");
        Check(w1.Observe("seat-a").ToJson() == w2.Observe("seat-a").ToJson(),
              "[LS-002] the hidden unknown/neutral worlds give the prober the same observation");
        Check(w1.State.FindCity("city-a").Gold == 1000 && w2.State.FindCity("city-a").Gold == 1000,
              "[LS-002] an invalid hidden-target probe spends no gold");
        Check(Find(w1.Log, "rumor") == null && Find(w2.Log, "rumor") == null,
              "[LS-002] an invalid hidden-target probe has no mission outcome");

        // Capability and affordability are checked before hidden target facts. A seat without either
        // learns only its own gate status, never whether the hidden target exists/is neutral.
        var noLevel = PrivacyWorld(623);
        AddHiddenCity(noLevel, null);
        var noResearch = noLevel.Submit(PrivacyOrder(ActionType.Rumor, "seat-a", "city-hidden"));
        Check(!noResearch.Ok && noResearch.Error.Contains("needs Espionage 1")
           && noLevel.State.FindCity("city-a").Gold == 1000,
              $"[LS-002] research gate precedes hidden-target responses ({noResearch.Error})");

        var poor = PrivacyWorld(624);
        AddHiddenCity(poor, null);
        SetLevel(poor, "seat-a", ResearchLine.Espionage, 1);
        poor.State.FindCity("city-a").Gold = 100;
        var noGold = poor.Submit(PrivacyOrder(ActionType.Rumor, "seat-a", "city-hidden"));
        Check(!noGold.Ok && noGold.Error.Contains("not enough gold")
           && poor.State.FindCity("city-a").Gold == 100,
              $"[LS-002] affordability precedes hidden-target responses and spends nothing ({noGold.Error})");

        // A hidden enemy city is a valid paid target even with no sight/live report: ADR-015 still
        // allows a chronicle-named target. The success seed is derived from the same RNG sequence.
        int seed = FindSpySeed(ActionType.Scout, success: true, espionageLevel: 1);
        var valid = PrivacyWorld(seed);
        AddHiddenCity(valid, "seat-b");
        SetLevel(valid, "seat-a", ResearchLine.Espionage, 1);
        var validScout = valid.Submit(PrivacyOrder(ActionType.Scout, "seat-a", "city-hidden"));
        Check(validScout.Ok && !valid.InSight("seat-a", 5, 0),
              $"[LS-002] an out-of-sight chronicle-named enemy city can still be scouted ({validScout.Error})");
        Check(Find(valid.Log, "scout_report") != null
           && valid.Observe("seat-a").FindScoutedCity("city-hidden") != null,
              "[LS-002] the valid out-of-sight mission produces its report");
        Check(valid.State.FindCity("city-a").Gold == 1000 - BalanceConfig.ScoutGoldCost,
              "[LS-002] a valid hidden-target mission pays the normal gold cost");

        // The same ADR-015 allowance applies to a chronicle-named hidden enemy for 散播謠言.
        int rumorSeed = FindSpySeed(ActionType.Rumor, success: true, espionageLevel: 1);
        var validRumor = PrivacyWorld(rumorSeed);
        AddHiddenCity(validRumor, "seat-b");
        SetLevel(validRumor, "seat-a", ResearchLine.Espionage, 1);
        var hiddenRumor = validRumor.Submit(PrivacyOrder(ActionType.Rumor, "seat-a", "city-hidden"));
        Check(hiddenRumor.Ok && !validRumor.InSight("seat-a", 5, 0),
              $"[LS-002] an out-of-sight chronicle-named enemy city can still be rumoured ({hiddenRumor.Error})");
        Check(Find(validRumor.Log, "rumor") != null
           && validRumor.State.FindCity("city-hidden").Morale == 100 - BalanceConfig.RumorMoraleLoss,
              "[LS-002] the valid hidden-target rumour lands normally");
        Check(validRumor.State.FindCity("city-a").Gold == 1000 - BalanceConfig.RumorGoldCost,
              "[LS-002] the valid hidden-target rumour pays the normal gold cost");

        // Specific own/neutral answers are safe only when the city is currently known.
        var known = PrivacyWorld(625);
        AddHiddenCity(known, null);
        SetLevel(known, "seat-a", ResearchLine.Espionage, 1);
        known.State.WatchTowers.Add(new WatchTowerState
        {
            Id = "tower-known", Name = "Known Tower", X = 3, Z = 0, OwnerSeatId = "seat-a",
        });
        var knownNeutral = known.Submit(PrivacyOrder(ActionType.Rumor, "seat-a", "city-hidden"));
        Check(!knownNeutral.Ok && knownNeutral.Error.Contains("neutral city")
           && known.State.FindCity("city-a").Gold == 1000,
              $"[LS-002] a known city may receive the specific neutral rejection ({knownNeutral.Error})");
        var knownOwn = known.Submit(PrivacyOrder(ActionType.Rumor, "seat-a", "city-a"));
        Check(!knownOwn.Ok && knownOwn.Error.Contains("your own city")
           && known.State.FindCity("city-a").Gold == 1000,
              $"[LS-002] a known own city may receive the specific own-city rejection ({knownOwn.Error})");
    }
}
