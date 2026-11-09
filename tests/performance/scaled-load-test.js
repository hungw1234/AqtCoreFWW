// AqtCoreFW — Scaled load test (500 / 1000 / 2000 CCU tiers)
// Purpose: validate SLOs across different deployment sizing tiers
//
// Run by tier:
//   k6 run scaled-load-test.js --env BASE_URL=https://staging.internal --env API_KEY=<key> --env TIER=500
//   k6 run scaled-load-test.js --env BASE_URL=https://staging.internal --env API_KEY=<key> --env TIER=1000
//   k6 run scaled-load-test.js --env BASE_URL=https://staging.internal --env API_KEY=<key> --env TIER=2000
//
// UPDATED 2026-05-01 (PR #71 cleanup): retargeted from deleted Cases endpoint to
// /api/v1/forms/templates as new golden-path endpoint per code-reviewer H3 recommendation.

import http from 'k6/http';
import { check, group, sleep } from 'k6';
import { Rate } from 'k6/metrics';
import { getApiKeyHeaders } from './lib/auth.js';
import { pageParams, parseBody } from './lib/helpers.js';

// ─── Tier Config ─────────────────────────────────────────────────────────────

const BASE_URL  = __ENV.BASE_URL  || 'https://staging.internal';
const TIER      = parseInt(__ENV.TIER || '500', 10);
const TENANT_ID = __ENV.TENANT_ID || '00000000-0000-0000-0000-000000000001';

const TIERS = {
  500:  { vus: 500,  read: 200,  write: 400,  errorRate: 0.01 },
  1000: { vus: 1000, read: 300,  write: 600,  errorRate: 0.01 },
  2000: { vus: 2000, read: 500,  write: 1000, errorRate: 0.02 },
};

const cfg = TIERS[TIER] || TIERS[500];

export const options = {
  stages: [
    { duration: '5m',  target: Math.round(cfg.vus * 0.25) },
    { duration: '5m',  target: Math.round(cfg.vus * 0.75) },
    { duration: '5m',  target: cfg.vus },
    { duration: '20m', target: cfg.vus },
    { duration: '5m',  target: 0 },
  ],
  thresholds: {
    [`http_req_duration{operation:read}`]:  [`p(95)<${cfg.read}`],
    'http_req_failed': [`rate<${cfg.errorRate}`],
  },
};

// ─── Custom metrics ───────────────────────────────────────────────────────────

const readErrors = new Rate('read_errors');

// ─── Main scenario ────────────────────────────────────────────────────────────

export function setup() {
  return { tier: TIER, cfg };
}

export default function (data) {
  const readHeaders = getApiKeyHeaders();

  // ── LIST (paginated read) ────────────────────────────────────────────────────
  group('List form templates', () => {
    const res = http.get(
      `${BASE_URL}/api/v1/forms/templates?tenantId=${TENANT_ID}&${pageParams(1, 20)}`,
      { headers: readHeaders, tags: { operation: 'read' } }
    );

    const ok = check(res, {
      'GET /forms/templates 200':      (r) => r.status === 200,
      'GET /forms/templates has data': (r) => {
        const body = parseBody(r);
        return Array.isArray(body?.data?.items) || Array.isArray(body?.data);
      },
    });

    readErrors.add(!ok);
  });

  sleep(0.7);
}

// ─── Summary ─────────────────────────────────────────────────────────────────

export function handleSummary(data) {
  const p95Read = data.metrics['http_req_duration{operation:read}']?.values?.['p(95)'] ?? 0;
  const errRate = data.metrics['http_req_failed']?.values?.rate ?? 0;

  const summary = {
    tier:          TIER,
    sloRead_ms:    cfg.read,
    p95Read_ms:    Math.round(p95Read),
    errorRate_pct: (errRate * 100).toFixed(2) + '%',
    sloMet: p95Read <= cfg.read && errRate <= cfg.errorRate,
  };

  console.log(`\n=== SCALED LOAD TEST — TIER ${TIER} CCU ===`);
  console.log(`p95 read : ${summary.p95Read_ms}ms  (SLO: <${cfg.read}ms)  ${summary.p95Read_ms <= cfg.read ? 'PASS' : 'BREACHED'}`);
  console.log(`Error rate: ${summary.errorRate_pct}  (SLO: <${cfg.errorRate * 100}%)`);
  console.log(`SLOs met: ${summary.sloMet ? 'YES' : 'NO'}`);

  return {
    'stdout': JSON.stringify(summary, null, 2),
    [`/tests/results/scaled-${TIER}-result.json`]: JSON.stringify(summary, null, 2),
  };
}
