"""March and attack classification from the pre-order observation and displayed options only.

Target and intent stay unknown unless the seat's own pre-order evidence establishes a target. The
scenario geometry is reported separately and labelled as geometry, never as intent.
"""
from experiment_context_observation import (entity_at, observation_facts, own_army_position,
                                            visible_contact)
from experiment_context_prompt import option_text


def analyse_march(request, command, prompt_facts, observation, obs_status, geometry, seat_id):
    army_id = command.get("target_id")
    destination = None
    if isinstance(command.get("param_a"), int) and isinstance(command.get("param_b"), int):
        destination = (command["param_a"], command["param_b"])
    origin = own_army_position(observation, army_id)
    movement = prompt_facts.get("movement")
    army_block = movement.get(army_id) if isinstance(movement, dict) else None

    displayed = None
    unlisted = None
    if army_block is not None:
        displayed = army_block.get("options")
        unlisted = army_block.get("unlisted_options")
    destination_in_displayed = None
    if displayed is not None and destination is not None:
        destination_in_displayed = any(option["x"] == destination[0]
                                       and option["z"] == destination[1] for option in displayed)

    entity, entity_status = entity_at(observation, destination, seat_id)
    if entity is None:
        target_at_destination = None
        target_basis = entity_status if obs_status == "ok" else "observation_" + obs_status
    else:
        target_at_destination = entity
        target_basis = "visible_in_observation"

    geometry_info = geometry.relation(origin, destination)
    toward = geometry.toward_centre_options(origin, displayed)

    return {
        "request_index": request.request_index,
        "army_id": army_id,
        "origin": list(origin) if origin else None,
        "origin_source": "observation_json armies[]" if origin else "unknown",
        "destination": list(destination) if destination else None,
        "destination_in_displayed_options": destination_in_displayed,
        "displayed_option_count": None if displayed is None else len(displayed),
        "unlisted_option_count": unlisted,
        "total_reachable_estimate": (len(displayed) + unlisted)
        if displayed is not None and unlisted is not None else None,
        "option_list_truncated": None if unlisted is None else unlisted > 0,
        "displayed_options": option_text(displayed),
        "displayed_options_raw": displayed,
        "army_block_position": [army_block["x"], army_block["z"]] if army_block else None,
        "army_block_blocked": army_block.get("blocked") if army_block else None,
        "prompt_status": prompt_facts.get("status"),
        "visible_entities_before": visible_contact(
            observation_facts(observation, seat_id), seat_id),
        "observation_status": obs_status,
        "target_at_destination": target_at_destination,
        "target_basis": target_basis,
        "intent": "unknown",
        "intent_reason": "no pre-order evidence establishes a goal; later state, hidden "
                         "entities and the model's own explanation are not used",
        "geometry": geometry_info,
        "offered_toward_centre_options": toward,
        "accepted": request.acceptance(),
        "sources": request.cites(),
    }


def analyse_attack(command, observation, obs_status, seat_id, request_index=None,
                   accepted=None, result_error=None, cites=None):
    destination = None
    if isinstance(command.get("param_a"), int) and isinstance(command.get("param_b"), int):
        destination = (command["param_a"], command["param_b"])
    entity, status = entity_at(observation, destination, seat_id)
    return {
        "request_index": request_index,
        "type": command.get("type"),
        "army_id": command.get("target_id"),
        "target_position": list(destination) if destination else None,
        "visible_target": entity,
        "visible_target_basis": status,
        "observation_status": obs_status,
        "accepted": accepted,
        "result_error": result_error,
        "sources": cites,
    }
