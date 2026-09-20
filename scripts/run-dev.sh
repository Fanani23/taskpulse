#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 ASPNETCORE_ENVIRONMENT=Development

if command -v pg_lsclusters >/dev/null; then
  PG_PORT="$(pg_lsclusters -h | awk 'NR==1{print $3}')"
  export ConnectionStrings__Tasks="${ConnectionStrings__Tasks:-Host=localhost;Port=$PG_PORT;Database=taskpulse_dev;Username=taskpulse_dev;Password=taskpulse_dev}"
fi
# Writes need the express JWT secret; read it from a sibling express-template checkout when present.
EXPRESS_ENV="${EXPRESS_ENV:-$ROOT/../express-template/apps/sample-api/.env}"
if [[ -z "${Api__JwtSecret:-}" && -f "$EXPRESS_ENV" ]]; then
  export Api__JwtSecret="$(sed -n 's/^JWT_SECRET=//p' "$EXPRESS_ENV" | head -1)"
fi
export Api__JwtSecret="${Api__JwtSecret:-dev-only-secret-change-me-please-32chars}"
export Api__RealtimeInternalUrl="${Api__RealtimeInternalUrl:-http://127.0.0.1:5090/internal/broadcast}"

dotnet watch --project "$ROOT/src/TaskPulse.Api"  --non-interactive run &
dotnet watch --project "$ROOT/src/TaskPulse.Realtime" --non-interactive run &
trap 'kill 0' INT TERM
wait
