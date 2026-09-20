#!/usr/bin/env bash
# Nightly backup of everything TaskPulse persists: the PostgreSQL database (pg_dump, custom format) and the
# upload files. Keeps the newest KEEP sets. Installed as a systemd timer by install.sh; run by hand with sudo.
#   sudo scripts/backup.sh                 # writes /var/backups/taskpulse/<timestamp>.{dump,uploads.tar.gz}
#   sudo scripts/backup.sh --restore FILE  # restore a .dump into the taskpulse database (stops the API meanwhile)
set -euo pipefail

DB_NAME=taskpulse
UPLOADS=/var/lib/taskpulse/uploads
DEST="${TASKPULSE_BACKUP_DIR:-/var/backups/taskpulse}"
KEEP="${TASKPULSE_BACKUP_KEEP:-7}"

[[ $EUID -eq 0 ]] || { echo "run as root: sudo $0" >&2; exit 1; }
PG_PORT="$(pg_lsclusters -h | awk 'NR==1{print $3}')"

if [[ "${1:-}" == "--restore" ]]; then
  FILE="${2:?usage: $0 --restore FILE.dump}"
  echo "restoring $FILE into $DB_NAME (the API is stopped while this runs)"
  systemctl stop taskpulse-api
  sudo -u postgres pg_restore -p "$PG_PORT" -d "$DB_NAME" --clean --if-exists --no-owner "$FILE"
  systemctl start taskpulse-api
  echo "restored; the API is back"
  exit 0
fi

install -d -m 0750 "$DEST"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
sudo -u postgres pg_dump -p "$PG_PORT" -Fc "$DB_NAME" > "$DEST/$STAMP.dump"
if [[ -d "$UPLOADS" ]]; then
  tar -czf "$DEST/$STAMP.uploads.tar.gz" -C "$(dirname "$UPLOADS")" "$(basename "$UPLOADS")"
fi
chmod 0640 "$DEST/$STAMP".*

# keep the newest KEEP dumps (and their upload archives)
ls -1t "$DEST"/*.dump 2>/dev/null | tail -n +$((KEEP + 1)) | while read -r old; do
  rm -f "$old" "${old%.dump}.uploads.tar.gz"
done

printf 'backup %s: %s (%s), uploads %s\n' "$STAMP" "$DEST/$STAMP.dump" "$(du -h "$DEST/$STAMP.dump" | cut -f1)" \
  "$([[ -f "$DEST/$STAMP.uploads.tar.gz" ]] && du -h "$DEST/$STAMP.uploads.tar.gz" | cut -f1 || echo none)"
