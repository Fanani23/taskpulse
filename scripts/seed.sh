#!/usr/bin/env bash
set -euo pipefail

API="${API:-http://127.0.0.1:8088}"
FORCE=0
for a in "$@"; do
  case "$a" in
    --force) FORCE=1 ;;
    http*)   API="$a" ;;
    *) echo "usage: $0 [--force] [http://host:port]" >&2; exit 2 ;;
  esac
done
API="${API%/}"

total=$(curl -sf "$API/api/tasks?pageSize=1" | sed -E 's/.*"total":([0-9]+).*/\1/')
if [[ "$total" != "0" && $FORCE -eq 0 ]]; then
  echo "seed: $API already has $total task(s); use --force to add the sample set anyway"
  exit 0
fi

create() {
  local title=$1 desc=$2 status=$3
  local body id
  body=$(curl -sf -X POST "$API/api/tasks" -H 'Content-Type: application/json' -H 'X-Correlation-Id: seed' \
         --data "$(printf '{"title":%s,"description":%s}' "$(json "$title")" "$(json "$desc")")")
  id=$(sed -E 's/.*"id":"([^"]+)".*/\1/' <<<"$body")
  if [[ "$status" != "Todo" ]]; then
    curl -sf -o /dev/null -X PUT "$API/api/tasks/$id" -H 'Content-Type: application/json' -H 'X-Correlation-Id: seed' \
         --data "$(printf '{"title":%s,"description":%s,"status":"%s"}' "$(json "$title")" "$(json "$desc")" "$status")"
  fi
  printf '  %-10s %s\n' "$status" "$title"
}

json() {
  local s=${1//\\/\\\\}
  s=${s//\"/\\\"}
  printf '"%s"' "$s"
}

echo "seed: $API"
create "Set up the PostgreSQL cluster"            "Provision roles, move the cluster to 5433, verify peer auth over the Unix socket."   Done
create "Write the tasks REST API"                 "Minimal API with paging, filtering, validation and RFC 9457 problem responses."      Done
create "Add optimistic concurrency"               "Use xmin as the concurrency token; return 409 with the current version on conflict." Done
create "Harden the systemd units"                 "Dedicated user, ProtectSystem=strict, no new privileges; target systemd-analyze security below 2." Done
create "Implement WebSocket broadcast"            "Connection registry with a per-socket send lock; broadcast to every open socket."    InProgress
create "Graceful shutdown on SIGTERM"             "Send close code 1001 to every client before the host stops."                        InProgress
create "Add readiness probe for the database"     "/health/ready should fail while migrations are pending."                            InProgress
create "Publish Docker images"                    "Multi-stage builds, non-root runtime user, compose file with a Postgres service."    Todo
create "Write the smoke test"                     "Exercise the real path through nginx: REST, WebSocket upgrade, correlation id."      Todo
create "Document the things learned"              "Cancelled ReceiveAsync aborts the socket; peer auth needs matching OS user; and more." Todo

total=$(curl -sf "$API/api/tasks?pageSize=1" | sed -E 's/.*"total":([0-9]+).*/\1/')
echo "seed: done, $total task(s) in $API"
