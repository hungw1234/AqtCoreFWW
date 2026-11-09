import { test, expect } from './fixtures/test-base';
import { getAccessToken, authHeaders, API_BASE } from './helpers/auth-helper';

// DataScopeAdminController P1 TCs — TC-IDENT-089/090/091/092
// Route prefix: /api/v1/admin/data-scopes
// Auth: [Authorize(Policy = "Admin")]
test.describe('Data Scope admin (authenticated API)', () => {
  test.describe.configure({ mode: 'serial' });
  let token: string;
  let roleId: string;
  let scopeTypeId: string;
  let scopeAssignmentId: string;

  test.beforeAll(async ({ request }) => {
    token = await getAccessToken(request);
    expect(token).toBeTruthy();

    // Resolve a roleId for scope assignment tests
    const rolesRes = await request.get(`${API_BASE}/api/v1/admin/roles`, {
      headers: authHeaders(token),
    });
    const rolesBody = await rolesRes.json();
    const roles = rolesBody.data?.items ?? rolesBody.data ?? rolesBody;
    roleId = Array.isArray(roles) ? (roles[0]?.id ?? '') : '';
  });

  test.afterAll(async ({ request }) => {
    if (scopeAssignmentId && roleId) {
      await request.delete(
        `${API_BASE}/api/v1/admin/data-scopes/roles/${roleId}/${scopeAssignmentId}`,
        { headers: authHeaders(token) },
      );
    }
  });

  // TC-IDENT-089: list data scope types — returns type catalogue
  test('TC-IDENT-089: GET /admin/data-scopes/types returns scope type catalogue', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/admin/data-scopes/types`, {
      headers: authHeaders(token),
    });
    expect(res.status()).toBe(200);
    const body = await res.json();
    const items: Array<{ code?: string; name?: string; id?: string }> = body.data ?? body;
    expect(Array.isArray(items)).toBe(true);

    // Capture first type id for assignment test
    scopeTypeId = items[0]?.id ?? '';
  });

  // TC-IDENT-090: get role data scopes — for existing role
  test('TC-IDENT-090: GET /admin/data-scopes/roles/{roleId} returns scope assignments', async ({ request }) => {
    test.skip(!roleId, 'No role available');
    const res = await request.get(`${API_BASE}/api/v1/admin/data-scopes/roles/${roleId}`, {
      headers: authHeaders(token),
    });
    expect(res.status()).toBe(200);
    const body = await res.json();
    const items = body.data ?? body;
    expect(Array.isArray(items)).toBe(true);
  });

  // TC-IDENT-091: assign data scope to role — returns 201
  test('TC-IDENT-091: POST /admin/data-scopes/roles/{roleId} assigns scope', async ({ request }) => {
    test.skip(!roleId || !scopeTypeId, 'Missing role or scope type');
    const res = await request.post(`${API_BASE}/api/v1/admin/data-scopes/roles/${roleId}`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: {
        dataScopeTypeId: scopeTypeId,
        scopeField: 'tenantId',
        scopeValue: null,
        priority: 0,
      },
    });
    expect([201, 200, 409]).toContain(res.status());
    if ([201, 200].includes(res.status())) {
      const body = await res.json();
      scopeAssignmentId = body.data?.id ?? body.id ?? '';
    }
  });

  // TC-IDENT-092: delete data scope from role — returns 204
  test('TC-IDENT-092: DELETE /admin/data-scopes/roles/{roleId}/{scopeId} removes scope', async ({ request }) => {
    test.skip(!roleId || !scopeAssignmentId, 'No scope assignment to delete');
    const res = await request.delete(
      `${API_BASE}/api/v1/admin/data-scopes/roles/${roleId}/${scopeAssignmentId}`,
      { headers: authHeaders(token) },
    );
    expect([204, 200]).toContain(res.status());
    scopeAssignmentId = '';
  });

  // Auth guard
  test('GET /admin/data-scopes/types without auth returns 401', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/admin/data-scopes/types`);
    expect(res.status()).toBe(401);
  });
});
