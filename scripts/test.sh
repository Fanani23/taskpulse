#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

if command -v pg_lsclusters >/dev/null; then
  PG_PORT="$(pg_lsclusters -h | awk 'NR==1{print $3}')"
  export TASKPULSE_TEST_PG="${TASKPULSE_TEST_PG:-Host=localhost;Port=$PG_PORT;Database=postgres;Username=taskpulse_dev;Password=taskpulse_dev}"
fi
# The change-stream tests need a Redis; they are skipped when none answers.
if [[ -z "${TASKPULSE_TEST_REDIS:-}" ]] && command -v redis-cli >/dev/null && redis-cli -h 127.0.0.1 ping 2>/dev/null | grep -q PONG; then
  export TASKPULSE_TEST_REDIS="127.0.0.1:6379"
fi
echo "tests use: ${TASKPULSE_TEST_PG:-<default: localhost:5432 as taskpulse_dev>}; redis: ${TASKPULSE_TEST_REDIS:-<none, stream tests skipped>}"
if [[ "${1:-}" == "--coverage" ]]; then
  shift
  rm -rf "$ROOT/TestResults"
  dotnet test "$ROOT/TaskPulse.sln" -c Release --collect:"XPlat Code Coverage" --results-directory "$ROOT/TestResults" "$@" \
    -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=cobertura
  for f in "$ROOT"/TestResults/*/coverage.cobertura.xml; do
    printf '  %s  line-rate %s\n' "$(basename "$(dirname "$f")")" "$(grep -o 'line-rate="[0-9.]*"' "$f" | head -1 | cut -d'"' -f2)"
  done
  exit 0
fi
exec dotnet test "$ROOT/TaskPulse.sln" -c Release "$@"
