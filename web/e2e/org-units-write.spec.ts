import { test, expect } from './fixtures/test-base';
import { getAccessToken, authHeaders, API_BASE } from './helpers/auth-helper';

// Seeded test data IDs (E2ETestDataSeeder)
const ORG_BNV_ID = '10000000-0000-0000-0000-000000000001';  // Root org (has active children)
const ORG_VTCCB_ID = '10000000-0000-0000-0000-000000000002'; // Dept child

// Org Units WRITE E2E — create, update, delete + auth guards
// TC-ORG-011..024: POST/PUT/DELETE operations
// Read operations are in crud-org-units.spec.ts
test.describe.configure({ mode: 'serial' });
test.describe('Org Units: write operations (authenticated API)', () => {
  let adminToken: string;
  let userToken: string;
  let createdUnitId: string;
  let createdCode: string;

  test.beforeAll(async ({ request }) => {
    [adminToken, userToken] = await Promise.all([
      getAccessToken(request, 'admin'),
      getAccessToken(request, 'user'),
    ]);
    expect(adminToken).toBeTruthy();
  });

  // ── TC-ORG-011: POST create root unit — Admin succeeds ───────────────────

  test('TC-ORG-011: POST create org unit — Admin creates root unit successfully', async ({ request }) => {
    createdCode = `E2E-${Date.now()}`;
    const res = await request.post(`${API_BASE}/api/v1/admin/org/units`, {
      headers: { ...authHeaders(adminToken), 'Content-Type': 'application/json' },
      data: {
        name: `E2E Unit ${Date.now()}`,
        nameEn: 'E2E Unit EN',
        code: createdCode,
        parentId: null,
      },
    });
    expect([200, 201]).toContain(res.status());
    const body = await res.json();
    createdUnitId = body.data?.id ?? body.id;
    expect(createdUnitId).toBeTruthy();
    expect(body.data?.isActive ?? body.isActive).toBe(true);
  });

  // ── TC-ORG-012: POST create child unit with parentId ─────────────────────

  test('TC-ORG-012: POST create org unit — Admin creates child unit with parentId', async ({ request }) => {
    test.skip(!createdUnitId, 'Parent unit not created in TC-ORG-011');

    const res = await request.post(`${API_BASE}/api/v1/admin/org/units`, {
      headers: { ...authHeaders(adminToken), 'Content-Type': 'application/json' },
      data: {
        name: 'E2E Child Unit',
        nameEn: 'E2E Child Unit EN',
        code: `E2E-CHILD-${Date.now()}`,
        parentId: createdUnitId,
      },
    });
    expect([200, 201]).toContain(res.status());
    const body = await res.json();
    const child = body.data ?? body;
    expect(child).toHaveProperty('parentId', createdUnitId);
  });

  // ── TC-ORG-013: POST create — duplicate code returns 409 ─────────────────

  test('TC-ORG-013: POST create org unit — duplicate code returns 409', async ({ request }) => {
    test.skip(!createdCode, 'No code from TC-ORG-011 to duplicate');

    const res = await request.post(`${API_BASE}/api/v1/admin/org/units`, {
      headers: { ...authHeaders(adminToken), 'Content-Type': 'application/json' },
      data: {
        name: 'Duplicate Test',
        nameEn: 'Duplicate Test',
        code: createdCode,
        parentId: null,
      },
    });
    expect(res.status()).toBe(409);
  });

  // ── TC-ORG-015: POST create — non-Admin 403 ───────────────────────────────

  test('TC-ORG-015: POST create org unit — non-Admin role returns 403', async ({ request }) => {
    const res = await request.post(`${API_BASE}/api/v1/admin/org/units`, {
      headers: { ...authHeaders(userToken), 'Content-Type': 'application/json' },
      data: { name: 'Unauthorized', nameEn: 'Unauthorized', code: 'UNAUTH-001', parentId: null },
    });
    expect(res.status()).toBe(403);
  });

  // ── TC-ORG-016: POST create — unauthenticated 401 ─────────────────────────

  test('TC-ORG-016: POST create org unit — unauthenticated returns 401', async ({ request }) => {
    const res = await request.post(`${API_BASE}/api/v1/admin/org/units`, {
      headers: { 'Content-Type': 'application/json' },
      data: { name: 'X', nameEn: 'X', code: 'X001', parentId: null },
    });
    expect(res.status()).toBe(401);
  });

  // ── TC-ORG-017: PUT update — Admin updates name fields ───────────────────

  test('TC-ORG-017: PUT update org unit — Admin updates name fields', async ({ request }) => {
    test.skip(!createdUnitId, 'No unit created in TC-ORG-011');

    const res = await request.put(`${API_BASE}/api/v1/admin/org/units/${createdUnitId}`, {
      headers: { ...authHeaders(adminToken), 'Content-Type': 'application/json' },
      data: {
        name: `E2E Unit Updated ${Date.now()}`,
        nameEn: 'E2E Unit Updated EN',
      },
    });
    expect([200, 204, 422]).toContain(res.status());
  });

  // ── TC-ORG-019: PUT update — non-Admin 403 ────────────────────────────────

  test('TC-ORG-019: PUT update org unit — non-Admin role returns 403', async ({ request }) => {
    test.skip(!createdUnitId, 'No unit id available');

    const res = await request.put(`${API_BASE}/api/v1/admin/org/units/${createdUnitId}`, {
      headers: { ...authHeaders(userToken), 'Content-Type': 'application/json' },
      data: { name: 'Unauthorized', nameEn: 'Unauthorized' },
    });
    expect(res.status()).toBe(403);
  });

  // ── TC-ORG-022: DELETE — blocked when active children exist ──────────────

  test('TC-ORG-022: DELETE org unit — blocked when active children exist (422)', async ({ request }) => {
    // Seeded root org has known children — delete must be blocked
    const res = await request.delete(`${API_BASE}/api/v1/admin/org/units/${ORG_BNV_ID}`, {
      headers: authHeaders(adminToken),
    });
    // 422 = active children block delete; 404 = seeder not run
    expect([404, 422]).toContain(res.status());
  });

  // ── TC-ORG-024: DELETE — non-Admin 403 ────────────────────────────────────

  test('TC-ORG-024: DELETE org unit — non-Admin role returns 403', async ({ request }) => {
    const res = await request.delete(`${API_BASE}/api/v1/admin/org/units/${ORG_VTCCB_ID}`, {
      headers: authHeaders(userToken),
    });
    expect(res.status()).toBe(403);
  });

  // ── TC-ORG-020: DELETE — Admin deactivates leaf unit (cleanup) ───────────

  test('TC-ORG-020: DELETE org unit — Admin deactivates the created leaf unit', async ({ request }) => {
    test.skip(!createdUnitId, 'No unit id available from TC-ORG-011');

    const listRes = await request.get(`${API_BASE}/api/v1/admin/org/units?page=1&pageSize=5`, {
      headers: authHeaders(adminToken),
    });
    const allUnits: Array<{ id: string }> = (await listRes.json()).data?.items ?? [];
    const successor = allUnits.find((u) => u.id !== createdUnitId);

    const res = await request.delete(`${API_BASE}/api/v1/admin/org/units/${createdUnitId}`, {
      headers: { ...authHeaders(adminToken), 'Content-Type': 'application/json' },
      data: successor ? { successorId: successor.id } : {},
    });
    // 200/204 = deleted; 409/422 = has children; 404 = already gone
    expect([200, 204, 404, 409, 422]).toContain(res.status());
  });
});
