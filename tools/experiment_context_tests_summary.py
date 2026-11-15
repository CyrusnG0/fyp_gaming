"""Summary reconciliation, season-end scoreboard, fidelity and multi-run scope tests."""
import json

from experiment_context_tests_support import (AnalysisCase, RUN_ID, SEAT_A, observation, prompt,
                                              request_exchange, result_exchange, run_record,
                                              summary)


class SummaryTests(AnalysisCase):
    def _turn_with_march_then_train(self, summary_turn=None):
        data = observation(SEAT_A, 1)
        march = {"type": "march", "target_id": "army-a-1", "param_a": 24, "param_b": 8,
                 "action_id": "a-1"}
        train = {"type": "train", "target_id": "city-a", "action_id": "a-2"}
        summary_data = summary(turns=[summary_turn]) if summary_turn else None
        self.workspace.write(
            [run_record(SEAT_A, 1, 1, 1, march, observation_data=data),
             run_record(SEAT_A, 1, 2, 1, train, observation_data=data)],
            [request_exchange(SEAT_A, 1, 1, 1, prompt(
                SEAT_A, 1, observation_data=data,
                movement={"army-a-1": {"position": (18, 8), "options": [(24, 8, 6)],
                                       "unlisted": 0}})),
             request_exchange(SEAT_A, 1, 2, 1, prompt(SEAT_A, 1, observation_data=data))],
            [result_exchange(SEAT_A, 1, 1, 1, march),
             result_exchange(SEAT_A, 1, 2, 1, train)],
            summary_data=summary_data)
        return self.workspace.analyse()

    def test_last_command_fidelity_matches_the_turn_last_accepted_order(self):
        model = self._turn_with_march_then_train({
            "season": 1, "seat_id": SEAT_A, "requests": 2, "accepted": 2,
            "last_command_json": json.dumps({"type": "train", "action_id": "a-2"})})
        turn = self.turn(model, SEAT_A, 1)
        self.assertEqual(turn["summary_last_command_fidelity"]["result"], "matches")
        self.assertEqual(turn["orders"]["accepted_by_type"]["march"], 1)
        verification = model["verification"]
        self.assertEqual(verification["accepted_marches_not_the_summary_last_command"], 1)
        self.assertEqual(verification["accepted_marches_total"], 1)
        self.assertEqual(verification["summary_last_command_fidelity_counts"], {"matches": 1})
        self.assertNotIn("undisclosed_accepted_types", json.dumps(model))

    def test_last_command_fidelity_differs_when_the_summary_rewrites_it(self):
        model = self._turn_with_march_then_train({
            "season": 1, "seat_id": SEAT_A, "requests": 2, "accepted": 2,
            "last_command_json": json.dumps({"type": "reclaim", "action_id": "a-2"})})
        fidelity = self.turn(model, SEAT_A, 1)["summary_last_command_fidelity"]
        self.assertEqual(fidelity["result"], "differs")
        self.assertEqual(fidelity["summary_type"], "reclaim")
        self.assertEqual(fidelity["raw_last_accepted_type"], "train")

    def test_absent_summary_last_command_is_unknown_not_vacuously_true(self):
        model = self.single_turn(summary_data=summary(
            turns=[{"season": 1, "seat_id": SEAT_A, "requests": 1, "accepted": 1,
                    "ended_by": "limit: action limit reached"}]))
        fidelity = self.turn(model, SEAT_A, 1)["summary_last_command_fidelity"]
        self.assertEqual(fidelity["result"], "unknown")
        self.assertEqual(fidelity["summary_status"], "absent")
        verification = model["verification"]
        self.assertEqual(verification["summary_last_command_fidelity_counts"], {"unknown": 1})
        self.assertEqual(verification["turns_with_unknown_summary_last_command"], 1)
        self.assertEqual(verification["accepted_marches_not_the_summary_last_command"], 0)
        self.assertNotIn("summary_last_command_hides_marches", verification)

    def test_summary_count_discrepancy_is_reported(self):
        model = self.single_turn(summary_data=summary(
            totals={"requests": 9, "accepted": 7, "rejected": 0}))
        fields = {item["field"] for item in model["reconciliation"]["discrepancies"]}
        self.assertEqual(fields, {"requests", "accepted"})

    def test_missing_summary_totals_are_not_compared(self):
        model = self.single_turn(summary_data=summary(totals={}))
        self.assertFalse(model["reconciliation"]["totals_compared"])
        self.assertIsNone(model["reconciliation"]["summary_declared_totals"])
        self.assertEqual(model["reconciliation"]["discrepancies"], [])

    def test_duplicate_summary_turn_is_diagnosed(self):
        turn = {"season": 1, "seat_id": SEAT_A, "requests": 1, "accepted": 1}
        model = self.single_turn(summary_data=summary(turns=[turn, dict(turn)]))
        self.assertEqual(len(self.diagnostics(model, "duplicate_summary_turn")), 1)
        self.assertIsNone(self.turn(model, SEAT_A, 1)["summary_cross_check"])

    def test_summary_from_another_run_is_not_joined(self):
        foreign = summary(run_id="run-someone-else",
                          turns=[{"season": 1, "seat_id": SEAT_A, "requests": 99,
                                  "accepted": 99,
                                  "last_command_json": json.dumps({"type": "march"})}])
        model = self.single_turn(summary_data=foreign)
        self.assertEqual(len(self.diagnostics(model, "summary_run_id_mismatch")), 1)
        self.assertFalse(model["reconciliation"]["summary_present"])
        self.assertIsNone(self.turn(model, SEAT_A, 1)["summary_cross_check"])
        self.assertEqual(self.turn(model, SEAT_A, 1)["turn_end"]["reason"], "unknown")


class ScoreboardTests(AnalysisCase):
    def _scoreboard_summary(self, food=4200, gold=1500, cities=2):
        return summary(
            turns=[{"season": 1, "seat_id": SEAT_A, "requests": 1, "accepted": 1,
                    "last_command_json": json.dumps({"type": "end_turn"})}],
            seasons=[{"season": 1, "scoreboard": [
                {"seat_id": SEAT_A, "faction": "Wei", "food": food, "gold": gold,
                 "cities": cities, "population": 21000, "garrison": 700}]}])

    def test_season_end_scoreboard_is_reported_not_substituted(self):
        model = self.single_turn(summary_data=self._scoreboard_summary(food=4200, gold=1500))
        resources = self.turn(model, SEAT_A, 1)["resources"]
        self.assertEqual(resources["last_pre_order_food_total"]["value"], 3000)
        scoreboard = resources["summary_scoreboard"]
        self.assertEqual(scoreboard["status"], "ok")
        self.assertEqual(scoreboard["seat"]["food"], 4200)
        self.assertEqual(scoreboard["seat"]["gold"], 1500)
        self.assertEqual(scoreboard["seat"]["cities"], 2)
        self.assertIn("season 1", scoreboard["citation"])
        self.assertIn("pre-order", scoreboard["distinct_from"])

    def test_scoreboard_unknown_without_a_summary(self):
        scoreboard = self.turn(self.single_turn(), SEAT_A, 1)["resources"][
            "summary_scoreboard"]
        self.assertEqual(scoreboard["status"], "unknown")
        self.assertEqual(scoreboard["reason"], "no_summary")
        self.assertIsNone(scoreboard["seat"])

    def test_scoreboard_unknown_when_the_seat_is_missing_from_it(self):
        model = self.single_turn(summary_data=summary(seasons=[
            {"season": 1, "scoreboard": [{"seat_id": "seat-other", "food": 1, "gold": 1,
                                          "cities": 1}]}]))
        scoreboard = self.turn(model, SEAT_A, 1)["resources"]["summary_scoreboard"]
        self.assertEqual(scoreboard["status"], "unknown")
        self.assertEqual(scoreboard["reason"], "seat_not_in_scoreboard")

    def test_duplicate_summary_season_is_diagnosed_and_unknown(self):
        season = {"season": 1, "scoreboard": [{"seat_id": SEAT_A, "food": 1, "gold": 1,
                                               "cities": 1}]}
        model = self.single_turn(summary_data=summary(seasons=[season, dict(season)]))
        self.assertEqual(len(self.diagnostics(model, "duplicate_summary_season")), 1)
        scoreboard = self.turn(model, SEAT_A, 1)["resources"]["summary_scoreboard"]
        self.assertEqual(scoreboard["status"], "unknown")
        self.assertEqual(scoreboard["reason"], "duplicate_summary_season")


class VerificationTests(AnalysisCase):
    def test_missing_shu_faction_reports_unknown_not_zero(self):
        model = self.single_turn()
        verification = model["verification"]
        self.assertFalse(verification["shu_faction_present"])
        self.assertIsNone(verification["shu_accepted_marches"])
        self.assertIsNone(verification["shu_march_count_all"])
        self.assertIsNone(verification["first_shu_marches"])
        self.assertIsNone(verification["first_shu_request_options"])
        report = __import__("experiment_context_markdown", fromlist=["render_markdown"]) \
            .render_markdown(model)
        self.assertIn("No Shu faction appears in this run", report)


class MultiRunSummaryTests(AnalysisCase):
    def test_summary_covering_several_runs_is_ambiguous_not_guessed(self):
        data = summary(
            turns=[{"season": 1, "seat_id": SEAT_A, "requests": 1, "accepted": 1,
                    "last_command_json": json.dumps({"type": "end_turn"})}],
            totals={"requests": 11, "accepted": 11, "rejected": 0},
            seasons=[{"season": 1, "scoreboard": [{"seat_id": SEAT_A, "food": 1234,
                                                   "gold": 99, "cities": 2}]}])
        data["run_ids"] = [RUN_ID, "run-second-0002"]
        model = self.single_turn(summary_data=data)
        self.assertEqual(len(self.diagnostics(model, "summary_scope_ambiguous")), 1)
        self.assertTrue(model["join"]["summary_scope_ambiguous"])
        self.assertFalse(model["reconciliation"]["totals_compared"])
        self.assertIsNone(model["reconciliation"]["summary_declared_totals"])
        self.assertEqual(model["reconciliation"]["discrepancies"], [])
        turn = self.turn(model, SEAT_A, 1)
        self.assertEqual(turn["resources"]["summary_scoreboard"]["status"], "unknown")
        self.assertEqual(turn["resources"]["summary_scoreboard"]["reason"],
                         "ambiguous_multi_run_summary")
        self.assertEqual(turn["turn_end"]["reason"], "unknown")
        self.assertIsNone(turn["summary_cross_check"])
        self.assertTrue(any("several run ids" in gap for gap in model["evidence_gaps"]))
