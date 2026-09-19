#!/usr/bin/env bash
# Runs the .NET test suite with coverage, merges the results, and enforces the
# thresholds documented in docs/development.md. AdyenOutLoud.Core carries the
# project's pure business logic (parsing, correlation, localization, dedupe),
# so it is held to the "critical logic" bar; the MAUI head project's platform
# glue is exercised by architecture tests and manual/UI-smoke checks instead
# (see docs/development.md for why it is not line-coverage gated).
set -euo pipefail
cd "$(dirname "$0")/.."

MIN_LINE=90
MIN_BRANCH=85
RESULTS_DIR="artifacts/coverage/dotnet"

rm -rf "$RESULTS_DIR"
mkdir -p "$RESULTS_DIR"

dotnet tool restore

dotnet test app/AdyenOutLoud.Tests/AdyenOutLoud.Tests.csproj \
  --results-directory "$RESULTS_DIR" \
  --collect:"XPlat Code Coverage" \
  -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=cobertura

dotnet tool run reportgenerator \
  -reports:"$RESULTS_DIR/**/coverage.cobertura.xml" \
  -targetdir:"$RESULTS_DIR/report" \
  -reporttypes:"TextSummary;Cobertura;Html" \
  -classfilters:"+AdyenOutLoud.*" \
  -assemblyfilters:"+AdyenOutLoud.Core"

SUMMARY="$RESULTS_DIR/report/Summary.txt"
cat "$SUMMARY"

extract() {
  grep "$1" "$SUMMARY" | grep -oE '[0-9]+(\.[0-9]+)?' | head -1
}

LINE_COVERAGE=$(extract "Line coverage:")
BRANCH_COVERAGE=$(extract "Branch coverage:")

echo
echo "AdyenOutLoud.Core line coverage:   ${LINE_COVERAGE}% (floor: ${MIN_LINE}%)"
echo "AdyenOutLoud.Core branch coverage: ${BRANCH_COVERAGE}% (floor: ${MIN_BRANCH}%)"

FAILED=0
if awk -v v="$LINE_COVERAGE" -v min="$MIN_LINE" 'BEGIN { exit !(v+0 < min+0) }'; then
  echo "::error::AdyenOutLoud.Core line coverage ${LINE_COVERAGE}% is below the ${MIN_LINE}% floor."
  FAILED=1
fi
if awk -v v="$BRANCH_COVERAGE" -v min="$MIN_BRANCH" 'BEGIN { exit !(v+0 < min+0) }'; then
  echo "::error::AdyenOutLoud.Core branch coverage ${BRANCH_COVERAGE}% is below the ${MIN_BRANCH}% floor."
  FAILED=1
fi

exit $FAILED
