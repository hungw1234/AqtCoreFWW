// AqtCoreFW — Audit module load test
// Purpose: validate read SLO for HMAC-chained audit log queries
// SLO: p95 < 200ms | error rate < 1%
// Run: k6 run audit-load-test.js --env BASE_URL=https://staging.internal --env API_KEY=<key>

import http from 'k6/http';
import { check, sleep } from 'k6';
import { Rate } from 'k6/metrics';
import { getApiKeyHeaders } from './lib/auth.js';
import { pageParams, parseBody } from './lib/helpers.js';

// ─── Config ──────────────────────────────────────────────────────────────────

const BASE_URL = __ENV.BASE_URL || 'https://staging.internal';

export const options = {
  stages: [
    { duration: '2m',  target: 100 },
    { duration: '10m', target: 100 },
    { duration: '2m',  target: 0   },
  ],
  thresholds: {
    'http_req_duration{operation:audit-read}': ['p(95)<200'],
    'http_req_failed': ['rate<0.01'],
  },
};

// ─── Custom metrics ───────────────────────────────────────────────────────────

const readErrors = new Rate('audit_read_errors');

// ─── Main scenario ────────────────────────────────────────────────────────────

export default function () {
  const res = http.get(
    `${BASE_URL}/api/v1/audit/logs?${pageParams(1, 20)}`,
    { headers: getApiKeyHeaders(), tags: { operation: 'audit-read' } }
  );

  const ok = check(res, {
    'GET /audit/logs status 200':  (r) => r.status === 200,
    'GET /audit/logs has data':    (r) => Array.isArray(parseBody(r)?.data),
  });

  readErrors.add(!ok);
  sleep(0.5);
}
