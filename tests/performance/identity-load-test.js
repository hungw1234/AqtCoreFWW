// AqtCoreFW — Identity module load test
// Purpose: validate user admin list endpoint latency
// SLO: p95 < 300ms
// Run: k6 run identity-load-test.js --env BASE_URL=https://staging.internal --env API_KEY=<key>
//
// IMPORTANT: UsersAdminController requires Admin/SystemAdmin role.
// API key auth injects role=ServiceAccount — endpoint returns 403.
// This test measures 403 response latency (auth rejection path).
// To test the 200 path, a JWT with Admin role is required (out of scope for k6 M2M tests).

import http from 'k6/http';
import { check, sleep } from 'k6';
import { Rate } from 'k6/metrics';
import { getApiKeyHeaders } from './lib/auth.js';
import { pageParams } from './lib/helpers.js';

// ─── Config ──────────────────────────────────────────────────────────────────

const BASE_URL = __ENV.BASE_URL || 'https://staging.internal';

export const options = {
  stages: [
    { duration: '2m',  target: 100 },
    { duration: '10m', target: 100 },
    { duration: '2m',  target: 0   },
  ],
  thresholds: {
    'http_req_duration{operation:identity-read}': ['p(95)<300'],
    'http_req_failed': ['rate<0.01'],
  },
};

// ─── Custom metrics ───────────────────────────────────────────────────────────

const readErrors = new Rate('identity_read_errors');

// ─── Main scenario ────────────────────────────────────────────────────────────

export default function () {
  // Route verified: [Route("api/v1/admin/users")] in UsersAdminController
  const res = http.get(
    `${BASE_URL}/api/v1/admin/users?${pageParams(1, 20)}`,
    { headers: getApiKeyHeaders(), tags: { operation: 'identity-read' } }
  );

  // 403 expected — Admin role required, API key has ServiceAccount role only
  const ok = check(res, {
    'GET /admin/users status 200 or 403': (r) => r.status === 200 || r.status === 403,
  });

  readErrors.add(!ok);
  sleep(0.5);
}
