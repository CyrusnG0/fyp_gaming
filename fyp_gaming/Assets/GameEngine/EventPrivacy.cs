using System;
using System.Collections.Generic;

namespace LuanShi.Engine
{
    /// <summary>
    /// Event-time privacy for the append-only log (LS-002, ADR-025).
    ///
    /// A record's audience is frozen when it is emitted: public records go to every seat that exists at
    /// that moment, actor/named parties always get the record, and <c>owner_and_observers</c> records also
    /// go to seats that can see the event's subject *then*. Later movement, destruction or new sight
    /// therefore neither reveals an old undisclosed event nor removes one already delivered. The canonical
    /// <see cref="EventLog"/> keeps the full payload; a seat's <see cref="Observation"/> receives a
    /// projected copy: the actor, named parties and (for subject-scoped events) seats that see the
    /// subject at emit get the canonical payload; every other recipient gets a safe summary.
    /// </summary>
    internal static class EventPrivacy
    {
        // Payload keys that name a seat as a party to the event. These seats keep the canonical payload;
        // the actor is always added separately. They are also the recipients of owner_only records such
        // as rumor/sow_distrust (which are deliberately built with no actor).
        private static readonly string[] PartySeatKeys =
        {
            "to_seat_id", "from_seat_id", "attacker_seat_id", "defender_seat_id",
            "seat_id", "winner_seat_id", "breaker_seat_id", "counterpart_seat_id",
            "detected_by_seat_id", "faction_a", "faction_b",
        };

        /// <summary>Freezes <paramref name="rec"/>'s audience and non-party projection. Called once, at emit.</summary>
        public static void Freeze(EventRecord rec, GameState state,
            Func<string, int, int, bool> inSight, int? subjectX = null, int? subjectZ = null)
        {
            if (rec == null) return;

            var fullSeats = new List<string>();
            AddSeat(fullSeats, rec.ActorSeatId);
            foreach (string key in PartySeatKeys) AddSeat(fullSeats, Value(rec, key));

            // The canonical payload is already safe for a public event whose type is not listed here;
            // a null LimitedPayload means "every recipient may read the canonical payload".
            rec.LimitedPayload = LimitedPayloadFor(rec);

            var recipients = new List<string>();
            if (state != null)
            {
                foreach (var faction in state.Factions)
                {
                    string seat = faction.SeatId;
                    bool subjectVisible = SubjectInSight(rec, state, seat, inSight, subjectX, subjectZ);
                    if (rec.Visibility == "public")
                    {
                        recipients.Add(seat);
                        // A seat that can see the city/army at event time already sees its exact state
                        // under ADR-011, so a subject-scoped record may keep its full payload for it.
                        if (subjectVisible && WitnessGetsFullPayload(rec.EventType)) AddSeat(fullSeats, seat);
                    }
                    else if (fullSeats.Contains(seat))
                    {
                        recipients.Add(seat);
                    }
                    else if (rec.Visibility == "owner_and_observers" && subjectVisible)
                    {
                        recipients.Add(seat);
                        if (WitnessGetsFullPayload(rec.EventType)) AddSeat(fullSeats, seat);
                    }
                }
            }
            rec.FullSeatIds = fullSeats;
            rec.Recipients = recipients;
        }

        /// <summary>
        /// Event types whose whole payload is about the visible subject (city/army), so a seat that can
        /// see that subject at event time is entitled to the canonical payload under ADR-011. Events whose
        /// payload mixes in hidden facts (a march's origin/cost, the unseen side of a battle) are not here
        /// and stay on the limited projection even for a witness.
        /// </summary>
        private static bool WitnessGetsFullPayload(string eventType)
        {
            switch (eventType)
            {
                case "city_resolved":
                case "recruit":
                case "troops_trained":
                case "city_fortified":
                case "disaster_relieved":
                case "land_reclaimed":
                case "labor_lightened":
                case "starvation":
                case "revolt":
                case "city_captured":
                case "garrison_converted":
                case "army_garrisoned":
                case "army_destroyed":
                    return true;
            }
            return false;
        }

        /// <summary>
        /// The safe payload a non-entitled recipient receives. Null means the canonical payload is already
        /// a public-safe payload for that event. Full recipients (actor, named parties and entitled subject
        /// witnesses) keep the canonical payload.
        /// </summary>
        private static Dictionary<string, string> LimitedPayloadFor(EventRecord rec)
        {
            switch (rec.EventType)
            {
                // A generic action record is public (ADR-018), but the order's parameters, target and
                // reason are the actor's business. Only the action type is public news.
                case "action":
                    return Pick(rec, "action");

                // Movement and combat history: a seat that could see the event's subject knows it happened,
                // not where the unit came from, what it cost, or how many men the unseen side had.
                case "army_moved":
                    return Pick(rec, "army_id", "to");
                case "army_routed":
                    return Pick(rec, "army_id", "cause", "to");
                case "army_destroyed":
                    // Public news that an army was destroyed, with no hidden army id, location or strength
                    // for a seat that could not see it. The owner and event-time witnesses keep the
                    // canonical army_id/cause through WitnessGetsFullPayload above.
                    return new Dictionary<string, string>();
                case "garrison_converted":
                    return Pick(rec, "mode");
                case "army_garrisoned":
                    return Pick(rec, "mode");

                // Internal city state: an in-sight witness gets the full record above; this fallback
                // (used only if such an event ever gains a non-witness recipient) names no numbers.
                case "city_resolved":
                case "recruit":
                case "troops_trained":
                case "city_fortified":
                case "disaster_relieved":
                case "land_reclaimed":
                case "labor_lightened":
                case "starvation":
                case "revolt":
                case "city_captured":
                    return Pick(rec, "city_id");
                case "tax_levied":
                    return new Dictionary<string, string>();   // public news of a tax, not the amount/city
                case "prestige_changed":
                    // 威望 is public, the cause and delta are not. A bystander sees where the new value
                    // stands, never the private reason (research_governance_l5, gift, sneak_attack, ...).
                    return Pick(rec, "seat_id", "prestige");
                case "watch_tower_captured":
                    return Pick(rec, "watch_tower_id");

                // Explicitly public battle news (ACTIONS.md M2/M3) keeps the report's shape but replaces
                // exact strengths and the round log with placeholders. Unit types are omitted. This
                // applies to every non-participant, including a seat that can see only one side of a
                // ranged battle; the current observation still shows any unit that is actually in sight.
                case "battle_field":
                    return BattleFieldSummary(rec);
                case "battle_siege":
                    return BattleSiegeSummary(rec);

                // Explicitly public design events whose canonical payload is already public-safe. Every
                // other event type falls through to the empty projection at the end (fail safe).
                case "season_start":
                case "season_end":
                case "game_over":
                case "war_declared":
                case "treaty_ratified":
                case "treaty_expired":
                case "treaty_broken":
                case "spy_exposed":
                case "message_sent":
                    return null;
            }
            // Unknown/new event types are private by default: never hand the canonical payload to a
            // non-party recipient just because the event type has not been classified yet.
            return new Dictionary<string, string>();
        }

        /// <summary>
        /// A public battle report for a seat that was not a participant: who fought and who won is news,
        /// the exact order of battle is not. The keys the UI reads are kept, with "?" standing in for
        /// values the seat is not entitled to.
        /// </summary>
        private static Dictionary<string, string> BattleFieldSummary(EventRecord rec)
        {
            var d = new Dictionary<string, string>();
            d["attacker_seat_id"] = ValueOr(rec, "attacker_seat_id");
            d["defender_seat_id"] = ValueOr(rec, "defender_seat_id");
            // The unit types are omitted rather than blanked: the report renderer treats a missing
            // unit_type as unknown, while a placeholder would be normalised to "infantry".
            d["attacker_troops_before"] = "?";
            d["attacker_troops_after"] = "?";
            d["defender_troops_before"] = "?";
            d["defender_troops_after"] = "?";
            d["rounds"] = ValueOr(rec, "rounds");
            d["outcome"] = ValueOr(rec, "outcome");
            d["winner_seat_id"] = ValueOr(rec, "winner_seat_id");
            d["attack_legitimacy"] = ValueOr(rec, "attack_legitimacy");
            return d;
        }

        private static Dictionary<string, string> BattleSiegeSummary(EventRecord rec)
        {
            var d = new Dictionary<string, string>();
            d["city_id"] = ValueOr(rec, "city_id");
            d["attacker_seat_id"] = ValueOr(rec, "attacker_seat_id");
            d["defender_seat_id"] = ValueOr(rec, "defender_seat_id");
            d["garrison_before"] = "?";
            d["garrison_after"] = "?";
            d["troops_before"] = "?";
            d["troops_after"] = "?";
            d["rounds"] = ValueOr(rec, "rounds");
            d["outcome"] = ValueOr(rec, "outcome");
            d["attack_legitimacy"] = ValueOr(rec, "attack_legitimacy");
            return d;
        }

        /// <summary>
        /// True when the event's subject is visible to <paramref name="seatId"/> at emit time. An explicit
        /// subject hex (used by army_destroyed, whose army has already left canonical state) is checked
        /// first; otherwise the payload's city/army ids are resolved against the state as it stands.
        /// </summary>
        private static bool SubjectInSight(EventRecord rec, GameState state, string seatId,
            Func<string, int, int, bool> inSight, int? subjectX, int? subjectZ)
        {
            if (inSight == null) return false;
            if (subjectX.HasValue && subjectZ.HasValue && inSight(seatId, subjectX.Value, subjectZ.Value))
                return true;

            string cityId = Value(rec, "city_id");
            if (cityId != null)
            {
                var city = state.FindCity(cityId);
                if (city != null && inSight(seatId, city.X, city.Z)) return true;
            }

            string armyId = Value(rec, "army_id") ?? Value(rec, "attacker_army_id")
                         ?? Value(rec, "defender_army_id");
            if (armyId != null)
            {
                var army = state.FindArmy(armyId);
                if (army != null && inSight(seatId, army.X, army.Z)) return true;
            }
            return false;
        }

        private static void AddSeat(List<string> seats, string seatId)
        {
            if (string.IsNullOrEmpty(seatId)) return;
            if (!seats.Contains(seatId)) seats.Add(seatId);
        }

        private static string Value(EventRecord rec, string key)
            => rec.Payload != null && rec.Payload.TryGetValue(key, out string value) ? value : null;

        private static string ValueOr(EventRecord rec, string key, string fallback = "")
            => Value(rec, key) ?? fallback;

        /// <summary>Copies the named payload keys that are present, preserving the declared order.</summary>
        private static Dictionary<string, string> Pick(EventRecord rec, params string[] keys)
        {
            var d = new Dictionary<string, string>();
            foreach (string key in keys)
            {
                string value = Value(rec, key);
                if (value != null) d[key] = value;
            }
            return d;
        }
    }
}
