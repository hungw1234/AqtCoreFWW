// AqtCoreFW — K6 load test
// Purpose: validate SLOs under sustained load (Phase 10: p95 read<200ms, write<400ms)
// Run: k6 run load-test.js --env BASE_URL=https://staging.internal --env API_KEY=<key>
//
// UPDATED 2026-05-01 (PR #71 cleanup): retargeted from deleted Cases endpoint to
// /api/v1/forms/templates as new golden-path endpoint per code-reviewer H3 recommendation.

import http from 'k6/http';
import { check, group, sleep } from 'k6';
import { Counter, Rate, Trend } from 'k6/metrics';
import { getJsonApiKeyHeaders } from './lib/auth.js';
import { parseBody } from './lib/helpers.js';

// ─── Config ──────────────────────────────────────────────────────────────────

const BASE_URL = __ENV.BASE_URL || 'https://staging.internal';
const IS_DOCKER = (__ENV.ENVIRONMENT || '').toLowerCase() === 'docker';

export const options = {
  stages: [
    { duration: '5m',  target: 500 },
    { duration: '20m', target: 500 },
    { duration: '5m',  target: 0   },
  ],
  thresholds: {
    'http_req_duration{operation:read}':  [IS_DOCKER ? 'p(95)<500' : 'p(95)<200'],
    'http_req_duration{operation:write}': [IS_DOCKER ? 'p(95)<800' : 'p(95)<400'],
    'http_req_failed': ['rate<0.01'],
  },
};

// ─── Custom metrics ───────────────────────────────────────────────────────────

const createErrors  = new Rate('create_errors');
const readErrors    = new Rate('read_errors');

// ─── Main scenario ────────────────────────────────────────────────────────────

export default function () {
  const headers = getJsonApiKeyHeaders();

  // ── LIST (read) ─────────────────────────────────────────────────────────────
  group('List form templates', () => {
    const res = http.get(`${BASE_URL}/api/v1/forms/templates?page=1&pageSize=20`, {
      headers,
      tags: { operation: 'read' },
    });

    const ok = check(res, {
      'GET /api/v1/forms/templates status 200': (r) => r.status === 200,
      'GET /api/v1/forms/templates has data':   (r) => {
        const body = parseBody(r);
        return body?.success && (Array.isArray(body?.data?.items) || Array.isArray(body?.data));
      },
    });

    readErrors.add(!ok);
  });

  sleep(0.5);
}
