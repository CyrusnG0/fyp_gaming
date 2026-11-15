"""Deliberation, request-kind, reconstruction and per-request research tests."""
import json

import experiment_context_analysis as analysis
from experiment_context_tests_support import (AnalysisCase, SEAT_A, observation, prompt,
                                              request_exchange, result_exchange, run_record,
                                              system)


class DeliberationTests(AnalysisCase):
    def test_inquiry_and_note_are_offered_and_counted_when_used(self):
        model = self.single_turn(
            command=None,
            raw_model_text=json.dumps({"tool": "manual_lookup", "topic": "research"}))
        turn = self.turn(model, SEAT_A, 1)
        self.assertTrue(turn["deliberation"]["inquiry_offered"])
        self.assertTrue(turn["deliberation"]["note_offered"])
        self.assertEqual(len(turn["deliberation"]["inquiry_replies"]), 1)
        self.assertEqual(turn["deliberation"]["inquiry_replies"][0]["tool"], "manual_lookup")
        self.assertEqual(turn["deliberation"]["inquiry_replies"][0]["topic"], "research")

    def test_inquiry_and_note_requests_are_not_counted_as_orders(self):
        model = self.single_turn(
            command=None, raw_model_text=json.dumps({"note": "hold the river"}))
        turn = self.turn(model, SEAT_A, 1)
        self.assertEqual(turn["orders"]["all_commands"], [])
        self.assertEqual(turn["orders"]["accepted"], 0)
        self.assertEqual(turn["requests"]["by_kind"]["note"], 1)
        self.assertEqual(turn["requests"]["by_kind"]["command"], 0)
        self.assertEqual(turn["requests"]["tool_activity"][0]["kind"], "note")

    def test_note_from_previous_turn_is_detected_in_the_prompt(self):
        model = self.single_turn(prompt_text=prompt(SEAT_A, 1, note_text="hold the river"))
        self.assertTrue(self.turn(model, SEAT_A, 1)["deliberation"]
                        ["note_present_from_previous_turn"])

    def test_no_command_request_is_reported_as_a_kind_not_an_order(self):
        model = self.single_turn(command=None, raw_model_text=json.dumps({"unexpected": True}))
        turn = self.turn(model, SEAT_A, 1)
        self.assertEqual(turn["orders"]["all_commands"], [])
        self.assertEqual(turn["requests"]["by_kind"]["no_command"], 1)
        self.assertEqual(turn["requests"]["tool_activity"][0]["kind"], "no_command")


class ReconstructedDeliberationTests(AnalysisCase):
    def _write_turn(self, records):
        runs, requests, results = [], [], []
        for index, record in enumerate(records, 1):
            command = record.get("command")
            data = record.get("observation") or observation(SEAT_A, 1)
            runs.append(run_record(SEAT_A, 1, index, 1, command, observation_data=data,
                                   parse_ok=record.get("parse_ok", True),
                                   result_error=record.get("result_error"),
                                   omit_raw_model_text=True, extra=record.get("extra")))
            requests.append(request_exchange(
                SEAT_A, 1, index, 1,
                prompt(SEAT_A, 1, options=[{"type": "end_turn", "cost": 0}],
                       observation_data=data), system()))
            result = result_exchange(SEAT_A, 1, index, 1, command,
                                     parse_ok=record.get("parse_ok", True))
            if record.get("result_error") is not None:
                result["outcome"]["result_error"] = record["result_error"]
            results.append(result)
        self.workspace.write(runs, requests, results)
        return self.turn(self.workspace.analyse(), SEAT_A, 1)

    def test_inquiry_is_reconstructed_from_the_deliberation_event(self):
        turn = self._write_turn([
            {"command": None,
             "extra": {"inquiry_tool": "manual_lookup",
                       "deliberation_event": {"kind": "inquiry", "tool": "manual_lookup",
                                              "outcome": "answered", "result_chars": 123}}},
            {"command": {"type": "end_turn"}}])
        inquiry = turn["deliberation"]["inquiry_replies"][0]
        self.assertEqual(inquiry["tool"], "manual_lookup")
        self.assertEqual(inquiry["evidence"], "deliberation_event")
        self.assertEqual(inquiry["outcome"], "answered")
        self.assertEqual(turn["deliberation"]["inquiries_confirmed_answered"], 1)
        self.assertEqual(turn["orders"]["all_commands"][0]["type"], "end_turn")

    def test_inquiry_tool_alone_reconstructs_the_tool_without_an_outcome(self):
        turn = self._write_turn([
            {"command": None, "extra": {"inquiry_tool": "inspect_entity"}},
            {"command": {"type": "end_turn"}}])
        inquiry = turn["deliberation"]["inquiry_replies"][0]
        self.assertEqual(inquiry["tool"], "inspect_entity")
        self.assertEqual(inquiry["evidence"], "inquiry_tool")
        self.assertIsNone(inquiry["outcome"])

    def test_note_reply_and_confirmed_save_are_distinct(self):
        turn = self._write_turn([
            {"command": None,
             "extra": {"deliberation_event": {"kind": "note", "outcome": "saved",
                                              "result_chars": 44}}},
            {"command": None, "parse_ok": False,
             "extra": {"deliberation_event": {"kind": "note",
                                              "outcome": "invalid:empty_note",
                                              "result_chars": 0}}},
            {"command": {"type": "end_turn"}}])
        self.assertEqual(len(turn["deliberation"]["note_replies"]), 2)
        self.assertEqual(turn["deliberation"]["notes_saved_confirmed"], 1)
        self.assertEqual(turn["deliberation"]["notes_saved"][0]["evidence"],
                         "deliberation_event")
        self.assertEqual(turn["deliberation"]["note_replies"][0]["note_chars"], 44)
        failures = turn["deliberation"]["note_failures"]
        self.assertEqual(len(failures), 1)
        self.assertEqual(failures[0]["outcome"], "invalid:empty_note")
        self.assertEqual(failures[0]["failure_class"], "invalid_reply")
        self.assertEqual(turn["orders"]["invalid_replies"], 1)

    def test_storage_failure_is_a_tool_failure_not_an_invalid_reply(self):
        turn = self._write_turn([
            {"command": None, "parse_ok": False,
             "extra": {"deliberation_event": {"kind": "note", "outcome": "storage_failed"}}},
            {"command": {"type": "end_turn"}}])
        failure = turn["deliberation"]["note_failures"][0]
        self.assertEqual(failure["failure_class"], "storage")
        self.assertEqual(turn["deliberation"]["notes_saved_confirmed"], 0)
        self.assertEqual(turn["orders"]["invalid_replies"], 0)

    def test_legacy_storage_failure_without_sidecar_is_not_an_invalid_reply(self):
        turn = self._write_turn([
            {"command": None, "parse_ok": False, "result_error": "note_storage_failed"},
            {"command": {"type": "end_turn"}}])
        self.assertEqual(turn["deliberation"]["note_failures"], [])
        self.assertEqual(turn["orders"]["invalid_replies"], 0)

    def test_a_note_reply_without_confirmation_is_not_a_save(self):
        model = self.single_turn(command=None,
                                 raw_model_text=json.dumps({"note": "hold the river"}))
        turn = self.turn(model, SEAT_A, 1)
        self.assertEqual(len(turn["deliberation"]["note_replies"]), 1)
        self.assertEqual(turn["deliberation"]["notes_saved_confirmed"], 0)

    def test_stated_explanation_carries_reason_and_summary_but_no_reasoning(self):
        command = {"type": "end_turn", "reason": "REASON-TEXT"}
        raw = json.dumps({"type": "end_turn", "reason": "REASON-TEXT",
                          "summary": "SUMMARY-TEXT"})
        model = self.single_turn(command=command, raw_model_text=raw,
                                 extra_run={"reasoning_content": "COT-MARKER"})
        explanation = self.turn(model, SEAT_A, 1)["stated_explanations"][0]
        self.assertEqual(explanation["reason"], "REASON-TEXT")
        self.assertEqual(explanation["summary"], "SUMMARY-TEXT")
        self.assertNotIn("COT-MARKER", json.dumps(model))


class PerRequestResearchTests(AnalysisCase):
    def _option(self, line, name, price):
        return {"type": "research", "args": {"line": line}, "cost": 1,
                "label": "Research %s to level 1 for %d research points" % (name, price)}

    def _two_request_model(self):
        data = observation(SEAT_A, 1, research_points=40)
        first = prompt(SEAT_A, 1, observation_data=data, options=[
            self._option("agri", "Agriculture", 20),
            self._option("military", "Military", 20),
            {"type": "end_turn", "cost": 0}])
        second = prompt(SEAT_A, 1, observation_data=data, options=[
            self._option("governance", "Administration", 20),
            {"type": "end_turn", "cost": 0}])
        agri = {"type": "research", "target_id": None, "param_a": 0, "param_b": 0,
                "args": {"line": "agri"}}
        military = {"type": "research", "target_id": None, "param_a": 0, "param_b": 0,
                    "args": {"line": "military"}}
        self.workspace.write(
            [run_record(SEAT_A, 1, 1, 1, agri, observation_data=data),
             run_record(SEAT_A, 1, 2, 1, military, observation_data=data)],
            [request_exchange(SEAT_A, 1, 1, 1, first, system(research=True)),
             request_exchange(SEAT_A, 1, 2, 1, second, system(research=True))],
            [result_exchange(SEAT_A, 1, 1, 1, agri),
             result_exchange(SEAT_A, 1, 2, 1, military)])
        return self.workspace.analyse()

    def test_research_availability_is_recorded_per_request(self):
        choices = {choice["line"]: choice
                   for choice in self.turn(self._two_request_model(), SEAT_A, 1)
                   ["research"]["chosen"]}
        agri = choices["agri"]
        self.assertTrue(agri["rules_offered_in_request"])
        self.assertTrue(agri["line_offered_in_request"])
        self.assertEqual(agri["offered_lines_in_request"], ["agri", "military"])
        self.assertEqual(agri["command_point_cost"], 1)
        self.assertEqual(agri["research_points_price"], 20)
        military = choices["military"]
        self.assertTrue(military["rules_offered_in_request"])
        self.assertFalse(military["line_offered_in_request"])
        self.assertEqual(military["offered_lines_in_request"], ["governance"])
        self.assertIsNone(military["research_points_price"])

    def test_turn_level_available_lines_are_the_first_request_view(self):
        turn = self.turn(self._two_request_model(), SEAT_A, 1)
        self.assertEqual([item["line"] for item in turn["research"]["available_lines"]],
                         ["agri", "military"])
        self.assertEqual(turn["research"]["available_lines"][0]["research_points_price"], 20)

    def test_report_calls_the_numeric_cost_command_points_not_level_price(self):
        markdown = analysis.render_markdown(self._two_request_model())
        self.assertIn("command-point cost 1", markdown)
        self.assertIn("research points 20", markdown)
        self.assertNotIn("level price", markdown)

    def test_attack_entry_carries_acceptance_and_result(self):
        data = observation(SEAT_A, 1, armies=[
            {"id": "army-a-1", "owner_seat_id": SEAT_A, "troops": 2000,
             "unit_type": "infantry", "x": 4, "z": 4, "morale": 60},
            {"id": "army-enemy-1", "owner_seat_id": "seat-9", "troops": 500,
             "unit_type": "infantry", "x": 3, "z": 3, "morale": 40}])
        command = {"type": "attack_army", "target_id": "army-a-1", "param_a": 3, "param_b": 3}
        model = self.single_turn(command=command, observation_data=data)
        attack = self.turn(model, SEAT_A, 1)["attacks"][0]
        self.assertEqual(attack["request_index"], 1)
        self.assertTrue(attack["accepted"])
        self.assertIsNone(attack["result_error"])
        self.assertEqual(attack["visible_target"]["id"], "army-enemy-1")
        self.assertIn("run", attack["sources"])
