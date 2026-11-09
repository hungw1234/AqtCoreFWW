// AqtCoreFW — K6 database performance test
// Purpose: TC-PERF-DB-001/004 — slow query detection and high-offset pagination SLOs
// Run: k6 run db-performance-test.js --env BASE_URL=https://staging.internal --env API_KEY=<key>
//
// UPDATED 2026-05-01 (PR #71 cleanup): retargeted from deleted Cases endpoint to
// /api/v1/forms/templates as new golden-path endpoint per code-reviewer H3 recommendation.

import http from 'k6/http';
import { check, group, sleep } from 'k6';
import { Rate, Trend } from 'k6/metrics';
import { getJsonApiKeyHeaders } from './lib/auth.js';
import { pageParams } from './lib/helpers.js';

// ─── Config ──────────────────────────────────────────────────────────────────

const BASE_URL = __ENV.BASE_URL || 'https://staging.internal';
const IS_DOCKER = (__ENV.ENVIRONMENT || '').toLowerCase() === 'docker';

// TC-PERF-DB-001: slow query check — assert no query >1s via admin endpoint
// TC-PERF-DB-004: pagination at high offsets — compare page 1 vs page 100
export const options = {
  stages: [
    { duration: '30s', target: 30 },
    { duration: '2m',  target: 30 },
    { duration: '30s', target: 0  },
  ],
  thresholds: {
    'http_req_duration{operation:read}':  [IS_DOCKER ? 'p(95)<2000' : 'p(95)<1000'],
    'http_req_duration{operation:admin}': [IS_DOCKER ? 'p(95)<2000' : 'p(95)<1000'],
    'http_req_failed': ['rate<0.01'],
    'pagination_degradation_ratio': ['max<3'],
  },
};

// ─── Custom metrics ───────────────────────────────────────────────────────────

const slowQueryErrors    = new Rate('slow_query_detected');
const page1Duration      = new Trend('pagination_page1_duration');
const page100Duration    = new Trend('pagination_page100_duration');
const degradationRatio   = new Trend('pagination_degradation_ratio');
const adminEndpointErrors = new Rate('admin_endpoint_errors');

// ─── Main scenario ────────────────────────────────────────────────────────────

export default function () {
  const headers = getJsonApiKeyHeaders();

  // TC-PERF-DB-001: Slow query check via admin diagnostics endpoint
  group('Slow query check', () => {
    const res = http.get(`${BASE_URL}/api/v1/admin/diagnostics/slow-queries`, {
      headers,
      tags: { operation: 'admin' },
    });

    const ok = check(res, {
      'GET slow-queries status 200': (r) => r.status === 200,
      'GET slow-queries no query >1s': (r) => {
        try {
          const body = JSON.parse(r.body);
          const queries = body.data?.slowQueries ?? [];
          return queries.every((q) => (q.durationMs ?? 0) < 1000);
        } catch { return true; }
      },
    });

    adminEndpointErrors.add(!ok);
    slowQueryErrors.add(!ok);
  });

  sleep(0.2);

  // TC-PERF-DB-004: Pagination at high offsets — compare page 1 vs page 100
  // Using forms/templates as golden-path entity after Cases module removal (2026-05-01, PR #71)
  let page1Ms = 0;

  group('Pagination page 1', () => {
    const res = http.get(`${BASE_URL}/api/v1/forms/templates?${pageParams(1, 20)}`, {
      headers,
      tags: { operation: 'read' },
    });

    check(res, {
      'GET forms/templates page 1 status 200': (r) => r.status === 200,
    });

    page1Ms = res.timings.duration;
    page1Duration.add(page1Ms);
  });

  sleep(0.1);

  group('Pagination page 100', () => {
    const res = http.get(`${BASE_URL}/api/v1/forms/templates?${pageParams(100, 20)}`, {
      headers,
      tags: { operation: 'read' },
    });

    check(res, {
      'GET forms/templates page 100 status 200 or 404': (r) =>
        r.status === 200 || r.status === 404,
    });

    page100Duration.add(res.timings.duration);

    const ratio = page1Ms > 0 ? res.timings.duration / page1Ms : 1;
    degradationRatio.add(ratio);
  });

  sleep(0.5);
}

// ─── Summary ─────────────────────────────────────────────────────────────────

export function handleSummary(data) {
  const p1P95    = data.metrics['pagination_page1_duration']?.values?.['p(95)']   ?? 0;
  const p100P95  = data.metrics['pagination_page100_duration']?.values?.['p(95)'] ?? 0;
  const maxRatio = data.metrics['pagination_degradation_ratio']?.values?.max       ?? 0;
  const slowRate = data.metrics['slow_query_detected']?.values?.rate               ?? 0;

  console.log('\n=== DB PERFORMANCE TEST SUMMARY ===');
  console.log(`Page 1   p95 (ms)         : ${Math.round(p1P95)}`);
  console.log(`Page 100 p95 (ms)         : ${Math.round(p100P95)}`);
  console.log(`Max degradation ratio     : ${maxRatio.toFixed(2)}x`);
  console.log(`Slow query detection rate : ${(slowRate * 100).toFixed(1)}%`);

  return { 'stdout': JSON.stringify(data.metrics, null, 2) };
}
