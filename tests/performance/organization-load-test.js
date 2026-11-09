// AqtCoreFW — K6 organization load test
// Purpose: TC-PERF-ORG-001/002 — org tree query and descendants CTE SLOs
// Run: k6 run organization-load-test.js --env BASE_URL=https://staging.internal --env API_KEY=<key>

import http from 'k6/http';
import { check, group, sleep } from 'k6';
import { Rate, Trend } from 'k6/metrics';
import { getJsonApiKeyHeaders } from './lib/auth.js';
import { pageParams, parseBody } from './lib/helpers.js';

// ─── Config ──────────────────────────────────────────────────────────────────

const BASE_URL = __ENV.BASE_URL || 'https://staging.internal';
const IS_DOCKER = (__ENV.ENVIRONMENT || '').toLowerCase() === 'docker';

// TC-PERF-ORG-001: 30 VUs — org tree query p95<800ms
// TC-PERF-ORG-002: 30 VUs — org descendants CTE p95<500ms
export const options = {
  stages: [
    { duration: '1m', target: 30 },  // ramp up
    { duration: '2m', target: 30 },  // hold
    { duration: '1m', target: 0  },  // ramp down
  ],
  thresholds: {
    // TC-PERF-ORG-001: tree query — more expensive recursive operation
    'http_req_duration{operation:tree}':        [IS_DOCKER ? 'p(95)<1600' : 'p(95)<800'],
    // TC-PERF-ORG-002: descendants CTE — optimised recursive query
    'http_req_duration{operation:descendants}': [IS_DOCKER ? 'p(95)<1000' : 'p(95)<500'],
    'http_req_failed': ['rate<0.01'],
  },
};

// ─── Custom metrics ───────────────────────────────────────────────────────────

const treeErrors        = new Rate('org_tree_errors');
const descendantErrors  = new Rate('org_descendants_errors');
const treeDuration      = new Trend('org_tree_duration');
const descendantDuration = new Trend('org_descendants_duration');

// ─── Main scenario ────────────────────────────────────────────────────────────

export default function () {
  const headers = getJsonApiKeyHeaders();
  let rootUnitId = null;

  // TC-PERF-ORG-001: Org tree query (full hierarchy)
  group('Org tree query', () => {
    const res = http.get(`${BASE_URL}/api/v1/admin/org/units`, {
      headers,
      tags: { operation: 'tree' },
    });

    const ok = check(res, {
      'GET /api/v1/admin/org/units status 200': (r) => r.status === 200,
      'GET org tree has data': (r) => {
        const body = parseBody(r);
        const items = body?.data?.items;
        if (Array.isArray(items) && items.length > 0) {
          rootUnitId = items[0].id;
        }
        return Array.isArray(items);
      },
    });

    treeErrors.add(!ok);
    treeDuration.add(res.timings.duration);
  });

  sleep(0.2);

  // TC-PERF-ORG-002: Org descendants CTE (recursive SQL query from a known node)
  group('Org descendants CTE', () => {
    // Use a fixed root unit or the one discovered above
    const unitId = rootUnitId || '1';
    const res = http.get(
      `${BASE_URL}/api/v1/admin/org/units/${unitId}/descendants?${pageParams(1, 50)}`,
      { headers, tags: { operation: 'descendants' } },
    );

    const ok = check(res, {
      'GET /api/v1/admin/org/units/:id/descendants status 200': (r) => r.status === 200,
      'GET org descendants has data array': (r) => {
        const body = parseBody(r);
        return Array.isArray(body?.data?.items ?? body?.data);
      },
    });

    descendantErrors.add(!ok);
    descendantDuration.add(res.timings.duration);
  });

  sleep(0.5);
}

// ─── Summary ─────────────────────────────────────────────────────────────────

export function handleSummary(data) {
  const treeP95 = data.metrics['org_tree_duration']?.values?.['p(95)'] ?? 0;
  const descP95 = data.metrics['org_descendants_duration']?.values?.['p(95)'] ?? 0;

  console.log('\n=== ORGANIZATION LOAD TEST SUMMARY ===');
  console.log(`Tree query p95 (ms)        : ${Math.round(treeP95)}`);
  console.log(`Descendants CTE p95 (ms)   : ${Math.round(descP95)}`);

  return { 'stdout': JSON.stringify(data.metrics, null, 2) };
}
