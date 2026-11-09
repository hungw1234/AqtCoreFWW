// AqtCoreFW — Notifications module load test
// Purpose: validate in-app notification list SLO
// SLO: p95 < 200ms | error rate < 1%
// Run: k6 run notifications-load-test.js --env BASE_URL=https://staging.internal --env API_KEY=<key>

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
    'http_req_duration{operation:notif-read}': ['p(95)<200'],
    'http_req_failed': ['rate<0.01'],
  },
};

// ─── Custom metrics ───────────────────────────────────────────────────────────

const readErrors = new Rate('notifications_read_errors');

// ─── Main scenario ────────────────────────────────────────────────────────────

export default function () {
  // Route verified: [Route("api/v1/notifications")] in NotificationsController
  const res = http.get(
    `${BASE_URL}/api/v1/notifications?${pageParams(1, 20)}`,
    { headers: getApiKeyHeaders(), tags: { operation: 'notif-read' } }
  );

  const ok = check(res, {
    'GET /notifications status 200': (r) => r.status === 200,
  });

  readErrors.add(!ok);
  sleep(0.5);
}
