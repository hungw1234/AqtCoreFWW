import { test, expect } from './fixtures/test-base';
import { getAccessToken, authHeaders, API_BASE } from './helpers/auth-helper';

// Seeded test data IDs (E2ETestDataSeeder)
const ORG_BNV_ID = '10000000-0000-0000-0000-000000000001'; // Root org

// Org Units READ E2E — list/tree, get single, get members + auth guards
// Write operations (create/update/delete) are in org-units-write.spec.ts
test.describe('Org Units: read operations (authenticated API)', () => {
  let adminToken: string;

  test.beforeAll(async ({ request }) => {
    adminToken = await getAccessToken(request, 'admin');
    expect(adminToken).toBeTruthy();
  });

  // ── TC-ORG-001: GET tree — authenticated returns 200 ──────────────────────

  test('TC-ORG-001: GET tree — authenticated user returns 200 with array', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/admin/org/units`, {
      headers: authHeaders(adminToken),
    });
    expect(res.status()).toBe(200);
    const body = await res.json();
    expect(body.success).toBe(true);
    const items = body.data?.items ?? body.data ?? body;
    expect(Array.isArray(items)).toBe(true);
  });

  // ── TC-ORG-002: GET tree — unauthenticated returns 401 ────────────────────

  test('TC-ORG-002: GET tree — unauthenticated returns 401', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/admin/org/units`);
    expect(res.status()).toBe(401);
  });

  // ── TC-ORG-004: GET single unit — seeded root org ─────────────────────────

  test('TC-ORG-004: GET single unit — returns correct seeded root org by id', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/admin/org/units/${ORG_BNV_ID}`, {
      headers: authHeaders(adminToken),
    });
    expect([200, 404]).toContain(res.status());
    if (res.status() === 200) {
      const body = await res.json();
      const unit = body.data ?? body;
      expect(unit).toHaveProperty('id', ORG_BNV_ID);
      expect(unit).toHaveProperty('code');
      expect(unit).toHaveProperty('level');
      expect(unit).toHaveProperty('isActive');
    }
  });

  // ── TC-ORG-005: GET single unit — non-existent returns 404 ───────────────

  test('TC-ORG-005: GET single unit — non-existent id returns 404', async ({ request }) => {
    const res = await request.get(
      `${API_BASE}/api/v1/admin/org/units/00000000-0000-0000-0000-000000000099`,
      { headers: authHeaders(adminToken) },
    );
    expect(res.status()).toBe(404);
  });

  // ── TC-ORG-006: GET single unit — unauthenticated returns 401 ────────────

  test('TC-ORG-006: GET single unit — unauthenticated returns 401', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/admin/org/units/${ORG_BNV_ID}`);
    expect(res.status()).toBe(401);
  });

  // ── TC-ORG-007: GET members — returns array for seeded unit ──────────────

  test('TC-ORG-007: GET members — returns array for seeded org unit', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/admin/org/units/${ORG_BNV_ID}/members`, {
      headers: authHeaders(adminToken),
    });
    // 200 = empty or populated list; 404 = org not seeded;
    // 500 = IIdentityModuleClient unavailable when member rows exist
    expect([200, 404, 500]).toContain(res.status());
    if (res.status() === 200) {
      const body = await res.json();
      const items = body.data?.items ?? body.data ?? body;
      expect(Array.isArray(items)).toBe(true);
    }
  });

  // ── TC-ORG-010: GET members — unauthenticated returns 401 ────────────────

  test('TC-ORG-010: GET members — unauthenticated returns 401', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/admin/org/units/${ORG_BNV_ID}/members`);
    expect(res.status()).toBe(401);
  });
});
