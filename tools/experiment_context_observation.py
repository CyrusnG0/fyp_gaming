"""Seat-visible observation facts, contact classification and summary scoreboard extraction.

Only a seat's own filtered observation is read here. Neutral (owner-less) entities are visible
evidence exactly like foreign ones, but they are labelled ``neutral`` and never treated as hostile.
"""
from collections import OrderedDict

from experiment_context_constants import (CAPTURE_EVENT_TYPES, MORALE_YIELD_PENALTY_THRESHOLD,
                                          PENALTY_EVENT_TYPES, PRESSURE_EVENT_TYPES)
from experiment_context_loader import _as_int


def observation_facts(observation, seat_id):
    """Seat-visible facts only. Every field is optional in the raw JSON."""
    facts = {
        "status": "ok" if isinstance(observation, dict) else "unknown",
        "season": None, "command_points": None, "research_points": None,
        "research_levels": None, "cities": [], "armies": [], "scouted_cities": [],
        "factions": [], "events": [], "scenario_id": None,
    }
    if not isinstance(observation, dict):
        return facts
    facts["season"] = _as_int(observation.get("season"))
    facts["command_points"] = _as_int(observation.get("command_points"))
    facts["research_points"] = _as_int(observation.get("research_points"))
    levels = observation.get("research")
    if isinstance(levels, dict):
        facts["research_levels"] = {str(key): _as_int(value) for key, value in levels.items()}
    facts["scenario_id"] = observation.get("scenario_id")
    for city in observation.get("cities") or []:
        if not isinstance(city, dict):
            continue
        facts["cities"].append({
            "id": city.get("id"), "name": city.get("name"),
            "x": _as_int(city.get("x")), "z": _as_int(city.get("z")),
            "owner_seat_id": city.get("owner_seat_id"),
            "food": _as_int(city.get("food")), "gold": _as_int(city.get("gold")),
            "morale": _as_int(city.get("morale")),
            "population": _as_int(city.get("population")),
            "garrison": _as_int(city.get("garrison")),
            "reclaim_pct": _as_int(city.get("reclaim_pct")),
        })
    for army in observation.get("armies") or []:
        if not isinstance(army, dict):
            continue
        facts["armies"].append({
            "id": army.get("id"), "owner_seat_id": army.get("owner_seat_id"),
            "x": _as_int(army.get("x")), "z": _as_int(army.get("z")),
            "troops": _as_int(army.get("troops")),
            "unit_type": army.get("unit_type"),
        })
    for city in observation.get("scouted_cities") or []:
        if isinstance(city, dict):
            facts["scouted_cities"].append({
                "id": city.get("id"), "owner_seat_id": city.get("owner_seat_id"),
                "x": _as_int(city.get("x")), "z": _as_int(city.get("z"))})
    for faction in observation.get("factions") or []:
        if isinstance(faction, dict):
            facts["factions"].append({"seat_id": faction.get("seat_id"),
                                      "name": faction.get("name")})
    for event in observation.get("events") or []:
        if not isinstance(event, dict):
            continue
        payload = event.get("payload") if isinstance(event.get("payload"), dict) else {}
        facts["events"].append({
            "seq": _as_int(event.get("seq")), "event_type": event.get("event_type"),
            "visibility": event.get("visibility"),
            "actor_seat_id": event.get("actor_seat_id"),
            "season": _as_int(event.get("season")),
            "payload_keys": sorted(str(key) for key in payload),
            "payload_fingerprint": "|".join("%s=%s" % (key, payload[key])
                                            for key in sorted(payload)),
            "payload_city_id": payload.get("city_id"), "payload_army_id": payload.get("army_id"),
            "payload_cause": payload.get("cause"), "payload_morale": payload.get("morale"),
        })
    facts["cities"].sort(key=lambda city: str(city.get("id")))
    return facts


def owner_class(owner_seat_id, seat_id):
    """foreign | neutral | own — neutral means no recorded owner, not an enemy."""
    if owner_seat_id == seat_id:
        return "own"
    return "neutral" if owner_seat_id in (None, "") else "foreign"


def visible_contact(facts, seat_id):
    """Everything visible that this seat does not own, including neutral objectives."""
    contact = []
    for city in facts.get("cities") or []:
        category = owner_class(city.get("owner_seat_id"), seat_id)
        if category == "own":
            continue
        contact.append({"kind": "city", "id": city.get("id"),
                        "owner_seat_id": city.get("owner_seat_id"),
                        "owner_class": category, "x": city.get("x"), "z": city.get("z")})
    for army in facts.get("armies") or []:
        category = owner_class(army.get("owner_seat_id"), seat_id)
        if category == "own":
            continue
        contact.append({"kind": "army", "id": army.get("id"),
                        "owner_seat_id": army.get("owner_seat_id"),
                        "owner_class": category, "x": army.get("x"), "z": army.get("z")})
    for city in facts.get("scouted_cities") or []:
        contact.append({"kind": "scouted_city", "id": city.get("id"),
                        "owner_seat_id": city.get("owner_seat_id"),
                        "owner_class": owner_class(city.get("owner_seat_id"), seat_id),
                        "x": city.get("x"), "z": city.get("z")})
    return contact


def entity_at(observation, point, seat_id=None):
    """(entity, basis) for a visible city/army/scouted city at a coordinate."""
    if not isinstance(observation, dict) or point is None:
        return None, "unknown"
    for kind in ("cities", "armies", "scouted_cities"):
        for item in observation.get(kind) or []:
            if not isinstance(item, dict):
                continue
            if _as_int(item.get("x")) == point[0] and _as_int(item.get("z")) == point[1]:
                entity_kind = {"cities": "city", "armies": "army",
                               "scouted_cities": "scouted_city"}[kind]
                return {"kind": entity_kind, "id": item.get("id"),
                        "owner_seat_id": item.get("owner_seat_id"),
                        "owner_class": owner_class(item.get("owner_seat_id"), seat_id)}, "visible"
    return None, "not_visible"


def own_army_position(observation, army_id):
    if not isinstance(observation, dict) or not army_id:
        return None
    for army in observation.get("armies") or []:
        if isinstance(army, dict) and army.get("id") == army_id:
            x, z = _as_int(army.get("x")), _as_int(army.get("z"))
            return None if x is None or z is None else (x, z)
    return None


def event_key(event):
    """Stable dedup key: the engine's event seq, or a structural fallback when it is absent.

    The fallback includes the season and the full payload fingerprint as well as the event type
    and actor, so two legitimately different seq-less events cannot collapse into one.
    """
    seq = event.get("seq")
    if isinstance(seq, int) and not isinstance(seq, bool):
        return ("seq", seq)
    return ("struct", event.get("season"), event.get("event_type"), event.get("actor_seat_id"),
            event.get("visibility"), event.get("payload_fingerprint"))


def aggregate_observations(observations, seat_id):
    """Union of every pre-order observation in the seat-turn, deduplicated.

    ``observations`` is a list of ``(request_index, facts, status)``. An entity, event or capture
    that appears only in a middle request must not be lost; duplicates collapse by event seq (or a
    structural key), and each survivor keeps the request it was first seen in.
    """
    contact = OrderedDict()
    events = OrderedDict()
    for request_index, facts, status in observations:
        if status != "ok":
            continue
        for item in visible_contact(facts, seat_id):
            contact.setdefault((item["kind"], item["id"], item["owner_seat_id"],
                                item["x"], item["z"]), dict(item))
        for event in facts.get("events") or []:
            key = event_key(event)
            if key not in events:
                copy = dict(event)
                copy["first_seen_request_index"] = request_index
                events[key] = copy
    event_list = list(events.values())
    return {
        "contact": list(contact.values()),
        "events": event_list,
        "penalty_events": [event for event in event_list
                           if event["event_type"] in PENALTY_EVENT_TYPES],
        "pressure_events": [event for event in event_list
                            if event["event_type"] in PRESSURE_EVENT_TYPES],
        "captures": [event for event in event_list
                     if event["event_type"] in CAPTURE_EVENT_TYPES],
        "observations": len(observations),
        "max_events_in_one_observation": max(
            [len(facts.get("events") or []) for _, facts, status in observations
             if status == "ok"] or [None]),
        "events_without_seq": sum(1 for event in event_list
                                  if not isinstance(event.get("seq"), int)),
    }


def cities_below_morale_threshold(facts):
    """Cities whose visible morale is under the LOCKED yield-penalty threshold."""
    return [{"city_id": city["id"], "morale": city["morale"]}
            for city in facts.get("cities") or []
            if isinstance(city["morale"], int)
            and city["morale"] < MORALE_YIELD_PENALTY_THRESHOLD]


def summary_scoreboard(season_entry, seat_id, season, citation, summary_present,
                       ambiguous=False, duplicate_season=False):
    """Season-end food/gold for one seat from the spectator summary's scoreboard.

    The scoreboard is the spectator's resolution-time view; it is deliberately separate from the
    seat's pre-order observations, and its absence is unknown rather than zero.
    """
    info = {"status": "unknown", "reason": None, "source": None, "citation": None,
            "seat": None, "scoreboard_size": None,
            "distinct_from": "the seat's first and last pre-order observation totals"}
    if not summary_present:
        info["reason"] = "no_summary"
        return info
    if ambiguous:
        info["reason"] = "ambiguous_multi_run_summary"
        return info
    if duplicate_season:
        info["reason"] = "duplicate_summary_season"
        return info
    if not isinstance(season_entry, dict):
        info["reason"] = "season_not_in_summary"
        return info
    rows = season_entry.get("scoreboard")
    if not isinstance(rows, list):
        info["reason"] = "scoreboard_absent"
        return info
    match = next((row for row in rows
                  if isinstance(row, dict) and row.get("seat_id") == seat_id), None)
    if match is None:
        info["reason"] = "seat_not_in_scoreboard"
        return info
    info.update({
        "status": "ok",
        "source": "summary seasons[].scoreboard (spectator season-end snapshot)",
        "citation": "%s seasons[] season %d scoreboard" % (citation or "summary", season),
        "scoreboard_size": len(rows),
        "seat": {
            "faction": match.get("faction"),
            "food": match.get("food") if isinstance(match.get("food"), int) else None,
            "gold": match.get("gold") if isinstance(match.get("gold"), int) else None,
            "cities": match.get("cities") if isinstance(match.get("cities"), int) else None,
            "population": (match.get("population")
                           if isinstance(match.get("population"), int) else None),
            "garrison": match.get("garrison") if isinstance(match.get("garrison"), int) else None,
        },
    })
    return info
