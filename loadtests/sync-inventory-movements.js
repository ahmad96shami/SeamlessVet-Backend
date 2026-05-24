// M13 task 13 — sustained load on /sync/inventory_movements, capturing p50/p95/p99.
//
// PRD §14's concern is field-doctor reconnect/sync storms. This drives the sync write path at a
// constant arrival rate (default 50 RPS) and reports the latency distribution. Each request is a
// `sale_deduct` of 1 unit from the central warehouse, round-robining a product pool so concurrent
// writers rarely touch the same stock_items row (which would be a real, but here unwanted, 409).
//
// NOTE: the per-user token-bucket rate limit on /sync/* is OFF for this capacity test (one user
// can't otherwise reach 50 RPS) — local-baseline.sh sets RateLimiting__Enabled=false. The limiter
// itself is covered by SyncRateLimitTests. See loadtests/README.md.
import http from 'k6/http';
import { check } from 'k6';
import exec from 'k6/execution';
import { BASE_URL, WAREHOUSE_ID, login, seedProducts, newGuid, idemKey, authHeaders } from './lib/common.js';

const RATE = Number(__ENV.RATE || 50);
const DURATION = __ENV.DURATION || '60s';
const PRODUCTS = Number(__ENV.PRODUCTS || 100);
const STOCK_EACH = Number(__ENV.STOCK_EACH || 1000000);
const P95_BUDGET_MS = Number(__ENV.P95_BUDGET_MS || 500);

export const options = {
  scenarios: {
    sync_inventory: {
      executor: 'constant-arrival-rate',
      rate: RATE, timeUnit: '1s', duration: DURATION,
      preAllocatedVUs: Math.max(20, RATE),
      maxVUs: Math.max(50, RATE * 4),
    },
  },
  summaryTrendStats: ['avg', 'min', 'med', 'p(50)', 'p(90)', 'p(95)', 'p(99)', 'max'],
  thresholds: {
    'http_req_failed{name:sync_movement}': ['rate<0.01'],
    'http_req_duration{name:sync_movement}': [`p(95)<${P95_BUDGET_MS}`],
  },
};

export function setup() {
  if (!WAREHOUSE_ID) throw new Error('WAREHOUSE_ID env var is required (the central warehouse id).');
  const { accessToken } = login();
  const productIds = seedProducts(accessToken, PRODUCTS, STOCK_EACH);
  return { token: accessToken, productIds, warehouseId: WAREHOUSE_ID };
}

export default function (data) {
  const productId = data.productIds[exec.scenario.iterationInTest % data.productIds.length];
  const key = idemKey('syncinv');
  const res = http.put(`${BASE_URL}/sync/inventory_movements`, JSON.stringify({
    id: newGuid(),
    product_id: productId,
    movement_type: 'sale_deduct',
    from_location_type: 'warehouse',
    from_location_id: data.warehouseId,
    quantity_delta: 1,
    idempotency_key: key,
  }), { headers: authHeaders(data.token, key), tags: { name: 'sync_movement' } });
  check(res, { 'sync_movement 200': (r) => r.status === 200 });
}
