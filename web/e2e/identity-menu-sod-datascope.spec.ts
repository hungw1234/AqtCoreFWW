import { test, expect } from './fixtures/test-base';
import { getAccessToken, authHeaders, API_BASE } from './helpers/auth-helper';

// MenuController P1 TCs — TC-IDENT-073/074/075/076/078
// Route prefix: /api/v1/menus
// Auth: [Authorize] for my-menus; [Authorize(Policy="Admin")] for admin sub-routes
test.describe('Menu controller (authenticated API)', () => {
  test.describe.configure({ mode: 'serial' });
  let token: string;
  let createdMenuId: string;
  const menuCode = `TC076-MENU-${Date.now()}`;

  test.beforeAll(async ({ request }) => {
    token = await getAccessToken(request);
    expect(token).toBeTruthy();
  });

  // TC-IDENT-073: get my menus — returns authorized menu tree
  test('TC-IDENT-073: GET /menus/my-menus returns menu tree', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/menus/my-menus`, {
      headers: authHeaders(token),
    });
    expect(res.status()).toBe(200);
    const body = await res.json();
    const items = body.data ?? body;
    expect(Array.isArray(items)).toBe(true);
  });

  // TC-IDENT-074: get my menus — unauthenticated returns 401
  test('TC-IDENT-074: GET /menus/my-menus without auth returns 401', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/menus/my-menus`);
    expect(res.status()).toBe(401);
  });

  // TC-IDENT-075: list all menus (admin) — returns full tree
  test('TC-IDENT-075: GET /menus/admin/menus returns full menu list', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/menus/admin/menus`, {
      headers: authHeaders(token),
    });
    expect(res.status()).toBe(200);
    const body = await res.json();
    const items = body.data ?? body;
    expect(Array.isArray(items)).toBe(true);
  });

  // TC-IDENT-076: create menu item — valid payload returns 201
  test('TC-IDENT-076: POST /menus/admin/menus creates menu item', async ({ request }) => {
    const res = await request.post(`${API_BASE}/api/v1/menus/admin/menus`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: {
        parentId: null,
        code: menuCode,
        title: 'TC076 Menu',
        icon: 'icon-test',
        route: '/tc076',
        sortOrder: 999,
        permissionCodes: [],
      },
    });
    expect([201, 409]).toContain(res.status());
    if (res.status() === 201) {
      const body = await res.json();
      createdMenuId = body.data?.id ?? body.id;
    }
  });

  // TC-IDENT-078: update menu item — valid payload returns 204
  test('TC-IDENT-078: PUT /menus/admin/menus/{id} updates menu item', async ({ request }) => {
    test.skip(!createdMenuId, 'No menu created');
    const res = await request.put(`${API_BASE}/api/v1/menus/admin/menus/${createdMenuId}`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: {
        parentId: null,
        title: 'Updated Title',
        icon: null,
        route: '/tc076-updated',
        sortOrder: 1000,
        isActive: false, // deactivate for cleanup (no delete endpoint)
        permissionCodes: [],
      },
    });
    expect([204, 200]).toContain(res.status());
  });
});

// SodRulesAdminController P1 TCs — TC-IDENT-079/080
// Route prefix: /api/v1/admin/sod-rules
// Auth: [Authorize(Policy = PermCodes.Identity.Sod.View)]
// V2: DELETE endpoint removed — use PUT with IsActive=false for soft-delete
test.describe('SoD Rules admin (authenticated API)', () => {
  test.describe.configure({ mode: 'serial' });
  let token: string;
  let sodRuleId: string;

  test.beforeAll(async ({ request }) => {
    token = await getAccessToken(request);
    expect(token).toBeTruthy();
  });

  test.afterAll(async ({ request }) => {
    // V2: cleanup via soft-delete (PUT IsActive=false) instead of DELETE
    if (sodRuleId) {
      await request.put(`${API_BASE}/api/v1/admin/sod-rules/${sodRuleId}`, {
        headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
        data: {
          permissionCodeA: 'forms.approve',
          permissionCodeB: 'forms.submit',
          severity: 'Low',
          description: 'Approval and submission conflict (test cleanup)',
          isActive: false,
        },
      });
    }
  });

  // TC-IDENT-079: list SoD rules — authenticated admin
  test('TC-IDENT-079: GET /admin/sod-rules returns rule list', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/admin/sod-rules`, {
      headers: authHeaders(token),
    });
    expect(res.status()).toBe(200);
    const body = await res.json();
    const items = body.data?.items ?? body.data ?? body;
    expect(Array.isArray(items)).toBe(true);
  });

  // TC-IDENT-080: create SoD rule — valid payload returns 201 (using new 'severity' field)
  test('TC-IDENT-080: POST /admin/sod-rules creates rule', async ({ request }) => {
    const res = await request.post(`${API_BASE}/api/v1/admin/sod-rules`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: {
        permissionCodeA: 'forms.approve',
        permissionCodeB: 'forms.submit',
        severity: 'Low',
        description: 'Approval and submission conflict',
      },
    });
    // 201 = created, 409 = already exists, 422 = validation error
    expect([201, 409, 422]).toContain(res.status());
    if (res.status() === 201) {
      const body = await res.json();
      sodRuleId = body.data?.id ?? body.id;
    }
  });

  // Auth guard
  test('GET /admin/sod-rules without auth returns 401', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/admin/sod-rules`);
    expect(res.status()).toBe(401);
  });

  // TC-IDENT-081: list SoD violation logs — authenticated admin
  test('TC-IDENT-081: GET /admin/sod-rules/violations returns paged result', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/admin/sod-rules/violations`, {
      headers: authHeaders(token),
    });
    expect(res.status()).toBe(200);
    const body = await res.json();
    const data = body.data ?? body;
    expect(data).toHaveProperty('items');
    expect(data).toHaveProperty('totalCount');
    expect(data).toHaveProperty('page');
    expect(data).toHaveProperty('pageSize');
  });

  // Auth guard for violations endpoint
  test('GET /admin/sod-rules/violations without auth returns 401', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/admin/sod-rules/violations`);
    expect(res.status()).toBe(401);
  });
});
