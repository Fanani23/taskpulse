#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PREFIX=/opt/taskpulse
SVC_USER=taskpulse
UNITS=(taskpulse-api taskpulse-realtime)
NGINX_SITE=taskpulse.conf
ENV_DIR=/etc/taskpulse
DB_NAME=taskpulse
DEV_ROLE=taskpulse_dev
DEV_PASSWORD=taskpulse_dev

log() { printf '\033[1;34m==> %s\033[0m\n' "$*"; }

[[ $EUID -eq 0 ]] || { echo "Run with sudo: sudo $0" >&2; exit 1; }
command -v dotnet >/dev/null || { echo "dotnet SDK not found (apt install dotnet-sdk-10.0)" >&2; exit 1; }
command -v pg_lsclusters >/dev/null || { echo "PostgreSQL not found (apt install postgresql)" >&2; exit 1; }

if [[ "${1:-}" == "--uninstall" ]]; then
  log "Stopping and removing units"
  for u in "${UNITS[@]}"; do systemctl disable --now "$u" 2>/dev/null || true; rm -f "/etc/systemd/system/$u.service"; done
  systemctl daemon-reload
  rm -f "/etc/nginx/sites-enabled/$NGINX_SITE" "/etc/nginx/sites-available/$NGINX_SITE"
  command -v nginx >/dev/null && nginx -t 2>/dev/null && systemctl reload nginx || true
  rm -rf "$PREFIX" "$ENV_DIR"
  userdel "$SVC_USER" 2>/dev/null || true
  log "Uninstalled (PostgreSQL databases and roles were left in place)"
  exit 0
fi

if ! id -u "$SVC_USER" >/dev/null 2>&1; then
  log "Creating system user '$SVC_USER'"
  useradd --system --no-create-home --shell /usr/sbin/nologin "$SVC_USER"
fi

PG_PORT="$(pg_lsclusters -h | awk 'NR==1{print $3}')"
log "Provisioning PostgreSQL (cluster port $PG_PORT)"
systemctl enable --now postgresql >/dev/null
psql_admin() { sudo -u postgres psql -p "$PG_PORT" -v ON_ERROR_STOP=1 -qAt "$@"; }
cd /tmp
psql_admin <<SQL
DO \$\$ BEGIN
  IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = '$SVC_USER') THEN CREATE ROLE "$SVC_USER" LOGIN; END IF;
  IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = '$DEV_ROLE') THEN CREATE ROLE "$DEV_ROLE" LOGIN PASSWORD '$DEV_PASSWORD' CREATEDB; END IF;
END \$\$;
SQL
for spec in "$DB_NAME:$SVC_USER" "${DB_NAME}_dev:$DEV_ROLE"; do
  db="${spec%%:*}"; owner="${spec##*:}"
  if [[ "$(psql_admin -c "SELECT 1 FROM pg_database WHERE datname = '$db'")" != "1" ]]; then
    sudo -u postgres createdb -p "$PG_PORT" -O "$owner" "$db"
  fi
done
cd "$ROOT"

install -d -m 0755 "$ENV_DIR"
{
  printf 'ConnectionStrings__Tasks=Host=/var/run/postgresql;Port=%s;Database=%s;Username=%s\n' "$PG_PORT" "$DB_NAME" "$SVC_USER"
  i=0
  for origin in ${TASKPULSE_ALLOWED_ORIGINS:-}; do
    printf 'Api__AllowedOrigins__%s=%s\n' "$i" "$origin"; i=$((i + 1))
  done
} > "$ENV_DIR/api.env"
chmod 0600 "$ENV_DIR/api.env"

BUILD_USER="${SUDO_USER:-$USER}"
STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT
chown "$BUILD_USER" "$STAGE"

publish() {
  log "Publishing $1"
  sudo -u "$BUILD_USER" -H env DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 \
    dotnet publish "$ROOT/src/$1/$1.csproj" -c Release -o "$STAGE/$2" --nologo -v quiet
}
publish TaskPulse.Api      api
publish TaskPulse.Realtime realtime

log "Installing to $PREFIX"
mkdir -p "$PREFIX"
for d in api realtime; do
  rm -rf "$PREFIX/$d"
  cp -r "$STAGE/$d" "$PREFIX/$d"
done
cp "$ROOT/README.md" "$PREFIX/README.md" 2>/dev/null || true
chown -R root:"$SVC_USER" "$PREFIX"
chmod -R u=rwX,g=rX,o= "$PREFIX"

log "Installing systemd units"
for u in "${UNITS[@]}"; do install -m 0644 "$ROOT/deploy/systemd/$u.service" "/etc/systemd/system/$u.service"; done
systemctl daemon-reload
systemctl enable "${UNITS[@]}" >/dev/null
for u in "${UNITS[@]}"; do
  systemctl restart "$u" || { echo "!! $u failed to start:"; journalctl -u "$u" -n 15 --no-pager -o cat; exit 1; }
done

if command -v nginx >/dev/null; then
  log "Configuring nginx site $NGINX_SITE (port 8088)"
  install -m 0644 "$ROOT/deploy/nginx/$NGINX_SITE" "/etc/nginx/sites-available/$NGINX_SITE"
  ln -sf "/etc/nginx/sites-available/$NGINX_SITE" "/etc/nginx/sites-enabled/$NGINX_SITE"
  nginx -t
  systemctl enable --now nginx
  systemctl reload nginx
else
  log "nginx not installed — skipping reverse proxy (apt install nginx, then re-run)"
fi

log "Waiting for services"
for i in $(seq 1 40); do
  curl -fs http://127.0.0.1:5080/health/ready >/dev/null 2>&1 && curl -fs http://127.0.0.1:5090/health >/dev/null 2>&1 && break
  sleep 0.5
done
echo
systemctl --no-pager --lines=0 status "${UNITS[@]}" | grep -E "●|Active:" || true
echo
log "Done."
echo "  REST API   http://127.0.0.1:5080/api/tasks      (direct)   http://127.0.0.1:8088/api/tasks (nginx)"
echo "  OpenAPI    http://127.0.0.1:5080/openapi/v1.json"
echo "  WebSocket  ws://127.0.0.1:5090/ws               (direct)   ws://127.0.0.1:8088/ws          (nginx)"
echo "  Test page  http://127.0.0.1:8088/"
echo "  Database   $DB_NAME on PostgreSQL port $PG_PORT (service role '$SVC_USER', peer auth over the Unix socket)"
echo "  Logs       journalctl -u taskpulse-api -f   |   journalctl -u taskpulse-realtime -f"
if [[ "$PG_PORT" != "5432" ]]; then
  echo
  echo "  NOTE: PostgreSQL is on port $PG_PORT, not 5432. For dotnet run / tests:"
  echo "    export ConnectionStrings__Tasks='Host=localhost;Port=$PG_PORT;Database=${DB_NAME}_dev;Username=$DEV_ROLE;Password=$DEV_PASSWORD'"
  echo "    export TASKPULSE_TEST_PG='Host=localhost;Port=$PG_PORT;Database=postgres;Username=$DEV_ROLE;Password=$DEV_PASSWORD'"
  echo "  (scripts/test.sh and scripts/run-dev.sh set these for you)"
fi
