#!/usr/bin/env python3
"""Unittest discovery entry point for the experiment-analysis tools.

Run: python -m unittest discover -s tools -p "test_experiment_context_analysis.py" -v

The tests themselves live in focused ``experiment_context_tests_*.py`` modules so no single test
module grows into a monolith. This wrapper re-exports them for discovery; it also disables
bytecode caching so importing the analysis modules leaves no ``__pycache__`` behind.
"""
import sys

sys.dont_write_bytecode = True

import unittest  # noqa: E402

from experiment_context_tests_deliberation import (  # noqa: F401,E402
    DeliberationTests, PerRequestResearchTests, ReconstructedDeliberationTests)
from experiment_context_tests_join import (  # noqa: F401,E402
    BytecodeTests, JoinTests, MetricTests, PathTests, PromptTests)
from experiment_context_tests_march import (  # noqa: F401,E402
    MarchTests, ObservationAggregationTests)
from experiment_context_tests_redaction import (  # noqa: F401,E402
    EndToEndTests, RedactionTests)
from experiment_context_tests_summary import (  # noqa: F401,E402
    MultiRunSummaryTests, ScoreboardTests, SummaryTests, VerificationTests)

if __name__ == "__main__":
    unittest.main(verbosity=2)
