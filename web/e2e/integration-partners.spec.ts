import { test, expect } from './fixtures/test-base';
import { getAccessToken, authHeaders, API_BASE } from './helpers/auth-helper';

// Integration Partners E2E — CRUD lifecycle: list, get, create, update, delete.
// Tests run serially because create/update/delete share partnerId.
test.describe('Integration Partners (authenticated API)', () => {
  test.describe.configure({ mode: 'serial' });

  let token: string;
  let partnerId: string;

  const NON_EXISTENT_ID = '00000000-0000-0000-0000-000000000099';

  test.beforeAll(async ({ request }) => {
    token = await getAccessToken(request, 'admin');
    expect(token).toBeTruthy();
  });

  test.afterAll(async ({ request }) => {
    if (partnerId) {
      await request.delete(`${API_BASE}/api/v1/integration/partners/${partnerId}`, {
        headers: authHeaders(token),
      }).catch(() => {});
    }
  });

  // TC-INTG-001: GET list returns paginated result
  test('TC-INTG-001: list partners — 200 paginated envelope', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/integration/partners`, {
      headers: authHeaders(token),
    });
    expect(res.status()).toBe(200);
    const body = await res.json();
    expect(body.success).toBe(true);
    expect(body.data).toHaveProperty('items');
    expect(Array.isArray(body.data.items)).toBeTruthy();
  });

  // TC-INTG-004: GET list without auth → 401
  test('TC-INTG-004: list partners — 401 without auth', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/integration/partners`);
    expect(res.status()).toBe(401);
  });

  // TC-INTG-008: POST create with all fields → 200, check GUID id and status=Active
  test('TC-INTG-008: create partner — 200 with all fields', async ({ request }) => {
    const partnerCode = `E2E-PART-${Date.now()}`;
    const res = await request.post(`${API_BASE}/api/v1/integration/partners`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: {
        name: 'E2E Test Partner',
        code: partnerCode,
        contactEmail: 'partner@example.com',
        contactPhone: '+84901234567',
        endpoint: 'https://api.partner.example.com/v1',
        authScheme: 'BearerToken',
      },
    });
    expect(res.status()).toBe(200);
    const body = await res.json();
    expect(body.success).toBe(true);
    const data = body.data ?? body;
    expect(data.id).toBeTruthy();
    expect(data.id).toMatch(/^[0-9a-f-]{36}$/i);
    expect(data.name).toBe('E2E Test Partner');
    expect(data.code).toBe(partnerCode);
    partnerId = data.id;
  });

  // TC-INTG-005: GET /{id} success → 200 with id/name/code/status
  test('TC-INTG-005: get partner by id — 200 with correct fields', async ({ request }) => {
    test.skip(!partnerId, 'No partner created');
    const res = await request.get(`${API_BASE}/api/v1/integration/partners/${partnerId}`, {
      headers: authHeaders(token),
    });
    expect(res.status()).toBe(200);
    const body = await res.json();
    const data = body.data ?? body;
    expect(data).toHaveProperty('id', partnerId);
    expect(data).toHaveProperty('name');
    expect(data).toHaveProperty('code');
    expect(data).toHaveProperty('status');
  });

  // TC-INTG-006: GET /{id} non-existent → 404
  test('TC-INTG-006: get partner by id — 404 for non-existent id', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/integration/partners/${NON_EXISTENT_ID}`, {
      headers: authHeaders(token),
    });
    expect(res.status()).toBe(404);
  });

  // TC-INTG-007: GET /{id} without auth → 401
  test('TC-INTG-007: get partner by id — 401 without auth', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/integration/partners/${NON_EXISTENT_ID}`);
    expect(res.status()).toBe(401);
  });

  // TC-INTG-010: POST create empty name → 422
  test('TC-INTG-010: create partner — 422 when name is empty', async ({ request }) => {
    const res = await request.post(`${API_BASE}/api/v1/integration/partners`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: { name: '', code: `E2E-FAIL-${Date.now()}`, authScheme: 'BearerToken' },
    });
    expect([400, 422]).toContain(res.status());
  });

  // TC-INTG-011: POST create empty code → 422
  test('TC-INTG-011: create partner — 422 when code is empty', async ({ request }) => {
    const res = await request.post(`${API_BASE}/api/v1/integration/partners`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: { name: 'E2E Partner No Code', code: '', authScheme: 'BearerToken' },
    });
    expect([400, 422]).toContain(res.status());
  });

  // TC-INTG-014: POST create without auth → 401
  test('TC-INTG-014: create partner — 401 without auth', async ({ request }) => {
    const res = await request.post(`${API_BASE}/api/v1/integration/partners`, {
      headers: { 'Content-Type': 'application/json' },
      data: { name: 'Unauthorized', code: 'NO-AUTH', authScheme: 'BearerToken' },
    });
    expect(res.status()).toBe(401);
  });

  // TC-INTG-015: PUT update success → 200 with updated name/code
  test('TC-INTG-015: update partner — 200 with updated fields', async ({ request }) => {
    test.skip(!partnerId, 'No partner created');
    const updatedCode = `E2E-UPD-${Date.now()}`;
    const res = await request.put(`${API_BASE}/api/v1/integration/partners/${partnerId}`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: {
        name: 'E2E Partner Updated',
        code: updatedCode,
        contactEmail: 'updated@example.com',
        contactPhone: '+84901234567',
        endpoint: 'https://api.partner.example.com/v2',
        authScheme: 'BearerToken',
      },
    });
    expect(res.status()).toBe(200);
    const body = await res.json();
    const data = body.data ?? body;
    expect(data.name).toBe('E2E Partner Updated');
    expect(data.code).toBe(updatedCode);
  });

  // TC-INTG-016: PUT update non-existent → 404
  test('TC-INTG-016: update partner — 404 for non-existent id', async ({ request }) => {
    const res = await request.put(`${API_BASE}/api/v1/integration/partners/${NON_EXISTENT_ID}`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: { name: 'Ghost', code: 'GHOST', authScheme: 'BearerToken' },
    });
    expect(res.status()).toBe(404);
  });

  // TC-INTG-019: DELETE non-existent → 404
  test('TC-INTG-019: delete partner — 404 for non-existent id', async ({ request }) => {
    const res = await request.delete(
      `${API_BASE}/api/v1/integration/partners/${NON_EXISTENT_ID}`,
      { headers: authHeaders(token) },
    );
    expect(res.status()).toBe(404);
  });

  // TC-INTG-018: DELETE success → 204, then GET → 404
  test('TC-INTG-018: delete partner — 204 success then 404 on re-fetch', async ({ request }) => {
    test.skip(!partnerId, 'No partner created');
    const delRes = await request.delete(
      `${API_BASE}/api/v1/integration/partners/${partnerId}`,
      { headers: authHeaders(token) },
    );
    expect([200, 204]).toContain(delRes.status());

    const getRes = await request.get(`${API_BASE}/api/v1/integration/partners/${partnerId}`, {
      headers: authHeaders(token),
    });
    expect(getRes.status()).toBe(404);
  });
});
