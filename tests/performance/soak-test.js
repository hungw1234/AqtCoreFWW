// AqtCoreFW — K6 soak test
// Purpose: detect memory leaks and resource exhaustion under sustained load (60 min)
// Run: k6 run soak-test.js --env BASE_URL=https://staging.internal --env API_KEY=<key>
//
// UPDATED 2026-05-01 (PR #71 cleanup): retargeted from deleted Cases endpoint to
// /api/v1/forms/templates as new golden-path endpoint per code-reviewer H3 recommendation.

import http from 'k6/http';
import { check, group, sleep } from 'k6';
import { Trend, Rate, Counter } from 'k6/metrics';
import { getApiKeyHeaders } from './lib/auth.js';

// ─── Config ──────────────────────────────────────────────────────────────────

const BASE_URL = __ENV.BASE_URL || 'https://staging.internal';

export const options = {
  stages: [
    // Gentle ramp-up to avoid false memory spike on startup
    { duration: '5m',  target: 300 },
    // Sustain 300 VUs for 60 minutes — long enough to expose memory leaks
    { duration: '60m', target: 300 },
    // Ramp down
    { duration: '5m',  target: 0   },
  ],
  thresholds: {
    'http_req_failed': ['rate<0.01'],
    'http_req_duration': ['p(95)<500', 'p(99)<1000'],
  },
};

// ─── Custom metrics ───────────────────────────────────────────────────────────

const earlyPhaseDuration  = new Trend('duration_early_phase');
const middlePhaseDuration = new Trend('duration_middle_phase');
const latePhaseDuration   = new Trend('duration_late_phase');
const responseTimeTrend   = new Trend('soak_response_time_trend');
const requestErrors       = new Rate('soak_request_errors');
const totalRequests       = new Counter('soak_total_requests');

function phaseMetric(elapsed) {
  if (elapsed < 1200) return earlyPhaseDuration;
  if (elapsed < 2400) return middlePhaseDuration;
  return latePhaseDuration;
}

// ─── Main scenario ────────────────────────────────────────────────────────────

export function setup() {
  return { startTime: Date.now() };
}

export default function (data) {
  const elapsed = (Date.now() - data.startTime) / 1000;
  const headers = getApiKeyHeaders();

  // Health check — fastest probe to detect cascading failures early
  group('Health probe', () => {
    const res = http.get(`${BASE_URL}/health`, { headers, tags: { operation: 'health' } });
    const ok = check(res, { 'health 200': (r) => r.status === 200 });
    requestErrors.add(!ok);
    phaseMetric(elapsed).add(res.timings.duration);
    responseTimeTrend.add(res.timings.duration);
    totalRequests.add(1);
  });

  sleep(0.3);

  // Form templates list — representative read workload (golden-path after Cases removal 2026-05-01)
  group('Form templates list', () => {
    const res = http.get(`${BASE_URL}/api/v1/forms/templates?page=1&pageSize=10`, {
      headers,
      tags: { operation: 'read' },
    });

    const ok = check(res, {
      'forms list 200': (r) => r.status === 200,
    });

    requestErrors.add(!ok);
    phaseMetric(elapsed).add(res.timings.duration);
    responseTimeTrend.add(res.timings.duration);
    totalRequests.add(1);
  });

  sleep(0.7);
}

// ─── Memory-leak detection summary ───────────────────────────────────────────

export function handleSummary(data) {
  const early  = data.metrics['duration_early_phase']?.values?.['p(95)']  ?? 0;
  const middle = data.metrics['duration_middle_phase']?.values?.['p(95)'] ?? 0;
  const late   = data.metrics['duration_late_phase']?.values?.['p(95)']   ?? 0;

  const degradationRatio = early > 0 ? (late / early) : 1;
  const leakSuspected    = degradationRatio > 1.5;
  const driftPct         = early > 0 ? ((late - early) / early) * 100 : 0;
  const driftExceeded    = driftPct > 20;
  const trendP95         = data.metrics['soak_response_time_trend']?.values?.['p(95)'] ?? 0;

  const summary = {
    memoryLeakAnalysis: {
      earlyPhaseP95Ms:   Math.round(early),
      middlePhaseP95Ms:  Math.round(middle),
      latePhaseP95Ms:    Math.round(late),
      degradationRatio:  degradationRatio.toFixed(2),
      leakSuspected,
      driftPct:          driftPct.toFixed(1),
      driftExceeded,
      rollingTrendP95Ms: Math.round(trendP95),
    },
  };

  console.log('\n=== SOAK TEST MEMORY LEAK ANALYSIS ===');
  console.log(`Early  phase p95: ${summary.memoryLeakAnalysis.earlyPhaseP95Ms} ms`);
  console.log(`Middle phase p95: ${summary.memoryLeakAnalysis.middlePhaseP95Ms} ms`);
  console.log(`Late   phase p95: ${summary.memoryLeakAnalysis.latePhaseP95Ms} ms`);
  console.log(`Degradation ratio: ${summary.memoryLeakAnalysis.degradationRatio}x`);
  console.log(`Memory leak suspected: ${leakSuspected ? 'YES - INVESTIGATE' : 'No'}`);

  return {
    'stdout': JSON.stringify(summary, null, 2),
    'tests/performance/soak-result.json': JSON.stringify(summary, null, 2),
  };
}
