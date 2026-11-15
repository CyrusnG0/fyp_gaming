"""Join, prompt-parsing, path/hash and metric tests for the experiment-analysis tools."""
import hashlib
import json
import os
import tempfile

import experiment_context_analysis as analysis
import experiment_context_prompt as prompt_module
from experiment_context_tests_support import (AnalysisCase, RUN_ID, SEAT_A, SEAT_B,
                                              observation, prompt, request_exchange,
                                              result_exchange, run_record)


def _sha256(path):
    with open(path, "rb") as stream:
        return hashlib.sha256(stream.read()).hexdigest()


class JoinTests(AnalysisCase):
    def test_composite_key_distinguishes_two_seats_at_the_same_index(self):
        first_observation = observation(SEAT_A, 1, armies=[
            {"id": "army-a-1", "owner_seat_id": SEAT_A, "troops": 100, "unit_type": "infantry",
             "x": 4, "z": 4, "morale": 50}])
        second_observation = observation(SEAT_B, 1, armies=[
            {"id": "army-b-1", "owner_seat_id": SEAT_B, "troops": 100, "unit_type": "infantry",
             "x": 9, "z": 9, "morale": 50}])
        command_a = {"type": "march", "target_id": "army-a-1", "param_a": 5, "param_b": 4}
        command_b = {"type": "march", "target_id": "army-b-1", "param_a": 8, "param_b": 9}
        self.workspace.write(
            [run_record(SEAT_A, 1, 1, 1, command_a, observation_data=first_observation),
             run_record(SEAT_B, 1, 1, 2, command_b, observation_data=second_observation)],
            [request_exchange(SEAT_A, 1, 1, 1, prompt(
                SEAT_A, 1, movement={"army-a-1": {"position": (4, 4),
                                                 "options": [(5, 4, 1)], "unlisted": 0}},
                observation_data=first_observation)),
             request_exchange(SEAT_B, 1, 1, 2, prompt(
                 SEAT_B, 1, movement={"army-b-1": {"position": (9, 9),
                                                  "options": [(8, 9, 1)], "unlisted": 0}},
                 observation_data=second_observation))],
            [result_exchange(SEAT_A, 1, 1, 1, command_a),
             result_exchange(SEAT_B, 1, 1, 2, command_b)])
        model = self.workspace.analyse()
        self.assertEqual(model["join"]["usable_requests"], 2)
        self.assertEqual(model["join"]["excluded_requests"], 0)
        self.assertEqual(len(self.diagnostics(model, "legacy_exchange_id_collision")), 1)
        self.assertEqual(self.turn(model, SEAT_A, 1)["marches"][0]["army_id"], "army-a-1")
        self.assertEqual(self.turn(model, SEAT_B, 1)["marches"][0]["army_id"], "army-b-1")

    def test_duplicate_composite_key_is_ambiguous_and_excluded(self):
        command = {"type": "end_turn"}
        run = run_record(SEAT_A, 1, 1, 1, command)
        self.workspace.write([run, dict(run)],
                             [request_exchange(SEAT_A, 1, 1, 1, prompt(SEAT_A, 1))],
                             [result_exchange(SEAT_A, 1, 1, 1, command)])
        model = self.workspace.analyse()
        self.assertEqual(len(self.diagnostics(model, "duplicate_composite_key")), 1)
        self.assertEqual(len(self.diagnostics(model, "ambiguous_composite_key")), 1)
        self.assertEqual(model["join"]["usable_requests"], 0)
        turn = self.turn(model, SEAT_A, 1)
        self.assertEqual(turn["requests"]["joined"], 0)
        self.assertEqual(turn["requests"]["excluded_ambiguous_or_incomplete"], 1)
        self.assertEqual(turn["orders"]["accepted"], 0)

    def test_identity_mismatch_on_turn_token_is_reported_and_excluded(self):
        command = {"type": "end_turn"}
        self.workspace.write([run_record(SEAT_A, 1, 1, 1, command)],
                             [request_exchange(SEAT_A, 1, 1, 1, prompt(SEAT_A, 1))],
                             [result_exchange(SEAT_A, 1, 1, 99, command)])
        model = self.workspace.analyse()
        problems = [item for item in self.diagnostics(model, "join_problem")
                    if item["problem"] == "identity_mismatch:turn_token"]
        self.assertEqual(len(problems), 1)
        self.assertEqual(model["join"]["usable_requests"], 0)

    def test_conflicting_command_representations_are_reported_and_excluded(self):
        run_command = {"type": "reclaim", "target_id": "city-a"}
        result_command = {"type": "recruit", "target_id": "city-a"}
        self.workspace.write(
            [run_record(SEAT_A, 1, 1, 1, run_command, result_ok=True)],
            [request_exchange(SEAT_A, 1, 1, 1, prompt(SEAT_A, 1))],
            [result_exchange(SEAT_A, 1, 1, 1, result_command, result_ok=True)])
        model = self.workspace.analyse()
        conflicts = [item for item in self.diagnostics(model, "representation_conflict")
                     if item["field"] == "command_json"]
        self.assertEqual(len(conflicts), 1)
        self.assertEqual(model["join"]["usable_requests"], 0)
        turn = self.turn(model, SEAT_A, 1)
        self.assertEqual(turn["orders"]["accepted"], 0)
        self.assertEqual(turn["orders"]["rejected"], 0)
        self.assertEqual(turn["requests"]["excluded_ambiguous_or_incomplete"], 1)
        self.assertEqual(turn["requests"]["excluded_keys"][0]["conflicts"][0]["field"],
                         "command_json")

    def test_conflicting_outcome_representations_do_not_infer_acceptance(self):
        command = {"type": "reclaim", "target_id": "city-a"}
        self.workspace.write(
            [run_record(SEAT_A, 1, 1, 1, command, result_ok=False)],
            [request_exchange(SEAT_A, 1, 1, 1, prompt(SEAT_A, 1))],
            [result_exchange(SEAT_A, 1, 1, 1, command, result_ok=True)])
        model = self.workspace.analyse()
        conflicts = [item for item in self.diagnostics(model, "representation_conflict")
                     if item["field"] == "result_ok"]
        self.assertEqual(len(conflicts), 1)
        turn = self.turn(model, SEAT_A, 1)
        self.assertEqual(turn["orders"]["accepted"], 0)
        self.assertEqual(turn["orders"]["rejected"], 0)
        self.assertEqual(turn["requests"]["excluded_ambiguous_or_incomplete"], 1)

    def test_missing_request_exchange_yields_unknown_prompt_facts(self):
        command = {"type": "end_turn"}
        self.workspace.write([run_record(SEAT_A, 1, 1, 1, command)], [],
                             [result_exchange(SEAT_A, 1, 1, 1, command)])
        model = self.workspace.analyse()
        self.assertEqual(len(self.diagnostics(model, "join_problem")), 1)
        turn = self.turn(model, SEAT_A, 1)
        self.assertIsNone(turn["deliberation"]["inquiry_offered"])
        self.assertIsNone(turn["research"]["rules_offered"])
        self.assertEqual(turn["requests"]["prompt_status"], "missing")

    def test_missing_result_exchange_uses_the_run_record_result(self):
        command = {"type": "march", "target_id": "army-a-1", "param_a": 5, "param_b": 4}
        data = observation(SEAT_A, 1)
        self.workspace.write(
            [run_record(SEAT_A, 1, 1, 1, command, observation_data=data, result_ok=True)],
            [request_exchange(SEAT_A, 1, 1, 1, prompt(
                SEAT_A, 1, observation_data=data,
                movement={"army-a-1": {"position": (18, 8), "options": [(5, 4, 1)],
                                       "unlisted": 0}}))], [])
        model = self.workspace.analyse()
        self.assertTrue(self.turn(model, SEAT_A, 1)["marches"][0]["accepted"])

    def test_acceptance_is_unknown_when_no_record_states_it(self):
        command = {"type": "end_turn"}
        run = run_record(SEAT_A, 1, 1, 1, command)
        del run["result_ok"]
        self.workspace.write([run], [request_exchange(SEAT_A, 1, 1, 1, prompt(SEAT_A, 1))], [])
        model = self.workspace.analyse()
        self.assertEqual(self.turn(model, SEAT_A, 1)["orders"]["accepted"], 0)
        self.assertEqual(self.turn(model, SEAT_A, 1)["orders"]["rejected"], 0)

    def test_missing_summary_gives_unknown_end_reason(self):
        model = self.single_turn()
        self.assertFalse(model["reconciliation"]["summary_present"])
        self.assertEqual(self.turn(model)["turn_end"]["reason"], "unknown")
        self.assertTrue(any("no spectator summary" in gap for gap in model["evidence_gaps"]))

    def test_turn_end_sidecar_supplies_end_reason(self):
        command = {"type": "end_turn"}
        self.workspace.write(
            [run_record(SEAT_A, 1, 1, 1, command)],
            [request_exchange(SEAT_A, 1, 1, 1, prompt(SEAT_A, 1)),
             {"record_type": "llm_exchange", "exchange_event": "turn_end", "run_id": RUN_ID,
              "seat_id": SEAT_A, "season": 1, "turn_token": 1, "end_reason": "ActionLimit",
              "stop_ceiling": "actions/turn", "stopped_by_ceiling": True,
              "requests_this_turn": 1, "captured_utc": "2026-11-15T00:00:02Z"}],
            [result_exchange(SEAT_A, 1, 1, 1, command)])
        model = self.workspace.analyse()
        turn = self.turn(model, SEAT_A, 1)
        self.assertEqual(turn["turn_end"]["reason"], "ActionLimit")
        self.assertEqual(turn["turn_end"]["source"], "turn_end sidecar")
        self.assertTrue(turn["turn_end"]["stopped_by_ceiling"])

    def test_turn_end_without_a_seat_is_diagnosed(self):
        command = {"type": "end_turn"}
        self.workspace.write(
            [run_record(SEAT_A, 1, 1, 1, command)],
            [request_exchange(SEAT_A, 1, 1, 1, prompt(SEAT_A, 1)),
             {"record_type": "llm_exchange", "exchange_event": "turn_end", "run_id": RUN_ID,
              "season": 1, "turn_token": 1, "end_reason": "ActionLimit"}],
            [result_exchange(SEAT_A, 1, 1, 1, command)])
        model = self.workspace.analyse()
        self.assertEqual(len(self.diagnostics(model, "turn_end_without_turn_key")), 1)
        self.assertEqual(self.turn(model, SEAT_A, 1)["turn_end"]["reason"], "unknown")

    def test_duplicate_turn_end_is_diagnosed_and_not_used(self):
        command = {"type": "end_turn"}
        sidecar = {"record_type": "llm_exchange", "exchange_event": "turn_end",
                   "run_id": RUN_ID, "seat_id": SEAT_A, "season": 1, "turn_token": 1,
                   "end_reason": "ActionLimit", "stopped_by_ceiling": True}
        self.workspace.write(
            [run_record(SEAT_A, 1, 1, 1, command)],
            [request_exchange(SEAT_A, 1, 1, 1, prompt(SEAT_A, 1)), sidecar,
             dict(sidecar, end_reason="ModelEndedTurn")],
            [result_exchange(SEAT_A, 1, 1, 1, command)])
        model = self.workspace.analyse()
        duplicates = self.diagnostics(model, "duplicate_turn_end")
        self.assertEqual(len(duplicates), 1)
        self.assertEqual(model["join"]["turn_end_sidecars"], 0)
        self.assertEqual(self.turn(model, SEAT_A, 1)["turn_end"]["reason"], "unknown")

    def test_experiment_config_is_a_sidecar_not_a_request(self):
        command = {"type": "end_turn"}
        self.workspace.write(
            [run_record(SEAT_A, 1, 1, 1, command)],
            [{"record_type": "llm_exchange", "exchange_event": "experiment_config",
              "run_id": RUN_ID, "provider": "fake", "model": "fake-model",
              "settings": {"requests/turn": 8, "actions/turn": 5}},
             request_exchange(SEAT_A, 1, 1, 1, prompt(SEAT_A, 1))],
            [result_exchange(SEAT_A, 1, 1, 1, command)])
        model = self.workspace.analyse()
        self.assertEqual(model["join"]["experiment_config_records"], 1)
        self.assertEqual(model["join"]["usable_requests"], 1)
        self.assertEqual(self.diagnostics(model, "record_without_composite_key"), [])

    def test_record_without_composite_key_is_diagnosed(self):
        record = run_record(SEAT_A, 1, 1, 1, {"type": "end_turn"})
        del record["seat_id"]
        self.workspace.write([record], [], [])
        model = self.workspace.analyse()
        self.assertEqual(len(self.diagnostics(model, "record_without_composite_key")), 1)

    def test_malformed_json_line_is_diagnosed_without_echoing_content(self):
        self.workspace.write([], [], [], raw_run_lines=[
            '{"run_id": "' + RUN_ID + '", "api_key": "sk-live-ABCDEFGH1234567890", broken'])
        model = self.workspace.analyse()
        diagnostics = model["diagnostics"]["items"]
        self.assertEqual(len(diagnostics), 1)
        self.assertTrue(diagnostics[0]["kind"].startswith("invalid_json"))
        serialised = json.dumps(model, sort_keys=True)
        self.assertNotIn("sk-live-ABCDEFGH1234567890", serialised)
        self.assertNotIn("broken", serialised)

    def test_observation_json_prompt_mismatch_is_diagnosed(self):
        data = observation(SEAT_A, 1)
        embedded = json.loads(json.dumps(data))
        embedded["cities"][0]["food"] = 3100
        self.workspace.write(
            [run_record(SEAT_A, 1, 1, 1, {"type": "end_turn"}, observation_data=data)],
            [request_exchange(SEAT_A, 1, 1, 1,
                              prompt(SEAT_A, 1, observation_data=embedded))],
            [result_exchange(SEAT_A, 1, 1, 1, {"type": "end_turn"})])
        model = self.workspace.analyse()
        mismatches = self.diagnostics(model, "observation_prompt_mismatch")
        self.assertEqual(len(mismatches), 1)
        self.assertIn("observation_json", mismatches[0]["note"])


class PromptTests(AnalysisCase):
    def test_truncated_movement_option_list_is_explicit(self):
        data = observation(SEAT_A, 1)
        command = {"type": "march", "target_id": "army-a-1", "param_a": 20, "param_b": 8}
        self.workspace.write(
            [run_record(SEAT_A, 1, 1, 1, command, observation_data=data)],
            [request_exchange(SEAT_A, 1, 1, 1, prompt(
                SEAT_A, 1, observation_data=data,
                movement={"army-a-1": {"position": (18, 8),
                                       "options": [(16, 8, 2), (17, 8, 1), (18, 6, 2)],
                                       "unlisted": 37}}))],
            [result_exchange(SEAT_A, 1, 1, 1, command)])
        march = self.turn(self.workspace.analyse(), SEAT_A, 1)["marches"][0]
        self.assertFalse(march["destination_in_displayed_options"])
        self.assertEqual(march["displayed_option_count"], 3)
        self.assertEqual(march["unlisted_option_count"], 37)
        self.assertEqual(march["total_reachable_estimate"], 40)
        self.assertTrue(march["option_list_truncated"])

    def test_prompt_missing_markers_is_truncated_not_absent(self):
        data = observation(SEAT_A, 1)
        model = self.single_turn(prompt_text=prompt(SEAT_A, 1, observation_data=data,
                                                    footer=False),
                                 observation_data=data)
        turn = self.turn(model, SEAT_A, 1)
        self.assertEqual(turn["requests"]["prompt_status"], "truncated")
        self.assertTrue(any("output_contract_footer" in item["missing_markers"]
                            for item in self.diagnostics(model, "truncated_prompt")))

    def test_prompt_without_movement_section_reports_unknown_options(self):
        data = observation(SEAT_A, 1)
        command = {"type": "march", "target_id": "army-a-1", "param_a": 20, "param_b": 8}
        self.workspace.write(
            [run_record(SEAT_A, 1, 1, 1, command, observation_data=data)],
            [request_exchange(SEAT_A, 1, 1, 1, prompt(SEAT_A, 1, observation_data=data))],
            [result_exchange(SEAT_A, 1, 1, 1, command)])
        march = self.turn(self.workspace.analyse(), SEAT_A, 1)["marches"][0]
        self.assertIsNone(march["destination_in_displayed_options"])
        self.assertIsNone(march["displayed_option_count"])
        self.assertIsNotNone(march["destination"])

    def test_research_option_args_and_prices_parse(self):
        parsed = prompt_module.parse_prompt(prompt(SEAT_A, 1, options=[
            {"type": "research", "args": {"line": "agri"}, "cost": 1,
             "label": "Research Agriculture to level 1 for 20 research points"},
            {"type": "end_turn", "cost": 0, "label": "End turn"}]))
        research = [item for item in parsed["options"] if item["type"] == "research"][0]
        self.assertEqual(research["args"], {"line": "agri"})
        self.assertEqual(research["cost"], 1)
        self.assertEqual(prompt_module.research_points_price(research["label"]), 20)


class PathTests(AnalysisCase):
    def test_input_hash_is_independent_of_the_working_directory(self):
        self.workspace.write([run_record(SEAT_A, 1, 1, 1, {"type": "end_turn"})],
                             [request_exchange(SEAT_A, 1, 1, 1, prompt(SEAT_A, 1))],
                             [result_exchange(SEAT_A, 1, 1, 1, {"type": "end_turn"})])
        expected = _sha256(self.workspace.run_path)
        original = os.getcwd()
        elsewhere = tempfile.mkdtemp(prefix="luanshi-cwd-")
        try:
            os.chdir(elsewhere)
            _, model = analysis.build(self.workspace.run_path, self.workspace.exchange_path,
                                      None, None, None, auto_map=False)
        finally:
            os.chdir(original)
        self.assertEqual(model["inputs"]["run"]["sha256"], expected)
        self.assertNotIn("absolute_path", model["inputs"]["run"])

    def test_display_path_stays_relative_and_hash_uses_absolute(self):
        self.workspace.write([run_record(SEAT_A, 1, 1, 1, {"type": "end_turn"})], [], [])
        model = self.workspace.analyse()
        run_input = model["inputs"]["run"]
        self.assertEqual(run_input["sha256"], _sha256(self.workspace.run_path))
        self.assertFalse(os.path.isabs(run_input["path"]))


class MetricTests(AnalysisCase):
    def test_missing_tokens_are_unknown_not_zero(self):
        model = self.single_turn(tokens=(None, None, None),
                                 response_extra={"input_tokens": None, "output_tokens": None,
                                                 "reasoning_tokens": None})
        usage = self.turn(model, SEAT_A, 1)["usage"]
        for key in ("input_tokens", "output_tokens", "reasoning_tokens"):
            self.assertEqual(usage[key]["status"], "unknown")
            self.assertIsNone(usage[key]["value"])
            self.assertEqual(usage[key]["missing"], 1)
        self.assertIsNone(model["totals"]["input_tokens"]["value"])

    def test_partial_tokens_keep_missing_count(self):
        command = {"type": "end_turn"}
        data = observation(SEAT_A, 1)
        self.workspace.write(
            [run_record(SEAT_A, 1, 1, 1, command, tokens=(100, None, 20)),
             run_record(SEAT_A, 1, 2, 1, command, tokens=(50, 30, None))],
            [request_exchange(SEAT_A, 1, 1, 1, prompt(SEAT_A, 1, observation_data=data)),
             request_exchange(SEAT_A, 1, 2, 1, prompt(SEAT_A, 1, observation_data=data))],
            [result_exchange(SEAT_A, 1, 1, 1, command,
                             response_extra={"output_tokens": None}),
             result_exchange(SEAT_A, 1, 2, 1, command,
                             response_extra={"input_tokens": None, "output_tokens": None,
                                             "reasoning_tokens": None})])
        usage = self.turn(self.workspace.analyse(), SEAT_A, 1)["usage"]
        self.assertEqual(usage["input_tokens"]["value"], 150)
        self.assertEqual(usage["output_tokens"]["value"], 30)
        self.assertEqual(usage["output_tokens"]["missing"], 1)
        self.assertEqual(usage["reasoning_tokens"]["missing"], 1)


class BytecodeTests(AnalysisCase):
    def test_production_modules_are_not_bytecode_cached(self):
        tools_dir = os.path.dirname(os.path.abspath(analysis.__file__))
        cache_dir = os.path.join(tools_dir, "__pycache__")
        cached = []
        if os.path.isdir(cache_dir):
            cached = [name for name in os.listdir(cache_dir)
                      if name.startswith("experiment_context_")]
        self.assertEqual(cached, [], "production modules must not leave .pyc files")
        self.assertTrue(analysis.sys.dont_write_bytecode)
