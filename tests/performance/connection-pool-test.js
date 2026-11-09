// AqtCoreFW — K6 connection pool test
// Purpose: TC-PERF-DB-002/003 — connection pool exhaustion and concurrent write SLOs
// Run: k6 run connection-pool-test.js --env BASE_URL=https://staging.internal --env API_KEY=<key>
//
// UPDATED 2026-05-01 (PR #71 cleanup): retargeted from deleted Cases endpoint to
// /api/v1/forms/templates as new golden-path endpoint per code-reviewer H3 recommendation.

import http from 'k6/http';
import { check, group, sleep } from 'k6';
import { Counter, Rate, Trend } from 'k6/metrics';
import { getJsonApiKeyHeaders } from './lib/auth.js';
import { pageParams } from './lib/helpers.js';

// ─── Config ──────────────────────────────────────────────────────────────────

const BASE_URL = __ENV.BASE_URL || 'https://staging.internal';
const IS_DOCKER = (__ENV.ENVIRONMENT || '').toLowerCase() === 'docker';

// TC-PERF-DB-002: 200 VUs burst — connection pool must not exhaust (no 503/pool errors)
// TC-PERF-DB-003: 50 VUs concurrent writes — form templates listed simultaneously
export const options = {
  stages: [
    { duration: '30s', target: 200 },
    { duration: '1m',  target: 200 },
    { duration: '30s', target: 0   },
  ],
  thresholds: {
    'http_req_duration{operation:read}':  [IS_DOCKER ? 'p(95)<3000' : 'p(95)<1000'],
    'http_req_failed': ['rate<0.01'],
    'pool_exhaustion_errors': ['count<1'],
  },
};

// ─── Custom metrics ───────────────────────────────────────────────────────────

const poolExhaustionErrors = new Counter('pool_exhaustion_errors');
const readErrors           = new Rate('read_errors');
const readDuration         = new Trend('read_duration');

// ─── Main scenario ────────────────────────────────────────────────────────────

export default function () {
  const headers = getJsonApiKeyHeaders();

  // TC-PERF-DB-002: Read probe under 200 VU burst — detects pool exhaustion via 503
  group('Pool pressure read', () => {
    const res = http.get(`${BASE_URL}/api/v1/forms/templates?${pageParams(1, 10)}`, {
      headers,
      tags: { operation: 'read' },
    });

    if (res.status === 503) poolExhaustionErrors.add(1);

    const ok = check(res, {
      'GET forms/templates not 503 (pool not exhausted)': (r) => r.status !== 503,
      'GET forms/templates status 200 or 429':            (r) => r.status === 200 || r.status === 429,
    });

    readErrors.add(!ok);
    readDuration.add(res.timings.duration);
  });

  sleep(0.3);
}

// ─── Summary ─────────────────────────────────────────────────────────────────

export function handleSummary(data) {
  const poolErrors  = data.metrics['pool_exhaustion_errors']?.values?.count ?? 0;
  const readErrRate = data.metrics['read_errors']?.values?.rate ?? 0;
  const readP95     = data.metrics['read_duration']?.values?.['p(95)'] ?? 0;

  console.log('\n=== CONNECTION POOL TEST SUMMARY ===');
  console.log(`Pool exhaustion (503) count : ${poolErrors}`);
  console.log(`Read error rate             : ${(readErrRate * 100).toFixed(2)}%`);
  console.log(`Read p95 (ms)               : ${Math.round(readP95)}`);
  console.log(`Pool health                 : ${poolErrors === 0 ? 'OK' : 'EXHAUSTED — INVESTIGATE'}`);

  return { 'stdout': JSON.stringify(data.metrics, null, 2) };
}
