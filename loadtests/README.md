# Load tests (M13 tasks 13–14)

k6 load tests for the money/sync-critical write paths (PRD §12 sizing, §14 sync-storm risk).

| File | Purpose |
|---|---|
| `sync-inventory-movements.js` | **Task 13** — sustained RPS (default 50) on `PUT /sync/inventory_movements`; reports p50/p95/p99. |
| `representative-mix.js` | **Task 14** — concurrent mix of `POST /visits`, `POST /pos/invoices`, `PUT /sync/inventory_movements`; per-endpoint p50/p95/p99. |
| `lib/common.js` | Shared helpers: login, GUID/idempotency-key generation, fixture seeding. |
| `run-k6.sh` | Run one scenario via the `grafana/k6` Docker image against an already-running API. |
| `local-baseline.sh` | Stand up an **isolated throwaway stack**, run both scenarios, tear it down. |
| `results/` | Captured run summaries (gitignored). |

The tools run via Docker (`grafana/k6`) — **k6 does not need to be installed** on the host.

## Quick start — local baseline (one command)

```bash
./local-baseline.sh
```

This is fully self-contained and **does not touch your dev database or any running API**: it builds the
API (Release), starts an **ephemeral Postgres** (`vet-loadtest-pg` on `:55433`), applies migrations,
seeds the bootstrap admin + central warehouse, starts the Release API on `:5181` with **rate limiting
off**, runs both scenarios, and tears everything down. Requires the .NET SDK + EF tools
(`dotnet tool restore`) and Docker.

Env knobs: `DURATION` (default `60s`), `RATE` (task-13 RPS), `VISIT_RATE`/`POS_RATE`/`SYNC_RATE`
(task-14), `KEEP_PG=1` to leave Postgres up for inspection.

## Run against an existing target (e.g. staging)

```bash
export BASE_URL=https://api.staging.example.com
export WAREHOUSE_ID=<central warehouse id>      # SELECT id FROM warehouses LIMIT 1;
export PHONE=+970000000000 PASSWORD='…'          # a user that can write catalog/inventory/visits/POS
./run-k6.sh sync-inventory-movements.js
./run-k6.sh representative-mix.js
```

`WAREHOUSE_ID` is required because `ApplyMovementAsync` validates the warehouse exists and no API
exposes it (it's seeded once per environment). On macOS/Windows, set
`BASE_URL=http://host.docker.internal:<port>` and `K6_NETWORK=""` (Linux uses `--network host`).

### Why rate limiting is off for these runs

`/sync/*` has a **per-user** token bucket (M13 task 10) to absorb one doctor's reconnect storm. These
tests authenticate as a single user, so leaving it on would throttle the run and measure the limiter,
not endpoint capacity. The limiter itself is covered by `SyncRateLimitTests`; `local-baseline.sh` sets
`RateLimiting__Enabled=false`. In production each field doctor is a separate partition.

### Notes on the workload

- Each iteration uses a fresh GUID + unique idempotency key, so every write is genuinely applied (no
  dedupe-replays). Products are pre-seeded with 1,000,000 units each so `sale_deduct` never goes
  negative, and movements round-robin a 100-product pool so concurrent writers rarely contend on the
  same `stock_items` row (a real but here-unwanted 409).
- The p95 thresholds (500ms task 13, 800ms task 14) are **placeholders** — tune them to your staging SLO.

## Baseline results

No prior baseline existed, so these runs **establish** one; "no regression" (task 14) means future runs
stay within a tolerance of these figures. Numbers below are a **single-box developer baseline**, not a
production SLA — the load generator, API, and Postgres all share one machine, so a real (separate
load-gen → networked API → dedicated DB) setup will differ. Re-run on staging and update this table.

- **Environment:** AMD Ryzen 5 5500U (12 threads), 15 GiB RAM; .NET 10.0.104 Release build; Postgres 17
  in Docker; k6 v2.0.0; loopback. Rate limiting off. Date: 2026-05-24.

**Task 13 — `/sync/inventory_movements` @ 50 RPS, 60s (3000 requests, 0 failures):**

| metric | p50 | p95 | p99 | max | avg |
|---|---|---|---|---|---|
| `sync_movement` duration | 13.7 ms | 16.3 ms | 18.5 ms | 66.5 ms | 13.8 ms |

p95 budget (`p(95)<500ms`): **pass**. Throughput held a flat 50 iters/s; `http_req_failed` 0%.

**Task 14 — representative mix, 60s (visits 10 + POS 10 + sync 20 RPS = 2402 ops, 0 failures):**

| endpoint | p50 | p95 | p99 | max |
|---|---|---|---|---|
| `POST /visits` | 14.5 ms | 17.6 ms | 19.7 ms | 189 ms |
| `POST /pos/invoices` | 26.2 ms | 31.2 ms | 34.0 ms | 305 ms |
| `PUT /sync/inventory_movements` | 14.3 ms | 17.0 ms | 18.9 ms | 22 ms |

All per-endpoint p95 budgets (`p(95)<800ms`): **pass**; `http_req_failed` 0%. (POS is heaviest — it
writes the invoice, line items, payment, and an inventory deduction in one transaction.) The occasional
multi-hundred-ms `max` is first-iteration JIT/connection warmup, not steady state.

## Reading results

k6 prints the summary to stdout (and `run-k6.sh` tees it to `results/<scenario>-<timestamp>.txt`).
`summaryTrendStats` is set so p50/p95/p99 appear for `http_req_duration` and each custom per-endpoint
trend. A run "passes" when the `THRESHOLDS` block shows all `✓`.
