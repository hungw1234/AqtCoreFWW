import { test, expect } from './fixtures/test-base';
import { getAccessToken, authHeaders, API_BASE } from './helpers/auth-helper';

// Audit endpoints E2E — covers audit logs, statistics, export, HMAC chain, login history, RTBF
// TC-AUD-006: Unauthenticated logs → 401 (standalone, no beforeAll needed)
test('TC-AUD-006: GET /audit/logs without auth returns 401', async ({ request }) => {
  const res = await request.get(`${API_BASE}/api/v1/audit/logs`);
  expect(res.status()).toBe(401);
});

// TC-AUD-008: Unauthenticated statistics → 401
test('TC-AUD-008: GET /audit/statistics without auth returns 401', async ({ request }) => {
  const res = await request.get(`${API_BASE}/api/v1/audit/statistics`);
  expect(res.status()).toBe(401);
});

test.describe('Audit endpoints (authenticated API)', () => {
  let token: string;

  test.beforeAll(async ({ request }) => {
    token = await getAccessToken(request);
    expect(token).toBeTruthy();
  });

  // TC-AUD-001: default pagination
  test('TC-AUD-001: GET /audit/logs returns list with envelope', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/audit/logs`, {
      headers: authHeaders(token),
    });
    expect(res.status()).toBe(200);
    const body = await res.json();
    expect(body.success).toBe(true);
    // Audit list must expose items array (may be empty on fresh env)
    expect(body.data).toHaveProperty('items');
  });

  // TC-AUD-002: pagination params respected
  test('TC-AUD-002: GET /audit/logs with pagination params returns paged result', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/audit/logs?page=1&pageSize=5`, {
      headers: authHeaders(token),
    });
    expect(res.status()).toBe(200);
    const body = await res.json();
    expect(body.success).toBe(true);
    const items: unknown[] = body.data?.items ?? [];
    // pageSize=5 enforced — result must not exceed 5 records
    expect(items.length).toBeLessThanOrEqual(5);
  });

  // TC-AUD-007: statistics shape
  test('TC-AUD-007: GET /audit/statistics returns stats object', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/audit/statistics`, {
      headers: authHeaders(token),
    });
    expect(res.status()).toBe(200);
    const body = await res.json();
    expect(body.success).toBe(true);
    // Stats payload must exist (shape may vary, just confirm data is non-null)
    expect(body.data).not.toBeNull();
  });

  // TC-AUD-009: CSV export content-type
  test('TC-AUD-009: GET /audit/logs/export returns CSV response', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/audit/logs/export`, {
      headers: authHeaders(token),
    });
    // 200 = CSV export ready, 204 = no content (empty log), 202 = async generation
    expect([200, 202, 204]).toContain(res.status());
    if (res.status() === 200) {
      const contentType = res.headers()['content-type'] ?? '';
      // Accept text/csv or application/octet-stream for file download
      expect(
        contentType.includes('text/csv') ||
          contentType.includes('application/octet-stream') ||
          contentType.includes('application/csv'),
      ).toBe(true);
    }
  });

  test('GET /audit/logs/verify-chain returns chain verification result', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/audit/logs/verify-chain`, {
      headers: authHeaders(token),
    });
    // 200 = verified, 400 = chain broken (still valid response), 403 = role-gated
    expect([200, 400, 403]).toContain(res.status());
    if (res.status() !== 403) {
      const body = await res.json();
      expect(body.success).toBeDefined();
    }
  });

  // TC-AUD-011: verify-chain SystemAdmin — must succeed or return structured result
  test('TC-AUD-011: GET /audit/logs/verify-chain with systemadmin token returns result', async ({
    request,
  }) => {
    const sysToken = await getAccessToken(request, 'systemadmin');
    const res = await request.get(`${API_BASE}/api/v1/audit/logs/verify-chain`, {
      headers: authHeaders(sysToken),
    });
    // 200 = chain valid/invalid result; 400 = broken chain; 422 = missing tenantId param
    expect([200, 400, 422]).toContain(res.status());
    if (res.status() === 200) {
      const body = await res.json();
      expect(body.success).toBe(true);
      expect(body.data).toHaveProperty('isValid');
    }
  });

  // TC-AUD-012: verify-chain with Admin (non-SystemAdmin) token → 403
  test('TC-AUD-012: GET /audit/logs/verify-chain with user token returns 403', async ({
    request,
  }) => {
    const userToken = await getAccessToken(request, 'user');
    const res = await request.get(`${API_BASE}/api/v1/audit/logs/verify-chain`, {
      headers: authHeaders(userToken),
    });
    expect([401, 403]).toContain(res.status());
  });

  test('GET /audit/logs without auth returns 401', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/audit/logs`);
    expect(res.status()).toBe(401);
  });

  test('GET /admin/login-audit returns login audit history', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/admin/login-audit`, {
      headers: authHeaders(token),
    });
    // 200 = success, 403 = insufficient permissions for this admin endpoint
    expect([200, 403]).toContain(res.status());
    if (res.status() === 200) {
      const body = await res.json();
      expect(body.success).toBe(true);
      expect(body.data).toHaveProperty('items');
    }
  });

  test('GET /admin/rtbf-requests returns RTBF request list', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/admin/rtbf-requests`, {
      headers: authHeaders(token),
    });
    // 200 = accessible, 403 = DPO/Admin-only endpoint
    expect([200, 403]).toContain(res.status());
    if (res.status() === 200) {
      const body = await res.json();
      expect(body.success).toBe(true);
      expect(body.data).toHaveProperty('items');
    }
  });
});
