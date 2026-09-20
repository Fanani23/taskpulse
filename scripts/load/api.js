// k6 load profile for the REST API through nginx. Mix per virtual user: list (paged), stats, one read, and - when a
// token is given - a create + update + purge cycle, so the write path (audit row, change event, rate limiter) is in
// the numbers too. Run: scripts/load/run.sh [base-url]   (mints the token like the smoke test does)
import http from 'k6/http';
import { check, sleep } from 'k6';

const BASE = (__ENV.BASE || 'http://127.0.0.1:8088').replace(/\/$/, '');
const TOKEN = __ENV.TOKEN || '';
const auth = TOKEN ? { Authorization: `Bearer ${TOKEN}` } : {};

export const options = {
  scenarios: {
    readers: { executor: 'ramping-vus', startVUs: 5, stages: [{ duration: '20s', target: 25 }, { duration: '40s', target: 25 }, { duration: '10s', target: 0 }], exec: 'reader' },
    writers: { executor: 'constant-vus', vus: TOKEN ? 2 : 0, duration: '70s', exec: 'writer' },
  },
  thresholds: {
    'http_req_failed{scenario:readers}': ['rate<0.01'],
    'http_req_duration{scenario:readers}': ['p(95)<300'],
    'http_req_duration{scenario:writers}': ['p(95)<500'],
    'http_req_failed{scenario:writers}': ['rate<0.01'],
  },
};

export function reader() {
  const page = 1 + (__VU % 4);
  const list = http.get(`${BASE}/api/tasks?page=${page}&pageSize=10`, { tags: { name: 'list' } });
  check(list, { 'list 200': (r) => r.status === 200 });
  const stats = http.get(`${BASE}/api/tasks/stats?days=14`, { tags: { name: 'stats' } });
  check(stats, { 'stats 200': (r) => r.status === 200 });
  const items = list.json('items');
  if (items && items.length) {
    const one = http.get(`${BASE}/api/tasks/${items[0].id}`, { tags: { name: 'get' } });
    check(one, { 'get 200': (r) => r.status === 200 });
  }
  http.get(`${BASE}/api/catalog/countries?pageSize=20`, { tags: { name: 'catalog' } });
  sleep(0.5 + Math.random());
}

export function writer() {
  const headers = { 'Content-Type': 'application/json', ...auth };
  const created = http.post(`${BASE}/api/tasks`, JSON.stringify({ title: `load ${__VU}-${__ITER}`, description: 'k6' }), { headers, tags: { name: 'create' } });
  if (!check(created, { 'create 201': (r) => r.status === 201 })) { sleep(1); return; }
  const id = created.json('id');
  const updated = http.put(`${BASE}/api/tasks/${id}`, JSON.stringify({ title: `load ${__VU}-${__ITER}`, status: 'Done' }), { headers, tags: { name: 'update' } });
  check(updated, { 'update 200': (r) => r.status === 200 });
  const purged = http.del(`${BASE}/api/tasks/${id}?permanent=true`, null, { headers: auth, tags: { name: 'purge' } });
  check(purged, { 'purge 204': (r) => r.status === 204 });
  sleep(4); // 2 VUs × 3 writes per ~4 s ≈ 90/min: under the API's 120/min per-address write limit
}

export function handleSummary(data) {
  const m = data.metrics;
  const pct = (name, p) => (m[name] && m[name].values[p] !== undefined ? m[name].values[p].toFixed(1) : 'n/a');
  const lines = [
    `requests: ${m.http_reqs.values.count} in ${(data.state.testRunDurationMs / 1000).toFixed(0)}s (${m.http_reqs.values.rate.toFixed(1)}/s), failed ${(100 * m.http_req_failed.values.rate).toFixed(2)}%`,
    `readers  med ${pct('http_req_duration{scenario:readers}', 'med')} ms  p95 ${pct('http_req_duration{scenario:readers}', 'p(95)')} ms  max ${pct('http_req_duration{scenario:readers}', 'max')} ms`,
    `writers  med ${pct('http_req_duration{scenario:writers}', 'med')} ms  p95 ${pct('http_req_duration{scenario:writers}', 'p(95)')} ms  max ${pct('http_req_duration{scenario:writers}', 'max')} ms`,
  ];
  return { stdout: '\n' + lines.join('\n') + '\n', 'scripts/load/last-run.json': JSON.stringify({ at: new Date().toISOString(), base: BASE, lines }, null, 2) };
}
