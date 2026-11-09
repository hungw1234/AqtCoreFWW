// AqtCoreFW — K6 AI module load test
// Purpose: TC-PERF-AI-001/002 — model profiles + prompt templates SLO
// Run: k6 run ai-load-test.js --env BASE_URL=https://staging.internal --env API_KEY=<key>

import http from 'k6/http';
import { check, group, sleep } from 'k6';
import { Rate, Trend } from 'k6/metrics';
import { getJsonApiKeyHeaders } from './lib/auth.js';
import { parseBody } from './lib/helpers.js';

// ─── Config ──────────────────────────────────────────────────────────────────

const BASE_URL = __ENV.BASE_URL || 'https://staging.internal';
const IS_DOCKER = (__ENV.ENVIRONMENT || '').toLowerCase() === 'docker';

// TC-PERF-AI-001/002: 30 VUs — AI config reads p95<300ms
export const options = {
  stages: [
    { duration: '1m', target: 30 },
    { duration: '2m', target: 30 },
    { duration: '1m', target: 0 },
  ],
  thresholds: {
    'http_req_duration{operation:ai-models}':    [IS_DOCKER ? 'p(95)<800' : 'p(95)<300'],
    'http_req_duration{operation:ai-templates}': [IS_DOCKER ? 'p(95)<800' : 'p(95)<300'],
    'http_req_failed': ['rate<0.05'],
  },
};

// ─── Custom metrics ─────────────────────────────────────────────────────────

const modelErrors      = new Rate('ai_model_errors');
const modelDuration    = new Trend('ai_model_duration');
const templateErrors   = new Rate('ai_template_errors');
const templateDuration = new Trend('ai_template_duration');

// ─── Main scenario ──────────────────────────────────────────────────────────

export default function () {
  const headers = getJsonApiKeyHeaders();

  // TC-PERF-AI-001: List AI model profiles
  group('AI model profiles', () => {
    const res = http.get(
      `${BASE_URL}/api/v1/ai/model-profiles`,
      { headers, tags: { operation: 'ai-models' } },
    );

    const ok = check(res, {
      'GET /ai/model-profiles status 200': (r) => r.status === 200,
      'GET /ai/model-profiles returns array': (r) => {
        const body = parseBody(r);
        return Array.isArray(body?.data);
      },
    });

    modelErrors.add(!ok);
    modelDuration.add(res.timings.duration);
  });

  sleep(0.3);

  // TC-PERF-AI-002: List prompt templates
  group('AI prompt templates', () => {
    const res = http.get(
      `${BASE_URL}/api/v1/ai/prompt-templates`,
      { headers, tags: { operation: 'ai-templates' } },
    );

    const ok = check(res, {
      'GET /ai/prompt-templates status 200': (r) => r.status === 200,
    });

    templateErrors.add(!ok);
    templateDuration.add(res.timings.duration);
  });

  sleep(0.5);
}

// ─── Summary ─────────────────────────────────────────────────────────────────

export function handleSummary(data) {
  const mp95 = data.metrics['ai_model_duration']?.values?.['p(95)'] ?? 0;
  const tp95 = data.metrics['ai_template_duration']?.values?.['p(95)'] ?? 0;

  console.log('\n=== AI LOAD TEST SUMMARY ===');
  console.log(`Model profiles p95 (ms)   : ${Math.round(mp95)}`);
  console.log(`Prompt templates p95 (ms) : ${Math.round(tp95)}`);

  return { 'stdout': JSON.stringify(data.metrics, null, 2) };
}
