#!/usr/bin/env bash
# Restore drill against the compose stack (what CI runs): write a task, back the database up with pg_dump exactly the
# way backup.sh does, delete the task for good, restore the dump, and prove the task is back. A backup nobody has
# restored is a hope, not a backup.
#   scripts/restore-drill.sh            # expects `docker compose up -d` already running on :8088
set -euo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
API="${API:-http://127.0.0.1:8088}"
TOKEN_SUB=drill TOKEN_EMAIL=drill@taskpulse.local; . "$HERE/token.sh"
[[ -n "$TOKEN" ]] || { echo "restore-drill: no token (TASKPULSE_JWT_SECRET)" >&2; exit 1; }
PG="docker compose exec -T postgres"
DB=taskpulse; USER=taskpulse
DUMP="$(mktemp -d)/drill.dump"

title="drill $(date +%s)"
id=$(curl -sf -X POST "$API/api/tasks" "${AUTH[@]}" -H 'Content-Type: application/json' -d "{\"title\":\"$title\"}" | sed -E 's/.*"id":"([^"]+)".*/\1/')
echo "1. created task $id"

$PG pg_dump -U "$USER" -Fc "$DB" > "$DUMP"
echo "2. dumped the database ($(du -h "$DUMP" | cut -f1)) - same command as backup.sh"

curl -sf -o /dev/null -X DELETE "$API/api/tasks/$id?permanent=true" "${AUTH[@]}"
code=$(curl -s -o /dev/null -w '%{http_code}' "$API/api/tasks/$id?includeDeleted=true")
[[ "$code" == 404 ]] || { echo "expected the task to be gone (404), got $code" >&2; exit 1; }
echo "3. purged it: GET -> $code"

# restore.sh's recipe: the API paused, --clean --if-exists --no-owner into the live database
docker compose stop api >/dev/null
$PG pg_restore -U "$USER" -d "$DB" --clean --if-exists --no-owner < "$DUMP"
docker compose start api >/dev/null
for i in $(seq 1 30); do curl -fs "$API/health/ready" >/dev/null 2>&1 && break; sleep 2; done
echo "4. restored the dump and started the API again"

back=$(curl -sf "$API/api/tasks/$id" | sed -E 's/.*"title":"([^"]+)".*/\1/')
[[ "$back" == "$title" ]] || { echo "restore failed: task not back (got '$back')" >&2; exit 1; }
echo "5. the task is back: \"$back\""
curl -sf -o /dev/null -X DELETE "$API/api/tasks/$id?permanent=true" "${AUTH[@]}"
rm -f "$DUMP"
echo "RESTORE DRILL PASSED"
