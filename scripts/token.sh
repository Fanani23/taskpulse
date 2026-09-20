#!/usr/bin/env bash
# Sourced by seed.sh and smoke-test.sh: sets TOKEN (and AUTH, a curl header array) for the API's protected writes.
# Order: TASKPULSE_TOKEN as given · else sign one (HS256, Admin, 10 min) with TASKPULSE_JWT_SECRET, or the secret
# read from /etc/taskpulse/api.env (directly, or through sudo -n). Empty when none of these is available.
b64url() { openssl base64 -A | tr '+/' '-_' | tr -d '='; }
mint_token() {
  local secret=$1 now header payload
  now=$(date +%s)
  header=$(printf '{"alg":"HS256","typ":"JWT"}' | b64url)
  payload=$(printf '{"sub":"%s","roles":["Admin"],"user_meta":{"email":"%s"},"iat":%s,"exp":%s}' "${TOKEN_SUB:-script}" "${TOKEN_EMAIL:-script@taskpulse.local}" "$now" $((now + 600)) | b64url)
  printf '%s.%s.%s' "$header" "$payload" "$(printf '%s.%s' "$header" "$payload" | openssl dgst -sha256 -hmac "$secret" -binary | b64url)"
}
TOKEN="${TASKPULSE_TOKEN:-}"
if [[ -z "$TOKEN" ]]; then
  SECRET="${TASKPULSE_JWT_SECRET:-}"
  [[ -z "$SECRET" && -r /etc/taskpulse/api.env ]] && SECRET=$(sed -n 's/^Api__JwtSecret=//p' /etc/taskpulse/api.env)
  [[ -z "$SECRET" ]] && SECRET=$(sudo -n sed -n 's/^Api__JwtSecret=//p' /etc/taskpulse/api.env 2>/dev/null || true)
  [[ -n "$SECRET" ]] && TOKEN=$(mint_token "$SECRET")
fi
AUTH=(); [[ -n "$TOKEN" ]] && AUTH=(-H "Authorization: Bearer $TOKEN")
