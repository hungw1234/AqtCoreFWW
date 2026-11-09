// AqtCoreFW — K6 smoke test
// Purpose: validate baseline health after every deployment (fast, low VUs)
// Run: k6 run smoke-test.js --env BASE_URL=https://staging.internal
//
// UPDATED 2026-05-01 (PR #71 cleanup): retargeted from deleted Cases endpoint to
// /api/v1/forms/templates as new golden-path endpoint per code-reviewer H3 recommendation.

import http from 'k6/http';
import { check, sleep } from 'k6';
import { Rate } from 'k6/metrics';
import { parseBody } from './lib/helpers.js';

// ─── Config ──────────────────────────────────────────────────────────────────

const BASE_URL = __ENV.BASE_URL || 'https://staging.internal';
const IS_DOCKER = (__ENV.ENVIRONMENT || '').toLowerCase() === 'docker';

export const options = {
  vus: 5,
  duration: '1m',
  setupTimeout: '30s',
  thresholds: {
    http_req_failed: ['rate<0.01'],
    http_req_duration: [IS_DOCKER ? 'p(95)<500' : 'p(95)<200'],
    'http_req_duration{endpoint:health}':      [IS_DOCKER ? 'p(95)<200' : 'p(95)<100'],
    'http_req_duration{endpoint:forms-list}':  [IS_DOCKER ? 'p(95)<500' : 'p(95)<200'],
  },
};

// ─── Custom metrics ───────────────────────────────────────────────────────────

const healthCheckErrors = new Rate('health_check_errors');
const formsEndpointErrors = new Rate('forms_endpoint_errors');

// ─── Test scenarios ───────────────────────────────────────────────────────────

export default function () {
  http.setResponseCallback(http.expectedStatuses(200, 201, 204, 401));

  // --- Health endpoint (ready probe) ---
  const healthRes = http.get(`${BASE_URL}/health/ready`, {
    tags: { endpoint: 'health' },
  });

  const healthOk = check(healthRes, {
    'GET /health/ready status 200': (r) => r.status === 200,
    'GET /health/ready response time < 100ms': (r) => r.timings.duration < 100,
    'GET /health/ready body contains "Healthy"': (r) => r.body && r.body.includes('Healthy'),
  });

  healthCheckErrors.add(!healthOk);

  sleep(0.5);

  // --- Forms templates list endpoint (golden-path after Cases module removal 2026-05-01) ---
  const formsRes = http.get(`${BASE_URL}/api/v1/forms/templates`, {
    headers: { Accept: 'application/json' },
    tags: { endpoint: 'forms-list' },
  });

  const formsOk = check(formsRes, {
    'GET /api/v1/forms/templates status 200 or 401': (r) => r.status === 200 || r.status === 401,
    'GET /api/v1/forms/templates response time < 200ms': (r) => r.timings.duration < 200,
  });

  formsEndpointErrors.add(!formsOk);

  sleep(0.5);
}

// ─── Setup / teardown ─────────────────────────────────────────────────────────

export function handleSummary(data) {
  return {
    'stdout': JSON.stringify(data, null, 2),
  };
}
