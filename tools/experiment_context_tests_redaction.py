"""Redaction, privacy and end-to-end determinism tests."""
import json

import experiment_context_analysis as analysis
from experiment_context_constants import REDACTED
from experiment_context_tests_support import (AnalysisCase, SEAT_A, observation, prompt,
                                              request_exchange, result_exchange, run_record,
                                              summary, system)


class RedactionTests(AnalysisCase):
    SECRET = "sk-live-ABCDEFGH1234567890"
    PRIVATE = "PRIVATE-MARKER-7f3a91"
    PROMPT_MARKER = "PROMPT-ONLY-MARKER-9911"

    def test_raw_body_reasoning_and_error_detail_never_reach_the_output(self):
        command = {"type": "end_turn", "reason": "api_key=%s" % self.SECRET}
        model = self.single_turn(
            command=command, system_text=system(marker=self.PROMPT_MARKER),
            extra_run={"error_detail": "provider said %s and %s" % (self.PRIVATE, self.SECRET),
                       "reasoning_content": self.PRIVATE,
                       "raw_body": self.PRIVATE})
        serialised = json.dumps(model, sort_keys=True, ensure_ascii=False)
        for absent in (self.SECRET, self.PRIVATE, self.PROMPT_MARKER, "raw_body",
                       "reasoning_content", "content_text", "error_detail"):
            self.assertNotIn(absent, serialised)
        self.assertIn(REDACTED, serialised)

    def test_private_summary_fields_are_not_emitted(self):
        data = summary(
            turns=[{"season": 1, "seat_id": SEAT_A, "requests": 1, "accepted": 1,
                    "last_summary": self.PRIVATE}],
            config_redacted="provider=fake; api_key=%s; marker=%s" % (self.SECRET, self.PRIVATE))
        model = self.single_turn(summary_data=data)
        serialised = json.dumps(model, sort_keys=True)
        self.assertNotIn(self.SECRET, serialised)
        self.assertNotIn(self.PRIVATE, serialised)
        self.assertNotIn("config_redacted", serialised)

    def test_report_contains_no_secrets(self):
        model = self.single_turn(
            command={"type": "end_turn", "reason": "token=%s" % self.SECRET})
        markdown = analysis.render_markdown(model)
        self.assertNotIn(self.SECRET, markdown)
        self.assertNotIn(self.PRIVATE, markdown)
        self.assertIn("Redaction", markdown)


class EndToEndTests(AnalysisCase):
    def test_two_runs_over_the_same_input_are_byte_identical(self):
        data = observation(SEAT_A, 1)
        command = {"type": "march", "target_id": "army-a-1", "param_a": 24, "param_b": 8}
        self.workspace.write(
            [run_record(SEAT_A, 1, 1, 1, command, observation_data=data,
                        scenario_id="compact-contact-v1")],
            [request_exchange(SEAT_A, 1, 1, 1, prompt(
                SEAT_A, 1, observation_data=data,
                movement={"army-a-1": {"position": (18, 8), "options": [(24, 8, 6)],
                                       "unlisted": 3}}))],
            [result_exchange(SEAT_A, 1, 1, 1, command)])
        first = self.workspace.analyse()
        second = self.workspace.analyse()
        self.assertEqual(json.dumps(first, sort_keys=True), json.dumps(second, sort_keys=True))
        self.assertEqual(analysis.render_markdown(first), analysis.render_markdown(second))
        self.assertEqual(first["tool"]["provider_calls"], 0)
        self.assertTrue(first["tool"]["offline"])

    def test_acceptance_comes_from_result_ok_not_from_a_permissive_prompt(self):
        data = observation(SEAT_A, 1)
        command = {"type": "levy_tax", "target_id": "city-a"}
        model = self.single_turn(command=command, observation_data=data, result_ok=False)
        turn = self.turn(model, SEAT_A, 1)
        self.assertEqual(turn["orders"]["accepted"], 0)
        self.assertEqual(turn["orders"]["rejected"], 1)
        self.assertEqual(turn["orders"]["rejected_commands"][0]["result_error"],
                         "action rejected")
