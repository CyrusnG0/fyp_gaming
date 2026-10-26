using System;
using System.Collections.Generic;
using LuanShi.Engine;

// [LS-002] Event privacy and stable seat history (ADR-025).
//
// These assertions pin the event-time audience freeze and per-seat payload projection introduced for
// LS-002. Every assertion name carries the [LS-002] tag so the new count stays distinguishable from the
// recovered/LS-001 assertions in the shared harness output.

internal static partial class Harness
{
    private static void RunEventPrivacy()
    {
        Section("[new] LS-002 event privacy and stable seat history (ADR-025)");

        GenericActionPrivacy();
        PairedHiddenMarch();
        PairedPrivateReasonAndTarget();
        PairedExactPrivateStrengths();
        CovertActionPrivacy();
        DeliveredKnowledgeSurvivesDestruction();
        PairedHiddenDestruction();
        NewlyRevealedLocation();
        RejectionOracle();
        PublicBattlePayloads();
        PublicSiegeAndDefensiveMergePayloads();
        HiddenWorldEventPrivacy();
        PublicDesignEventsPreserved();
        FailSafeUnknownEvents();
        EspionageTargetOracle();
        PrivacyDeterminism();
    }

    // ---- the fixtures -------------------------------------------------------

    /// <summary>
    /// Seat A (the observer) owns city-a at 0 and can see hexes 0..3. Seats B and C own city-b at 10
    /// and city-c at 15. Everything at 10/15 is hidden from A unless a tower is placed at 7 first. All
    /// hidden-fact variations in this section are expressed as parameters of this one world.
    /// </summary>
    private static GameEngine PrivacyWorld(int seed, bool observerTower = false,
        int cityBGarrison = 1000, int hiddenArmyTroops = 0, int cityBGold = 3000)
    {
        var e = new GameEngine(seed);
        e.State.Map = Corridor(20);
        e.State.Factions.Add(new FactionState { SeatId = "seat-a", Name = "Wei", Controller = ControllerType.Human });
        e.State.Factions.Add(new FactionState { SeatId = "seat-b", Name = "Shu", Controller = ControllerType.ScriptedAi });
        e.State.Factions.Add(new FactionState { SeatId = "seat-c", Name = "Wu", Controller = ControllerType.ScriptedAi });
        e.State.Cities.Add(new CityState
        {
            Id = "city-a", Name = "City A", X = 0, Z = 0, OwnerSeatId = "seat-a",
            Population = 10000, Food = 5000, Gold = 1000, Garrison = 777, Morale = 100,
        });
        e.State.Cities.Add(new CityState
        {
            Id = "city-b", Name = "City B", X = 10, Z = 0, OwnerSeatId = "seat-b",
            Population = 10000, Food = 5000, Gold = cityBGold, Garrison = cityBGarrison, Morale = 100,
        });
        e.State.Cities.Add(new CityState
        {
            Id = "city-c", Name = "City C", X = 15, Z = 0, OwnerSeatId = "seat-c",
            Population = 10000, Food = 5000, Gold = 1000, Garrison = 333, Morale = 100,
        });
        if (hiddenArmyTroops > 0)
            e.State.Armies.Add(new ArmyState
            {
                Id = "army-seat-b-1", Name = "B Army", OwnerSeatId = "seat-b",
                Troops = hiddenArmyTroops, X = 10, Z = 0,
            });
        if (observerTower)
            e.State.WatchTowers.Add(new WatchTowerState
            {
                Id = "tower-a", Name = "Tower", X = 7, Z = 0, OwnerSeatId = "seat-a",
            });
        e.BeginSeason();
        e.State.Season = 1;
        e.State.Phase = "player_orders";
        return e;
    }

    private static ActionCommand PrivacyOrder(string type, string actor, string target = null,
        int paramA = 0, int paramB = 0, string reason = null, string text = null,
        Dictionary<string, string> args = null)
        => new ActionCommand
        {
            Type = type, ActorSeatId = actor, TargetId = target,
            ParamA = paramA, ParamB = paramB, Reason = reason, Text = text, Args = args,
        };

    private static void AddHiddenCity(GameEngine e, string ownerSeatId)
    {
        e.State.Cities.Add(new CityState
        {
            Id = "city-hidden", Name = "Hidden City", X = 5, Z = 0, OwnerSeatId = ownerSeatId,
            Population = 10000, Food = 1000, Gold = 100, Garrison = 500, Morale = 100,
        });
    }

    private static EventView ObsEvent(Observation obs, string eventType)
    {
        EventView found = null;
        if (obs == null) return null;
        foreach (var rec in obs.Events) if (rec.EventType == eventType) found = rec;
        return found;
    }

    private static EventView ObsAction(Observation obs, string actionType)
    {
        EventView found = null;
        if (obs == null) return null;
        foreach (var rec in obs.Events)
            if (rec.EventType == "action" && rec.Payload.TryGetValue("action", out string action)
                && action == actionType)
                found = rec;
        return found;
    }

    // ---- generic action records --------------------------------------------

    private static void GenericActionPrivacy()
    {
        // A march's destination and the commander's private reasoning are research data, not public
        // news. The action type stays public (ADR-018), its parameters do not.
        {
            var e = PrivacyWorld(601, hiddenArmyTroops: 800);
            var army = e.State.FindArmy("army-seat-b-1");
            var r = e.Submit(PrivacyOrder(ActionType.March, "seat-b", army.Id, 12, 0,
                reason: "secret march reason"));
            var canonical = LastAction(e, ActionType.March);
            var obsA = e.Observe("seat-a");
            var actionA = ObsAction(obsA, ActionType.March);
            var obsB = e.Observe("seat-b");
            var actionB = ObsAction(obsB, ActionType.March);
            string jsonA = obsA.ToJson();

            Check(r.Ok && canonical != null, $"[LS-002] a hidden march is executed and logged ({r.Error})");
            Check(canonical != null && canonical.Visibility == "public",
                  "[LS-002] its generic action record stays public (ADR-018)");
            Check(canonical != null && canonical.Payload["reason"] == "secret march reason"
               && canonical.Payload["param_a"] == "12",
                  "[LS-002] the canonical log keeps the full order for the researcher");
            Check(e.Log.ToJsonLines().Contains("secret march reason"),
                  "[LS-002] the canonical log serializes the private reason");
            Check(actionA != null && actionA.Payload.Count == 1 && actionA.Payload["action"] == "march",
                  "[LS-002] a non-party seat's action record carries the action type only");
            Check(actionA != null && actionA.Args == null && actionA.Text == null,
                  "[LS-002] the non-party action record drops args/text");
            Check(!jsonA.Contains("secret march reason") && !jsonA.Contains("\"param_a\"")
               && !jsonA.Contains("\"target_id\""),
                  "[LS-002] the observing seat's JSON carries no reason, destination or target");
            Check(actionB != null && actionB.Payload["reason"] == "secret march reason"
               && actionB.Payload["param_a"] == "12",
                  "[LS-002] the actor keeps the full action record in its own observation");
        }

        // A treaty offer's args and text are private between the two seats until it becomes public news;
        // the public action record must not publish them either.
        {
            var e = PrivacyWorld(602);
            var r = e.Submit(PrivacyOrder(ActionType.ProposeTreaty, "seat-b", "seat-c",
                text: "secret pact",
                reason: "private plan",
                args: new Dictionary<string, string> { { "treaty_type", "alliance" } }));
            var obsA = e.Observe("seat-a");
            var actionA = ObsAction(obsA, ActionType.ProposeTreaty);
            var proposalA = ObsEvent(obsA, "treaty_proposed");
            var proposalC = ObsEvent(e.Observe("seat-c"), "treaty_proposed");
            var actionB = ObsAction(e.Observe("seat-b"), ActionType.ProposeTreaty);
            string jsonA = obsA.ToJson();

            Check(r.Ok, $"[LS-002] a treaty offer is accepted ({r.Error})");
            Check(actionA != null && actionA.Payload.Count == 1 && actionA.Args == null && actionA.Text == null,
                  "[LS-002] the public action record has no treaty args/text");
            Check(!jsonA.Contains("alliance") && !jsonA.Contains("secret pact")
               && !jsonA.Contains("private plan"),
                  "[LS-002] the bystander JSON has no treaty type, text or reason");
            Check(proposalA == null, "[LS-002] a bystander's event log has no private treaty offer");
            Check(obsA.Proposals.Count == 0, "[LS-002] and no pending proposal in the observation");
            Check(proposalC != null && proposalC.Payload["text"] == "secret pact",
                  "[LS-002] the addressed seat still sees the offer and its text");
            Check(actionB != null && actionB.Args != null && actionB.Args["treaty_type"] == "alliance"
               && actionB.Text == "secret pact",
                  "[LS-002] the proposer keeps args/text in its own action record");
        }

        // A public message is deliberately public (朝議); only its generic action record is reduced,
        // the message_sent event itself must stay readable.
        {
            var e = PrivacyWorld(603);
            var r = e.Submit(PrivacyOrder(ActionType.SendMessage, "seat-b", "seat-c",
                text: "open envoy message"));
            var sentA = ObsEvent(e.Observe("seat-a"), "message_sent");
            Check(r.Ok && sentA != null && sentA.Payload["text"] == "open envoy message",
                  "[LS-002] a public message stays public to a bystander");
        }

        // A gift's amount, recipient and trust move are party-only; its public prestige news must not
        // carry the private reason/delta either.
        {
            var e = PrivacyWorld(620);
            var r = e.Submit(PrivacyOrder(ActionType.Gift, "seat-b", "seat-c", 2000, reason: "bribe"));
            var canonicalPrestige = Find(e.Log, "prestige_changed");
            var obsA = e.Observe("seat-a");
            var shownA = ObsEvent(obsA, "prestige_changed");
            var giftA = ObsEvent(obsA, "gift_sent");
            var giftC = ObsEvent(e.Observe("seat-c"), "gift_sent");

            Check(r.Ok && canonicalPrestige != null, $"[LS-002] a gift is accepted and changes prestige ({r.Error})");
            Check(canonicalPrestige != null && canonicalPrestige.Payload["reason"] == "gift"
               && canonicalPrestige.Payload.ContainsKey("delta"),
                  "[LS-002] the canonical gift prestige event keeps its reason and delta");
            Check(shownA != null && shownA.Payload.ContainsKey("seat_id")
               && shownA.Payload.ContainsKey("prestige")
               && !shownA.Payload.ContainsKey("reason") && !shownA.Payload.ContainsKey("delta"),
                  "[LS-002] a bystander sees only public prestige state for a gift");
            Check(giftA == null, "[LS-002] a bystander sees no gift amount, recipient or trust move");
            Check(!obsA.ToJson().Contains("bribe") && !obsA.ToJson().Contains("2000"),
                  "[LS-002] the bystander JSON carries neither the gift reason nor its amount");
            Check(giftC != null && giftC.Payload["gold"] == "2000",
                  "[LS-002] the recipient still sees the gift amount");
        }
    }
    // ---- covert mission ------------------------------------------------

    private static void CovertActionPrivacy()
    {
        int seed = FindSpySeed(ActionType.Rumor, success: true, espionageLevel: 2);
        var e = PrivacyWorld(seed, cityBGold: 5000);
        SetLevel(e, "seat-b", ResearchLine.Espionage, 2);
        var r = e.Submit(PrivacyOrder(ActionType.Rumor, "seat-b", "city-c", reason: "deniable mission"));
        var canonical = LastAction(e, ActionType.Rumor);
        var obsA = e.Observe("seat-a");
        var obsB = e.Observe("seat-b");
        var obsC = e.Observe("seat-c");
        string jsonA = obsA.ToJson();

        Check(r.Ok && canonical != null, $"[LS-002] the covert mission succeeds on the selected seed ({r.Error})");
        Check(canonical != null && canonical.Visibility == "owner_only",
              "[LS-002] the mission's generic action record is owner-only");
        Check(ObsAction(obsA, ActionType.Rumor) == null && ObsEvent(obsA, "rumor") == null,
              "[LS-002] a bystander sees neither the mission order nor the rumour event");
        Check(!jsonA.Contains("deniable mission") && !jsonA.Contains("rumor"),
              "[LS-002] no covert trace appears in the bystander JSON");
        Check(ObsAction(obsB, ActionType.Rumor).Payload["reason"] == "deniable mission",
              "[LS-002] the spy keeps its own action record and reason");
        Check(ObsEvent(obsC, "rumor") != null && ObsEvent(obsC, "rumor").ActorSeatId == null,
              "[LS-002] the victim is told, with no author named (ADR-015)");
        Check(Find(e.Log, "spy_exposed") == null, "[LS-002] the successful mission leaves no public exposure.");
    }
    // ---- explicitly public design events stay public ----------------------

    private static void PublicDesignEventsPreserved()
    {
        var e = PrivacyWorld(615);
        var r = e.Submit(PrivacyOrder(ActionType.LevyTax, "seat-b", "city-b"));
        var canonical = Find(e.Log, "tax_levied");
        var obsA = e.Observe("seat-a");
        var delivered = ObsEvent(obsA, "tax_levied");
        Check(r.Ok && canonical != null && canonical.Visibility == "public",
              $"[LS-002] tax_levied remains a public event ({r.Error})");
        Check(delivered != null, "[LS-002] a bystander still receives the public tax news");
        Check(delivered != null && delivered.Payload.Count == 0,
              "[LS-002] but its public projection carries no city/gold/morale detail");
        Check(canonical != null && canonical.Payload["gold_gained"] == BalanceConfig.TaxLevyAmount.ToString(),
              "[LS-002] the canonical event keeps the amount for the researcher/owner");
    }
    // ---- fail-safe projections and unknown/future event types ---------------

    private static void FailSafeUnknownEvents()
    {
        var e = PrivacyWorld(621);
        var rec = new EventRecord
        {
            GameId = e.State.GameId, Season = e.State.Season, Phase = e.State.Phase,
            ActorSeatId = "seat-b", ControllerType = ControllerType.ScriptedAi,
            EventType = "future_private_event", Visibility = "public",
            Payload = new Dictionary<string, string>
            {
                { "army_id", "army-secret" }, { "troops", "9999" }, { "reason", "secret reason" },
            },
        };
        EventPrivacy.Freeze(rec, e.State, (seat, x, z) => e.InSight(seat, x, z));
        var projected = rec.ForSeat("seat-a");
        Check(e.SeesEvent("seat-a", rec) && e.SeesEvent("seat-c", rec),
              "[LS-002] an unknown public event type is still delivered as public news");
        Check(projected.Payload.Count == 0
           && !projected.ToJson().Contains("army-secret")
           && !projected.ToJson().Contains("9999")
           && !projected.ToJson().Contains("secret reason"),
              "[LS-002] an unknown event type defaults to an empty projection (fail safe)");
        Check(rec.ForSeat("seat-b").Payload["troops"] == "9999",
              "[LS-002] the actor still sees the canonical payload of an unknown event");

        var unfrozen = e.Log.Emit(e.State.GameId, e.State.Season, e.State.Phase,
            "seat-b", ControllerType.ScriptedAi, "future_unfrozen", "public",
            new Dictionary<string, string> { { "secret", "unfrozen value" } }, null);
        Check(!e.SeesEvent("seat-a", unfrozen) && !e.SeesEvent("seat-b", unfrozen),
              "[LS-002] a direct Log.Emit without Freeze is unreadable by every seat");
    }
    // ---- determinism -------------------------------------------------------

    private static void PrivacyDeterminism()
    {
        string observation1 = PrivacyReplay();
        string observation2 = PrivacyReplay();
        Check(observation1 == observation2,
              "[LS-002] the projected observation is byte-identical across two identical runs");
        Check(observation1.Length > 0 && observation1.Contains("army_moved"),
              "[LS-002] the determinism fixture exercises a delivered privacy event");
    }

    private static string PrivacyReplay()
    {
        var e = PrivacyWorld(616, hiddenArmyTroops: 800);
        var army = e.State.FindArmy("army-seat-b-1");
        e.Submit(PrivacyOrder(ActionType.March, "seat-b", army.Id, 12, 0, reason: "replay reason"));
        e.Submit(PrivacyOrder(ActionType.LightenLabor, "seat-b", "city-b", reason: "replay labor"));
        return e.Observe("seat-a").ToJson() + "\n" + e.Log.ToJsonLines();
    }
}
