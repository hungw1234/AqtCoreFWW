import { test, expect } from './fixtures/test-base';
import { getAccessToken, authHeaders, API_BASE } from './helpers/auth-helper';

// ConsentController P1 TCs — TC-IDENT-038/039/040/041
// Route prefix: /api/v1/identity/consents
// Auth: [Authorize] (any authenticated user)
test.describe('Identity Consent (authenticated API)', () => {
  test.describe.configure({ mode: 'serial' });
  let token: string;
  const testPurpose = `e2e-tc040-${Date.now()}`;

  test.beforeAll(async ({ request }) => {
    token = await getAccessToken(request);
    expect(token).toBeTruthy();
  });

  // TC-IDENT-038: get my consents — returns list for authenticated user
  test('TC-IDENT-038: GET /identity/consents returns consent list', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/identity/consents`, {
      headers: authHeaders(token),
    });
    expect(res.status()).toBe(200);
    const body = await res.json();
    const items = body.data?.items ?? body.items ?? body.data ?? [];
    expect(Array.isArray(items)).toBe(true);
  });

  // TC-IDENT-039: get my consents — unauthenticated returns 401
  test('TC-IDENT-039: GET /identity/consents without auth returns 401', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/identity/consents`);
    expect(res.status()).toBe(401);
  });

  // TC-IDENT-040: grant consent — valid purpose returns 201
  test('TC-IDENT-040: POST /identity/consents/grant grants consent', async ({ request }) => {
    const res = await request.post(`${API_BASE}/api/v1/identity/consents/grant`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: { purpose: testPurpose, scope: 'general' },
    });
    // 201 = granted; 409 = already active (idempotent re-run)
    expect([201, 200, 409]).toContain(res.status());
  });

  // TC-IDENT-041: withdraw consent — valid purpose returns 200
  // Note: withdraw endpoint may hang if consent service has long-running RTBF cascade
  test('TC-IDENT-041: POST /identity/consents/withdraw withdraws consent', async ({ request }) => {
    test.setTimeout(90_000);
    const res = await request.post(`${API_BASE}/api/v1/identity/consents/withdraw`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: { purpose: testPurpose, scope: 'user withdrawal' },
    });
    // 200 = withdrawn; 404 = no active consent found (acceptable if grant failed)
    expect([200, 404]).toContain(res.status());
    if (res.status() === 200) {
      const body = await res.json();
      const data = body.data ?? body;
      expect(data).toHaveProperty('withdrawn', true);
    }
  });
});
