import { test, expect } from './fixtures/test-base';
import { getAccessToken, authHeaders, API_BASE } from './helpers/auth-helper';

// GroupsAdminController P1 TCs — TC-IDENT-024/025/027/029/030/031/032/033
// Route prefix: /api/v1/admin/groups
// Auth: [Authorize(Policy = "Admin")]
test.describe('Identity Groups admin (authenticated API)', () => {
  test.describe.configure({ mode: 'serial' });
  let token: string;
  let groupId: string;
  let userId: string;
  let roleId: string;
  const groupCode = `TC027-GRP-${Date.now()}`;

  test.beforeAll(async ({ request }) => {
    token = await getAccessToken(request);
    expect(token).toBeTruthy();

    // Fetch a userId for member tests
    const usersRes = await request.get(`${API_BASE}/api/v1/admin/users?page=1&pageSize=1`, {
      headers: authHeaders(token),
    });
    const users = (await usersRes.json()).data?.items ?? [];
    userId = users[0]?.id ?? '';

    // Fetch a roleId for role assignment tests
    const rolesRes = await request.get(`${API_BASE}/api/v1/admin/roles`, {
      headers: authHeaders(token),
    });
    const rolesBody = await rolesRes.json();
    const roles = rolesBody.data?.items ?? rolesBody.data ?? rolesBody;
    roleId = Array.isArray(roles) ? (roles[0]?.id ?? '') : '';
  });

  test.afterAll(async ({ request }) => {
    if (groupId) {
      // Best-effort cleanup — mark inactive via PUT if no DELETE endpoint
      await request.put(`${API_BASE}/api/v1/admin/groups/${groupId}`, {
        headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
        data: { name: groupCode, description: 'cleanup', isActive: false },
      });
    }
  });

  // TC-IDENT-024: list groups — authenticated admin
  test('TC-IDENT-024: GET /admin/groups returns group list', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/admin/groups`, {
      headers: authHeaders(token),
    });
    expect(res.status()).toBe(200);
    const body = await res.json();
    const items = body.data?.items ?? body.data ?? body;
    expect(Array.isArray(items)).toBe(true);
  });

  // TC-IDENT-027: create group — valid payload returns 201
  test('TC-IDENT-027: POST /admin/groups creates group', async ({ request }) => {
    const res = await request.post(`${API_BASE}/api/v1/admin/groups`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: { code: groupCode, name: 'TC027 Group', description: 'Test group' },
    });
    expect([201, 409]).toContain(res.status());
    if (res.status() === 201) {
      const body = await res.json();
      groupId = body.data?.id ?? body.id;
    }
  });

  // TC-IDENT-025: get group by ID — existing group
  test('TC-IDENT-025: GET /admin/groups/{id} returns group detail', async ({ request }) => {
    test.skip(!groupId, 'No group created');
    const res = await request.get(`${API_BASE}/api/v1/admin/groups/${groupId}`, {
      headers: authHeaders(token),
    });
    expect(res.status()).toBe(200);
    const body = await res.json();
    const data = body.data ?? body;
    expect(data).toHaveProperty('id', groupId);
  });

  // TC-IDENT-029: update group — valid payload returns 204
  test('TC-IDENT-029: PUT /admin/groups/{id} updates group', async ({ request }) => {
    test.skip(!groupId, 'No group created');
    const res = await request.put(`${API_BASE}/api/v1/admin/groups/${groupId}`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: { name: 'Updated Group Name', description: 'New desc', isActive: true },
    });
    expect([204, 200]).toContain(res.status());
  });

  // TC-IDENT-030: add member to group — idempotent, returns 204
  test('TC-IDENT-030: POST /admin/groups/{id}/members adds member', async ({ request }) => {
    test.skip(!groupId || !userId, 'Missing group or user');
    const res = await request.post(`${API_BASE}/api/v1/admin/groups/${groupId}/members`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: { userId },
    });
    expect([204, 200]).toContain(res.status());

    // Idempotent: second call should also succeed
    const res2 = await request.post(`${API_BASE}/api/v1/admin/groups/${groupId}/members`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: { userId },
    });
    expect([204, 200, 409]).toContain(res2.status());
  });

  // TC-IDENT-031: remove member from group — returns 204
  test('TC-IDENT-031: DELETE /admin/groups/{id}/members/{userId} removes member', async ({ request }) => {
    test.skip(!groupId || !userId, 'Missing group or user');
    const res = await request.delete(`${API_BASE}/api/v1/admin/groups/${groupId}/members/${userId}`, {
      headers: authHeaders(token),
    });
    expect([204, 200, 404]).toContain(res.status());
  });

  // TC-IDENT-032: assign role to group — returns 204
  test('TC-IDENT-032: POST /admin/groups/{id}/roles assigns role', async ({ request }) => {
    test.skip(!groupId || !roleId, 'Missing group or role');
    const res = await request.post(`${API_BASE}/api/v1/admin/groups/${groupId}/roles`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: { roleId },
    });
    expect([204, 200]).toContain(res.status());
  });

  // TC-IDENT-033: remove role from group — returns 204
  test('TC-IDENT-033: DELETE /admin/groups/{id}/roles/{roleId} removes role', async ({ request }) => {
    test.skip(!groupId || !roleId, 'Missing group or role');
    const res = await request.delete(`${API_BASE}/api/v1/admin/groups/${groupId}/roles/${roleId}`, {
      headers: authHeaders(token),
    });
    expect([204, 200, 404]).toContain(res.status());
  });

  // Auth guard: unauthenticated returns 401
  test('GET /admin/groups without auth returns 401', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/admin/groups`);
    expect(res.status()).toBe(401);
  });
});
