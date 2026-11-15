"""Markdown rendering for the per-faction detail and march evidence sections."""


def cell(value):
    if value is None:
        return "unknown"
    if isinstance(value, bool):
        return "yes" if value else "no"
    if isinstance(value, (list, tuple)):
        return ", ".join(str(item) for item in value) if value else "none"
    return str(value)


def metric_text(item):
    if not isinstance(item, dict):
        return "unknown"
    if item.get("status") != "known":
        return "unknown (known %d, missing %d)" % (item.get("known", 0), item.get("missing", 0))
    text = str(item.get("value"))
    if item.get("missing"):
        text += " (known %d, missing %d)" % (item["known"], item["missing"])
    return text


def entity_text(entity):
    if not isinstance(entity, dict):
        return cell(entity)
    return "%s %s (%s)" % (cell(entity.get("kind")), cell(entity.get("id")),
                           cell(entity.get("owner_class")))


def _counter_text(items):
    return ", ".join("%s: %d" % item for item in items.items())


def render_faction_details(model, add):
    add("## Per faction and season")
    add("")
    for faction in model["factions"]:
        add("### %s (%s)" % (faction.get("faction") or "unknown faction", faction["seat_id"]))
        add("")
        for turn in faction["seasons"]:
            _render_turn(turn, add)


def _render_turn(turn, add):
    add("#### Season %s" % turn["season"])
    add("")
    add("- Requests: %d; accepted orders: %d; rejected orders: %d; invalid replies: %d."
        % (turn["requests"]["joined"], turn["orders"]["accepted"],
           turn["orders"]["rejected"], turn["orders"]["invalid_replies"]))
    add("- Request kinds: %s." % _counter_text(turn["requests"]["by_kind"]))
    if turn["requests"].get("degraded"):
        add("- Degraded joins (analysed, with missing facts left unknown): %s."
            % ", ".join("request %s: %s" % (item["request_index"], ", ".join(item["problems"]))
                        for item in turn["requests"]["degraded"]))
    if turn["requests"].get("excluded_ambiguous_or_incomplete"):
        add("- Excluded (ambiguous or incomplete join): %d — counted as unknown, not as no-action."
            % turn["requests"]["excluded_ambiguous_or_incomplete"])
    add("- Accepted order types: %s."
        % (_counter_text(turn["orders"]["accepted_by_type"])
           if turn["orders"]["accepted_by_type"] else "none"))
    add("- Turn end: %s (source: %s)." % (turn["turn_end"]["reason"],
                                         turn["turn_end"]["source"]))
    usage = turn["usage"]
    add("- Tokens: input %s, output %s, reasoning %s; latency %s ms."
        % (metric_text(usage["input_tokens"]), metric_text(usage["output_tokens"]),
           metric_text(usage["reasoning_tokens"]), metric_text(usage["latency_ms"])))
    add("- Provider finish reasons: %s." % _counter_text(turn["finish_reasons"]))
    _render_resources(turn, add)
    _render_deliberation(turn, add)
    _render_research(turn, add)
    add("- Attacks ordered: %d; visible captures (city and watchtower): %d."
        % (len(turn["attacks"]), len(turn["captures_visible"])))
    if turn["summary_cross_check"]:
        check = turn["summary_cross_check"]
        add("- Summary cross-check: summary requests %s vs raw %s; summary accepted %s vs raw %s."
            % (cell(check["summary_requests"]), check["raw_joined_requests"],
               cell(check["summary_accepted"]), check["raw_accepted"]))
    fidelity = turn["summary_last_command_fidelity"]
    add("- Summary last command `%s` (%s) vs raw last accepted `%s` (%s): %s."
        % (cell(fidelity["summary_type"]), cell(fidelity["summary_action_id"]),
           cell(fidelity["raw_last_accepted_type"]), cell(fidelity["raw_last_accepted_action_id"]),
           fidelity["result"]))
    add("")
    _render_commands(turn, add)
    _render_attacks(turn, add)
    if turn["stated_explanations"]:
        add("Stated explanations (model text, separate from the analyst classification):")
        add("")
        for item in turn["stated_explanations"]:
            add("- request %s `%s`: \"%s\"%s"
                % (item["request_index"], cell(item["action"]), item["reason"] or "",
                   (" — summary: \"%s\"" % item["summary"] if item.get("summary") else "")))
        add("")


def _render_resources(turn, add):
    resources = turn["resources"]
    add("- Visible food %s / gold %s across %d cities at the first request; %s / %s before the "
        "last order of the turn."
        % (metric_text(resources["first_food_total"]), metric_text(resources["first_gold_total"]),
           len(resources["first_cities"]),
           metric_text(resources["last_pre_order_food_total"]),
           metric_text(resources["last_pre_order_gold_total"])))
    scoreboard = resources.get("summary_scoreboard") or {}
    if scoreboard.get("status") == "ok" and scoreboard.get("seat"):
        seat_row = scoreboard["seat"]
        add("- Season-end scoreboard (summary, season %s): food %s / gold %s across %s cities; "
            "population %s, garrison %s. Source: `%s`."
            % (turn["season"], cell(seat_row.get("food")), cell(seat_row.get("gold")),
               cell(seat_row.get("cities")), cell(seat_row.get("population")),
               cell(seat_row.get("garrison")), scoreboard.get("citation")))
    else:
        add("- Season-end scoreboard: unknown (%s)." % cell(scoreboard.get("reason")))
    add("- The scoreboard is the spectator's resolution-time view; it is not the seat's last "
        "pre-order observation.")
    low_morale = resources["cities_below_morale_threshold_first_observation"]
    if low_morale:
        add("- Cities below the morale %d yield-penalty threshold at the first request (visible "
            "values): %s." % (resources["morale_threshold"],
                              ", ".join("%s morale %s" % (city["city_id"], city["morale"])
                                        for city in low_morale)))
    penalty = resources["visible_penalty_events"]
    if penalty:
        add("- Visible penalty events across %d pre-order observations, deduplicated: %s."
            % (resources["observations_counted"],
               _counter_text(_event_counter(penalty))))
    else:
        add("- Visible famine/revolt/desertion events: none in the %d pre-order observations "
            "(up to %s events each; a capped list means absence is not proof). Food upkeep and "
            "morale resolution are not itemised in the observation."
            % (resources["observations_counted"], cell(resources["events_in_observation"])))
    contact = turn["visible_contact"]
    if contact:
        add("- Visible non-own entities across all pre-order observations: %s."
            % ", ".join("%s %s (%s)" % (cell(item["kind"]), cell(item["id"]),
                                        cell(item.get("owner_class"))) for item in contact))
    else:
        add("- Visible non-own entities across all pre-order observations: none (fog of war).")


def _event_counter(events):
    counts = {}
    for event in events:
        counts[event["event_type"]] = counts.get(event["event_type"], 0) + 1
    return dict(sorted(counts.items()))


def _render_deliberation(turn, add):
    deliberation = turn["deliberation"]
    add("- Inquiry offered: %s; inquiry replies: %d (confirmed answered: %d)."
        % (cell(deliberation["inquiry_offered"]), len(deliberation["inquiry_replies"]),
           deliberation["inquiries_confirmed_answered"]))
    add("- Note offered: %s; note replies: %d; confirmed saves: %d%s."
        % (cell(deliberation["note_offered"]), len(deliberation["note_replies"]),
           deliberation["notes_saved_confirmed"],
           (" (failed: %s)" % ", ".join("%s=%s" % (item["outcome"], item["failure_class"])
                                        for item in deliberation["note_failures"]))
           if deliberation["note_failures"] else ""))
    for inquiry in deliberation["inquiry_replies"]:
        add("- Inquiry at request %s: tool `%s`; evidence %s; outcome %s."
            % (inquiry["request_index"], cell(inquiry.get("tool")), cell(inquiry.get("evidence")),
               cell(inquiry.get("outcome"))))


def _render_research(turn, add):
    research = turn["research"]
    lines = ", ".join("%s (command-point cost %s, research points %s)"
                      % (item["line"], cell(item["command_point_cost"]),
                         cell(item.get("research_points_price")))
                      for item in research["available_lines"]) or "none"
    add("- Research rules offered in the first request: %s; lines available there: %s."
        % (cell(research["rules_offered"]), lines))
    for choice in research["chosen"]:
        add("- Research choice at request %s: line `%s`; rules offered in that request: %s; "
            "line offered in that request: %s; offered lines there: %s; label price %s research "
            "points; accepted: %s."
            % (choice["request_index"], cell(choice["line"]),
               cell(choice["rules_offered_in_request"]),
               cell(choice["line_offered_in_request"]),
               cell(choice["offered_lines_in_request"]),
               cell(choice["research_points_price"]), cell(choice["accepted"])))


def _render_commands(turn, add):
    commands = turn["orders"]["all_commands"]
    if not commands:
        return
    add("| req | order | action_id | target | dest | accepted | result | sources |")
    add("| --- | --- | --- | --- | --- | --- | --- | --- |")
    for order in commands:
        dest = "-"
        if order["type"] == "march" and order["param_a"] is not None:
            dest = "%s,%s" % (order["param_a"], order["param_b"])
        if order["accepted"] is True:
            result = "ok"
        elif order["accepted"] is False:
            result = order["result_error"] or "rejected"
        else:
            result = "unknown"
        add("| %s | %s | %s | %s | %s | %s | %s | %s |" % (
            order["request_index"], cell(order["type"]), cell(order.get("action_id")),
            cell(order["target_id"]), dest, cell(order["accepted"]), cell(result),
            ", ".join("`%s`" % cite for cite in order["sources"].values())))
    add("")


def _render_attacks(turn, add):
    if not turn["attacks"]:
        return
    add("| req | attack | visible target | position | accepted | result | sources |")
    add("| --- | --- | --- | --- | --- | --- | --- |")
    for attack in turn["attacks"]:
        result = (attack.get("result_error") if attack.get("result_error")
                  else ("ok" if attack.get("accepted") else "unknown"))
        add("| %s | %s | %s | %s | %s | %s | %s |" % (
            cell(attack.get("request_index")), cell(attack["type"]),
            entity_text(attack["visible_target"]), cell(attack["target_position"]),
            cell(attack.get("accepted")), cell(result),
            ", ".join("`%s`" % cite for cite in (attack.get("sources") or {}).values())))
    add("")


def render_march_evidence(model, add):
    add("## March evidence")
    add("")
    add("Classification uses only the seat's pre-order observation, the exact pre-order dispatched "
        "prompt and the declared scenario geometry. Target and intent stay unknown unless that "
        "evidence establishes them.")
    add("")
    add("| seat | season | army | origin | destination | in displayed options | unlisted | "
        "target at destination | intent | geometry | sources |")
    add("| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |")
    for faction in model["factions"]:
        for turn in faction["seasons"]:
            for march in turn["marches"]:
                add("| %s | %s | %s | %s | %s | %s | %s | %s | %s | %s | %s |" % (
                    turn["seat_id"], turn["season"], march["army_id"],
                    cell(march["origin"]), cell(march["destination"]),
                    cell(march["destination_in_displayed_options"]),
                    cell(march["unlisted_option_count"]),
                    entity_text(march["target_at_destination"]),
                    cell(march["intent"]),
                    "%s %s->%s" % (march["geometry"].get("status"),
                                   march["geometry"].get("origin_distance"),
                                   march["geometry"].get("destination_distance")),
                    ", ".join("`%s`" % cite for cite in march["sources"].values())))
    add("")
    for faction in model["factions"]:
        for turn in faction["seasons"]:
            for march in turn["marches"]:
                _render_march(march, turn, add)


def _render_march(march, turn, add):
    add("### %s season %s — %s %s -> %s" % (
        turn["seat_id"], turn["season"], march["army_id"],
        cell(march["origin"]), cell(march["destination"])))
    add("")
    add("- Geometry (not intent): %s relative to centre %s by %s (%s -> %s)."
        % (cell(march["geometry"].get("relation")), cell(march["geometry"].get("centre")),
           march["geometry"].get("metric"),
           cell(march["geometry"].get("origin_distance")),
           cell(march["geometry"].get("destination_distance"))))
    add("- Destination listed in displayed options: %s; options displayed: %s; unlisted due to "
        "the per-army cap: %s (total reachable about %s)."
        % (cell(march["destination_in_displayed_options"]),
           cell(march["displayed_option_count"]), cell(march["unlisted_option_count"]),
           cell(march.get("total_reachable_estimate"))))
    if march["displayed_options"]:
        add("- Displayed options: %s." % "; ".join(march["displayed_options"]))
    if march["offered_toward_centre_options"] is not None:
        add("- Offered options that reduce distance to the centre: %s."
            % (", ".join("%d,%d (d=%d)" % (item["x"], item["z"], item["distance"])
                         for item in march["offered_toward_centre_options"])
               or "none of the displayed options"))
    else:
        add("- Offered toward-centre options: unknown (no geometry or no displayed options).")
    if march["visible_entities_before"]:
        add("- Visible non-own entities before the order: %s."
            % ", ".join("%s %s (%s) at %s,%s" % (cell(item["kind"]), cell(item["id"]),
                                                 cell(item.get("owner_class")),
                                                 item["x"], item["z"])
                        for item in march["visible_entities_before"]))
    else:
        add("- Visible non-own entities before the order: none.")
    add("- Target at destination: %s (%s); intent: %s — %s."
        % (entity_text(march["target_at_destination"]), march["target_basis"],
           march["intent"], march["intent_reason"]))
    add("- Sources: %s." % ", ".join("`%s`" % cite for cite in march["sources"].values()))
    add("")
