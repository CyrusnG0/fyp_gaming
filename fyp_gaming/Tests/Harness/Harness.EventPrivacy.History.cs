using System;
using System.Collections.Generic;
using LuanShi.Engine;

// [LS-002] Event privacy: paired hidden-world and stable-history assertions.
// Kept separate from the payload/oracle modules so each test module stays focused.

internal static partial class Harness
{
    // ---- paired worlds: hidden march facts ---------------------------------

    private static void PairedHiddenMarch()
    {
        // Two worlds identical to seat-a; only the hidden destination and private reason differ.
        var w1 = PrivacyWorld(604, hiddenArmyTroops: 800);
        var w2 = PrivacyWorld(604, hiddenArmyTroops: 800);
        var a1 = w1.State.FindArmy("army-seat-b-1");
        var a2 = w2.State.FindArmy("army-seat-b-1");
        w1.Submit(PrivacyOrder(ActionType.March, "seat-b", a1.Id, 11, 0, reason: "reason one"));
        w2.Submit(PrivacyOrder(ActionType.March, "seat-b", a2.Id, 12, 0, reason: "reason two"));

        var obsA1 = w1.Observe("seat-a");
        var obsA2 = w2.Observe("seat-a");
        string json1 = obsA1.ToJson(), json2 = obsA2.ToJson();

        Check(json1 == json2, "[LS-002] two hidden march destinations produce the same bystander observation");
        Check(ObsEvent(obsA1, "army_moved") == null && ObsEvent(obsA2, "army_moved") == null,
              "[LS-002] an out-of-sight army_moved event is not delivered to the bystander");
        Check(obsA1.FindArmy("army-seat-b-1") == null && obsA2.FindArmy("army-seat-b-1") == null,
              "[LS-002] the hidden army is absent from the bystander's observation");
        Check(!json1.Contains("reason one") && !json1.Contains("reason two"),
              "[LS-002] neither private reason reaches the bystander");
        Check(ObsEvent(w1.Observe("seat-b"), "army_moved") != null,
              "[LS-002] the acting seat still receives its own army_moved event");
        Check(ObsEvent(w1.Observe("seat-b"), "army_moved").Payload["to"] == "[11,0]",
              "[LS-002] the actor sees the real destination");

        // A witness who sees only the destination must not learn the hidden origin or path cost.
        var e = PrivacyWorld(619);
        var hiddenArmy = AddArmy(e, "seat-b", 10, 800);
        AddArmy(e, "seat-a", 14, 500);
        var witnessMarch = e.Submit(PrivacyOrder(ActionType.March, "seat-b", hiddenArmy.Id, 13, 0,
            reason: "hidden origin"));
        var witnessEvent = ObsEvent(e.Observe("seat-a"), "army_moved");
        Check(witnessMarch.Ok && witnessEvent != null,
              $"[LS-002] a march into sight is delivered to the witness ({witnessMarch.Error})");
        Check(witnessEvent != null && witnessEvent.Payload["to"] == "[13,0]"
           && !witnessEvent.Payload.ContainsKey("from")
           && !witnessEvent.Payload.ContainsKey("cost")
           && !witnessEvent.Payload.ContainsKey("march_count"),
              "[LS-002] the witness gets the destination only, not the hidden origin/cost/history");
    }

    // ---- paired worlds: private reason / hidden target ---------------------

    private static void PairedPrivateReasonAndTarget()
    {
        var w1 = PrivacyWorld(605);
        var w2 = PrivacyWorld(605);
        w1.Submit(PrivacyOrder(ActionType.Reclaim, "seat-b", "city-b", reason: "build food first"));
        w2.Submit(PrivacyOrder(ActionType.Reclaim, "seat-b", "city-b", reason: "prepare for war"));

        var obsA1 = w1.Observe("seat-a");
        var obsA2 = w2.Observe("seat-a");
        Check(obsA1.ToJson() == obsA2.ToJson(),
              "[LS-002] two private reasons on an ordinary action give the same bystander observation");
        Check(ObsEvent(obsA1, "land_reclaimed") == null && ObsEvent(obsA2, "land_reclaimed") == null,
              "[LS-002] a hidden city's land_reclaimed event is not delivered");
        Check(!obsA1.ToJson().Contains("build food first") && !obsA2.ToJson().Contains("prepare for war"),
              "[LS-002] neither reason appears in the bystander's events");
        Check(ObsAction(obsA1, ActionType.Reclaim) != null
           && ObsAction(obsA1, ActionType.Reclaim).Payload.Count == 1,
              "[LS-002] the ordinary action record is still public, as a type-only record");
        Check(ObsAction(w1.Observe("seat-b"), ActionType.Reclaim).Payload["reason"] == "build food first",
              "[LS-002] the actor's own observation keeps the reason");
    }

    // ---- paired worlds: exact private strengths ---------------------------

    private static void PairedExactPrivateStrengths()
    {
        var w1 = PrivacyWorld(606, cityBGarrison: 1000, hiddenArmyTroops: 500);
        var w2 = PrivacyWorld(606, cityBGarrison: 9000, hiddenArmyTroops: 7777);
        string json1 = w1.Observe("seat-a").ToJson();
        string json2 = w2.Observe("seat-a").ToJson();

        Check(json1 == json2,
              "[LS-002] hidden garrisons/armies of different sizes give the bystander the same observation");
        Check(!json1.Contains("9000") && !json1.Contains("7777"),
              "[LS-002] the hidden exact strengths never appear in the bystander JSON");
        Check(w2.State.FindCity("city-b").Garrison == 9000
           && w2.State.FindArmy("army-seat-b-1").Troops == 7777,
              "[LS-002] the canonical state really does differ (the world is hidden, not equal)");
        Check(w2.Observe("seat-b").FindCity("city-b").Garrison == 9000,
              "[LS-002] the owner still sees its own exact garrison");
    }
    // ---- knowledge already delivered survives movement/destruction ---------

    private static void DeliveredKnowledgeSurvivesDestruction()
    {
        var e = PrivacyWorld(607, observerTower: true);
        var convert = e.Submit(PrivacyOrder(ActionType.ConvertGarrison, "seat-b", "city-b", 400));
        var formed = e.State.FindArmy("army-seat-b-1");
        var obsAfterForm = e.Observe("seat-a");
        var convertedA = ObsEvent(obsAfterForm, "garrison_converted");
        var convertedB = ObsEvent(e.Observe("seat-b"), "garrison_converted");
        var convertedC = ObsEvent(e.Observe("seat-c"), "garrison_converted");

        Check(convert.Ok && formed != null, $"[LS-002] a visible garrison forms an army ({convert.Error})");
        Check(convertedA != null && convertedA.Payload["troops"] == "400",
              "[LS-002] a seat that can see the city gets the full transfer detail (ADR-011)");
        Check(convertedB != null && convertedB.Payload["troops"] == "400",
              "[LS-002] the owner keeps the exact transfer count");
        Check(convertedC == null, "[LS-002] a seat with no sight of the city gets no transfer event");

        var garrison = e.Submit(PrivacyOrder(ActionType.GarrisonArmy, "seat-b", formed.Id, 400));
        var obs = e.Observe("seat-a");
        var obsC = e.Observe("seat-c");
        var garrisonedA = ObsEvent(obs, "army_garrisoned");
        var destroyedA = ObsEvent(obs, "army_destroyed");
        var garrisonedB = ObsEvent(e.Observe("seat-b"), "army_garrisoned");
        string jsonC = obsC.ToJson();

        Check(garrison.Ok, $"[LS-002] the visible army is fully garrisoned ({garrison.Error})");
        Check(obs.FindArmy("army-seat-b-1") == null, "[LS-002] the destroyed army is gone from the observation");
        Check(garrisonedA != null && destroyedA != null && destroyedA.Payload["army_id"] == "army-seat-b-1",
              "[LS-002] the already-delivered transfer/destruction events remain after destruction");
        Check(garrisonedA != null && garrisonedA.Payload["remaining_troops"] == "0",
              "[LS-002] the in-sight witness keeps the full army_garrisoned record");
        Check(garrisonedB != null && garrisonedB.Payload["remaining_troops"] == "0",
              "[LS-002] the owner's army_garrisoned record keeps remaining_troops");
        var destroyedC = ObsEvent(obsC, "army_destroyed");
        Check(ObsEvent(obsC, "army_garrisoned") == null,
              "[LS-002] an out-of-sight seat gets no transfer event");
        Check(destroyedC != null && destroyedC.Visibility == "public" && destroyedC.Payload.Count == 0,
              "[LS-002] the out-of-sight seat gets public destruction news with no payload");
        Check(!jsonC.Contains("army-seat-b-1") && !jsonC.Contains("400")
           && !jsonC.Contains("remaining_troops"),
              "[LS-002] the out-of-sight JSON carries no army id, transfer count or remaining_troops");
        Check(e.SeesEvent("seat-a", Find(e.Log, "garrison_converted"))
           && e.SeesEvent("seat-a", Find(e.Log, "army_destroyed")),
              "[LS-002] SeesEvent agrees after the entity has left canonical state");
        Check(ObsAction(obs, ActionType.ConvertGarrison) == null
           && ObsAction(obs, ActionType.GarrisonArmy) == null,
              "[LS-002] the transfer action records stay owner-only");
    }

    // ---- paired worlds: a destroyed hidden entity -------------------------

    private static void PairedHiddenDestruction()
    {
        var w1 = PrivacyWorld(608, hiddenArmyTroops: 4321);
        var w2 = PrivacyWorld(608, hiddenArmyTroops: 4321);
        var army = w1.State.FindArmy("army-seat-b-1");
        // World 1 destroys the hidden army; world 2 leaves it in place. Both worlds are identical to A.
        var r = w1.Submit(PrivacyOrder(ActionType.GarrisonArmy, "seat-b", army.Id, 4321));

        var obsA1 = w1.Observe("seat-a");
        var obsA2 = w2.Observe("seat-a");
        Check(r.Ok && w1.State.FindArmy("army-seat-b-1") == null,
              $"[LS-002] the hidden army is destroyed in world 1 ({r.Error})");
        var destroyedA = ObsEvent(obsA1, "army_destroyed");
        Check(destroyedA != null && destroyedA.Visibility == "public" && destroyedA.Payload.Count == 0,
              "[LS-002] hidden destruction stays public news, with no payload");
        Check(!obsA1.ToJson().Contains("army-seat-b-1") && !obsA1.ToJson().Contains("4321"),
              "[LS-002] the public news leaks no hidden army id or strength");
        Check(ObsEvent(obsA1, "army_garrisoned") == null,
              "[LS-002] the hidden transfer event itself stays out of the bystander's log");
        Check(ObsEvent(obsA2, "army_destroyed") == null,
              "[LS-002] the intact world has no destruction news");
        Check(Find(w1.Log, "army_destroyed").Visibility == "public",
              "[LS-002] army_destroyed is public again, as ACTIONS.md states");
    }

    // ---- event-time freeze: newly revealed locations -----------------------

    private static void NewlyRevealedLocation()
    {
        var noTower = PrivacyWorld(609);
        var r = noTower.Submit(PrivacyOrder(ActionType.LightenLabor, "seat-b", "city-b"));
        var labor = Find(noTower.Log, "labor_lightened");
        var before = noTower.Observe("seat-a");
        Check(r.Ok && labor != null, $"[LS-002] a hidden city's labor event is logged ({r.Error})");
        Check(before.FindCity("city-b") == null && ObsEvent(before, "labor_lightened") == null,
              "[LS-002] the city and its event are both undisclosed before new sight");

        noTower.State.WatchTowers.Add(new WatchTowerState
        {
            Id = "tower-late", Name = "Late Tower", X = 7, Z = 0, OwnerSeatId = "seat-a",
        });
        var afterReveal = noTower.Observe("seat-a");
        Check(afterReveal.FindCity("city-b") != null,
              "[LS-002] the newly placed tower does reveal the city's current state");
        Check(ObsEvent(afterReveal, "labor_lightened") == null,
              "[LS-002] but it does not reveal the old labor event (event-time freeze)");
        Check(!noTower.SeesEvent("seat-a", labor),
              "[LS-002] SeesEvent stays false for that old undisclosed record");

        var withTower = PrivacyWorld(609, observerTower: true);
        withTower.Submit(PrivacyOrder(ActionType.LightenLabor, "seat-b", "city-b"));
        var laborWithTower = Find(withTower.Log, "labor_lightened");
        var seen = withTower.Observe("seat-a");
        var seenLabor = ObsEvent(seen, "labor_lightened");
        Check(seenLabor != null, "[LS-002] a seat that had sight at event time receives the event");
        Check(seenLabor != null && seenLabor.Payload["city_id"] == "city-b"
           && seenLabor.Payload.ContainsKey("gold_spent"),
              "[LS-002] a witness sees the same city detail the current observation carries");
        Check(ObsEvent(withTower.Observe("seat-c"), "labor_lightened") == null,
              "[LS-002] a seat with no sight of the city does not receive it");

        withTower.State.WatchTowers.RemoveAll(t => t.Id == "tower-a");
        var afterLoss = withTower.Observe("seat-a");
        Check(afterLoss.FindCity("city-b") == null,
              "[LS-002] losing sight hides the city's current state");
        Check(ObsEvent(afterLoss, "labor_lightened") != null && withTower.SeesEvent("seat-a", laborWithTower),
              "[LS-002] but the delivered history survives the loss of sight");
    }

    // ---- rejection feedback / validator oracle -----------------------------

    private static void RejectionOracle()
    {
        // A hidden army at a non-adjacent hex must give the same rejection as an empty hex. Before the
        // LS-002 ordering fix the first said "out of range" and the second "no army at", which leaked
        // the hidden army's existence (OPEN_QUESTIONS 18).
        {
            var w1 = PrivacyWorld(610);
            var w2 = PrivacyWorld(610);
            var armyA1 = AddArmy(w1, "seat-a", 0, 1000);
            var armyA2 = AddArmy(w2, "seat-a", 0, 1000);
            w1.State.Armies.Add(new ArmyState
            {
                Id = "army-probe", Name = "Probe", OwnerSeatId = "seat-b",
                Troops = 5000, X = 5, Z = 0,
            });
            var probe1 = w1.Submit(PrivacyOrder(ActionType.AttackArmy, "seat-a", armyA1.Id, 5, 0,
                reason: "probe reason"));
            var probe2 = w2.Submit(PrivacyOrder(ActionType.AttackArmy, "seat-a", armyA2.Id, 5, 0,
                reason: "probe reason"));

            Check(!probe1.Ok && !probe2.Ok, "[LS-002] both hidden-army probes are rejected");
            Check(probe1.Error == probe2.Error,
                  $"[LS-002] a hidden army at range does not change the rejection ({probe1.Error})");
            Check(w1.Observe("seat-a").ToJson() == w2.Observe("seat-a").ToJson(),
                  "[LS-002] the two probe worlds give the prober the same permitted observation");
            var reject1 = Find(w1.Log, "action_rejected");
            Check(reject1 != null && reject1.Visibility == "owner_only",
                  "[LS-002] rejection records stay owner-only");
            Check(!w1.SeesEvent("seat-b", reject1) && !w1.SeesEvent("seat-c", reject1),
                  "[LS-002] and no other seat receives the rejection feedback");
        }

        // The same ordering rule for a hidden non-adjacent city.
        {
            var w1 = PrivacyWorld(611);
            var w2 = PrivacyWorld(611);
            var armyA1 = AddArmy(w1, "seat-a", 0, 1000);
            var armyA2 = AddArmy(w2, "seat-a", 0, 1000);
            w1.State.Cities.Add(new CityState
            {
                Id = "city-probe", Name = "Probe City", X = 5, Z = 0, OwnerSeatId = "seat-c",
                Population = 10000, Food = 1000, Gold = 100, Garrison = 9000, Morale = 100,
            });
            var probe1 = w1.Submit(PrivacyOrder(ActionType.AttackCity, "seat-a", armyA1.Id, 5, 0));
            var probe2 = w2.Submit(PrivacyOrder(ActionType.AttackCity, "seat-a", armyA2.Id, 5, 0));
            Check(!probe1.Ok && probe1.Error == probe2.Error,
                  $"[LS-002] a hidden non-adjacent city does not change the rejection ({probe1.Error})");
            Check(!probe1.Error.Contains("garrison") && !probe1.Error.Contains("9000"),
                  "[LS-002] the rejection names no hidden garrison");
        }

        // An adjacent (= in-sight) city's 3:1 message may name its garrison, but only to the actor.
        {
            var e = PrivacyWorld(612);
            var armyA = AddArmy(e, "seat-a", 9, 100);
            var probe = e.Submit(PrivacyOrder(ActionType.AttackCity, "seat-a", armyA.Id, 10, 0));
            var reject = Find(e.Log, "action_rejected");
            Check(!probe.Ok && probe.Error.Contains("garrison 1000"),
                  $"[LS-002] the visible city's 3:1 message may name its garrison ({probe.Error})");
            Check(reject != null && !e.SeesEvent("seat-b", reject) && !e.SeesEvent("seat-c", reject),
                  "[LS-002] but the exact-garrison feedback reaches no other seat");
        }
    }
}
