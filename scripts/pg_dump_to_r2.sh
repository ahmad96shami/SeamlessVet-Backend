#!/usr/bin/env bash
# Daily logical backup of the production Postgres → Cloudflare R2.
#
# Dumps from inside the running postgres container (so pg_dump always matches the server version),
# gzips, and uploads a date-stamped object to R2. Run from cron; see scripts/crontab.example.
# 30-day retention is enforced by an R2 lifecycle rule on the bucket (see vet-backend/RUNBOOK.md),
# not by this script.
#
# Config: secrets/backup.env (copy from secrets/backup.env.example).
# Requires: docker (with the prod stack up), the AWS CLI v2, gzip.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$(dirname "$SCRIPT_DIR")"
COMPOSE=(docker compose --env-file "$PROJECT_DIR/.env.prod" -f "$PROJECT_DIR/docker-compose.prod.yaml")

log() { printf '%s  %s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$*"; }
die() { log "ERROR: $*" >&2; exit 1; }

# --- config ------------------------------------------------------------------
BACKUP_ENV="${BACKUP_ENV_FILE:-$PROJECT_DIR/secrets/backup.env}"
[ -f "$BACKUP_ENV" ] || die "missing $BACKUP_ENV (copy from secrets/backup.env.example)"
# shellcheck disable=SC1090
set -a; . "$BACKUP_ENV"; set +a

: "${R2_BACKUP_ENDPOINT:?set in backup.env}"
: "${R2_BACKUP_BUCKET:?set in backup.env}"
: "${R2_BACKUP_ACCESS_KEY:?set in backup.env}"
: "${R2_BACKUP_SECRET_KEY:?set in backup.env}"
PREFIX="${R2_BACKUP_PREFIX:-}"
PREFIX="${PREFIX%/}"  # normalize: no trailing slash

command -v aws >/dev/null 2>&1 || die "aws CLI not found"
command -v gzip >/dev/null 2>&1 || die "gzip not found"

# --- dump --------------------------------------------------------------------
STAMP="$(date -u +%Y%m%d-%H%M%S)"
OBJECT="${PREFIX:+$PREFIX/}vet-${STAMP}.sql.gz"
TMP="$(mktemp -t vet-pgdump.XXXXXX.sql.gz)"
trap 'rm -f "$TMP"' EXIT

log "dumping database 'vet' from the postgres container…"
# -T: no TTY (cron-safe). Plain SQL → gzip. Exit status of pg_dump propagates via pipefail.
"${COMPOSE[@]}" exec -T postgres pg_dump -U vet -d vet --no-owner --no-privileges | gzip -9 > "$TMP"
SIZE="$(du -h "$TMP" | cut -f1)"
log "dump complete (${SIZE} compressed) → $TMP"

# --- upload ------------------------------------------------------------------
export AWS_ACCESS_KEY_ID="$R2_BACKUP_ACCESS_KEY"
export AWS_SECRET_ACCESS_KEY="$R2_BACKUP_SECRET_KEY"
export AWS_DEFAULT_REGION="auto"   # R2 ignores region but the CLI requires one
# Newer AWS CLIs send checksums R2 rejects; only add when required.
export AWS_REQUEST_CHECKSUM_CALCULATION="when_required"

log "uploading → s3://${R2_BACKUP_BUCKET}/${OBJECT}"
aws s3 cp "$TMP" "s3://${R2_BACKUP_BUCKET}/${OBJECT}" --endpoint-url "$R2_BACKUP_ENDPOINT" --only-show-errors
log "backup uploaded OK: ${OBJECT}"
