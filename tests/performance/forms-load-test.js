// AqtCoreFW — K6 forms load test
// Purpose: TC-PERF-FORM-001/002 — form template list and form submission SLOs
// Run: k6 run forms-load-test.js --env BASE_URL=https://staging.internal --env API_KEY=<key>

import http from 'k6/http';
import { check, group, sleep } from 'k6';
import { Counter, Rate } from 'k6/metrics';
import { getJsonApiKeyHeaders } from './lib/auth.js';
import { randomRef, pageParams, parseBody, getTenantId } from './lib/helpers.js';

// ─── Config ──────────────────────────────────────────────────────────────────

const BASE_URL = __ENV.BASE_URL || 'https://staging.internal';
const IS_DOCKER = (__ENV.ENVIRONMENT || '').toLowerCase() === 'docker';

// TC-PERF-FORM-001: 50 VUs — template list p95<500ms
// TC-PERF-FORM-002: 50 VUs — submission with 20 fields p95<1000ms
export const options = {
  stages: [
    { duration: '1m', target: 50 },  // ramp up
    { duration: '2m', target: 50 },  // hold
    { duration: '1m', target: 0  },  // ramp down
  ],
  thresholds: {
    'http_req_duration{operation:read}':  [IS_DOCKER ? 'p(95)<1000' : 'p(95)<500'],
    'http_req_duration{operation:write}': [IS_DOCKER ? 'p(95)<2000' : 'p(95)<1000'],
    'http_req_failed': ['rate<0.01'],
  },
};

// ─── Custom metrics ───────────────────────────────────────────────────────────

const templateListErrors  = new Rate('form_template_list_errors');
const submissionErrors    = new Rate('form_submission_errors');
const submissionsCreated  = new Counter('form_submissions_created_total');

// Build a 20-field form payload to simulate realistic submission size
function buildTemplatePayload() {
  const fields = [];
  for (let i = 1; i <= 20; i++) {
    fields.push({ name: `field_${i}`, label: `Field ${i}`, type: 'text', required: false });
  }
  return JSON.stringify({
    tenantId:    getTenantId(),
    name:        randomRef('TPL'),
    description: 'k6 load test template',
    fields,
  });
}

// ─── Main scenario ────────────────────────────────────────────────────────────

export default function () {
  const headers = getJsonApiKeyHeaders();
  let templateId = null;

  // TC-PERF-FORM-001: Form template list
  group('Form template list', () => {
    const res = http.get(`${BASE_URL}/api/v1/forms/templates?${pageParams(1, 20)}`, {
      headers,
      tags: { operation: 'read' },
    });

    const ok = check(res, {
      'GET /api/v1/forms/templates status 200': (r) => r.status === 200,
      'GET /api/v1/forms/templates has data': (r) => {
        const body = parseBody(r);
        const items = body?.data?.items;
        if (Array.isArray(items) && items.length > 0) {
          templateId = items[0].id;
        }
        return body?.success && Array.isArray(items);
      },
    });

    templateListErrors.add(!ok);
  });

  sleep(0.2);

  // TC-PERF-FORM-002: Form template create (POST)
  group('Create form template', () => {
    const payload = buildTemplatePayload();

    const res = http.post(`${BASE_URL}/api/v1/forms/templates`, payload, {
      headers,
      tags: { operation: 'write' },
    });

    const ok = check(res, {
      'POST /api/v1/forms/templates status 200 or 201': (r) =>
        r.status === 200 || r.status === 201,
      'POST /api/v1/forms/templates has id': (r) => {
        const body = parseBody(r);
        return body?.success && body?.data?.id !== undefined;
      },
    });

    submissionErrors.add(!ok);
    if (ok) submissionsCreated.add(1);
  });

  sleep(0.5);
}

// ─── Summary ─────────────────────────────────────────────────────────────────

export function handleSummary(data) {
  return { 'stdout': JSON.stringify(data.metrics, null, 2) };
}
