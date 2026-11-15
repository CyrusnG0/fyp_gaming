#!/usr/bin/env python3
"""Offline evidence analysis for a LuanShi LLM experiment run.

Rebuilds what each faction actually did from the run JSONL, the request/result exchange sidecars
and the spectator summary, then writes machine-readable JSON and a reproducible Markdown report.
No network, no provider call, no credential or private reasoning in output.

Usage:
  python tools/experiment_context_analysis.py \\
      --run runs/run-<id>.jsonl \\
      --exchanges runs/run-<id>-exchanges.ndjson \\
      --summary runs/pilot-run-<id>-summary.json \\
      --out-json runs/experiment-analysis-<id>/analysis.json \\
      --out-md  runs/experiment-analysis-<id>/REPORT.md

Missing evidence is reported as unknown with known/missing counts; it is never counted as zero.
"""
import sys

# Contract F10: the analysis tools do not leave bytecode caches behind (see also the test wrapper).
sys.dont_write_bytecode = True

import argparse  # noqa: E402
import json  # noqa: E402
import os  # noqa: E402

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from experiment_context_constants import TOOL_VERSION  # noqa: E402
from experiment_context_experiment import load_experiment  # noqa: E402
from experiment_context_geometry import geometry_for  # noqa: E402
from experiment_context_markdown import render_markdown  # noqa: E402
from experiment_context_run import analyse_run  # noqa: E402


def _repo_root():
    return os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def build(run_path, exchange_path=None, summary_path=None, map_path=None, label=None,
          auto_map=True):
    """(experiment, model) for one run. Shared by the CLI and the focused tests."""
    from pathlib import Path
    repo_root = Path(_repo_root())
    experiment, sources = load_experiment(
        Path(run_path), Path(exchange_path) if exchange_path else None,
        Path(summary_path) if summary_path else None, label, repo_root)
    geometry = geometry_for(experiment, Path(map_path) if map_path else None, repo_root,
                            auto_map=auto_map)
    return experiment, analyse_run(experiment, geometry, sources, label)


def main(argv=None):
    parser = argparse.ArgumentParser(
        description="Reconstruct and classify a stored LuanShi LLM experiment run offline.")
    parser.add_argument("--run", required=True, help="run JSONL (one record per request)")
    parser.add_argument("--exchanges", help="request/result exchange NDJSON sidecar")
    parser.add_argument("--summary", help="spectator summary JSON")
    parser.add_argument("--map", help="exported demo map JSON used for scenario geometry")
    parser.add_argument("--label", help="label used in source citations")
    parser.add_argument("--out-json", required=True)
    parser.add_argument("--out-md", required=True)
    args = parser.parse_args(argv)

    _, model = build(args.run, args.exchanges, args.summary, args.map, args.label)
    os.makedirs(os.path.dirname(os.path.abspath(args.out_json)), exist_ok=True)
    os.makedirs(os.path.dirname(os.path.abspath(args.out_md)), exist_ok=True)
    with open(args.out_json, "w", encoding="utf-8", newline="\n") as stream:
        json.dump(model, stream, indent=2, ensure_ascii=False)
        stream.write("\n")
    with open(args.out_md, "w", encoding="utf-8", newline="\n") as stream:
        stream.write(render_markdown(model))

    counts = model["join"]
    print("run ids: %s" % (", ".join(model["run_ids"]) or "unknown"))
    print("joined requests: %d (usable %d, degraded %d, excluded %d)"
          % (counts["joined_requests"], counts["usable_requests"],
             counts["degraded_requests"], counts["excluded_requests"]))
    print("diagnostics: %s" % json.dumps(model["diagnostics"]["counts"]))
    print("tool version: %s; schema: %s" % (TOOL_VERSION, model["schema"]))
    print("wrote %s and %s" % (args.out_json, args.out_md))
    return 0


if __name__ == "__main__":
    sys.exit(main())
