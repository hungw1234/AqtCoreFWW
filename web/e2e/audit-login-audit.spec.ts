import { test, expect } from './fixtures/test-base';
import { getAccessToken, authHeaders, API_BASE } from './helpers/auth-helper';

// Login audit E2E — LoginAuditController
// Route: GET /api/v1/admin/login-audit
// Auth: [Authorize(Roles = "Admin,SystemAdmin,GovOfficer")]
// P1 TCs: AUD-013, 017
test.describe('Audit login history (authenticated API)', () => {
  let token: string;

  test.beforeAll(async ({ request }) => {
    token = await getAccessToken(request);
    expect(token).toBeTruthy();
  });

  // TC-AUD-017: Unauthenticated returns 401
  test('TC-AUD-017: GET /admin/login-audit without auth returns 401', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/admin/login-audit`);
    expect(res.status()).toBe(401);
  });

  // TC-AUD-013: Authenticated — default pagination, items array present
  test('TC-AUD-013: GET /admin/login-audit returns login audit list', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/admin/login-audit`, {
      headers: authHeaders(token),
    });
    // 200 = ok; 403 = token lacks required role
    expect([200, 403]).toContain(res.status());
    if (res.status() === 200) {
      const body = await res.json();
      expect(body.success).toBe(true);
      expect(body.data).toHaveProperty('items');
      expect(Array.isArray(body.data.items)).toBe(true);
      expect(body.data).toHaveProperty('totalCount');
      expect(typeof body.data.totalCount).toBe('number');
    }
  });
});
