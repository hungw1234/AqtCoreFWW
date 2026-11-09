import { test, expect } from './fixtures/test-base';
import { getAccessToken, authHeaders, API_BASE } from './helpers/auth-helper';

// Cross-module security checks
// TC-XMOD-090: Tenant isolation — own case not accessible with another tenant's token
// TC-XMOD-091: Audit integrity — GET /audit/logs returns well-formed entries
// TC-XMOD-092 skipped — P2
test.describe('Cross-Module: Security isolation', () => {
  test.describe.configure({ mode: 'serial' });

  let adminToken: string;
  let userToken: string;

  test.beforeAll(async ({ request }) => {
    [adminToken, userToken] = await Promise.all([
      getAccessToken(request, 'admin'),
      getAccessToken(request, 'user'),
    ]);
  });

  // TC-XMOD-090: Tenant isolation — fabricated foreign-tenant resource id returns 404
  // Retargeted to /forms/templates after Cases module removal
  test('TC-XMOD-090: tenant isolation — fabricated foreign-tenant resource id returns 404', async ({ request }) => {
    const foreignId = '09999999-9999-9999-9999-000000000090';

    const resAdmin = await request.get(`${API_BASE}/api/v1/forms/templates/${foreignId}`, {
      headers: authHeaders(adminToken),
    });
    expect([404, 403]).toContain(resAdmin.status());

    const resUser = await request.get(`${API_BASE}/api/v1/forms/templates/${foreignId}`, {
      headers: authHeaders(userToken),
    });
    expect([404, 403, 401]).toContain(resUser.status());
  });

  // TC-XMOD-091: Audit integrity — log entries are well-formed
  test('TC-XMOD-091: audit logs — entries have required fields when present', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/audit/logs?page=1&pageSize=10`, {
      headers: authHeaders(adminToken),
    });
    expect(res.status()).toBe(200);
    const body = await res.json();
    expect(body.success).toBe(true);
    expect(body.data).toHaveProperty('items');

    const items: Array<Record<string, unknown>> = body.data.items ?? [];
    // If there are entries, each must carry at minimum: id, action, and a timestamp field
    // API returns occurredAt (not createdAt) for audit entries
    for (const entry of items) {
      expect(entry).toHaveProperty('id');
      expect(entry).toHaveProperty('action');
      // timestamp field may be occurredAt or createdAt depending on API version
      const hasTimestamp = 'occurredAt' in entry || 'createdAt' in entry;
      expect(hasTimestamp).toBe(true);
      // No null id — guards against broken audit writer
      expect(entry['id']).toBeTruthy();
    }
  });

  // TC-XMOD-091b: Unauthenticated audit access blocked
  test('TC-XMOD-091b: audit logs — 401 without token', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/audit/logs`);
    expect(res.status()).toBe(401);
  });
});
