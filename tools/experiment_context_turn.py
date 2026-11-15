"""One seat-turn: requests, real commands, tool activity, resources and visible evidence.

Only engine commands become orders. Inquiry, note and no-command requests are reported as request
kinds with their own tool activity, so no phantom order is ever counted.
"""
import json
from collections import OrderedDict

from experiment_context_constants import (MORALE_YIELD_PENALTY_THRESHOLD, REQUEST_KINDS)
from experiment_context_deliberation import tool_activity
from experiment_context_loader import REQUEST_KIND_COMMAND
from experiment_context_march import analyse_attack, analyse_march
from experiment_context_observation import (aggregate_observations,
                                            cities_below_morale_threshold,
                                            observation_facts)
from experiment_context_prompt import parse_prompt, research_points_price, research_lines_in


def metric(values, expected=None):
    """Known/missing accounting. A missing figure never becomes zero."""
    known = [value for value in values if isinstance(value, int) and not isinstance(value, bool)]
    total = len(values) if expected is None else max(expected, len(values))
    return {
        "value": sum(known) if known else None,
        "known": len(known),
        "missing": max(0, total - len(known)),
        "status": "known" if known else "unknown",
    }


def counter(values):
    counts = OrderedDict()
    for value in values:
        key = str(value) if value is not None else "unknown"
        counts[key] = counts.get(key, 0) + 1
    return OrderedDict(sorted(counts.items()))


def _text(value, limit=None):
    if not isinstance(value, str):
        return None
    text = value.strip()
    if not text:
        return None
    return text if limit is None or len(text) <= limit else text[:limit] + "..."


def _summary_last_command(summary_turn):
    raw = summary_turn.get("last_command_json") if isinstance(summary_turn, dict) else None
    if not isinstance(raw, str):
        return {"status": "absent", "type": None, "action_id": None}
    try:
        parsed = json.loads(raw)
    except ValueError:
        return {"status": "unparseable", "type": None, "action_id": None}
    if not isinstance(parsed, dict):
        return {"status": "not_object", "type": None, "action_id": None}
    return {"status": "ok", "type": parsed.get("type"), "action_id": parsed.get("action_id")}


def last_command_fidelity(summary_turn, accepted):
    """How the summary's single last command compares with the turn's last accepted order."""
    summary = _summary_last_command(summary_turn)
    raw_last = accepted[-1] if accepted else None
    if summary["status"] != "ok" or raw_last is None:
        result = "unknown"
    else:
        type_match = summary["type"] == raw_last["type"]
        id_match = (summary["action_id"] is None or raw_last["action_id"] is None
                    or summary["action_id"] == raw_last["action_id"])
        result = "matches" if (type_match and id_match) else "differs"
    return {
        "result": result,
        "summary_status": summary["status"],
        "summary_type": summary["type"],
        "summary_action_id": summary["action_id"],
        "raw_last_accepted_type": raw_last["type"] if raw_last else None,
        "raw_last_accepted_action_id": raw_last["action_id"] if raw_last else None,
        "raw_last_accepted_request_index": raw_last["request_index"] if raw_last else None,
        "explanation": "the summary stores one last command per seat-turn, not the turn's whole "
                       "action set",
    }


def _research_choice(request, command, prompt_facts, request_system, result_ok, result_error):
    args = command.get("args") if isinstance(command.get("args"), dict) else {}
    line = args.get("line")
    request_options = prompt_facts.get("options")
    matched = None
    if request_options is not None:
        matched = next((option for option in request_options
                        if option["type"] == "research"
                        and (option.get("args") or {}).get("line") == line), None)
    return {
        "request_index": request.request_index,
        "line": line,
        "rules_offered_in_request": None if request_system is None else
            ("RESEARCH ORDERS" in request_system),
        "line_offered_in_request": None if request_options is None else matched is not None,
        "offered_lines_in_request": research_lines_in(request_options),
        "command_point_cost": matched.get("cost") if matched else None,
        "research_points_price": research_points_price(matched.get("label")) if matched else None,
        "label": matched.get("label") if matched else None,
        "accepted": result_ok,
        "result_error": result_error,
        "sources": request.cites(),
    }


def analyse_turn(request_list, experiment, geometry, seat_id, season, summary_turn,
                 excluded_count=0, summary_scoreboard=None, summary_citation=None):
    seat_requests = list(request_list)
    first = seat_requests[0] if seat_requests else None
    first_observation, first_obs_status, first_obs_source = (
        first.observation() if first else (None, "unknown", None))
    last_observation = seat_requests[-1].observation()[0] if seat_requests else None
    opening = observation_facts(first_observation, seat_id)
    closing = observation_facts(last_observation, seat_id)

    observations = []
    for request in seat_requests:
        observation, status, source = request.observation()
        observations.append((request.request_index, observation_facts(observation, seat_id),
                             status, source))

    prompt = parse_prompt(first.prompt_text()) if first else parse_prompt(None)
    system = first.system_text() if first else None
    offered_research = None if system is None else ("RESEARCH ORDERS" in system)
    offered_inquiry = None if system is None else (
        "manual_lookup" in system or "OPTIONAL DELIBERATION" in system)
    offered_note = None if system is None else ('{"note"' in system)

    accepted, rejected, orders = [], [], []
    invalid_replies = 0
    inquiries, notes, notes_saved, note_failures = [], [], [], []
    research_choices, marches, attacks = [], [], []
    token_values = {"input_tokens": [], "output_tokens": [], "reasoning_tokens": [],
                    "latency_ms": []}
    finish_reasons, explanations, tool_requests = [], [], []

    for request in seat_requests:
        command, command_source = request.command()
        reply, reply_status = request.model_reply()
        result_ok = request.acceptance()
        parse_ok = request.parse_ok()
        observation, obs_status, _ = request.observation()
        prompt_facts = parse_prompt(request.prompt_text())
        request_system = request.system_text()
        response = request.response_fields()
        run_record = request.run
        kind = request.request_kind()

        if prompt_facts.get("status") == "truncated":
            experiment.diagnostics.append({
                "kind": "truncated_prompt",
                "key": [request.run_id, seat_id, season, request.request_index],
                "missing_markers": prompt_facts["missing_markers"],
                "sources": request.cites()})
        _diagnose_observation_mismatch(experiment, request, seat_id, season)

        activity = tool_activity(request, kind)
        if activity["inquiry"] is not None:
            inquiries.append(activity["inquiry"])
        if activity["note"] is not None:
            notes.append(activity["note"])
        if activity["note_saved"] is not None:
            notes_saved.append(activity["note_saved"])
        if activity["note_failure"] is not None:
            note_failures.append(activity["note_failure"])
        if kind != REQUEST_KIND_COMMAND:
            tool_requests.append({
                "request_index": request.request_index, "kind": kind,
                "evidence": (activity["inquiry"] or activity["note"] or {}).get("evidence"),
                "outcome": (activity["inquiry"] or {}).get("outcome"),
                "sources": request.cites()})

        if kind == REQUEST_KIND_COMMAND:
            order = {
                "request_index": request.request_index,
                "action_id": command.get("action_id"),
                "type": command.get("type"),
                "target_id": command.get("target_id"),
                "param_a": command.get("param_a"),
                "param_b": command.get("param_b"),
                "args": command.get("args") if isinstance(command.get("args"), dict) else None,
                "accepted": result_ok,
                "result_error": request.result_error(),
                "parse_ok": parse_ok,
                "command_source": command_source,
                "sources": request.cites(),
            }
            orders.append(order)
            if result_ok is True:
                accepted.append(order)
            elif result_ok is False:
                rejected.append(order)
            if command.get("type") == "march":
                marches.append(analyse_march(request, command, prompt_facts, observation,
                                             obs_status, geometry, seat_id))
            elif command.get("type") in ("attack_army", "attack_city"):
                attacks.append(analyse_attack(command, observation, obs_status, seat_id,
                                              request_index=request.request_index,
                                              accepted=result_ok,
                                              result_error=request.result_error(),
                                              cites=request.cites()))
            elif command.get("type") == "research":
                research_choices.append(_research_choice(
                    request, command, prompt_facts, request_system, result_ok,
                    request.result_error()))
            explanations.append({
                "request_index": request.request_index,
                "action": command.get("type"),
                "reason": _text(command.get("reason"), 400),
                "summary": _text(reply.get("summary"), 400) if isinstance(reply, dict) else None,
                "sources": request.cites()})

        note_failure_class = (activity["note_failure"] or {}).get("failure_class")
        storage_failure = ((kind == "note" and note_failure_class in ("storage", "tool"))
                           or request.result_error() == "note_storage_failed")
        if parse_ok is False and not storage_failure:
            invalid_replies += 1

        for key in token_values:
            value = run_record.get(key) if run_record is not None else None
            if value is None:
                value = response.get(key)
            token_values[key].append(
                value if isinstance(value, int) and not isinstance(value, bool) else None)
        finish_reasons.append(response.get("finish_reason") or
                              (run_record.get("finish_reason") if run_record is not None else None))

    for key in token_values:
        token_values[key].extend([None] * excluded_count)

    available_research = []
    option_types = {}
    for option in prompt["options"] or []:
        option_types[option["type"]] = option_types.get(option["type"], 0) + 1
        if option["type"] == "research":
            available_research.append({
                "line": (option.get("args") or {}).get("line"),
                "label": option.get("label"),
                "command_point_cost": option.get("cost"),
                "research_points_price": research_points_price(option.get("label")),
                "likely_affordable": option.get("likely_affordable"),
            })
    offered_order_types = sorted(option_types) if prompt["options"] is not None else None

    aggregate = aggregate_observations(
        [(index, facts, status) for index, facts, status, _ in observations], seat_id)
    turn_end = experiment.turn_end_for(seat_id, season)
    end_reason, end_source = None, "unknown"
    if turn_end is not None:
        end_reason, end_source = turn_end.get("reason"), turn_end.get("source")
    if (end_reason in (None, "unknown")) and isinstance(summary_turn, dict) and summary_turn:
        end_reason = summary_turn.get("ended_by") or summary_turn.get("end_reason")
        end_source = "summary turn"

    summary_check = None
    fidelity = last_command_fidelity(summary_turn, accepted)
    if isinstance(summary_turn, dict) and summary_turn:
        summary_check = {
            "summary_requests": summary_turn.get("requests"),
            "raw_joined_requests": len(seat_requests),
            "summary_accepted": summary_turn.get("accepted"),
            "raw_accepted": len(accepted),
            "summary_last_command_type": fidelity["summary_type"],
            "summary_last_command_action_id": fidelity["summary_action_id"],
            "raw_last_accepted_type": fidelity["raw_last_accepted_type"],
            "raw_last_accepted_action_id": fidelity["raw_last_accepted_action_id"],
            "last_command_fidelity": fidelity["result"],
        }

    kinds = counter([request.request_kind() for request in seat_requests])
    for kind in REQUEST_KINDS:
        kinds.setdefault(kind, 0)
    if summary_scoreboard is None:
        summary_scoreboard = {
            "status": "unknown", "reason": "not_analysed", "source": None, "citation": None,
            "seat": None, "scoreboard_size": None,
            "distinct_from": "the seat's first and last pre-order observation totals"}

    return {
        "seat_id": seat_id,
        "faction": _faction_name(first_observation, seat_id, summary_turn),
        "season": season,
        "turn_token": first.turn_token if first else None,
        "requests": {
            "joined": len(seat_requests),
            "excluded_ambiguous_or_incomplete": excluded_count,
            "request_indexes": [request.request_index for request in seat_requests],
            "by_kind": OrderedDict((kind, kinds.get(kind, 0)) for kind in REQUEST_KINDS),
            "tool_activity": tool_requests,
            "prompt_status": prompt["status"] if first else "missing",
            "prompt_version": prompt.get("prompt_version"),
            "requests_left_in_prompt": prompt.get("requests_left"),
            "actions_left_in_prompt": prompt.get("actions_left"),
        },
        "orders": {
            "accepted": len(accepted),
            "rejected": len(rejected),
            "invalid_replies": invalid_replies,
            "accepted_commands": accepted,
            "rejected_commands": rejected,
            "all_commands": orders,
            "accepted_by_type": counter([order["type"] for order in accepted]),
        },
        "turn_end": {"reason": end_reason or "unknown", "source": end_source,
                     "stopped_by_ceiling": (turn_end or {}).get("stopped_by_ceiling"),
                     "stop_ceiling": (turn_end or {}).get("stop_ceiling")},
        "resources": {
            "first_observation_source": first_obs_source,
            "first_observation_status": first_obs_status,
            "first_cities": opening["cities"],
            "first_food_total": metric([city["food"] for city in opening["cities"]]),
            "first_gold_total": metric([city["gold"] for city in opening["cities"]]),
            "last_pre_order_cities": closing["cities"],
            "last_pre_order_food_total": metric([city["food"] for city in closing["cities"]]),
            "last_pre_order_gold_total": metric([city["gold"] for city in closing["cities"]]),
            "command_points_first": opening["command_points"],
            "command_points_last_pre_order": closing["command_points"],
            "research_points_first": opening["research_points"],
            "research_levels_first": opening["research_levels"],
            "summary_scoreboard": summary_scoreboard,
            "cities_below_morale_threshold_first_observation":
                cities_below_morale_threshold(opening),
            "morale_threshold": MORALE_YIELD_PENALTY_THRESHOLD,
            "visible_penalty_events": aggregate["penalty_events"],
            "visible_pressure_events": aggregate["pressure_events"],
            "observations_counted": aggregate["observations"],
            "events_in_observation": aggregate["max_events_in_one_observation"],
            "events_without_seq": aggregate["events_without_seq"],
            "observation_event_cap_note": "each prompt carries a bounded event list, so an "
                                          "absent event means not displayed, not not-happened",
        },
        "visible_contact": aggregate["contact"],
        "marches": marches,
        "attacks": attacks,
        "captures_visible": aggregate["captures"],
        "deliberation": {
            "inquiry_offered": offered_inquiry,
            "note_offered": offered_note,
            "inquiry_replies": inquiries,
            "inquiries_confirmed_answered": len([item for item in inquiries
                                                 if item.get("outcome") == "answered"]),
            "note_replies": notes,
            "notes_saved": notes_saved,
            "notes_saved_confirmed": len(notes_saved),
            "note_failures": note_failures,
            "note_present_from_previous_turn": None if system is None else
                ("STRATEGIC NOTE FROM YOUR PREVIOUS TURN" in (first.prompt_text() or "")),
        },
        "research": {
            "rules_offered": offered_research,
            "available_lines": available_research,
            "chosen": research_choices,
            "offered_order_types": offered_order_types,
        },
        "usage": {key: metric(values) for key, values in token_values.items()},
        "finish_reasons": counter(finish_reasons),
        "stated_explanations": explanations,
        "summary_cross_check": summary_check,
        "summary_last_command_fidelity": fidelity,
    }


def _faction_name(observation, seat_id, summary_turn):
    if isinstance(observation, dict):
        for faction in observation.get("factions") or []:
            if isinstance(faction, dict) and faction.get("seat_id") == seat_id:
                return faction.get("name")
    if isinstance(summary_turn, dict):
        return summary_turn.get("faction")
    return None


def _diagnose_observation_mismatch(experiment, request, seat_id, season):
    """Diagnose a disagreement between observation_json and the prompt's embedded copy."""
    run_observation, prompt_observation = request.observation_variants()
    if run_observation is None or prompt_observation is None:
        return
    if json.dumps(run_observation, sort_keys=True) == json.dumps(prompt_observation,
                                                                 sort_keys=True):
        return
    experiment.diagnostics.append({
        "kind": "observation_prompt_mismatch",
        "key": [request.run_id, seat_id, season, request.request_index],
        "note": "the run record's observation_json differs from the prompt's embedded copy; "
                "the seat-visible facts are reported from observation_json",
        "sources": request.cites()})
