"""Whole-run assembly: per-seat/seasons, independent recounts, reconciliation and gaps."""
from collections import OrderedDict

from experiment_context_constants import SCHEMA, TOOL_VERSION, redact_structure
from experiment_context_experiment import input_metadata
from experiment_context_observation import summary_scoreboard
from experiment_context_turn import analyse_turn, counter


def analyse_run(experiment, geometry, sources, run_label=None):
    run_ids = sorted({request.run_id for request in experiment.joined.values()
                      if request.run_id})
    seasons, factions = [], OrderedDict()
    summary_citation = (sources.get("summary") or {}).get("path") or "summary"

    for seat_id, season_numbers in experiment.seats().items():
        faction_seasons = []
        for season in season_numbers:
            request_list = experiment.requests_for(seat_id, season)
            excluded = experiment.excluded_for(seat_id, season)
            degraded = experiment.degraded_for(seat_id, season)
            scoreboard = summary_scoreboard(
                experiment.summary_season(season), seat_id, season, summary_citation,
                bool(experiment.summary), experiment.summary_ambiguous,
                duplicate_season=(season in experiment.duplicate_summary_seasons))
            turn = analyse_turn(
                request_list, experiment, geometry, seat_id, season,
                experiment.summary_turns.get((seat_id, season)),
                excluded_count=len(excluded), summary_scoreboard=scoreboard,
                summary_citation=summary_citation)
            turn["requests"]["excluded_keys"] = [request.to_json() for request in excluded]
            turn["requests"]["degraded"] = [
                {"request_index": request.request_index, "problems": request.problems,
                 "sources": request.cites()} for request in degraded]
            faction_seasons.append(turn)
            seasons.append(turn)
        factions[seat_id] = {
            "seat_id": seat_id,
            "faction": next((turn["faction"] for turn in faction_seasons if turn["faction"]),
                            None),
            "seasons": faction_seasons,
        }

    diagnostics = list(experiment.diagnostics)
    unmatched = [item for item in diagnostics if item.get("kind") in
                 ("record_without_composite_key", "join_problem", "ambiguous_composite_key",
                  "duplicate_composite_key")]
    model = {
        "schema": SCHEMA,
        "tool": {"name": "experiment_context_analysis", "version": TOOL_VERSION,
                 "offline": True, "provider_calls": 0,
                 "bytecode": "not written by the analysis modules"},
        "run_ids": run_ids,
        "label": run_label,
        "inputs": input_metadata(sources),
        "join": {
            "composite_key": ["run_id", "seat_id", "season", "request_index"],
            "legacy_exchange_id_used_as_join_key": False,
            "joined_requests": len(experiment.joined),
            "usable_requests": len(experiment.clean_requests()),
            "degraded_requests": len([request for request in experiment.clean_requests()
                                      if request.degraded]),
            "excluded_requests": len(experiment.joined) - len(experiment.clean_requests()),
            "turn_end_sidecars": len(experiment.turn_ends),
            "experiment_config_records": len(experiment.experiment_configs),
            "experiment_config_lines": [entry.cite for entry in experiment.experiment_configs],
            "summary_turns_indexed": len(experiment.summary_turns),
            "summary_scope_ambiguous": experiment.summary_ambiguous,
        },
        "diagnostics": {
            "counts": experiment.diagnostic_counts(),
            "items": diagnostics,
            "unmatched_or_ambiguous": unmatched,
        },
        "geometry": geometry.to_json(),
        "totals": _usage_totals(seasons),
        "factions": list(factions.values()),
        "verification": verification(factions, seasons),
        "reconciliation": reconciliation(experiment, seasons),
        "evidence_gaps": evidence_gaps(experiment, seasons, geometry),
    }
    return redact_structure(model)


def _usage_totals(turns):
    totals = {}
    for key in ("input_tokens", "output_tokens", "reasoning_tokens", "latency_ms"):
        values = [turn["usage"][key]["value"] for turn in turns]
        totals[key] = {
            "value": sum(value for value in values if value is not None)
            if any(value is not None for value in values) else None,
            "known": sum(turn["usage"][key]["known"] for turn in turns),
            "missing": sum(turn["usage"][key]["missing"] for turn in turns),
            "status": "known" if any(value is not None for value in values) else "unknown",
        }
    return totals


def _shu_turns(factions):
    for faction in factions.values():
        if faction.get("faction") == "Shu":
            return faction["seasons"]
    return None


def verification(factions, seasons):
    """Independent recount of the claims a reader might otherwise take on trust.

    A run without a Shu faction reports None for every Shu-specific value: there is no Shu fact to
    count, and zero would be a fabricated observation about a faction that never played.
    """
    shu_turns = _shu_turns(factions)
    shu_marches = [march for turn in (shu_turns or []) for march in turn["marches"]]
    first_shu = shu_turns[0] if shu_turns else None

    fidelity = [turn["summary_last_command_fidelity"] for turn in seasons]
    accepted_marches = [march for turn in seasons for march in turn["marches"]
                        if march["accepted"] is True]
    marches_not_last = [march for turn in seasons for march in turn["marches"]
                        if march["accepted"] is True
                        and isinstance(turn["summary_last_command_fidelity"]["summary_type"],
                                       str)
                        and turn["summary_last_command_fidelity"]["summary_type"] != "march"]

    research_offered = [turn["research"]["rules_offered"] for turn in seasons
                        if turn["research"]["rules_offered"] is not None]
    inquiry_offered = [turn["deliberation"]["inquiry_offered"] for turn in seasons
                       if turn["deliberation"]["inquiry_offered"] is not None]
    note_offered = [turn["deliberation"]["note_offered"] for turn in seasons
                    if turn["deliberation"]["note_offered"] is not None]
    inquiries = [item for turn in seasons for item in turn["deliberation"]["inquiry_replies"]]
    notes = [item for turn in seasons for item in turn["deliberation"]["note_replies"]]
    notes_saved = [item for turn in seasons for item in turn["deliberation"]["notes_saved"]]
    research_choices = [item for turn in seasons for item in turn["research"]["chosen"]]

    return {
        "shu_accepted_marches": (len([march for march in shu_marches
                                      if march["accepted"] is True])
                                 if shu_turns is not None else None),
        "shu_march_count_all": len(shu_marches) if shu_turns is not None else None,
        "shu_faction_present": shu_turns is not None,
        "summary_last_command_fidelity_counts": counter([item["result"] for item in fidelity]),
        "turns_with_unknown_summary_last_command": len(
            [item for item in fidelity if item["result"] == "unknown"]),
        "accepted_marches_total": len(accepted_marches),
        "accepted_marches_not_the_summary_last_command": len(marches_not_last),
        "research_rules_offered_in_any_prompt": (any(research_offered)
                                                if research_offered else None),
        "inquiry_tools_offered_in_any_prompt": (any(inquiry_offered)
                                               if inquiry_offered else None),
        "note_tool_offered_in_any_prompt": any(note_offered) if note_offered else None,
        "inquiry_replies": len(inquiries),
        "note_replies": len(notes),
        "notes_saved_confirmed": len(notes_saved),
        "research_choices": len(research_choices),
        "research_choices_offered_in_their_own_request": len(
            [choice for choice in research_choices
             if choice.get("line_offered_in_request") is True]),
        "first_shu_request_options": (first_shu["research"]["offered_order_types"]
                                     if first_shu else None),
        "first_shu_marches": ([
            {"army_id": march["army_id"], "origin": march["origin"],
             "destination": march["destination"],
             "destination_in_displayed_options": march["destination_in_displayed_options"],
             "displayed_options": march["displayed_options"],
             "unlisted_option_count": march["unlisted_option_count"],
             "geometry_relation": march["geometry"].get("relation"),
             "sources": march["sources"]}
            for march in first_shu["marches"]] if first_shu else None),
        "all_march_destinations_displayed": all(
            march["destination_in_displayed_options"] is True
            for turn in seasons for march in turn["marches"]) if any(
            turn["marches"] for turn in seasons) else None,
    }


def reconciliation(experiment, seasons):
    """Raw-run totals and last-command fidelity versus the spectator summary."""
    summary = experiment.summary or {}
    totals = summary.get("totals") or {}
    raw_accepted = sum(turn["orders"]["accepted"] for turn in seasons)
    raw_rejected = sum(turn["orders"]["rejected"] for turn in seasons)
    raw_requests = sum(turn["requests"]["joined"] for turn in seasons)
    totals_compared = (bool(experiment.summary) and not experiment.summary_ambiguous
                       and bool(totals))
    summary_requests = totals.get("requests") if totals_compared else None
    summary_accepted = totals.get("accepted") if totals_compared else None

    discrepancies = []
    if totals_compared:
        if isinstance(summary_requests, int) and summary_requests != raw_requests:
            discrepancies.append({"field": "requests", "summary": summary_requests,
                                  "raw": raw_requests})
        if isinstance(summary_accepted, int) and summary_accepted != raw_accepted:
            discrepancies.append({"field": "accepted", "summary": summary_accepted,
                                  "raw": raw_accepted})
        if isinstance(totals.get("rejected"), int) and totals["rejected"] != raw_rejected:
            discrepancies.append({"field": "rejected", "summary": totals.get("rejected"),
                                  "raw": raw_rejected})

    fidelity_rows = [{
        "seat_id": turn["seat_id"], "season": turn["season"],
        "result": turn["summary_last_command_fidelity"]["result"],
        "summary_type": turn["summary_last_command_fidelity"]["summary_type"],
        "summary_action_id": turn["summary_last_command_fidelity"]["summary_action_id"],
        "raw_last_accepted_type": turn["summary_last_command_fidelity"]["raw_last_accepted_type"],
        "raw_last_accepted_action_id":
            turn["summary_last_command_fidelity"]["raw_last_accepted_action_id"],
    } for turn in seasons]

    return {
        "summary_present": bool(experiment.summary),
        "summary_scope_ambiguous": experiment.summary_ambiguous,
        "totals_compared": totals_compared,
        "summary_declared_totals": ({"requests": summary_requests, "accepted": summary_accepted}
                                    if totals_compared else None),
        "raw_totals": {"requests": raw_requests, "accepted": raw_accepted,
                       "rejected": raw_rejected},
        "discrepancies": discrepancies,
        "summary_last_command_fidelity": {
            "result_counts": counter([row["result"] for row in fidelity_rows]),
            "turns": fidelity_rows,
            "explanation": "the summary keeps one last command per seat-turn; it is compared "
                           "with the turn's last accepted order only, and is not a claim about "
                           "the turn's whole action set",
        },
        "note": "summary last_command_json is never used as the turn's action list",
    }


def evidence_gaps(experiment, seasons, geometry):
    gaps = []
    if not experiment.summary:
        gaps.append("no spectator summary supplied: ending resources and end reasons unavailable")
    elif experiment.summary_ambiguous:
        gaps.append("summary covers several run ids and is keyed by seat/season only: its totals, "
                    "turns and scoreboards are diagnosed as ambiguous and not used")
    if not experiment.experiment_configs:
        gaps.append("no experiment_config sidecar: ceilings and flags are unknown from the raw run")
    if not experiment.turn_ends:
        gaps.append("no turn_end sidecar: turn end reasons come only from the summary")
    if geometry.status() != "ok":
        gaps.append("scenario geometry unavailable (%s): march direction is unknown"
                    % geometry.status())
    for turn in seasons:
        if turn["deliberation"]["inquiry_replies"]:
            continue
        if turn["deliberation"]["inquiry_offered"] is None:
            gaps.append("seat %s season %s: request exchange missing, so offered tools are unknown"
                        % (turn["seat_id"], turn["season"]))
    if experiment.summary and not experiment.summary_ambiguous:
        for turn in seasons:
            scoreboard = turn["resources"].get("summary_scoreboard") or {}
            if scoreboard.get("status") != "ok":
                gaps.append("seat %s season %s: summary scoreboard unavailable (%s), so the "
                            "season-end food/gold are unknown"
                            % (turn["seat_id"], turn["season"],
                               scoreboard.get("reason") or "unknown"))
    mismatches = [item for item in experiment.diagnostics
                  if item.get("kind") == "observation_prompt_mismatch"]
    if mismatches:
        gaps.append("%d request(s) have an observation_json that differs from the prompt's "
                    "embedded copy; facts are reported from observation_json" % len(mismatches))
    if all(not turn["marches"] for turn in seasons):
        gaps.append("no accepted march in this run")
    if all(not turn["visible_contact"] for turn in seasons):
        gaps.append("no seat ever saw a foreign or neutral city, army or scouted city: contact, "
                    "attacks and captures cannot be attributed from these observations")
    if all(turn["usage"]["reasoning_tokens"]["missing"] for turn in seasons):
        gaps.append("reasoning token counts are missing for every request")
    cost = (experiment.summary or {}).get("cost_usd")
    if cost is None:
        gaps.append("provider cost is unknown (summary cost_usd is null)")
    return gaps
