#!/usr/bin/env bash
# Portable runner for the LuanShi engine assertion harness.
#
# Locates the Unity project from this script's own directory (no machine-specific paths) and uses
# whatever `dotnet` SDK is on PATH. The harness compiles Assets/GameEngine/*.cs directly, so it needs
# no UnityEngine assemblies and no NuGet package.
#
# Usage: Tests/run-tests.sh
# Exit code: 0 when every assertion passes, 1 when one fails, 2 on a setup problem.
set -uo pipefail

here=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
project="$here/Harness/EngineHarness.csproj"
engine="$here/../Assets/GameEngine"

if [ ! -f "$project" ]; then
  echo "error: harness project not found at $project" >&2
  exit 2
fi
if [ ! -d "$engine" ]; then
  echo "error: engine sources not found at $engine" >&2
  exit 2
fi
if ! command -v dotnet >/dev/null 2>&1; then
  echo "error: the .NET SDK ('dotnet') is not on PATH" >&2
  exit 2
fi

# Count the engine sources with a plain shell glob, so the script does not depend on GNU find options.
sources=0
for source in "$engine"/*.cs; do
  [ -f "$source" ] || continue
  sources=$((sources + 1))
done
echo "LuanShi engine assertion harness"
echo "  project: Tests/Harness/EngineHarness.csproj"
echo "  engine : Assets/GameEngine ($sources sources)"
echo "  suite  : recovered assertions first, then sections named [new]"
echo

dotnet build "$project" -c Release --nologo -v q || exit $?
dotnet run --project "$project" -c Release --no-build
