// M13 task 14 — representative operational mix across /visits, /pos/invoices, and /sync/*.
//
// PRD §12 sizes the system at ~100k visits/year (a low average rate; the point is the op *mix* and
// concurrency, not raw volume). Three concurrent scenarios approximate a clinic's write traffic:
// in-clinic visits, walk-in POS sales (each deducts inventory), and field stock sync. There was no
// prior baseline, so this run ESTABLISHES one — record the per-endpoint p50/p95/p99 in README.md and
// treat "no regression" as future runs staying within a tolerance of those numbers.
import http from 'k6/http';
import { check } from 'k6';
import exec from 'k6/execution';
import { Trend } from 'k6/metrics';
import {
  BASE_URL, WAREHOUSE_ID, login, seedProducts, seedCustomerPet, newGuid, idemKey, authHeaders,
} from './lib/common.js';

const DURATION = __ENV.DURATION || '60s';
const VISIT_RATE = Number(__ENV.VISIT_RATE || 10);
const POS_RATE = Number(__ENV.POS_RATE || 10);
const SYNC_RATE = Number(__ENV.SYNC_RATE || 20);
const PRODUCTS = Number(__ENV.PRODUCTS || 100);

// Per-operation latency, reported with the full percentile set below.
const visitMs = new Trend('visit_create_ms', true);
const posMs = new Trend('pos_invoice_ms', true);
const syncMs = new Trend('sync_movement_ms', true);

export const options = {
  scenarios: {
    visits: { executor: 'constant-arrival-rate', exec: 'visitOp', rate: VISIT_RATE, timeUnit: '1s', duration: DURATION, preAllocatedVUs: 20, maxVUs: 80 },
    pos: { executor: 'constant-arrival-rate', exec: 'posOp', rate: POS_RATE, timeUnit: '1s', duration: DURATION, preAllocatedVUs: 20, maxVUs: 80 },
    sync: { executor: 'constant-arrival-rate', exec: 'syncOp', rate: SYNC_RATE, timeUnit: '1s', duration: DURATION, preAllocatedVUs: 20, maxVUs: 80 },
  },
  summaryTrendStats: ['avg', 'min', 'med', 'p(50)', 'p(90)', 'p(95)', 'p(99)', 'max'],
  thresholds: {
    http_req_failed: ['rate<0.02'],
    visit_create_ms: ['p(95)<800'],
    pos_invoice_ms: ['p(95)<800'],
    sync_movement_ms: ['p(95)<800'],
  },
};

export function setup() {
  if (!WAREHOUSE_ID) throw new Error('WAREHOUSE_ID env var is required (the central warehouse id).');
  const { accessToken, userId } = login();
  const productIds = seedProducts(accessToken, PRODUCTS, 1000000);
  const { customerId, petId } = seedCustomerPet(accessToken);
  return { token: accessToken, doctorId: userId, productIds, customerId, petId, warehouseId: WAREHOUSE_ID };
}

export function visitOp(data) {
  const key = idemKey('visit');
  const res = http.post(`${BASE_URL}/visits`, JSON.stringify({
    id: newGuid(), visitType: 'in_clinic', customerId: data.customerId, petId: data.petId,
    doctorId: data.doctorId, status: 'open', chiefComplaint: 'فحص دوري',
  }), { headers: authHeaders(data.token, key), tags: { name: 'visit_create' } });
  check(res, { 'visit_create 200': (r) => r.status === 200 });
  visitMs.add(res.timings.duration);
}

export function posOp(data) {
  const key = idemKey('pos');
  const productId = data.productIds[exec.scenario.iterationInTest % data.productIds.length];
  const res = http.post(`${BASE_URL}/pos/invoices`, JSON.stringify({
    id: newGuid(), customerId: null, discountAmount: 0,
    items: [{ productId, quantity: 1, discountAmount: 0 }],
    payments: [{ method: 'cash', amount: 2 }],
    idempotencyKey: key,
  }), { headers: authHeaders(data.token, key), tags: { name: 'pos_invoice' } });
  check(res, { 'pos_invoice 200': (r) => r.status === 200 });
  posMs.add(res.timings.duration);
}

export function syncOp(data) {
  const key = idemKey('sync');
  const productId = data.productIds[exec.scenario.iterationInTest % data.productIds.length];
  const res = http.put(`${BASE_URL}/sync/inventory_movements`, JSON.stringify({
    id: newGuid(), product_id: productId, movement_type: 'sale_deduct',
    from_location_type: 'warehouse', from_location_id: data.warehouseId,
    quantity_delta: 1, idempotency_key: key,
  }), { headers: authHeaders(data.token, key), tags: { name: 'sync_movement' } });
  check(res, { 'sync_movement 200': (r) => r.status === 200 });
  syncMs.add(res.timings.duration);
}
