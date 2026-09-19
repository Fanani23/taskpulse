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

count() { curl -sf "$API/api/tasks?pageSize=1" | sed -E 's/.*"total":([0-9]+).*/\1/'; }

total=$(count)
if [[ "$total" != "0" && $FORCE -eq 0 ]]; then
  echo "seed: $API already has $total task(s); use --force to add the sample set anyway"
  exit 0
fi

json() {
  local s=${1//\\/\\\\}
  s=${s//\"/\\\"}
  printf '"%s"' "$s"
}

create() {
  local status=$1 title=$2 desc=$3
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

echo "seed: $API"
while IFS='|' read -r status title desc; do
  [[ -z "$status" ]] && continue
  create "$status" "$title" "$desc"
done <<'EOF'
Done|Set up the PostgreSQL cluster|Provision roles, move the cluster to 5433, verify peer auth over the Unix socket.
Done|Write the tasks REST API|Minimal API with paging, filtering, validation and RFC 9457 problem responses.
Done|Add optimistic concurrency|Use xmin as the concurrency token; return 409 with the current version on conflict.
Done|Harden the systemd units|Dedicated user, ProtectSystem=strict, no new privileges; systemd-analyze security below 2.
Done|Add correlation ids|Echo X-Correlation-Id on every response and include it in the log scope.
Done|Structured JSON logging|One JSON object per line so journalctl output pipes straight into jq.
Done|Validate options at startup|Bind TaskLimits and connection settings with ValidateOnStart so a bad config fails fast.
Done|OpenAPI document|Serve /openapi/v1.json with the problem+json responses described.
Done|Liveness and readiness endpoints|/health answers immediately; /health/ready checks the database.
Done|EF Core migrations on startup|Apply pending migrations before the first request is served.
Done|Move PostgreSQL to a Unix socket|Peer authentication means no password exists anywhere on the box.
Done|Indexes on status and created_at|The list endpoint sorts by created_at and filters by status.
Done|Return 404 as problem+json|Unknown ids produce the same problem shape as validation errors.
Done|Trim and normalise titles|Whitespace-only titles are rejected with a 400 that names the field.
Done|Delete returns 204|And a second delete of the same id returns 404, not 500.
Done|Integration tests on a real cluster|Each test class gets its own throw-away database on the local server.
InProgress|Implement WebSocket broadcast|Connection registry with a per-socket send lock; broadcast to every open socket.
InProgress|Graceful shutdown on SIGTERM|Send close code 1001 to every client before the host stops.
InProgress|Add readiness probe for the database|/health/ready should fail while migrations are pending.
InProgress|Frame reassembly with a size cap|Reassemble fragmented text frames up to 64 KiB, then close with 1009.
InProgress|Keep-alive pings|Ping every 30 seconds so idle sockets survive nginx and load balancers.
InProgress|Browser console client|A static page at / that shows every frame and lets the tester send echo, broadcast and ping.
InProgress|Per-connection statistics|/stats reports open sockets, frames in and out, and uptime.
InProgress|Paging metadata|Return page, pageSize and total alongside items.
InProgress|Filter tasks by status|GET /api/tasks?status=InProgress uses the status index.
InProgress|Reject unknown status values|A bad enum value is a 400 with the allowed values listed.
InProgress|nginx WebSocket upgrade|Map Upgrade and Connection headers so Kestrel sees a proper handshake.
InProgress|Docker compose with Postgres|One command brings up both services and a database for local review.
Todo|Publish Docker images|Multi-stage builds, non-root runtime user, compose file with a Postgres service.
Todo|Write the smoke test|Exercise the real path through nginx: REST, WebSocket upgrade, correlation id.
Todo|Document the things learned|Cancelled ReceiveAsync aborts the socket; peer auth needs a matching OS user; and more.
Todo|Add ETag support|Expose the row version as an ETag and honour If-Match on PUT.
Todo|Rate limit the write endpoints|A small fixed-window limiter per client address on POST, PUT and DELETE.
Todo|Task due dates|Optional dueAt with a filter for overdue tasks.
Todo|Bulk status update|PATCH /api/tasks with a list of ids and a target status.
Todo|Soft delete with restore|Keep deleted rows for seven days and add a restore endpoint.
Todo|Metrics endpoint|Expose request counts and durations for Prometheus.
Todo|Client reconnect guidance|Document exponential backoff for WebSocket clients after a 1001 close.
Todo|Load test the broadcast path|Measure fan-out latency with 500 idle sockets and one publisher.
Todo|Archive completed tasks|A nightly job that moves Done tasks older than 30 days to an archive table.
EOF

echo "seed: done, $(count) task(s) in $API"
