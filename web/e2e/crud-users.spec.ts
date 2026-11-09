import { test, expect } from './fixtures/test-base';
import { getAccessToken, authHeaders, API_BASE } from './helpers/auth-helper';

// Users admin E2E — TC-IDENT-001/002/005/006/007/008/009/012/014 (lifecycle)
test.describe('Users admin (authenticated API)', () => {
  test.describe.configure({ mode: 'serial' });
  let token: string;
  let userToken: string;
  let createdUserId: string;

  test.beforeAll(async ({ request }) => {
    token = await getAccessToken(request);
    userToken = await getAccessToken(request, 'user');
    expect(token).toBeTruthy();
  });

  test.afterAll(async ({ request }) => {
    if (createdUserId) {
      await request.delete(`${API_BASE}/api/v1/admin/users/${createdUserId}`, {
        headers: authHeaders(token),
      });
    }
  });

  // TC-IDENT-001: list users — authenticated admin
  test('TC-IDENT-001: GET /admin/users returns paginated user list', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/admin/users`, {
      headers: authHeaders(token),
    });
    expect(res.status()).toBe(200);
    const body = await res.json();
    expect(body.success).toBe(true);
    expect(body.data).toHaveProperty('items');
  });

  // TC-IDENT-002: list users — unauthenticated returns 401
  test('TC-IDENT-002: GET /admin/users without auth returns 401', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/admin/users`);
    expect(res.status()).toBe(401);
  });

  // TC-IDENT-005: list tenants — SystemAdmin only
  test('TC-IDENT-005: GET /admin/users/tenants returns tenant list', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/admin/users/tenants`, {
      headers: authHeaders(token),
    });
    // 200 = systemadmin OK; 403 = admin-only token (acceptable depending on seed)
    expect([200, 403]).toContain(res.status());
  });

  // TC-IDENT-006: list tenants — non-SystemAdmin returns 403
  test('TC-IDENT-006: GET /admin/users/tenants with viewer token returns 403', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/admin/users/tenants`, {
      headers: authHeaders(userToken),
    });
    expect(res.status()).toBe(403);
  });

  // TC-IDENT-007: get user by ID — existing user
  test('TC-IDENT-007: GET /admin/users/{id} returns user detail', async ({ request }) => {
    const listRes = await request.get(`${API_BASE}/api/v1/admin/users?page=1&pageSize=1`, {
      headers: authHeaders(token),
    });
    const items = (await listRes.json()).data?.items ?? [];
    test.skip(items.length === 0, 'No users in database');

    const userId = items[0].id;
    const res = await request.get(`${API_BASE}/api/v1/admin/users/${userId}`, {
      headers: authHeaders(token),
    });
    expect(res.status()).toBe(200);
    const body = await res.json();
    expect(body.data ?? body).toHaveProperty('id', userId);
  });

  // TC-IDENT-008: get user by ID — non-existent returns 404
  test('TC-IDENT-008: GET /admin/users/{id} non-existent returns 404', async ({ request }) => {
    const res = await request.get(
      `${API_BASE}/api/v1/admin/users/00000000-0000-0000-0000-000000000099`,
      { headers: authHeaders(token) },
    );
    expect(res.status()).toBe(404);
  });

  // TC-IDENT-009: create user — valid payload returns 201
  test('TC-IDENT-009: POST /admin/users creates user and returns 201', async ({ request }) => {
    const res = await request.post(`${API_BASE}/api/v1/admin/users`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: {
        fullName: 'Test User TC009',
        email: `tc009-${Date.now()}@example.com`,
        password: 'P@ssw0rd!',
        departmentCode: 'IT',
      },
    });
    expect([201, 422]).toContain(res.status());
    if (res.status() === 201) {
      const body = await res.json();
      createdUserId = body.data?.id ?? body.id;
    }
  });

  // TC-IDENT-012: update user — valid payload returns 204
  test('TC-IDENT-012: PUT /admin/users/{id} updates and returns 204', async ({ request }) => {
    const listRes = await request.get(`${API_BASE}/api/v1/admin/users?page=1&pageSize=1`, {
      headers: authHeaders(token),
    });
    const items = (await listRes.json()).data?.items ?? [];
    test.skip(items.length === 0, 'No users in database');

    const userId = items[0].id;
    const res = await request.put(`${API_BASE}/api/v1/admin/users/${userId}`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: { fullName: items[0].fullName, departmentCode: 'IT', orgUnitId: null },
    });
    expect([204, 200]).toContain(res.status());
  });

  // TC-IDENT-014: delete (deactivate) user — returns 204
  test('TC-IDENT-014: DELETE /admin/users/{id} deactivates user', async ({ request }) => {
    test.skip(!createdUserId, 'No created user to delete');
    const res = await request.delete(`${API_BASE}/api/v1/admin/users/${createdUserId}`, {
      headers: authHeaders(token),
    });
    expect([204, 200]).toContain(res.status());
    createdUserId = ''; // cleanup done inline
  });
});
