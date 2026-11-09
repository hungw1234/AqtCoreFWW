import { test, expect } from './fixtures/test-base';
import { getAccessToken, authHeaders, API_BASE } from './helpers/auth-helper';

const DOC_TEMPLATES_URL = `${API_BASE}/api/v1/document-templates`;

// Document Templates E2E — list/create/update/publish (P1 TCs: DTPL-001..020)
test.describe('Document Templates (authenticated API)', () => {
  test.describe.configure({ mode: 'serial' });
  let token: string;
  let userToken: string;
  let createdId: string | undefined;

  test.beforeAll(async ({ request }) => {
    token = await getAccessToken(request);
    userToken = await getAccessToken(request, 'user');
    expect(token).toBeTruthy();
  });

  // ── Auth guards ─────────────────────────────────────────────────────────────

  // TC-DTPL-001
  test('TC-DTPL-001: GET /document-templates without auth returns 401', async ({ request }) => {
    const res = await request.get(DOC_TEMPLATES_URL);
    expect(res.status()).toBe(401);
  });

  // TC-DTPL-007
  test('TC-DTPL-007: POST /document-templates without auth returns 401', async ({ request }) => {
    const res = await request.post(DOC_TEMPLATES_URL, {
      headers: { 'Content-Type': 'application/json' },
      data: { name: 'Test', code: 'T001', outputFormat: 0, templateContent: 'Hello' },
    });
    expect(res.status()).toBe(401);
  });

  // TC-DTPL-013
  test('TC-DTPL-013: PUT /document-templates/{id} without auth returns 401', async ({ request }) => {
    const res = await request.put(
      `${DOC_TEMPLATES_URL}/00000000-0000-0000-0000-000000000099`,
      {
        headers: { 'Content-Type': 'application/json' },
        data: { name: 'Updated', templateContent: 'New content' },
      },
    );
    expect(res.status()).toBe(401);
  });

  // TC-DTPL-017
  test('TC-DTPL-017: POST /document-templates/{id}/publish without auth returns 401', async ({
    request,
  }) => {
    const res = await request.post(
      `${DOC_TEMPLATES_URL}/00000000-0000-0000-0000-000000000099/publish`,
    );
    expect(res.status()).toBe(401);
  });

  // ── Role guards ─────────────────────────────────────────────────────────────

  // TC-DTPL-008
  test('TC-DTPL-008: non-Admin POST /document-templates returns 403', async ({ request }) => {
    const res = await request.post(DOC_TEMPLATES_URL, {
      headers: { ...authHeaders(userToken), 'Content-Type': 'application/json' },
      data: { name: 'Test', code: 'T001_U', outputFormat: 0, templateContent: 'Hello' },
    });
    expect(res.status()).toBe(403);
  });

  // TC-DTPL-014
  test('TC-DTPL-014: non-Admin PUT /document-templates/{id} returns 403', async ({ request }) => {
    const res = await request.put(
      `${DOC_TEMPLATES_URL}/00000000-0000-0000-0000-000000000099`,
      {
        headers: { ...authHeaders(userToken), 'Content-Type': 'application/json' },
        data: { name: 'Updated', templateContent: 'Content' },
      },
    );
    expect(res.status()).toBe(403);
  });

  // TC-DTPL-018
  test('TC-DTPL-018: non-Admin POST publish returns 403', async ({ request }) => {
    const res = await request.post(
      `${DOC_TEMPLATES_URL}/00000000-0000-0000-0000-000000000099/publish`,
      { headers: authHeaders(userToken) },
    );
    expect(res.status()).toBe(403);
  });

  // ── List ────────────────────────────────────────────────────────────────────

  // TC-DTPL-002
  test('TC-DTPL-002: GET /document-templates authenticated returns paginated list', async ({
    request,
  }) => {
    const res = await request.get(DOC_TEMPLATES_URL, { headers: authHeaders(token) });
    expect(res.status()).toBe(200);
    const body = await res.json();
    // data may be paginated object or array
    const data = body.data;
    expect(data).toBeDefined();
  });

  // ── Create ──────────────────────────────────────────────────────────────────

  // TC-DTPL-009
  test('TC-DTPL-009: Admin creates Draft document template', async ({ request }) => {
    const ts = Date.now();
    const res = await request.post(DOC_TEMPLATES_URL, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: {
        name: 'E2E Invoice Template',
        code: `E2E_INV_${ts}`,
        description: 'E2E test template',
        outputFormat: 0,
        templateContent: 'Invoice #{{ invoice.number }} — Amount: {{ invoice.amount }}',
      },
    });
    expect([200, 201]).toContain(res.status());
    const body = await res.json();
    const tmpl = body.data ?? body;
    expect(tmpl).toHaveProperty('id');
    expect(tmpl.code ?? tmpl.Code).toBe(`E2E_INV_${ts}`);
    // Draft status = 0
    expect([0, 'Draft']).toContain(tmpl.status ?? tmpl.Status);
    createdId = tmpl.id ?? tmpl.Id;
  });

  // ── Update ──────────────────────────────────────────────────────────────────

  // TC-DTPL-015
  test('TC-DTPL-015: Admin updates Draft document template', async ({ request }) => {
    test.skip(!createdId, 'No template created');
    const res = await request.put(`${DOC_TEMPLATES_URL}/${createdId}`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: {
        name: 'Updated Invoice Template',
        description: 'Updated description',
        templateContent: 'Invoice #{{ invoice.number }} v2',
      },
    });
    expect([200, 204]).toContain(res.status());
    if (res.status() === 200) {
      const body = await res.json();
      const tmpl = body.data ?? body;
      expect(tmpl.name ?? tmpl.Name).toBe('Updated Invoice Template');
    }
  });

  // TC-DTPL-016
  test('TC-DTPL-016: PUT /document-templates/{id} unknown id returns 404', async ({ request }) => {
    const res = await request.put(
      `${DOC_TEMPLATES_URL}/00000000-0000-0000-0000-000000000099`,
      {
        headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
        data: { name: 'X', templateContent: 'x' },
      },
    );
    expect(res.status()).toBe(404);
  });

  // ── Publish ─────────────────────────────────────────────────────────────────

  // TC-DTPL-020
  test('TC-DTPL-020: POST /document-templates/{id}/publish unknown id returns 404', async ({
    request,
  }) => {
    const res = await request.post(
      `${DOC_TEMPLATES_URL}/00000000-0000-0000-0000-000000000099/publish`,
      { headers: authHeaders(token) },
    );
    expect(res.status()).toBe(404);
  });

  // TC-DTPL-019
  test('TC-DTPL-019: Admin publishes Draft template to Active', async ({ request }) => {
    test.skip(!createdId, 'No template created');
    const res = await request.post(`${DOC_TEMPLATES_URL}/${createdId}/publish`, {
      headers: authHeaders(token),
    });
    expect([200, 204]).toContain(res.status());
    if (res.status() === 200) {
      const body = await res.json();
      const tmpl = body.data ?? body;
      // Active status = 1
      expect([1, 'Active']).toContain(tmpl.status ?? tmpl.Status);
    }
  });
});
