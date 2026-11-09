// AqtCoreFW — K6 integration module load test
// Purpose: TC-PERF-INTEG-001/002/003/004 — partners list/create, contracts list, message-log SLOs
// Run: k6 run integration-load-test.js --env BASE_URL=https://staging.internal --env API_KEY=<key>

import http from 'k6/http';
import { check, group, sleep } from 'k6';
import { Counter, Rate, Trend } from 'k6/metrics';
import { getJsonApiKeyHeaders } from './lib/auth.js';
import { randomRef, pageParams, parseBody } from './lib/helpers.js';

// ─── Config ──────────────────────────────────────────────────────────────────

const BASE_URL = __ENV.BASE_URL || 'https://staging.internal';
const IS_DOCKER = (__ENV.ENVIRONMENT || '').toLowerCase() === 'docker';

// TC-PERF-INTEG-001: 30 VUs, 2-min hold — partners list p95<500ms
// TC-PERF-INTEG-002: 30 VUs — partner create p95<1000ms
// TC-PERF-INTEG-003: 30 VUs — contracts list p95<500ms
// TC-PERF-INTEG-004: 30 VUs — message-log write p95<800ms
export const options = {
  stages: [
    { duration: '1m', target: 30 }, // ramp up
    { duration: '2m', target: 30 }, // hold
    { duration: '1m', target: 0  }, // ramp down
  ],
  thresholds: {
    'http_req_duration{operation:partners-list}':    [IS_DOCKER ? 'p(95)<2000' : 'p(95)<500'],
    'http_req_duration{operation:partner-create}':   [IS_DOCKER ? 'p(95)<3000' : 'p(95)<1000'],
    'http_req_duration{operation:contracts-list}':   [IS_DOCKER ? 'p(95)<2000' : 'p(95)<500'],
    'http_req_duration{operation:message-log-write}': [IS_DOCKER ? 'p(95)<2500' : 'p(95)<800'],
    'http_req_failed': ['rate<0.05'],
  },
};

// ─── Custom metrics ─────────────────────────────────────────────────────────

const partnersListErrors    = new Rate('integ_partners_list_errors');
const partnersListDuration  = new Trend('integ_partners_list_duration');
const partnerCreateErrors   = new Rate('integ_partner_create_errors');
const partnerCreateDuration = new Trend('integ_partner_create_duration');
const contractsListErrors   = new Rate('integ_contracts_list_errors');
const contractsListDuration = new Trend('integ_contracts_list_duration');
const msgLogErrors          = new Rate('integ_msg_log_errors');
const msgLogDuration        = new Trend('integ_msg_log_duration');
const partnersCreated       = new Counter('integ_partners_created_total');

// ─── Payload builders ───────────────────────────────────────────────────────

function partnerPayload() {
  return JSON.stringify({
    name:        `Load Partner ${randomRef('PART')}`,
    partnerType: 0,   // External
    contactEmail: `load-test-${Date.now()}@example.com`,
    country:     'VN',
  });
}

function messageLogPayload() {
  return JSON.stringify({
    direction:   0,   // Inbound
    channel:     0,   // Email
    externalRef: randomRef('MSG'),
    subject:     'Load test inbound message',
    body:        'k6 performance validation — automated message log entry',
    receivedAt:  new Date().toISOString(),
  });
}

// ─── Main scenario ──────────────────────────────────────────────────────────

export default function () {
  const headers = getJsonApiKeyHeaders();

  // TC-PERF-INTEG-001: List partners (paginated)
  group('List partners', () => {
    const res = http.get(
      `${BASE_URL}/api/v1/partners?${pageParams(1, 20)}`,
      { headers, tags: { operation: 'partners-list' } },
    );

    const ok = check(res, {
      'GET /partners status 200': (r) => r.status === 200,
      'GET /partners has items': (r) => {
        const body = parseBody(r);
        return body?.success && (Array.isArray(body?.data?.items) || Array.isArray(body?.data));
      },
    });

    partnersListErrors.add(!ok);
    partnersListDuration.add(res.timings.duration);
  });

  sleep(0.2);

  // TC-PERF-INTEG-002: Create partner
  group('Create partner', () => {
    const res = http.post(
      `${BASE_URL}/api/v1/partners`,
      partnerPayload(),
      { headers, tags: { operation: 'partner-create' } },
    );

    const ok = check(res, {
      'POST /partners status 200': (r) => r.status === 200,
      'POST /partners has id': (r) => {
        const body = parseBody(r);
        return body?.success && body?.data?.id !== undefined;
      },
    });

    partnerCreateErrors.add(!ok);
    partnerCreateDuration.add(res.timings.duration);

    if (ok) {
      partnersCreated.add(1);
    }
  });

  sleep(0.2);

  // TC-PERF-INTEG-003: List contracts (paginated)
  group('List contracts', () => {
    const res = http.get(
      `${BASE_URL}/api/v1/contracts?${pageParams(1, 20)}`,
      { headers, tags: { operation: 'contracts-list' } },
    );

    const ok = check(res, {
      'GET /contracts status 200': (r) => r.status === 200,
      'GET /contracts has items': (r) => {
        const body = parseBody(r);
        return body?.success && (Array.isArray(body?.data?.items) || Array.isArray(body?.data));
      },
    });

    contractsListErrors.add(!ok);
    contractsListDuration.add(res.timings.duration);
  });

  sleep(0.2);

  // TC-PERF-INTEG-004: Log message (write-heavy endpoint — polled in integration flows)
  group('Log message', () => {
    const res = http.post(
      `${BASE_URL}/api/v1/message-logs`,
      messageLogPayload(),
      { headers, tags: { operation: 'message-log-write' } },
    );

    const ok = check(res, {
      'POST /message-logs status 200': (r) => r.status === 200,
      'POST /message-logs has id': (r) => {
        const body = parseBody(r);
        return body?.success && body?.data?.id !== undefined;
      },
    });

    msgLogErrors.add(!ok);
    msgLogDuration.add(res.timings.duration);
  });

  sleep(0.5);
}

// ─── Summary ─────────────────────────────────────────────────────────────────

export function handleSummary(data) {
  const plP95  = data.metrics['integ_partners_list_duration']?.values?.['p(95)']   ?? 0;
  const pcP95  = data.metrics['integ_partner_create_duration']?.values?.['p(95)']  ?? 0;
  const clP95  = data.metrics['integ_contracts_list_duration']?.values?.['p(95)']  ?? 0;
  const mlP95  = data.metrics['integ_msg_log_duration']?.values?.['p(95)']         ?? 0;

  console.log('\n=== INTEGRATION LOAD TEST SUMMARY ===');
  console.log(`Partners list p95 (ms)   : ${Math.round(plP95)}`);
  console.log(`Partner create p95 (ms)  : ${Math.round(pcP95)}`);
  console.log(`Contracts list p95 (ms)  : ${Math.round(clP95)}`);
  console.log(`Message log p95 (ms)     : ${Math.round(mlP95)}`);

  return { 'stdout': JSON.stringify(data.metrics, null, 2) };
}
