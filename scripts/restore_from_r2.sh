#!/usr/bin/env bash
# Restore a Postgres backup produced by pg_dump_to_r2.sh — for the M14 restore drill and real recovery.
#
# Downloads a .sql.gz object from R2 and restores it into a target database in the running postgres
# container. DESTRUCTIVE: it drops and recreates the target DB. Requires --yes to actually run.
#
#   scripts/restore_from_r2.sh --latest --target-db vet_restore_test --yes   # drill into a scratch DB
#   scripts/restore_from_r2.sh --key pg/vet-20260524-031500.sql.gz --target-db vet --yes
#
# Config: secrets/backup.env. Requires: docker (prod stack up), AWS CLI v2, gunzip.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$(dirname "$SCRIPT_DIR")"
COMPOSE=(docker compose --env-file "$PROJECT_DIR/.env.prod" -f "$PROJECT_DIR/docker-compose.prod.yaml")

log() { printf '%s  %s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$*"; }
die() { log "ERROR: $*" >&2; exit 1; }

KEY="" TARGET_DB="vet_restore_test" PICK_LATEST=false CONFIRM=false
while [ $# -gt 0 ]; do
  case "$1" in
    --latest)    PICK_LATEST=true; shift ;;
    --key)       KEY="${2:?}"; shift 2 ;;
    --target-db) TARGET_DB="${2:?}"; shift 2 ;;
    --yes)       CONFIRM=true; shift ;;
    *) die "unknown arg: $1" ;;
  esac
done

BACKUP_ENV="${BACKUP_ENV_FILE:-$PROJECT_DIR/secrets/backup.env}"
[ -f "$BACKUP_ENV" ] || die "missing $BACKUP_ENV"
# shellcheck disable=SC1090
set -a; . "$BACKUP_ENV"; set +a
: "${R2_BACKUP_ENDPOINT:?}"; : "${R2_BACKUP_BUCKET:?}"
: "${R2_BACKUP_ACCESS_KEY:?}"; : "${R2_BACKUP_SECRET_KEY:?}"
PREFIX="${R2_BACKUP_PREFIX:-}"; PREFIX="${PREFIX%/}"

export AWS_ACCESS_KEY_ID="$R2_BACKUP_ACCESS_KEY"
export AWS_SECRET_ACCESS_KEY="$R2_BACKUP_SECRET_KEY"
export AWS_DEFAULT_REGION="auto"
export AWS_REQUEST_CHECKSUM_CALCULATION="when_required"
S3="s3://${R2_BACKUP_BUCKET}"
EP=(--endpoint-url "$R2_BACKUP_ENDPOINT")

if $PICK_LATEST; then
  log "finding newest backup under ${S3}/${PREFIX}/ …"
  KEY="$(aws s3 ls "${S3}/${PREFIX:+$PREFIX/}" "${EP[@]}" | awk '/\.sql\.gz$/ {print $4}' | sort | tail -1)"
  [ -n "$KEY" ] || die "no .sql.gz backups found"
  KEY="${PREFIX:+$PREFIX/}$KEY"
fi
[ -n "$KEY" ] || die "specify --key <object> or --latest"

$CONFIRM || die "refusing without --yes (would DROP and recreate database '$TARGET_DB')"

TMP="$(mktemp -t vet-restore.XXXXXX.sql.gz)"
trap 'rm -f "$TMP"' EXIT
log "downloading ${S3}/${KEY} …"
aws s3 cp "${S3}/${KEY}" "$TMP" "${EP[@]}" --only-show-errors

log "recreating database '$TARGET_DB' …"
"${COMPOSE[@]}" exec -T postgres psql -U vet -d postgres -v ON_ERROR_STOP=1 \
  -c "DROP DATABASE IF EXISTS \"$TARGET_DB\" WITH (FORCE);" \
  -c "CREATE DATABASE \"$TARGET_DB\" OWNER vet;"

log "restoring into '$TARGET_DB' …"
gunzip -c "$TMP" | "${COMPOSE[@]}" exec -T postgres psql -U vet -d "$TARGET_DB" -v ON_ERROR_STOP=1 --quiet >/dev/null

COUNT="$("${COMPOSE[@]}" exec -T postgres psql -U vet -d "$TARGET_DB" -tAc \
  "SELECT count(*) FROM information_schema.tables WHERE table_schema='public';")"
log "restore complete: $TARGET_DB now has ${COUNT// /} public tables."
log "Verify, then point the API at it (or rename to 'vet') per RUNBOOK 'Restore from backup'."
