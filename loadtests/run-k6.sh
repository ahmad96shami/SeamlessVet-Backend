#!/usr/bin/env bash
# Run one k6 scenario via the grafana/k6 Docker image against an already-running API.
#
# Required env: BASE_URL, WAREHOUSE_ID  (PHONE/PASSWORD default to the bootstrap admin).
# Optional env (per scenario): RATE, DURATION, PRODUCTS, STOCK_EACH, P95_BUDGET_MS,
#                              VISIT_RATE, POS_RATE, SYNC_RATE.
# Usage: BASE_URL=... WAREHOUSE_ID=... ./run-k6.sh <script.js> [extra k6 args...]
#
# Linux: uses --network host so the container reaches a host-local API (127.0.0.1). On macOS/Windows,
# set BASE_URL=http://host.docker.internal:<port> and override K6_NETWORK="" (host networking is a no-op there).
set -euo pipefail
SCRIPT="${1:?usage: run-k6.sh <script.js> [k6 args...]}"; shift || true
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
: "${BASE_URL:?set BASE_URL (e.g. http://127.0.0.1:5181)}"
: "${WAREHOUSE_ID:?set WAREHOUSE_ID (SELECT id FROM warehouses LIMIT 1)}"
K6_IMAGE="${K6_IMAGE:-grafana/k6:latest}"
K6_NETWORK="${K6_NETWORK:---network host}"
mkdir -p "$DIR/results"
NAME="$(basename "$SCRIPT" .js)"
OUT="$DIR/results/${NAME}-$(date +%Y%m%d-%H%M%S).txt"

# shellcheck disable=SC2086
docker run --rm $K6_NETWORK \
  -e BASE_URL -e WAREHOUSE_ID -e PHONE -e PASSWORD \
  -e RATE -e DURATION -e PRODUCTS -e STOCK_EACH -e P95_BUDGET_MS \
  -e VISIT_RATE -e POS_RATE -e SYNC_RATE \
  -v "$DIR:/scripts:ro" \
  "$K6_IMAGE" run "/scripts/$SCRIPT" "$@" | tee "$OUT"
