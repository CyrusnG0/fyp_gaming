"""March classification and per-turn observation aggregation tests."""
import json

from experiment_context_tests_support import (AnalysisCase, SEAT_A, event, grid_map, observation,
                                              prompt, request_exchange, result_exchange,
                                              run_record)


class MarchTests(AnalysisCase):
    def _march_model(self, *, direction=(24, 8), options=((16, 8, 2), (24, 8, 6)),
                     unlisted=0, enemy_city_at=None, neutral_city_at=None, map_data=None,
                     scenario_id=None):
        data = observation(SEAT_A, 1, enemy_city_at=enemy_city_at,
                           neutral_city_at=neutral_city_at)
        command = {"type": "march", "target_id": "army-a-1",
                   "param_a": direction[0], "param_b": direction[1]}
        self.workspace.write(
            [run_record(SEAT_A, 1, 1, 1, command, observation_data=data,
                        scenario_id=scenario_id)],
            [request_exchange(SEAT_A, 1, 1, 1, prompt(
                SEAT_A, 1, observation_data=data,
                movement={"army-a-1": {"position": (18, 8), "options": list(options),
                                       "unlisted": unlisted}}))],
            [result_exchange(SEAT_A, 1, 1, 1, command)])
        if map_data is not None:
            with open(self.workspace.map_path, "w", encoding="utf-8") as stream:
                json.dump(map_data, stream)
        return self.workspace.analyse(use_map=map_data is not None)

    def test_hidden_enemy_at_destination_stays_unknown(self):
        march = self.turn(self._march_model(), SEAT_A, 1)["marches"][0]
        self.assertIsNone(march["target_at_destination"])
        self.assertEqual(march["intent"], "unknown")
        self.assertEqual(march["target_basis"], "not_visible")

    def test_visible_enemy_city_at_destination_is_evidence_not_intent(self):
        march = self.turn(self._march_model(enemy_city_at=(24, 8)), SEAT_A, 1)["marches"][0]
        self.assertEqual(march["target_at_destination"]["id"], "city-enemy")
        self.assertEqual(march["target_at_destination"]["owner_class"], "foreign")
        self.assertEqual(march["target_basis"], "visible_in_observation")
        self.assertEqual(march["intent"], "unknown")

    def test_visible_neutral_city_is_contact_evidence_not_hostility(self):
        model = self._march_model(neutral_city_at=(24, 8))
        turn = self.turn(model, SEAT_A, 1)
        self.assertEqual([item["id"] for item in turn["visible_contact"]], ["city-neutral"])
        self.assertEqual(turn["visible_contact"][0]["owner_class"], "neutral")
        march = turn["marches"][0]
        self.assertEqual(march["target_at_destination"]["owner_class"], "neutral")
        self.assertEqual(march["intent"], "unknown")
        serialised = json.dumps(march)
        self.assertNotIn("hostile", serialised)
        self.assertNotIn("enemy_intent", serialised)

    def test_foreign_and_neutral_contact_are_distinguished(self):
        model = self._march_model(enemy_city_at=(10, 10), neutral_city_at=(24, 8))
        turn = self.turn(model, SEAT_A, 1)
        classes = {item["id"]: item["owner_class"] for item in turn["visible_contact"]}
        self.assertEqual(classes, {"city-enemy": "foreign", "city-neutral": "neutral"})

    def test_geometry_is_away_and_offered_toward_options_are_listed(self):
        model = self._march_model(
            direction=(24, 8), options=((16, 8, 2), (17, 8, 1), (24, 8, 6), (24, 11, 6)),
            unlisted=39, map_data=grid_map(), scenario_id="compact-contact-v1")
        march = self.turn(model, SEAT_A, 1)["marches"][0]
        self.assertEqual(march["geometry"]["status"], "ok")
        self.assertEqual(march["geometry"]["relation"], "away")
        self.assertEqual(march["geometry"]["origin_distance"], 5)
        self.assertEqual(march["geometry"]["destination_distance"], 11)
        offered = [(item["x"], item["z"]) for item in march["offered_toward_centre_options"]]
        self.assertEqual(offered, [(16, 8), (17, 8)])

    def test_geometry_same_when_distance_does_not_change(self):
        model = self._march_model(direction=(17, 9), options=((17, 9, 0),),
                                  map_data=grid_map(), scenario_id="compact-contact-v1")
        march = self.turn(model, SEAT_A, 1)["marches"][0]
        self.assertEqual(march["geometry"]["relation"], "same")

    def test_geometry_unknown_without_a_map(self):
        march = self.turn(self._march_model(direction=(24, 8),
                                            scenario_id="compact-contact-v1"),
                          SEAT_A, 1)["marches"][0]
        self.assertEqual(march["geometry"]["status"], "unknown:map_missing")
        self.assertIsNone(march["offered_toward_centre_options"])

    def test_geometry_unknown_for_an_unsupported_scenario(self):
        march = self.turn(self._march_model(direction=(24, 8), scenario_id="some-other-map",
                                            map_data=grid_map()), SEAT_A, 1)["marches"][0]
        self.assertEqual(march["geometry"]["status"], "unknown:unsupported_scenario")
        self.assertIsNone(march["geometry"]["centre"])

    def test_later_state_is_never_used_for_classification(self):
        early = observation(SEAT_A, 1)
        later = observation(SEAT_A, 2, armies=[
            {"id": "army-a-1", "owner_seat_id": SEAT_A, "troops": 1000,
             "unit_type": "infantry", "x": 25, "z": 2, "morale": 40}])
        command = {"type": "march", "target_id": "army-a-1", "param_a": 24, "param_b": 8}
        self.workspace.write(
            [run_record(SEAT_A, 1, 1, 1, command, observation_data=early),
             run_record(SEAT_A, 2, 1, 2, {"type": "end_turn"}, observation_data=later)],
            [request_exchange(SEAT_A, 1, 1, 1, prompt(
                SEAT_A, 1, observation_data=early,
                movement={"army-a-1": {"position": (18, 8), "options": [(24, 8, 6)],
                                       "unlisted": 0}})),
             request_exchange(SEAT_A, 2, 1, 2, prompt(SEAT_A, 2, observation_data=later))],
            [result_exchange(SEAT_A, 1, 1, 1, command),
             result_exchange(SEAT_A, 2, 1, 2, {"type": "end_turn"})])
        march = self.turn(self.workspace.analyse(), SEAT_A, 1)["marches"][0]
        self.assertEqual(march["origin"], [18, 8])


class ObservationAggregationTests(AnalysisCase):
    def _write_three_requests(self, observations, commands=None):
        commands = commands or [{"type": "end_turn"}] * len(observations)
        runs, requests, results = [], [], []
        for index, data in enumerate(observations, 1):
            command = commands[index - 1]
            runs.append(run_record(SEAT_A, 1, index, 1, command, observation_data=data))
            requests.append(request_exchange(
                SEAT_A, 1, index, 1,
                prompt(SEAT_A, 1, options=[{"type": "end_turn", "cost": 0}],
                       observation_data=data)))
            results.append(result_exchange(SEAT_A, 1, index, 1, command))
        self.workspace.write(runs, requests, results)
        return self.workspace.analyse()

    def test_contact_and_captures_from_a_middle_request_are_kept(self):
        first = observation(SEAT_A, 1)
        first["events"] = [event(1, "season_start")]
        middle = observation(SEAT_A, 1, armies=[
            {"id": "army-a-1", "owner_seat_id": SEAT_A, "troops": 2000,
             "unit_type": "infantry", "x": 18, "z": 8, "morale": 60},
            {"id": "army-enemy-1", "owner_seat_id": "seat-9", "troops": 800,
             "unit_type": "infantry", "x": 16, "z": 11, "morale": 55}])
        middle["events"] = [
            event(1, "season_start"),
            event(5, "city_captured", actor="seat-9", payload={"city_id": "city-neutral"}),
            event(6, "watch_tower_captured", actor="seat-9", payload={"city_id": "tower-1"})]
        last = observation(SEAT_A, 1)
        last["events"] = [event(1, "season_start"),
                          event(5, "city_captured", actor="seat-9",
                                payload={"city_id": "city-neutral"})]
        turn = self.turn(self._write_three_requests([first, middle, last]), SEAT_A, 1)
        self.assertEqual([item["id"] for item in turn["visible_contact"]], ["army-enemy-1"])
        self.assertEqual(sorted(item["event_type"] for item in turn["captures_visible"]),
                         ["city_captured", "watch_tower_captured"])
        pressure = [item["event_type"] for item in
                    turn["resources"]["visible_pressure_events"]]
        self.assertEqual(pressure.count("city_captured"), 1)
        first_seen = {item["event_type"]: item["first_seen_request_index"]
                      for item in turn["resources"]["visible_pressure_events"]}
        self.assertEqual(first_seen["city_captured"], 2)
        self.assertEqual(turn["resources"]["observations_counted"], 3)

    def test_seq_less_events_are_deduplicated_structurally(self):
        without_seq = {"season": 1, "phase": "player_orders", "actor_seat_id": "seat-9",
                       "controller_type": "llm", "event_type": "watch_tower_captured",
                       "visibility": "public", "payload": {"city_id": "tower-1"},
                       "source_action_id": None}
        first = observation(SEAT_A, 1)
        first["events"] = [event(1, "season_start"), dict(without_seq)]
        second = observation(SEAT_A, 1)
        second["events"] = [event(1, "season_start"), dict(without_seq)]
        turn = self.turn(self._write_three_requests([first, second]), SEAT_A, 1)
        self.assertEqual(len(turn["captures_visible"]), 1)
        self.assertEqual(turn["resources"]["events_without_seq"], 1)

    def test_seq_less_events_with_different_payloads_are_not_collapsed(self):
        base = {"season": 1, "phase": "player_orders", "actor_seat_id": "seat-9",
                "controller_type": "llm", "event_type": "watch_tower_captured",
                "visibility": "public", "source_action_id": None}
        first = observation(SEAT_A, 1)
        first["events"] = [event(1, "season_start"),
                          dict(base, payload={"city_id": "tower-1"})]
        second = observation(SEAT_A, 1)
        second["events"] = [event(1, "season_start"),
                           dict(base, payload={"city_id": "tower-2"})]
        turn = self.turn(self._write_three_requests([first, second]), SEAT_A, 1)
        self.assertEqual(len(turn["captures_visible"]), 2)

    def test_hidden_contact_never_enters_the_aggregate(self):
        data = observation(SEAT_A, 1)
        data["events"] = [event(1, "season_start")]
        turn = self.turn(self._write_three_requests([data, data, data]), SEAT_A, 1)
        self.assertEqual(turn["visible_contact"], [])
        self.assertEqual(turn["captures_visible"], [])
