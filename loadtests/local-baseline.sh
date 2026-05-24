#!/usr/bin/env bash
# Stand up an ISOLATED throwaway stack (ephemeral Postgres + Release API, rate-limiting OFF), run both
# load-test scenarios against it, then tear everything down. Self-contained: it does NOT touch your dev
# database or any already-running API (its own PG container + alternate ports). Numbers are a
# single-box BASELINE, not a production SLA — run against staging for representative figures (README).
#
# Requires: dotnet SDK + EF tools (dotnet tool restore), Docker. Usage: ./local-baseline.sh
# Env knobs: DURATION (60s), RATE (task 13 RPS), VISIT_RATE/POS_RATE/SYNC_RATE (task 14), KEEP_PG=1.
set -euo pipefail
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="$(cd "$DIR/.." && pwd)"
APIDIR="$REPO/src/VetSystem.API"
DLL="$APIDIR/bin/Release/net10.0/VetSystem.API.dll"
PG_NAME="${PG_NAME:-vet-loadtest-pg}"
PG_PORT="${PG_PORT:-55433}"
API_PORT="${API_PORT:-5181}"
BASE_URL="http://127.0.0.1:$API_PORT"
PGCONN="Host=127.0.0.1;Port=$PG_PORT;Database=vet;Username=vet;Password=loadtest"
JWT="loadtest-jwt-secret-key-please-32-chars-minimum-xyz"
ADMIN_PW="LoadTest_123!"
export DURATION="${DURATION:-60s}"

cleanup() {
  [[ -n "${API_PID:-}" ]] && { kill "$API_PID" 2>/dev/null || true; wait "$API_PID" 2>/dev/null || true; }
  [[ "${KEEP_PG:-0}" == "1" ]] || docker rm -f "$PG_NAME" >/dev/null 2>&1 || true
}
trap cleanup EXIT

echo "==> Build API (Release)"
dotnet build "$APIDIR/VetSystem.API.csproj" -c Release >/dev/null
dotnet tool restore >/dev/null 2>&1 || true

echo "==> Ephemeral Postgres ($PG_NAME on :$PG_PORT)"
docker rm -f "$PG_NAME" >/dev/null 2>&1 || true
docker run -d --name "$PG_NAME" -e POSTGRES_USER=vet -e POSTGRES_PASSWORD=loadtest -e POSTGRES_DB=vet \
  -p "$PG_PORT:5432" postgres:17 >/dev/null
# wait for readiness (paced by docker-exec latency; ready in ~1-2s)
for _ in $(seq 1 150); do docker exec "$PG_NAME" pg_isready -U vet -d vet >/dev/null 2>&1 && break; done

echo "==> Apply migrations"
ASPNETCORE_ENVIRONMENT=Test ConnectionStrings__Postgres="$PGCONN" \
  dotnet ef database update --project "$REPO/src/VetSystem.Infrastructure" --startup-project "$APIDIR" \
  --configuration Release --no-build >/dev/null

echo "==> Seed bootstrap admin + central warehouse"
ASPNETCORE_ENVIRONMENT=Production ASPNETCORE_CONTENTROOT="$APIDIR" ConnectionStrings__Postgres="$PGCONN" \
  Jwt__SecretKey="$JWT" BootstrapAdmin__Password="$ADMIN_PW" \
  dotnet "$DLL" --seed >/dev/null

echo "==> Start Release API ($BASE_URL, RateLimiting__Enabled=false)"
ASPNETCORE_ENVIRONMENT=Production ASPNETCORE_CONTENTROOT="$APIDIR" ASPNETCORE_URLS="$BASE_URL" \
  ConnectionStrings__Postgres="$PGCONN" Jwt__SecretKey="$JWT" BootstrapAdmin__Password="$ADMIN_PW" \
  RateLimiting__Enabled=false \
  dotnet "$DLL" >/tmp/vet-loadtest-api.log 2>&1 &
API_PID=$!
curl -sS --retry 40 --retry-connrefused --retry-delay 1 -m3 "$BASE_URL/health/live" -o /dev/null \
  && echo "    API up (pid $API_PID)" || { echo "API failed to start:"; tail -30 /tmp/vet-loadtest-api.log; exit 1; }

export BASE_URL PHONE="+970000000000" PASSWORD="$ADMIN_PW"
export WAREHOUSE_ID
WAREHOUSE_ID="$(docker exec "$PG_NAME" psql -U vet -d vet -tAc 'SELECT id FROM warehouses LIMIT 1' | tr -d '[:space:]')"
echo "    warehouse=$WAREHOUSE_ID"

echo "==> [1/2] Task 13 — sustained ${RATE:-50} RPS on /sync/inventory_movements (${DURATION})"
"$DIR/run-k6.sh" sync-inventory-movements.js
echo "==> [2/2] Task 14 — representative mix /visits + /pos/invoices + /sync/* (${DURATION})"
"$DIR/run-k6.sh" representative-mix.js
echo "==> Done. Summaries are under loadtests/results/. Tearing down the throwaway stack."
