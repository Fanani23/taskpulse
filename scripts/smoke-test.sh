#!/usr/bin/env bash
set -euo pipefail

API="${1:-http://127.0.0.1:8088}"
WS="${2:-$(echo "$API" | sed -E 's#^http#ws#')}"
CID="smoke-$(date +%s)"
fail=0

pass() { printf '  \033[1;32mPASS\033[0m %s\n' "$*"; }
fail() { printf '  \033[1;31mFAIL\033[0m %s\n' "$*"; fail=1; }
check() {
  local desc=$1 want=$2; shift 2
  local got; got=$(curl -s -o /tmp/smoke-body -w '%{http_code}' -H "X-Correlation-Id: $CID" "$@")
  [[ "$got" == "$want" ]] && pass "$desc -> $got" || { fail "$desc -> $got (expected $want): $(head -c 200 /tmp/smoke-body)"; }
}

echo "REST API  $API"
check "GET  /health"               200 "$API/health"
check "GET  /health/ready"         200 "$API/health/ready"
check "GET  /openapi/v1.json"      200 "$API/openapi/v1.json"
check "GET  /api/tasks (empty ok)" 200 "$API/api/tasks"

check "POST /api/tasks (create)"   201 -X POST "$API/api/tasks" -H 'Content-Type: application/json' \
      -d '{"title":"Smoke test task","description":"created by smoke-test.sh"}'
ID=$(sed -E 's/.*"id":"([^"]+)".*/\1/' /tmp/smoke-body)
[[ ${#ID} -eq 36 ]] && pass "     returned id $ID" || fail "     no id in body"

check "GET  /api/tasks/{id}"       200 "$API/api/tasks/$ID"
check "GET  /api/tasks?status=Todo" 200 "$API/api/tasks?status=Todo"
check "PUT  /api/tasks/{id} (Done)" 200 -X PUT "$API/api/tasks/$ID" -H 'Content-Type: application/json' \
      -d '{"title":"Smoke test task","status":"Done"}'
grep -q '"status":"Done"' /tmp/smoke-body && pass "     status is Done" || fail "     status not updated"

check "POST /api/tasks (no title -> 400)" 400 -X POST "$API/api/tasks" -H 'Content-Type: application/json' -d '{"title":"  "}'
check "PUT  /api/tasks/{id} (bad enum -> 400)" 400 -X PUT "$API/api/tasks/$ID" -H 'Content-Type: application/json' -d '{"title":"x","status":"Nope"}'
check "DELETE /api/tasks/{id}"     204 -X DELETE "$API/api/tasks/$ID"
check "GET  /api/tasks/{id} (gone -> 404)" 404 "$API/api/tasks/$ID"

got=$(curl -s -D - -o /dev/null -H "X-Correlation-Id: $CID" "$API/health" | tr -d '\r' | grep -i '^x-correlation-id:' | awk '{print $2}')
[[ "$got" == "$CID" ]] && pass "X-Correlation-Id echoed ($CID)" || fail "X-Correlation-Id not echoed (got '$got')"

echo
echo "WebSocket $WS/ws"
if ! command -v node >/dev/null 2>&1 && [[ -s "$HOME/.nvm/nvm.sh" ]]; then . "$HOME/.nvm/nvm.sh"; fi
if command -v node >/dev/null 2>&1; then
  if node - "$WS/ws" <<'EOF'
const url = process.argv[2];
const ws = new WebSocket(url);
const expect = ["welcome", "echo", "pong"];
const timer = setTimeout(() => { console.error("  FAIL timeout waiting for", expect[0]); process.exit(1); }, 5000);
ws.onopen = () => { ws.send(JSON.stringify({ type: "echo", data: "smoke" })); ws.send(JSON.stringify({ type: "ping" })); };
ws.onmessage = e => {
  const m = JSON.parse(e.data);
  const want = expect.shift();
  if (m.type !== want) { console.error(`  FAIL expected ${want}, got ${e.data}`); process.exit(1); }
  console.log(`  PASS ${m.type.padEnd(8)} ${e.data}`);
  if (expect.length === 0) { clearTimeout(timer); ws.close(1000, "smoke done"); }
};
ws.onclose = e => { console.log(`  PASS closed code=${e.code} reason="${e.reason}"`); process.exit(expect.length === 0 ? 0 : 1); };
ws.onerror = () => { console.error("  FAIL socket error"); process.exit(1); };
EOF
  then :; else fail=1; fi
else
  echo "  SKIP node not found (needed for the WebSocket check)"
fi

echo
[[ $fail -eq 0 ]] && { printf '\033[1;32mALL CHECKS PASSED\033[0m\n'; } || { printf '\033[1;31mSOME CHECKS FAILED\033[0m\n'; exit 1; }
