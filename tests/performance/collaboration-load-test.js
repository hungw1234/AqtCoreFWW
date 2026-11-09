// AqtCoreFW — K6 collaboration module load test
// Purpose: TC-PERF-COLLAB-001/002 — conversation list + unread count SLO
// Run: k6 run collaboration-load-test.js --env BASE_URL=https://staging.internal --env API_KEY=<key>

import http from 'k6/http';
import { check, group, sleep } from 'k6';
import { Rate, Trend } from 'k6/metrics';
import { getJsonApiKeyHeaders } from './lib/auth.js';
import { pageParams, parseBody } from './lib/helpers.js';

// ─── Config ──────────────────────────────────────────────────────────────────

const BASE_URL = __ENV.BASE_URL || 'https://staging.internal';
const IS_DOCKER = (__ENV.ENVIRONMENT || '').toLowerCase() === 'docker';

// TC-PERF-COLLAB-001/002: 50 VUs — conversation queries p95<300ms
export const options = {
  stages: [
    { duration: '1m', target: 50 },
    { duration: '2m', target: 50 },
    { duration: '1m', target: 0 },
  ],
  thresholds: {
    'http_req_duration{operation:conv-list}':    [IS_DOCKER ? 'p(95)<800'  : 'p(95)<300'],
    'http_req_duration{operation:unread-count}': [IS_DOCKER ? 'p(95)<500'  : 'p(95)<200'],
    'http_req_failed': ['rate<0.05'],
  },
};

// ─── Custom metrics ─────────────────────────────────────────────────────────

const convListErrors     = new Rate('conv_list_errors');
const convListDuration   = new Trend('conv_list_duration');
const unreadErrors       = new Rate('unread_count_errors');
const unreadDuration     = new Trend('unread_count_duration');

// ─── Main scenario ──────────────────────────────────────────────────────────

export default function () {
  const headers = getJsonApiKeyHeaders();

  // TC-PERF-COLLAB-001: List conversations
  group('Conversation list', () => {
    const res = http.get(
      `${BASE_URL}/api/v1/conversations?${pageParams(1, 20)}`,
      { headers, tags: { operation: 'conv-list' } },
    );

    const ok = check(res, {
      'GET /conversations status 200': (r) => r.status === 200,
      'GET /conversations has data array': (r) => {
        const body = parseBody(r);
        return Array.isArray(body?.data);
      },
    });

    convListErrors.add(!ok);
    convListDuration.add(res.timings.duration);
  });

  sleep(0.3);

  // TC-PERF-COLLAB-002: Unread message count (polled frequently by FE)
  group('Unread count', () => {
    const res = http.get(
      `${BASE_URL}/api/v1/messages/unread-count`,
      { headers, tags: { operation: 'unread-count' } },
    );

    const ok = check(res, {
      'GET /messages/unread-count status 200': (r) => r.status === 200,
    });

    unreadErrors.add(!ok);
    unreadDuration.add(res.timings.duration);
  });

  sleep(0.5);
}

// ─── Summary ─────────────────────────────────────────────────────────────────

export function handleSummary(data) {
  const lp95 = data.metrics['conv_list_duration']?.values?.['p(95)'] ?? 0;
  const up95 = data.metrics['unread_count_duration']?.values?.['p(95)'] ?? 0;

  console.log('\n=== COLLABORATION LOAD TEST SUMMARY ===');
  console.log(`Conv list p95 (ms)    : ${Math.round(lp95)}`);
  console.log(`Unread count p95 (ms) : ${Math.round(up95)}`);

  return { 'stdout': JSON.stringify(data.metrics, null, 2) };
}
