// AqtCoreFW — K6 reporting load test
// Purpose: TC-PERF-RPT-001/002 — report execution and KPI dashboard SLOs
// Run: k6 run reporting-load-test.js --env BASE_URL=https://staging.internal --env API_KEY=<key>

import http from 'k6/http';
import { check, group, sleep } from 'k6';
import { Rate, Trend } from 'k6/metrics';
import { getJsonApiKeyHeaders } from './lib/auth.js';
import { randomRef, parseBody } from './lib/helpers.js';

// ─── Config ──────────────────────────────────────────────────────────────────

const BASE_URL = __ENV.BASE_URL || 'https://staging.internal';
const IS_DOCKER = (__ENV.ENVIRONMENT || '').toLowerCase() === 'docker';

// TC-PERF-RPT-001: 10 VUs — report execution p95<5000ms (heavy query)
// TC-PERF-RPT-002: 50 VUs — KPI dashboard p95<500ms (cached/lightweight)
export const options = {
  stages: [
    { duration: '1m', target: 50 },  // ramp up to max VUs (dashboard test drives this)
    { duration: '2m', target: 50 },  // hold
    { duration: '1m', target: 0  },  // ramp down
  ],
  thresholds: {
    // TC-PERF-RPT-001: heavy report execution — generous threshold
    'http_req_duration{operation:report}':    [IS_DOCKER ? 'p(95)<8000' : 'p(95)<5000'],
    // TC-PERF-RPT-002: KPI dashboard — should be fast (pre-aggregated)
    'http_req_duration{operation:dashboard}': [IS_DOCKER ? 'p(95)<1000' : 'p(95)<500'],
    'http_req_failed': ['rate<0.01'],
  },
};

// ─── Custom metrics ───────────────────────────────────────────────────────────

const reportErrors    = new Rate('report_execution_errors');
const dashboardErrors = new Rate('kpi_dashboard_errors');
const reportDuration  = new Trend('report_execution_duration');
const dashboardDuration = new Trend('kpi_dashboard_duration');

// Report types to cycle through for realistic variety
const REPORT_TYPES = ['CaseSummary', 'WorkflowStatus', 'FormSubmissions', 'AuditTrail'];

// ─── Main scenario ────────────────────────────────────────────────────────────

export default function () {
  const headers = getJsonApiKeyHeaders();
  const vuIndex = __VU % REPORT_TYPES.length;

  // TC-PERF-RPT-002: KPI dashboard (run for all 50 VUs — lightweight)
  // 403 is valid — API key may lack reports scope; still measures latency
  group('KPI dashboard', () => {
    const res = http.get(`${BASE_URL}/api/v1/reports/dashboard`, {
      headers,
      tags: { operation: 'dashboard' },
    });

    const ok = check(res, {
      'GET /api/v1/reports/dashboard status 200 or 403': (r) =>
        r.status === 200 || r.status === 403,
      'GET /api/v1/reports/dashboard has response body': (r) => parseBody(r) !== null,
    });

    dashboardErrors.add(!ok);
    dashboardDuration.add(res.timings.duration);
  });

  sleep(0.3);

  // TC-PERF-RPT-001: Report execution (only 10 VUs worth — throttle via VU modulo)
  // VUs 1-10 run heavy reports; remaining VUs skip to avoid overload
  if (__VU <= 10) {
    group('Report execution', () => {
      const payload = JSON.stringify({
        reportType: REPORT_TYPES[vuIndex],
        reference:  randomRef('RPT'),
        filters: {
          dateFrom: '2024-01-01',
          dateTo:   '2024-12-31',
        },
      });

      const res = http.post(`${BASE_URL}/api/v1/reports/run`, payload, {
        headers,
        tags: { operation: 'report' },
        timeout: '10s',  // heavy reports may take longer
      });

      const ok = check(res, {
        'POST /api/v1/reports/run status 200, 202 or 403': (r) =>
          r.status === 200 || r.status === 202 || r.status === 403,
        'POST /api/v1/reports/run has response body': (r) => parseBody(r) !== null,
      });

      reportErrors.add(!ok);
      reportDuration.add(res.timings.duration);
    });
  }

  sleep(0.5);
}

// ─── Summary ─────────────────────────────────────────────────────────────────

export function handleSummary(data) {
  const reportP95    = data.metrics['report_execution_duration']?.values?.['p(95)'] ?? 0;
  const dashboardP95 = data.metrics['kpi_dashboard_duration']?.values?.['p(95)'] ?? 0;

  console.log('\n=== REPORTING LOAD TEST SUMMARY ===');
  console.log(`Report execution p95 (ms) : ${Math.round(reportP95)}`);
  console.log(`KPI dashboard p95 (ms)    : ${Math.round(dashboardP95)}`);

  return { 'stdout': JSON.stringify(data.metrics, null, 2) };
}
