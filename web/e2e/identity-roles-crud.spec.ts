import { test, expect } from './fixtures/test-base';
import { getAccessToken, authHeaders, API_BASE } from './helpers/auth-helper';

// RolesAdminController P1 TCs — TC-IDENT-022/023
// Route prefix: GET /api/v1/admin/roles
// Auth: [Authorize(Policy = "Admin")]
test.describe('Identity Roles admin (authenticated API)', () => {
  let token: string;
  let userToken: string;

  test.beforeAll(async ({ request }) => {
    token = await getAccessToken(request);
    userToken = await getAccessToken(request, 'user');
    expect(token).toBeTruthy();
  });

  // TC-IDENT-022: get all roles — authenticated admin
  test('TC-IDENT-022: GET /admin/roles returns role list', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/admin/roles`, {
      headers: authHeaders(token),
    });
    expect(res.status()).toBe(200);
    const body = await res.json();
    const items = body.data?.items ?? body.data ?? body;
    expect(Array.isArray(items)).toBe(true);
    if (items.length > 0) {
      expect(items[0]).toHaveProperty('name');
    }
  });

  // TC-IDENT-023: get all roles — unauthenticated returns 401
  test('TC-IDENT-023: GET /admin/roles without auth returns 401', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/admin/roles`);
    expect(res.status()).toBe(401);
  });

  // Extra: non-admin viewer token returns 403
  test('GET /admin/roles with viewer token returns 403', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/admin/roles`, {
      headers: authHeaders(userToken),
    });
    expect(res.status()).toBe(403);
  });
});
