#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 ASPNETCORE_ENVIRONMENT=Development

if command -v pg_lsclusters >/dev/null; then
  PG_PORT="$(pg_lsclusters -h | awk 'NR==1{print $3}')"
  export ConnectionStrings__Tasks="${ConnectionStrings__Tasks:-Host=localhost;Port=$PG_PORT;Database=taskpulse_dev;Username=taskpulse_dev;Password=taskpulse_dev}"
fi

dotnet watch --project "$ROOT/src/TaskPulse.Api"  --non-interactive run &
dotnet watch --project "$ROOT/src/TaskPulse.Realtime" --non-interactive run &
trap 'kill 0' INT TERM
wait
