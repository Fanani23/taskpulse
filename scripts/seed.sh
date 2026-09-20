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
# Writes need a bearer token (see token.sh: TASKPULSE_TOKEN, TASKPULSE_JWT_SECRET or /etc/taskpulse/api.env).
TOKEN_SUB=seed TOKEN_EMAIL=seed@taskpulse.local; . "$(dirname "${BASH_SOURCE[0]}")/token.sh"
[[ -n "$TOKEN" ]] || { echo "seed: no token - set TASKPULSE_TOKEN or TASKPULSE_JWT_SECRET (or run with sudo on the box)" >&2; exit 1; }

count() { curl -sf "$API/api/tasks?pageSize=1" | sed -E 's/.*"total":([0-9]+).*/\1/'; }

json() {
  local s=${1//\/\\}
  s=${s//\"/\\\"}
  printf '"%s"' "$s"
}

create() {
  local status=$1 title=$2 desc=$3
  local body id
  body=$(curl -sf -X POST "$API/api/tasks" -H 'Content-Type: application/json' -H 'X-Correlation-Id: seed' "${AUTH[@]}" \
         --data "$(printf '{"title":%s,"description":%s}' "$(json "$title")" "$(json "$desc")")")
  id=$(sed -E 's/.*"id":"([^"]+)".*/\1/' <<<"$body")
  if [[ "$status" != "Todo" ]]; then
    curl -sf -o /dev/null -X PUT "$API/api/tasks/$id" -H 'Content-Type: application/json' -H 'X-Correlation-Id: seed' "${AUTH[@]}" \
         --data "$(printf '{"title":%s,"description":%s,"status":"%s"}' "$(json "$title")" "$(json "$desc")" "$status")"
  fi
  printf '  %-10s %s\n' "$status" "$title"
}

# catalog rows: code|label|parent codes (comma separated)|attributes JSON object|sort
seed_catalog() {
  local kind=$1 rows=$2 n=0 existing parents_json
  existing=$(curl -sf "$API/api/catalog/$kind" | grep -o '"code"' | wc -l || true)
  if [[ "$existing" != "0" && $FORCE -eq 0 ]]; then
    printf '  catalog/%-10s already has %s item(s), skipped\n' "$kind" "$existing"; return
  fi
  while IFS='|' read -r code label parents attributes sort; do
    [[ -z "$code" ]] && continue
    parents_json=$(printf '%s' "$parents" | sed -E 's/[^,]+/"&"/g')
    curl -sf -o /dev/null -X POST "$API/api/catalog/$kind" -H 'Content-Type: application/json' -H 'X-Correlation-Id: seed' "${AUTH[@]}" \
         --data "{\"code\":\"$code\",\"label\":$(json "$label"),\"parents\":[$parents_json],\"attributes\":${attributes:-null},\"sort\":${sort:-0}}" \
      || { echo "  catalog/$kind/$code failed" >&2; continue; }
    n=$((n + 1))
  done <<<"$rows"
  printf '  catalog/%-10s %s item(s)\n' "$kind" "$n"
}

echo "seed: $API"
total=$(count)
if [[ "$total" != "0" && $FORCE -eq 0 ]]; then
  echo "  tasks: already has $total, skipped (use --force to add the sample set anyway)"
else
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
echo "  tasks: $(count) created"
fi

seed_catalog regions 'asia|Asia||{"side":"east"}|1
europe|Europe||{"side":"east"}|2
na|North America||{"side":"west"}|3
sa|South America||{"side":"west"}|4
africa|Africa||{"side":"east"}|5
me|Middle East||{"side":"east"}|6'

seed_catalog countries 'russia|Russia|asia,europe||
japan|Japan|asia||
burma|Burma|asia||
indonesia|Indonesia|asia||
afghanistan|Afghanistan|asia,me||
germany|Germany|europe||
france|France|europe||
poland|Poland|europe||
sweden|Sweden|europe||
italy|Italy|europe||
united-states|United States|na||
canada|Canada|na||
brazil|Brazil|sa||
argentina|Argentina|sa||
ecuador|Ecuador|sa||
egypt|Egypt|africa,me||
nigeria|Nigeria|africa||
kenya|Kenya|africa||
liberia|Liberia|africa||
saudi-arabia|Saudi Arabia|me||'

seed_catalog states 'california|California|united-states||
new-york|New York|united-states||
ohio|Ohio|united-states||
utah|Utah|united-states||
texas|Texas|united-states||
ontario|Ontario|canada||
quebec|Quebec|canada||
bc|BC|canada||
alberta|Alberta|canada||
b1|B1|brazil||
b2|B2|brazil||
a1|A1|argentina||
a2|A2|argentina||
a3|A3|argentina||
ec1|EC1|ecuador||
ec2|EC2|ecuador||'

seed_catalog force 'aa1|aa1|||1
aa22|aa22|||2
aa23|aa23|||3
aa4|aa4|||4
aa5|aa5|||5
bb1|bb1|||6
bb22|bb22|||7
bb23|bb23|||8
bb4|bb4|||9
bb5|bb5|||10'

seed_catalog places 'this-application|This application||{"lat":-6.2088,"lng":106.8456,"detail":"Nevacloud VPS, Jakarta - Vue + Express and TaskPulse behind nginx"}|1
malang|Malang||{"lat":-7.9666,"lng":112.6326,"detail":"Where this submission was written"}|2
singapore|Singapore||{"lat":1.3521,"lng":103.8198,"detail":"Nearest large cloud region (ap-southeast-1)"}|3
surabaya|Surabaya||{"lat":-7.2575,"lng":112.7521,"detail":"East Java capital"}|4'

seed_catalog links 'taskpulse-openapi|TaskPulse OpenAPI||{"url":"/openapi/v1.json"}|1
express-template|es-labs/express-template||{"url":"https://github.com/es-labs/express-template"}|2
vue-antd-template|es-labs/vue-antd-template||{"url":"https://github.com/es-labs/vue-antd-template"}|3
ant-design-vue|Ant Design Vue docs||{"url":"https://antdv.com/components/overview"}|4'

seed_catalog types 'online|Online|||1
promotion|Promotion|||2
offline|Offline|||3'

seed_catalog resources 'sponsor|Sponsor|||1
venue|Venue|||2'

seed_catalog tags 'content-1|content 1||{"selected":true}|1
content-2|content 2||{"selected":false}|2
content-3|content 3||{"selected":true}|3
content-4|content 4||{"selected":false}|4
content-5|content 5||{"selected":false}|5
content-6|content 6||{"selected":true}|6
content-7|content 7||{"selected":false}|7
content-8|content 8||{"selected":false}|8
content-9|content 9||{"selected":true}|9
content-10|content 10||{"selected":false}|10'

echo "seed: done, $(count) task(s) in $API"
