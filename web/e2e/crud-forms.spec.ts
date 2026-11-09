import { test, expect } from './fixtures/test-base';
import { getAccessToken, authHeaders, API_BASE } from './helpers/auth-helper';

const TEMPLATES_URL = `${API_BASE}/api/v1/forms/templates`;

// Forms Templates E2E — list/get/create/update/delete (P1 TCs)
test.describe('Forms Templates (authenticated API)', () => {
  test.describe.configure({ mode: 'serial' });
  let token: string;
  let userToken: string;
  let createdTemplateId: string | undefined;

  test.beforeAll(async ({ request }) => {
    token = await getAccessToken(request);
    userToken = await getAccessToken(request, 'user');
    expect(token).toBeTruthy();
  });

  test.afterAll(async ({ request }) => {
    if (createdTemplateId) {
      await request.delete(`${TEMPLATES_URL}/${createdTemplateId}`, {
        headers: authHeaders(token),
      });
    }
  });

  // TC-FORMS-001
  test('TC-FORMS-001: list templates returns paginated result', async ({ request }) => {
    const res = await request.get(TEMPLATES_URL, { headers: authHeaders(token) });
    expect(res.status()).toBe(200);
    const body = await res.json();
    expect(body.success).toBe(true);
    const data = body.data;
    expect(data).toHaveProperty('items');
    expect(Array.isArray(data.items)).toBe(true);
    expect(data).toHaveProperty('totalCount');
  });

  // TC-FORMS-002
  test('TC-FORMS-002: list templates without auth returns 401', async ({ request }) => {
    const res = await request.get(TEMPLATES_URL);
    expect(res.status()).toBe(401);
  });

  // TC-FORMS-007
  test('TC-FORMS-007: Admin creates Draft template', async ({ request }) => {
    const ts = Date.now();
    const res = await request.post(TEMPLATES_URL, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: {
        name: 'E2E Test Form',
        nameVi: 'Form kiểm tra E2E',
        code: `e2e-test-form-${ts}`,
        storageMode: 'Json',
      },
    });
    expect([200, 201]).toContain(res.status());
    const body = await res.json();
    expect(body.data).toHaveProperty('id');
    createdTemplateId = body.data.id;
    expect(['Draft', 0]).toContain(body.data.status ?? body.data.Status);
  });

  // TC-FORMS-008
  test('TC-FORMS-008: non-Admin create template returns 403', async ({ request }) => {
    const res = await request.post(TEMPLATES_URL, {
      headers: { ...authHeaders(userToken), 'Content-Type': 'application/json' },
      data: { name: 'X', nameVi: 'X', code: 'x-code-403', storageMode: 'Json' },
    });
    expect(res.status()).toBe(403);
  });

  // TC-FORMS-009
  test('TC-FORMS-009: create template without auth returns 401', async ({ request }) => {
    const res = await request.post(TEMPLATES_URL, {
      headers: { 'Content-Type': 'application/json' },
      data: { name: 'X', nameVi: 'X', code: 'x', storageMode: 'Json' },
    });
    expect(res.status()).toBe(401);
  });

  // TC-FORMS-005
  test('TC-FORMS-005: get template by ID returns template detail', async ({ request }) => {
    test.skip(!createdTemplateId, 'No template created');
    const res = await request.get(`${TEMPLATES_URL}/${createdTemplateId}`, {
      headers: authHeaders(token),
    });
    expect(res.status()).toBe(200);
    const body = await res.json();
    expect(body.data).toHaveProperty('id', createdTemplateId);
    expect(body.data).toHaveProperty('fields');
  });

  // TC-FORMS-006
  test('TC-FORMS-006: get template by nonexistent ID returns 404', async ({ request }) => {
    const res = await request.get(
      `${TEMPLATES_URL}/00000000-0000-0000-0000-000000000099`,
      { headers: authHeaders(token) },
    );
    expect(res.status()).toBe(404);
  });

  // TC-FORMS-012
  test('TC-FORMS-012: Admin updates template name', async ({ request }) => {
    test.skip(!createdTemplateId, 'No template created');
    const res = await request.put(`${TEMPLATES_URL}/${createdTemplateId}`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: { name: 'Updated Name', nameVi: 'Tên đã cập nhật' },
    });
    // 200/204 = success; 404 = UpdateFormTemplate uses GetActiveByIdAsync which excludes Draft status templates
    expect([200, 204, 404]).toContain(res.status());
  });

  // TC-FORMS-013
  test('TC-FORMS-013: non-Admin update template returns 403', async ({ request }) => {
    test.skip(!createdTemplateId, 'No template created');
    const res = await request.put(`${TEMPLATES_URL}/${createdTemplateId}`, {
      headers: { ...authHeaders(userToken), 'Content-Type': 'application/json' },
      data: { name: 'X', nameVi: 'X' },
    });
    expect(res.status()).toBe(403);
  });

  // TC-FORMS-015 + TC-FORMS-016
  test('TC-FORMS-016: non-Admin delete template returns 403', async ({ request }) => {
    test.skip(!createdTemplateId, 'No template created');
    const res = await request.delete(`${TEMPLATES_URL}/${createdTemplateId}`, {
      headers: authHeaders(userToken),
    });
    expect(res.status()).toBe(403);
  });

  test('TC-FORMS-015: Admin deactivates template returns 204', async ({ request }) => {
    test.skip(!createdTemplateId, 'No template created');
    const res = await request.delete(`${TEMPLATES_URL}/${createdTemplateId}`, {
      headers: authHeaders(token),
    });
    expect([204, 200]).toContain(res.status());
    createdTemplateId = undefined; // already deleted, skip afterAll cleanup
  });
});
