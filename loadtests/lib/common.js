// Shared helpers for the VetSystem k6 load tests (M13 tasks 13–14).
//
// No remote (jslib) imports, so these run offline inside the grafana/k6 container. Endpoints use
// Web-defaults JSON (camelCase) — except /sync/*, which uses snake_case because that payload mirrors
// the Postgres column names PowerSync replicates. Every write needs a unique Idempotency-Key header
// (^[A-Za-z0-9._-]{8,128}$); /sync/inventory_movements also repeats the key in the body.
import http from 'k6/http';
import exec from 'k6/execution';

export const BASE_URL = (__ENV.BASE_URL || 'http://127.0.0.1:5181').replace(/\/+$/, '');
export const PHONE = __ENV.PHONE || '+970000000000';
export const PASSWORD = __ENV.PASSWORD || 'LoadTest_123!';
export const WAREHOUSE_ID = __ENV.WAREHOUSE_ID || '';
const JSON_HEADERS = { 'Content-Type': 'application/json' };

export function login() {
  const res = http.post(`${BASE_URL}/auth/login`,
    JSON.stringify({ phonePrimary: PHONE, password: PASSWORD }),
    { headers: JSON_HEADERS, tags: { name: 'login' } });
  if (res.status !== 200) throw new Error(`login failed (${res.status}): ${res.body}`);
  return JSON.parse(res.body); // { accessToken, userId, roleKey, ... }
}

function hex(n) {
  let s = '';
  for (let i = 0; i < n; i++) s += Math.floor(Math.random() * 16).toString(16);
  return s;
}

// v4-shaped GUID. The API accepts any Guid for a client-supplied id; v7 is a client convention for
// index locality, not a server check (verified against /sync/inventory_movements).
export function newGuid() {
  return `${hex(8)}-${hex(4)}-4${hex(3)}-${(8 + Math.floor(Math.random() * 4)).toString(16)}${hex(3)}-${hex(12)}`;
}

// Unique idempotency key — independent of exec.scenario so it also works inside setup(). The
// per-VU module counter + ms timestamp + random suffix make collisions effectively impossible, so
// every write is genuinely applied rather than dedupe-replayed.
let _seq = 0;
export function idemKey(tag) {
  _seq += 1;
  return `lt-${tag}-${Date.now().toString(36)}-${_seq}-${hex(6)}`;
}

export function authHeaders(token, key) {
  const h = { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' };
  if (key) h['Idempotency-Key'] = key;
  return h;
}

// Create `n` medication products and receive `stockEach` units of each into the central warehouse,
// so later sale_deduct movements never hit negative stock. Returns the client-generated product ids.
export function seedProducts(token, n, stockEach) {
  const ids = [];
  for (let i = 0; i < n; i++) {
    const pid = newGuid();
    const pk = idemKey(`prod${i}`);
    const r = http.post(`${BASE_URL}/admin/products`, JSON.stringify({
      id: pid, nameAr: `حِمل اختبار ${i}`, category: 'medication',
      purchasePrice: 1, sellingPrice: 2, reorderPoint: 0,
    }), { headers: authHeaders(token, pk), tags: { name: 'seed_product' } });
    if (r.status !== 200) throw new Error(`seed product ${i} (${r.status}): ${r.body}`);
    if (stockEach > 0) {
      const rk = idemKey(`recv${i}`);
      const r2 = http.post(`${BASE_URL}/inventory/receive`, JSON.stringify({
        productId: pid, quantity: stockEach, idempotencyKey: rk,
      }), { headers: authHeaders(token, rk), tags: { name: 'seed_receive' } });
      if (r2.status !== 200) throw new Error(`seed receive ${i} (${r2.status}): ${r2.body}`);
    }
    ids.push(pid);
  }
  return ids;
}

// One home customer + one pet, for the visit/POS mix. Returns their ids.
export function seedCustomerPet(token) {
  const ck = idemKey('cust');
  let r = http.post(`${BASE_URL}/customers`,
    JSON.stringify({ type: 'home', fullName: 'عميل اختبار الحِمل' }),
    { headers: authHeaders(token, ck), tags: { name: 'seed_customer' } });
  if (r.status !== 200) throw new Error(`seed customer (${r.status}): ${r.body}`);
  const customerId = JSON.parse(r.body).id;
  const pk = idemKey('pet');
  r = http.post(`${BASE_URL}/pets`,
    JSON.stringify({ customerId, name: 'مريض اختبار' }),
    { headers: authHeaders(token, pk), tags: { name: 'seed_pet' } });
  if (r.status !== 200) throw new Error(`seed pet (${r.status}): ${r.body}`);
  return { customerId, petId: JSON.parse(r.body).id };
}
