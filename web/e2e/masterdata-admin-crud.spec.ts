import { test, expect } from './fixtures/test-base';
import { getAccessToken, authHeaders, API_BASE } from './helpers/auth-helper';
import { cleanupEntity } from './helpers/cleanup-helper';

// MasterData Admin CRUD E2E — provinces, districts, wards via /api/v1/admin/master-data
// P1 TCs: TC-MD-018 to TC-MD-034
test.describe('MasterData Admin CRUD (admin API)', () => {
  test.describe.configure({ mode: 'serial' });

  let adminToken: string;
  let userToken: string;

  // IDs for cleanup
  let createdProvinceCode: string;
  let createdDistrictCode: string;

  test.beforeAll(async ({ request }) => {
    adminToken = await getAccessToken(request, 'admin');
    userToken = await getAccessToken(request, 'user');
    expect(adminToken).toBeTruthy();
    expect(userToken).toBeTruthy();
  });

  test.afterAll(async ({ request }) => {
    // Clean up in reverse FK order: district → province
    if (createdDistrictCode) {
      await cleanupEntity(request, adminToken, `/api/v1/admin/master-data/districts/${createdDistrictCode}`);
    }
    if (createdProvinceCode) {
      await cleanupEntity(request, adminToken, `/api/v1/admin/master-data/provinces/${createdProvinceCode}`);
    }
  });

  // ── Province CRUD ─────────────────────────────────────────────────────────────

  // TC-MD-018: POST province — non-admin user receives 403
  test('TC-MD-018: POST /admin/master-data/provinces with non-admin token — 403', async ({ request }) => {
    const res = await request.post(`${API_BASE}/api/v1/admin/master-data/provinces`, {
      headers: { ...authHeaders(userToken), 'Content-Type': 'application/json' },
      data: { code: 'P_NONADMIN', nameVi: 'Test', nameEn: 'Test', sortOrder: 0 },
    });
    expect(res.status()).toBe(403);
  });

  // TC-MD-019: POST province — unauthenticated receives 401
  test('TC-MD-019: POST /admin/master-data/provinces without auth — 401', async ({ request }) => {
    const res = await request.post(`${API_BASE}/api/v1/admin/master-data/provinces`, {
      headers: { 'Content-Type': 'application/json' },
      data: { code: 'P_ANON', nameVi: 'Test', nameEn: 'Test', sortOrder: 0 },
    });
    expect(res.status()).toBe(401);
  });

  // TC-MD-020: POST province — Admin creates new province successfully
  // Province.Code is MaxLength(10) — use short prefix + last 6 digits of timestamp
  test('TC-MD-020: POST /admin/master-data/provinces — Admin creates province', async ({ request }) => {
    const code = `P${String(Date.now()).slice(-7)}`;
    createdProvinceCode = code;
    const res = await request.post(`${API_BASE}/api/v1/admin/master-data/provinces`, {
      headers: { ...authHeaders(adminToken), 'Content-Type': 'application/json' },
      data: { code, nameVi: 'Tỉnh Kiểm Thử', nameEn: 'Test Province', sortOrder: 999 },
    });
    expect([200, 201]).toContain(res.status());
    if ([200, 201].includes(res.status())) {
      const body = await res.json();
      // accept wrapped or unwrapped id
      const id = body.data?.id ?? body.id ?? body.data;
      expect(id).toBeTruthy();
    }
  });

  // TC-MD-021: POST province — duplicate code returns 409
  test('TC-MD-021: POST /admin/master-data/provinces duplicate code — 409', async ({ request }) => {
    test.skip(!createdProvinceCode, 'Province not created in TC-MD-020');
    const res = await request.post(`${API_BASE}/api/v1/admin/master-data/provinces`, {
      headers: { ...authHeaders(adminToken), 'Content-Type': 'application/json' },
      data: { code: createdProvinceCode, nameVi: 'Tỉnh Kiểm Thử', nameEn: 'Test Province', sortOrder: 999 },
    });
    expect([409, 422]).toContain(res.status());
  });

  // TC-MD-023: PUT province — Admin updates existing province
  test('TC-MD-023: PUT /admin/master-data/provinces/{code} — Admin updates province', async ({ request }) => {
    test.skip(!createdProvinceCode, 'Province not created');
    const res = await request.put(
      `${API_BASE}/api/v1/admin/master-data/provinces/${createdProvinceCode}`,
      {
        headers: { ...authHeaders(adminToken), 'Content-Type': 'application/json' },
        data: {
          code: createdProvinceCode,
          nameVi: 'Tỉnh Cập Nhật',
          nameEn: 'Updated Province',
          sortOrder: 998,
        },
      },
    );
    expect([200, 204]).toContain(res.status());
  });

  // TC-MD-024: PUT province — non-existent code returns 404
  test('TC-MD-024: PUT /admin/master-data/provinces/ZZZNOTFOUND — 404', async ({ request }) => {
    const res = await request.put(
      `${API_BASE}/api/v1/admin/master-data/provinces/ZNOT_FOUND`,
      {
        headers: { ...authHeaders(adminToken), 'Content-Type': 'application/json' },
        data: { code: 'ZNOT_FOUND', nameVi: 'X', nameEn: 'X', sortOrder: 0 },
      },
    );
    expect(res.status()).toBe(404);
  });

  // TC-MD-025: DELETE province — Admin soft-deletes
  // Province.Code is MaxLength(10) — use short prefix + last 6 digits
  test('TC-MD-025: DELETE /admin/master-data/provinces/{code} — soft-deletes successfully', async ({ request }) => {
    // Create a dedicated province to delete so createdProvinceCode can still be used by district
    const code = `PD${String(Date.now()).slice(-6)}`;
    const createRes = await request.post(`${API_BASE}/api/v1/admin/master-data/provinces`, {
      headers: { ...authHeaders(adminToken), 'Content-Type': 'application/json' },
      data: { code, nameVi: 'Tỉnh Xóa', nameEn: 'Delete Province', sortOrder: 997 },
    });
    test.skip(![200, 201].includes(createRes.status()), 'Could not create province to delete');

    const delRes = await request.delete(
      `${API_BASE}/api/v1/admin/master-data/provinces/${code}`,
      { headers: authHeaders(adminToken) },
    );
    expect([200, 204]).toContain(delRes.status());
  });

  // TC-MD-026: DELETE province — non-existent code returns 404
  test('TC-MD-026: DELETE /admin/master-data/provinces/ZZZNOTFOUND — 404', async ({ request }) => {
    const res = await request.delete(
      `${API_BASE}/api/v1/admin/master-data/provinces/ZNOT_FOUND`,
      { headers: authHeaders(adminToken) },
    );
    expect(res.status()).toBe(404);
  });

  // ── District CRUD ─────────────────────────────────────────────────────────────

  // TC-MD-027: POST district — Admin creates new district linked to province
  // District.Code is MaxLength(10) — use short prefix + last 6 digits of timestamp
  test('TC-MD-027: POST /admin/master-data/districts — Admin creates district', async ({ request }) => {
    test.skip(!createdProvinceCode, 'Province not created');
    const code = `D${String(Date.now()).slice(-7)}`;
    createdDistrictCode = code;
    const res = await request.post(`${API_BASE}/api/v1/admin/master-data/districts`, {
      headers: { ...authHeaders(adminToken), 'Content-Type': 'application/json' },
      data: {
        code,
        provinceCode: createdProvinceCode,
        nameVi: 'Quận Kiểm Thử',
        nameEn: 'Test District',
        sortOrder: 999,
      },
    });
    expect([200, 201]).toContain(res.status());
    if ([200, 201].includes(res.status())) {
      const body = await res.json();
      const id = body.data?.id ?? body.id ?? body.data;
      expect(id).toBeTruthy();
    }
  });

  // TC-MD-028: POST district — duplicate code returns 409
  test('TC-MD-028: POST /admin/master-data/districts duplicate code — 409', async ({ request }) => {
    test.skip(!createdDistrictCode || !createdProvinceCode, 'District/province not created');
    const res = await request.post(`${API_BASE}/api/v1/admin/master-data/districts`, {
      headers: { ...authHeaders(adminToken), 'Content-Type': 'application/json' },
      data: {
        code: createdDistrictCode,
        provinceCode: createdProvinceCode,
        nameVi: 'Quận Kiểm Thử',
        nameEn: 'Test District',
        sortOrder: 999,
      },
    });
    expect([409, 422]).toContain(res.status());
  });

  // TC-MD-029 to TC-MD-034: District update/delete + Ward CRUD — see masterdata-admin-wards.spec.ts
});
