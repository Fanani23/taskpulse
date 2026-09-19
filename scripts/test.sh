#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

if command -v pg_lsclusters >/dev/null; then
  PG_PORT="$(pg_lsclusters -h | awk 'NR==1{print $3}')"
  export TASKPULSE_TEST_PG="${TASKPULSE_TEST_PG:-Host=localhost;Port=$PG_PORT;Database=postgres;Username=taskpulse_dev;Password=taskpulse_dev}"
fi
echo "tests use: ${TASKPULSE_TEST_PG:-<default: localhost:5432 as taskpulse_dev>}"
exec dotnet test "$ROOT/TaskPulse.sln" -c Release "$@"
