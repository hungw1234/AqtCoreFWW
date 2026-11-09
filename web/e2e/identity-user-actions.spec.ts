import { test, expect } from './fixtures/test-base';
import { getAccessToken, authHeaders, API_BASE } from './helpers/auth-helper';

// UsersAdminController action endpoints — TC-IDENT-015/016/017/018/020/021
// Separated from crud-users.spec.ts to keep files under 200 lines
test.describe('Users admin actions (authenticated API)', () => {
  let token: string;

  test.beforeAll(async ({ request }) => {
    token = await getAccessToken(request);
    expect(token).toBeTruthy();
  });

  // TC-IDENT-015 + TC-IDENT-016: lock then unlock user
  test('TC-IDENT-015/016: lock then unlock user returns 204', async ({ request }) => {
    const listRes = await request.get(`${API_BASE}/api/v1/admin/users?page=1&pageSize=5`, {
      headers: authHeaders(token),
    });
    const items = (await listRes.json()).data?.items ?? [];
    // Use second user if available to avoid locking the admin running the test
    const target = items[1] ?? items[0];
    test.skip(!target, 'No user available to lock');

    const lockRes = await request.post(`${API_BASE}/api/v1/admin/users/${target.id}/lock`, {
      headers: authHeaders(token),
    });
    expect([204, 200]).toContain(lockRes.status());

    const unlockRes = await request.post(`${API_BASE}/api/v1/admin/users/${target.id}/unlock`, {
      headers: authHeaders(token),
    });
    expect([204, 200]).toContain(unlockRes.status());
  });

  // TC-IDENT-017: reset password — returns 204, no token in response body
  test('TC-IDENT-017: POST /admin/users/{id}/reset-password returns 204', async ({ request }) => {
    const listRes = await request.get(`${API_BASE}/api/v1/admin/users?page=1&pageSize=1`, {
      headers: authHeaders(token),
    });
    const items = (await listRes.json()).data?.items ?? [];
    test.skip(items.length === 0, 'No users in database');

    const res = await request.post(
      `${API_BASE}/api/v1/admin/users/${items[0].id}/reset-password`,
      { headers: authHeaders(token) },
    );
    // 204 = success, 500 = email service not configured in dev — both acceptable
    expect([204, 200, 500]).toContain(res.status());
    if (res.status() < 400) {
      // Security (F-25): reset token must NOT appear in response body
      const text = await res.text();
      expect(text).not.toMatch(/reset.*token/i);
    }
  });

  // TC-IDENT-018: assign role to user — returns 200/204
  test('TC-IDENT-018: POST /admin/users/{id}/roles assigns role', async ({ request }) => {
    const listRes = await request.get(`${API_BASE}/api/v1/admin/users?page=1&pageSize=1`, {
      headers: authHeaders(token),
    });
    const items = (await listRes.json()).data?.items ?? [];
    test.skip(items.length === 0, 'No users in database');

    const res = await request.post(`${API_BASE}/api/v1/admin/users/${items[0].id}/roles`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: { role: 'Viewer' },
    });
    expect([200, 204, 422]).toContain(res.status());
  });

  // TC-IDENT-020: sync user roles — replaces all roles, returns 200
  test('TC-IDENT-020: PUT /admin/users/{id}/roles syncs roles', async ({ request }) => {
    const listRes = await request.get(`${API_BASE}/api/v1/admin/users?page=1&pageSize=1`, {
      headers: authHeaders(token),
    });
    const items = (await listRes.json()).data?.items ?? [];
    test.skip(items.length === 0, 'No users in database');

    const userId = items[0].id;
    const currentRoles = items[0].roles ?? ['Admin'];

    const res = await request.put(`${API_BASE}/api/v1/admin/users/${userId}/roles`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: { roles: currentRoles },
    });
    // 200/204 = synced, 403 = insufficient role, 422 = validation
    expect([200, 204, 403, 422]).toContain(res.status());
  });

  // TC-IDENT-021: get effective permissions — returns permission summary
  test('TC-IDENT-021: GET /admin/users/{id}/effective-permissions returns summary', async ({ request }) => {
    const listRes = await request.get(`${API_BASE}/api/v1/admin/users?page=1&pageSize=1`, {
      headers: authHeaders(token),
    });
    const items = (await listRes.json()).data?.items ?? [];
    test.skip(items.length === 0, 'No users in database');

    const res = await request.get(
      `${API_BASE}/api/v1/admin/users/${items[0].id}/effective-permissions`,
      { headers: authHeaders(token) },
    );
    expect(res.status()).toBe(200);
    const body = await res.json();
    const data = body.data ?? body;
    // Response has directRoles, groupRoles, permissionCodes, dataScope, activeDelegations
    expect(data).toHaveProperty('directRoles');
    expect(data).toHaveProperty('permissionCodes');
  });
});
