// AqtCoreFW — K6 spike test
// Purpose: validate circuit breaker and graceful degradation under sudden traffic spikes
// Run: k6 run spike-test.js --env BASE_URL=https://staging.internal --env API_KEY=<key>
//
// UPDATED 2026-05-01 (PR #71 cleanup): retargeted from deleted Cases endpoint to
// /api/v1/forms/templates as new golden-path endpoint per code-reviewer H3 recommendation.

import http from 'k6/http';
import { check, group, sleep } from 'k6';
import { Rate, Trend, Counter } from 'k6/metrics';
import { getApiKeyHeaders } from './lib/auth.js';

// ─── Config ──────────────────────────────────────────────────────────────────

const BASE_URL = __ENV.BASE_URL || 'https://staging.internal';

export const options = {
  stages: [
    { duration: '1m',  target: 10   },
    { duration: '30s', target: 1000 },
    { duration: '1m',  target: 1000 },
    { duration: '30s', target: 10   },
    { duration: '2m',  target: 10   },
  ],
  thresholds: {
    'http_req_failed{phase:spike}':    ['rate<0.30'],
    'http_req_failed{phase:recovery}': ['rate<0.01'],
    'circuit_breaker_activations':     ['count>0'],
  },
};

// ─── Custom metrics ───────────────────────────────────────────────────────────

const circuitBreakerActivations = new Counter('circuit_breaker_activations');
const spikeErrors               = new Rate('spike_errors');
const recoveryErrors            = new Rate('recovery_errors');
const spikeDuration             = new Trend('spike_response_duration');
const recoveryDuration          = new Trend('recovery_response_duration');
const recoveryTimeTrend         = new Trend('recovery_time_to_baseline');

function getPhase(elapsed) {
  if (elapsed < 60)  return 'baseline';
  if (elapsed < 150) return 'spike';
  return 'recovery';
}

// ─── Setup ────────────────────────────────────────────────────────────────────

export function setup() {
  return { startTime: Date.now() };
}

// ─── Main scenario ────────────────────────────────────────────────────────────

export default function (data) {
  const elapsed = (Date.now() - data.startTime) / 1000;
  const phase   = getPhase(elapsed);
  const headers = getApiKeyHeaders();

  // Health probe — monitors circuit breaker state
  group('Health probe', () => {
    const res = http.get(`${BASE_URL}/health`, {
      headers,
      tags: { operation: 'health', phase },
    });

    if (res.status === 503) circuitBreakerActivations.add(1);

    const ok = check(res, {
      'health 200 or 503 (cb open)': (r) => r.status === 200 || r.status === 503,
    });

    if (phase === 'spike')    spikeErrors.add(!ok);
    if (phase === 'recovery') recoveryErrors.add(!ok);
    if (phase === 'spike')    spikeDuration.add(res.timings.duration);
    if (phase === 'recovery') {
      recoveryDuration.add(res.timings.duration);
      recoveryTimeTrend.add(res.timings.duration);
    }
  });

  sleep(0.05);

  // Forms templates under spike pressure (golden-path after Cases removal 2026-05-01)
  group('Forms list under spike', () => {
    const res = http.get(`${BASE_URL}/api/v1/forms/templates?page=1&pageSize=5`, {
      headers,
      tags: { operation: 'read', phase },
    });

    check(res, {
      'forms 200, 429 (rate-limited), or 503 (cb open)': (r) =>
        r.status === 200 || r.status === 429 || r.status === 503,
    });

    if (res.status === 503) circuitBreakerActivations.add(1);
  });

  sleep(0.05);
}

// ─── Summary ─────────────────────────────────────────────────────────────────

export function handleSummary(data) {
  const cbCount      = data.metrics['circuit_breaker_activations']?.values?.count ?? 0;
  const spikeErrRate = data.metrics['spike_errors']?.values?.rate ?? 0;
  const recovErrRate = data.metrics['recovery_errors']?.values?.rate ?? 0;
  const recovP50     = data.metrics['recovery_time_to_baseline']?.values?.['p(50)'] ?? 0;
  const recovP95     = data.metrics['recovery_time_to_baseline']?.values?.['p(95)'] ?? 0;
  const recovMax     = data.metrics['recovery_time_to_baseline']?.values?.max ?? 0;

  const summary = {
    circuitBreakerActivations: cbCount,
    spikePhaseErrorRate:       (spikeErrRate * 100).toFixed(2) + '%',
    recoveryPhaseErrorRate:    (recovErrRate * 100).toFixed(2) + '%',
    circuitBreakerWorking:     cbCount > 0,
    systemRecovered:           recovErrRate < 0.01,
    recoveryTime: {
      p50Ms: Math.round(recovP50),
      p95Ms: Math.round(recovP95),
      maxMs: Math.round(recovMax),
      atBaselineP95: recovP95 < 200,
    },
  };

  console.log('\n=== SPIKE TEST — CIRCUIT BREAKER ANALYSIS ===');
  console.log(`Circuit breaker activations : ${cbCount}`);
  console.log(`Spike phase error rate      : ${summary.spikePhaseErrorRate}`);
  console.log(`Recovery phase error rate   : ${summary.recoveryPhaseErrorRate}`);
  console.log(`System recovered            : ${summary.systemRecovered ? 'YES' : 'NO - INVESTIGATE'}`);

  return {
    'stdout': JSON.stringify(summary, null, 2),
    'tests/performance/spike-result.json': JSON.stringify(summary, null, 2),
  };
}
