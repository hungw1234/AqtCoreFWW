// AqtCoreFW — K6 rules module load test
// Purpose: TC-PERF-RULE-001/002 — ruleset create + detail SLO
// Run: k6 run rules-load-test.js --env BASE_URL=https://staging.internal --env API_KEY=<key>

import http from 'k6/http';
import { check, group, sleep } from 'k6';
import { Rate, Trend } from 'k6/metrics';
import { getJsonApiKeyHeaders } from './lib/auth.js';
import { randomRef, parseBody, getTenantId } from './lib/helpers.js';

// ─── Config ──────────────────────────────────────────────────────────────────

const BASE_URL = __ENV.BASE_URL || 'https://staging.internal';
const IS_DOCKER = (__ENV.ENVIRONMENT || '').toLowerCase() === 'docker';

// TC-PERF-RULE-001/002: 30 VUs — rule set operations p95<500ms
export const options = {
  stages: [
    { duration: '1m', target: 30 },
    { duration: '2m', target: 30 },
    { duration: '1m', target: 0 },
  ],
  thresholds: {
    'http_req_duration{operation:rule-create}': [IS_DOCKER ? 'p(95)<1500' : 'p(95)<800'],
    'http_req_duration{operation:rule-detail}': [IS_DOCKER ? 'p(95)<1000' : 'p(95)<500'],
    'http_req_failed': ['rate<0.05'],
  },
};

// ─── Custom metrics ─────────────────────────────────────────────────────────

const ruleCreateErrors   = new Rate('rule_create_errors');
const ruleCreateDuration = new Trend('rule_create_duration');
const ruleDetailErrors   = new Rate('rule_detail_errors');
const ruleDetailDuration = new Trend('rule_detail_duration');

// ─── Main scenario ──────────────────────────────────────────────────────────

export default function () {
  const headers = getJsonApiKeyHeaders();
  let ruleSetId = null;

  // TC-PERF-RULE-001: Create draft rule set
  group('RuleSet create', () => {
    const payload = JSON.stringify({
      tenantId: getTenantId(),
      name: `K6 RuleSet ${randomRef('RULE')}`,
      description: 'k6 perf test — auto-created',
      category: 'Test',
      targetEntityType: 'Case',
    });

    const res = http.post(
      `${BASE_URL}/api/v1/rulesets`,
      payload,
      { headers, tags: { operation: 'rule-create' } },
    );

    const ok = check(res, {
      'POST /rulesets status 200|422': (r) => [200, 422].includes(r.status),
    });

    ruleCreateErrors.add(!ok);
    ruleCreateDuration.add(res.timings.duration);

    // Extract created ID for detail fetch
    if (res.status === 200) {
      const body = parseBody(res);
      ruleSetId = body?.data?.id ?? body?.data;
    }
  });

  sleep(0.3);

  // TC-PERF-RULE-002: Get rule set detail
  group('RuleSet detail', () => {
    // Use created ID or a fake one (will return 404 — still measures route latency)
    const id = ruleSetId || '00000000-0000-0000-0000-000000000099';
    const res = http.get(
      `${BASE_URL}/api/v1/rulesets/${id}`,
      { headers, tags: { operation: 'rule-detail' } },
    );

    const ok = check(res, {
      'GET /rulesets/{id} status 200|404': (r) => [200, 404].includes(r.status),
    });

    ruleDetailErrors.add(!ok);
    ruleDetailDuration.add(res.timings.duration);
  });

  sleep(0.5);
}

// ─── Summary ─────────────────────────────────────────────────────────────────

export function handleSummary(data) {
  const cp95 = data.metrics['rule_create_duration']?.values?.['p(95)'] ?? 0;
  const dp95 = data.metrics['rule_detail_duration']?.values?.['p(95)'] ?? 0;

  console.log('\n=== RULES LOAD TEST SUMMARY ===');
  console.log(`Create p95 (ms) : ${Math.round(cp95)}`);
  console.log(`Detail p95 (ms) : ${Math.round(dp95)}`);

  return { 'stdout': JSON.stringify(data.metrics, null, 2) };
}
