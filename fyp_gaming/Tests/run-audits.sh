#!/usr/bin/env bash
# Portable runner for the recovered C# static audits.
#
# Default targets are the Unity UI scripts, which are the files these guards were written for (the
# adapter that calls into the engine). Pass explicit .cs paths to audit something else.
#
# Usage: Tests/run-audits.sh [file.cs ...]
# Exit code: 0 when every audit is clean, 1 when an audit reports a problem, 2 on a setup problem.
set -uo pipefail

here=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
audits="$here/audits"
ui="$here/../Assets/GameUI/Scripts"

PY=${PYTHON:-}
if [ -z "$PY" ]; then
  for candidate in python3 python py; do
    if command -v "$candidate" >/dev/null 2>&1; then PY=$candidate; break; fi
  done
fi
if [ -z "$PY" ]; then
  echo "error: no Python 3 interpreter found (set PYTHON=/path/to/python)" >&2
  exit 2
fi

if [ "$#" -gt 0 ]; then
  targets=("$@")
else
  if [ ! -d "$ui" ]; then
    echo "error: UI scripts not found at $ui" >&2
    exit 2
  fi
  targets=("$ui"/*.cs)
fi

export PYTHONIOENCODING=utf-8
status=0

echo "namespace_guard.py (attribute / namespace resolution)"
"$PY" "$audits/namespace_guard.py" "${targets[@]}" || status=1
echo
echo "arity_audit.py (local method call sites)"
"$PY" "$audits/arity_audit.py" "${targets[@]}" || status=1
echo
echo "external_audit.py (calls into the engine vs the recorded signatures)"
"$PY" "$audits/external_audit.py" "${targets[@]}" || status=1
echo
echo "report_state_audit.py (court report reads only the observation)"
"$PY" "$audits/report_state_audit.py" "$ui/KingdomDemo.Report.cs" || status=1
echo
echo "canonical_log_audit.py (player-facing UI never touches the canonical log)"
"$PY" "$audits/canonical_log_audit.py" "$ui" || status=1
echo
echo "canonical_log_audit_selftest.py (spacing/alias bypasses are rejected)"
"$PY" "$audits/canonical_log_audit_selftest.py" || status=1

exit $status
