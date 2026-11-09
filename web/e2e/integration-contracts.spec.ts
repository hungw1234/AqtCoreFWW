import { test, expect } from './fixtures/test-base';
import { getAccessToken, authHeaders, API_BASE } from './helpers/auth-helper';

// Integration Contracts E2E — CRUD lifecycle requires a partner to exist first.
// beforeAll creates a partner for partnerId; afterAll cleans it up.
// Contract create/update/delete tests run serially and share contractId.
test.describe('Integration Contracts (authenticated API)', () => {
  test.describe.configure({ mode: 'serial' });

  let token: string;
  let partnerId: string;
  let contractId: string;

  const NON_EXISTENT_ID = '00000000-0000-0000-0000-000000000099';

  test.beforeAll(async ({ request }) => {
    token = await getAccessToken(request, 'admin');
    expect(token).toBeTruthy();

    // Create a partner to use as foreign key for contract tests
    const res = await request.post(`${API_BASE}/api/v1/integration/partners`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: {
        name: 'E2E Contract Setup Partner',
        code: `E2E-CSETUP-${Date.now()}`,
        contactEmail: 'setup@example.com',
        authScheme: 'BearerToken',
      },
    });
    const body = await res.json();
    partnerId = (body.data ?? body).id;
    expect(partnerId).toBeTruthy();
  });

  test.afterAll(async ({ request }) => {
    // Cleanup partner created in beforeAll
    if (partnerId) {
      await request.delete(`${API_BASE}/api/v1/integration/partners/${partnerId}`, {
        headers: authHeaders(token),
      });
    }
  });

  // TC-INTG-020: GET list → 200 paginated
  test('TC-INTG-020: list contracts — 200 paginated envelope', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/integration/contracts`, {
      headers: authHeaders(token),
    });
    expect(res.status()).toBe(200);
    const body = await res.json();
    expect(body.success).toBe(true);
    expect(body.data).toHaveProperty('items');
    expect(Array.isArray(body.data.items)).toBeTruthy();
  });

  // TC-INTG-023: GET list without auth → 401
  test('TC-INTG-023: list contracts — 401 without auth', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/integration/contracts`);
    expect(res.status()).toBe(401);
  });

  // TC-INTG-027: POST create success → 200 with partnerId/title/status
  // Actual DTO: PartnerId, Title, Description, EffectiveDate, ExpiryDate, DataScopeJson
  test('TC-INTG-027: create contract — 200 with all required fields', async ({ request }) => {
    test.skip(!partnerId, 'No partner available');
    const res = await request.post(`${API_BASE}/api/v1/integration/contracts`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: {
        partnerPublicId: partnerId,
        title: 'E2E Contract',
        description: null,
        effectiveDate: '2026-01-01T00:00:00Z',
        expiryDate: '2026-12-31T00:00:00Z',
        dataScopeJson: null,
      },
    });
    expect(res.status()).toBe(200);
    const body = await res.json();
    expect(body.success).toBe(true);
    const data = body.data ?? body;
    expect(data.id).toBeTruthy();
    expect(data.id).toMatch(/^[0-9a-f-]{36}$/i);
    expect(data.title).toBe('E2E Contract');
    contractId = data.id;
  });

  // TC-INTG-024: GET /{id} success → 200
  test('TC-INTG-024: get contract by id — 200 with correct fields', async ({ request }) => {
    test.skip(!contractId, 'No contract created');
    const res = await request.get(`${API_BASE}/api/v1/integration/contracts/${contractId}`, {
      headers: authHeaders(token),
    });
    expect(res.status()).toBe(200);
    const body = await res.json();
    const data = body.data ?? body;
    expect(data).toHaveProperty('id', contractId);
    expect(data).toHaveProperty('title');
    expect(data).toHaveProperty('partnerId');
  });

  // TC-INTG-025: GET /{id} non-existent → 404
  test('TC-INTG-025: get contract by id — 404 for non-existent id', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/integration/contracts/${NON_EXISTENT_ID}`, {
      headers: authHeaders(token),
    });
    expect(res.status()).toBe(404);
  });

  // TC-INTG-026: GET /{id} without auth → 401
  test('TC-INTG-026: get contract by id — 401 without auth', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/integration/contracts/${NON_EXISTENT_ID}`);
    expect(res.status()).toBe(401);
  });

  // TC-INTG-029: POST create empty title → 422
  test('TC-INTG-029: create contract — 422 when title is empty', async ({ request }) => {
    test.skip(!partnerId, 'No partner available');
    const res = await request.post(`${API_BASE}/api/v1/integration/contracts`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: {
        partnerId,
        title: '',
        description: null,
        effectiveDate: '2026-01-01T00:00:00Z',
        expiryDate: null,
        dataScopeJson: null,
      },
    });
    expect([400, 422]).toContain(res.status());
  });

  // TC-INTG-030: POST create without auth → 401
  test('TC-INTG-030: create contract — 401 without auth', async ({ request }) => {
    const res = await request.post(`${API_BASE}/api/v1/integration/contracts`, {
      headers: { 'Content-Type': 'application/json' },
      data: { partnerId, title: 'Unauthorized', effectiveDate: '2026-01-01T00:00:00Z' },
    });
    expect(res.status()).toBe(401);
  });

  // TC-INTG-031: PUT update success → 200
  // Actual UpdateContractBody: Title, Description, EffectiveDate, ExpiryDate, DataScopeJson
  test('TC-INTG-031: update contract — 200 with updated title', async ({ request }) => {
    test.skip(!contractId, 'No contract created');
    const res = await request.put(`${API_BASE}/api/v1/integration/contracts/${contractId}`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: {
        title: 'E2E Contract Updated',
        description: null,
        effectiveDate: '2026-01-01T00:00:00Z',
        expiryDate: '2026-12-31T00:00:00Z',
        dataScopeJson: null,
      },
    });
    expect(res.status()).toBe(200);
    const body = await res.json();
    const data = body.data ?? body;
    expect(data.title).toBe('E2E Contract Updated');
  });

  // TC-INTG-032: PUT update non-existent → 404
  test('TC-INTG-032: update contract — 404 for non-existent id', async ({ request }) => {
    const res = await request.put(`${API_BASE}/api/v1/integration/contracts/${NON_EXISTENT_ID}`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: { title: 'Ghost', description: null, effectiveDate: '2026-01-01T00:00:00Z', expiryDate: null, dataScopeJson: null },
    });
    expect(res.status()).toBe(404);
  });

  // TC-INTG-035: DELETE non-existent → 404
  test('TC-INTG-035: delete contract — 404 for non-existent id', async ({ request }) => {
    const res = await request.delete(
      `${API_BASE}/api/v1/integration/contracts/${NON_EXISTENT_ID}`,
      { headers: authHeaders(token) },
    );
    expect(res.status()).toBe(404);
  });

  // TC-INTG-034: DELETE success → 204
  test('TC-INTG-034: delete contract — 204 success', async ({ request }) => {
    test.skip(!contractId, 'No contract created');
    const res = await request.delete(
      `${API_BASE}/api/v1/integration/contracts/${contractId}`,
      { headers: authHeaders(token) },
    );
    expect([200, 204]).toContain(res.status());
  });
});
