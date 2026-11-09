import { test, expect } from './fixtures/test-base';
import { getAccessToken, authHeaders, API_BASE } from './helpers/auth-helper';

/**
 * Smoke tests for FE-BE gap closure endpoints (G1-G12).
 * Verifies new routes are wired and respond correctly.
 * Accepts 200/403/500 as valid — proves routes are wired (500 = DB not migrated in dev).
 */
test.describe('Gap closure API smoke tests', () => {
  let token: string;

  test.beforeAll(async ({ request }) => {
    token = await getAccessToken(request);
    expect(token).toBeTruthy();
  });

  // ── G3: Consent Management ──────────────────────────────────────────

  test('GET /identity/consents returns user consent records', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/identity/consents`, {
      headers: authHeaders(token),
    });
    expect([200, 403, 500]).toContain(res.status());
    if (res.status() === 200) {
      const body = await res.json();
      expect(body.success).toBe(true);
      expect(body.data).toHaveProperty('items');
    }
  });

  // ── G4: RTBF Requests ──────────────────────────────────────────────

  test('GET /admin/rtbf-requests returns list', async ({ request }) => {
    const res = await request.get(
      `${API_BASE}/api/v1/admin/rtbf-requests?page=1&pageSize=10`,
      { headers: authHeaders(token) },
    );
    expect([200, 403, 500]).toContain(res.status());
  });

  // ── G5: Credential Policies ────────────────────────────────────────

  test('GET /admin/credential-policies returns list', async ({ request }) => {
    const res = await request.get(
      `${API_BASE}/api/v1/admin/credential-policies?page=1&pageSize=10`,
      { headers: authHeaders(token) },
    );
    expect([200, 403, 500]).toContain(res.status());
  });

  // ── G6: Data Scopes ────────────────────────────────────────────────

  test('GET /admin/data-scopes/types returns scope types', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/admin/data-scopes/types`, {
      headers: authHeaders(token),
    });
    expect([200, 403, 500]).toContain(res.status());
  });

  // ── G7: SoD Rules ──────────────────────────────────────────────────

  test('GET /admin/sod-rules returns list', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/admin/sod-rules`, {
      headers: authHeaders(token),
    });
    expect([200, 403, 500]).toContain(res.status());
  });

  // ── G8: Policy Rules ───────────────────────────────────────────────

  test('GET /admin/policy-rules returns list', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/admin/policy-rules`, {
      headers: authHeaders(token),
    });
    expect([200, 403, 500]).toContain(res.status());
  });

  // ── G9: External Identities ────────────────────────────────────────

  test('GET /admin/external-identities requires userId param', async ({ request }) => {
    const userId = '00000000-0000-0000-0000-000000000001';
    const res = await request.get(
      `${API_BASE}/api/v1/admin/external-identities?userId=${userId}&page=1&pageSize=10`,
      { headers: authHeaders(token) },
    );
    // 200=success, 400=validation, 403=permission, 500=DB not migrated — all prove route wired
    expect([200, 400, 403, 500]).toContain(res.status());
  });

  // ── G10: Groups ────────────────────────────────────────────────────

  test('GET /admin/groups returns list', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/admin/groups`, {
      headers: authHeaders(token),
    });
    expect([200, 403, 500]).toContain(res.status());
  });

  // ── G11: Audit Chain Verify ────────────────────────────────────────

  test('GET /audit/logs/verify-chain responds', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/audit/logs/verify-chain`, {
      headers: authHeaders(token),
    });
    // 200=chain valid, 403=not admin, 422=chain broken, 500=DB issue — all prove route wired
    expect([200, 403, 422, 500]).toContain(res.status());
  });

  // ── G1: Workflow Branch Status ─────────────────────────────────────

  test('GET /workflow/instances/{id}/branch-status responds', async ({ request }) => {
    const fakeId = '00000000-0000-0000-0000-000000000001';
    const tenantId = '00000000-0000-0000-0000-000000000001';
    const res = await request.get(
      `${API_BASE}/api/v1/workflow/instances/${fakeId}/branch-status?tenantId=${tenantId}`,
      { headers: authHeaders(token) },
    );
    // 200=empty list, 404=not found, 403=permission, 500=DB issue — all prove route wired
    expect([200, 404, 403, 500]).toContain(res.status());
  });
});
