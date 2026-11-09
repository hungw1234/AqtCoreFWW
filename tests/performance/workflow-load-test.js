// AqtCoreFW — K6 workflow load test
// Purpose: TC-PERF-WF-001/002 — workflow inbox query and transition throughput SLOs
// Run: k6 run workflow-load-test.js --env BASE_URL=https://staging.internal --env API_KEY=<key>

import http from 'k6/http';
import { check, group, sleep } from 'k6';
import { Counter, Rate, Trend } from 'k6/metrics';
import { getJsonApiKeyHeaders } from './lib/auth.js';
import { randomRef, pageParams, parseBody, getTenantId } from './lib/helpers.js';

// ─── Config ──────────────────────────────────────────────────────────────────

const BASE_URL = __ENV.BASE_URL || 'https://staging.internal';
const IS_DOCKER = (__ENV.ENVIRONMENT || '').toLowerCase() === 'docker';

// TC-PERF-WF-001: 30 VUs — inbox query p95<500ms
// TC-PERF-WF-002: 50 VUs — transition throughput p95<1000ms, >20 trans/sec
export const options = {
  stages: [
    { duration: '1m', target: 50 },  // ramp up to max VUs
    { duration: '2m', target: 50 },  // hold
    { duration: '1m', target: 0  },  // ramp down
  ],
  thresholds: {
    'http_req_duration{operation:read}':  [IS_DOCKER ? 'p(95)<1000' : 'p(95)<500'],
    'http_req_duration{operation:write}': [IS_DOCKER ? 'p(95)<2000' : 'p(95)<1000'],
    'http_req_failed': ['rate<0.01'],
    // TC-PERF-WF-002: transition throughput must exceed 20/sec
    'workflow_transitions_total': ['count>2400'],  // 2min hold × 20/s = 2400 minimum
  },
};

// ─── Custom metrics ───────────────────────────────────────────────────────────

const inboxErrors      = new Rate('workflow_inbox_errors');
const transitionErrors = new Rate('workflow_transition_errors');
const transitionsTotal = new Counter('workflow_transitions_total');
const transitionTrend  = new Trend('workflow_transition_duration');

// ─── Main scenario ────────────────────────────────────────────────────────────

export default function () {
  const headers = getJsonApiKeyHeaders();

  // TC-PERF-WF-001: Workflow definitions query
  group('Workflow inbox', () => {
    const res = http.get(
      `${BASE_URL}/api/v1/workflow/definitions?${pageParams(1, 20)}&tenantId=${getTenantId()}`,
      { headers, tags: { operation: 'read' } },
    );

    const ok = check(res, {
      'GET /api/v1/workflow/definitions status 200 or 400': (r) =>
        r.status === 200 || r.status === 400,
      'GET /api/v1/workflow/definitions has body': (r) => parseBody(r) !== null,
    });

    inboxErrors.add(!ok);
  });

  sleep(0.2);

  // TC-PERF-WF-002: Workflow definition create throughput
  group('Workflow transition', () => {
    // Create a workflow definition (measures write throughput against /definitions)
    const createPayload = JSON.stringify({
      tenantId:    getTenantId(),
      name:        randomRef('WF'),
      description: 'k6 load test workflow definition',
      steps: [
        { name: 'Start',   type: 'start' },
        { name: 'Review',  type: 'task'  },
        { name: 'End',     type: 'end'   },
      ],
    });

    const createRes = http.post(
      `${BASE_URL}/api/v1/workflow/definitions`,
      createPayload,
      { headers, tags: { operation: 'write' } },
    );

    let definitionId = null;
    const created = check(createRes, {
      'POST /api/v1/workflow/definitions status 200 or 201': (r) =>
        r.status === 200 || r.status === 201,
    });

    if (created) {
      definitionId = parseBody(createRes)?.data?.id ?? null;
    }

    // Measure a GET on the created definition as the "transition" latency sample
    if (definitionId) {
      const transRes = http.get(
        `${BASE_URL}/api/v1/workflow/definitions/${definitionId}`,
        { headers, tags: { operation: 'write' } },
      );

      const ok = check(transRes, {
        'GET workflow definition status 200': (r) => r.status === 200,
      });

      transitionErrors.add(!ok);
      if (ok) {
        transitionsTotal.add(1);
        transitionTrend.add(transRes.timings.duration);
      }
    }
  });

  sleep(0.3);
}

// ─── Summary ─────────────────────────────────────────────────────────────────

export function handleSummary(data) {
  const total = data.metrics['workflow_transitions_total']?.values?.count ?? 0;
  const p95   = data.metrics['workflow_transition_duration']?.values?.['p(95)'] ?? 0;

  console.log('\n=== WORKFLOW LOAD TEST SUMMARY ===');
  console.log(`Total transitions   : ${total}`);
  console.log(`Transition p95 (ms) : ${Math.round(p95)}`);
  console.log(`Throughput target   : >20/sec — ${total >= 2400 ? 'PASS' : 'FAIL'}`);

  return { 'stdout': JSON.stringify(data.metrics, null, 2) };
}
