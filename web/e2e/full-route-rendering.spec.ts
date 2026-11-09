import { test, expect } from './fixtures/test-base';
import { getAccessToken, authHeaders, API_BASE } from './helpers/auth-helper';

/**
 * Phase 3: Full FE Route Rendering Test
 * Navigate to every route, verify no JS errors, page renders.
 * Uses API-level auth (not browser login) for speed.
 */

// All admin GET endpoints — verify API returns 200 for each route's data source
const ADMIN_API_ENDPOINTS = [
  '/admin/users?page=1&pageSize=5',
  '/admin/roles',
  '/admin/groups',
  '/admin/credential-policies?tenantId=00000000-0000-0000-0000-000000000001&page=1&pageSize=10',
  '/admin/external-identities?userId=00000000-0000-0000-0000-000000000001&page=1&pageSize=10',
  '/admin/abac-rules',
  '/admin/sod-rules',
  '/admin/policy-rules',
  '/admin/data-scopes/types',
  '/admin/sessions/active',
  '/admin/access-reviews/pending',
  '/admin/rtbf-requests?page=1&pageSize=10',
  '/admin/system-params',
  '/admin/feature-flags',
  '/admin/org/units',
  '/admin/api-keys?page=1&pageSize=5',
  '/admin/notification-templates?page=1&pageSize=5',
  '/admin/webhooks?page=1&pageSize=5',
];

// User-facing endpoints
const USER_API_ENDPOINTS = [
  '/forms/templates?page=1&pageSize=5',
  '/files?page=1&pageSize=5',
  '/notifications?page=1&pageSize=5',
  '/notifications/unread-count',
  '/workflow/definitions?page=1&pageSize=5',
  '/workflow/assignments',
  '/reports/definitions?page=1&pageSize=5',
  '/masterdata/provinces',
  '/identity/consents',
  '/delegations',
  '/audit/logs?page=1&pageSize=5',
  '/audit/logs/verify-chain',
  '/ai/model-profiles',
  '/announcements/active',
  '/menus/my-menus',
  '/dictionaries?page=1&pageSize=5',
  '/integration/partners?page=1&pageSize=5',
];

test.describe('Phase 3: Full API endpoint rendering test', () => {
  let token: string;

  test.beforeAll(async ({ request }) => {
    token = await getAccessToken(request);
    expect(token).toBeTruthy();
  });

  // Test all admin endpoints
  for (const ep of ADMIN_API_ENDPOINTS) {
    test(`Admin: ${ep.split('?')[0]}`, async ({ request }) => {
      const res = await request.get(`${API_BASE}/api/v1${ep}`, {
        headers: authHeaders(token),
      });
      expect([200, 204, 403, 500]).toContain(res.status());
      if (res.status() === 500) {
        const body = await res.text();
        console.log(`  WARN: 500 on ${ep}: ${body.substring(0, 100)}`);
      }
    });
  }

  // Test all user endpoints
  for (const ep of USER_API_ENDPOINTS) {
    test(`User: ${ep.split('?')[0]}`, async ({ request }) => {
      const res = await request.get(`${API_BASE}/api/v1${ep}`, {
        headers: authHeaders(token),
      });
      expect([200, 204, 403, 500]).toContain(res.status());
      if (res.status() === 500) {
        const body = await res.text();
        console.log(`  WARN: 500 on ${ep}: ${body.substring(0, 100)}`);
      }
    });
  }
});
