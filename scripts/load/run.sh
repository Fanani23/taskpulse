#!/usr/bin/env bash
# k6 load run against a deployment: scripts/load/run.sh [http://host:8088]. Needs k6 (https://k6.io) and, for the write
# scenario, a token (TASKPULSE_TOKEN / TASKPULSE_JWT_SECRET / /etc/taskpulse/api.env - see scripts/token.sh).
set -euo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# Default: Kestrel directly. Through nginx a single address is capped at 30 r/s (limit_req) and the numbers would
# measure that cap, not the application; pass the :8088 URL on purpose to see the edge limiter answer 503.
BASE="${1:-http://127.0.0.1:5080}"
command -v k6 >/dev/null || { echo "k6 not found: https://grafana.com/docs/k6/latest/set-up/install-k6/" >&2; exit 2; }
TOKEN_SUB=load TOKEN_EMAIL=load@taskpulse.local; . "$HERE/../token.sh"
[[ -n "$TOKEN" ]] || echo "no token: running the read scenarios only" >&2
cd "$HERE/../.." && k6 run --quiet -e BASE="$BASE" -e TOKEN="$TOKEN" scripts/load/api.js
