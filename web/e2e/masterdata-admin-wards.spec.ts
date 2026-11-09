import { test, expect } from './fixtures/test-base';
import { getAccessToken, authHeaders, API_BASE } from './helpers/auth-helper';
import { cleanupEntity } from './helpers/cleanup-helper';

// MasterData Admin Ward CRUD E2E — /api/v1/admin/master-data/wards
// P1 TCs: TC-MD-031 to TC-MD-034
// Depends on province + district created by masterdata-admin-crud.spec.ts pattern;
// this suite creates its own province+district hierarchy for full isolation.
test.describe('MasterData Admin Ward CRUD (admin API)', () => {
  test.describe.configure({ mode: 'serial' });

  let adminToken: string;
  let provinceCode: string;
  let districtCode: string;
  let wardCode: string;

  test.beforeAll(async ({ request }) => {
    adminToken = await getAccessToken(request, 'admin');
    expect(adminToken).toBeTruthy();

    // Create province + district hierarchy for ward tests
    // Province.Code and District.Code are MaxLength(10) — use short prefix + last 6 digits
    provinceCode = `WP${String(Date.now()).slice(-6)}`;
    await request.post(`${API_BASE}/api/v1/admin/master-data/provinces`, {
      headers: { ...authHeaders(adminToken), 'Content-Type': 'application/json' },
      data: { code: provinceCode, nameVi: 'Tỉnh Ward Test', nameEn: 'Ward Test Province', sortOrder: 995 },
    });

    districtCode = `WD${String(Date.now()).slice(-6)}`;
    await request.post(`${API_BASE}/api/v1/admin/master-data/districts`, {
      headers: { ...authHeaders(adminToken), 'Content-Type': 'application/json' },
      data: {
        code: districtCode,
        provinceCode,
        nameVi: 'Quận Ward Test',
        nameEn: 'Ward Test District',
        sortOrder: 995,
      },
    });
  });

  test.afterAll(async ({ request }) => {
    if (wardCode) {
      await cleanupEntity(request, adminToken, `/api/v1/admin/master-data/wards/${wardCode}`);
    }
    if (districtCode) {
      await cleanupEntity(request, adminToken, `/api/v1/admin/master-data/districts/${districtCode}`);
    }
    if (provinceCode) {
      await cleanupEntity(request, adminToken, `/api/v1/admin/master-data/provinces/${provinceCode}`);
    }
  });

  // TC-MD-031: POST ward — Admin creates new ward linked to district
  // Ward.Code is MaxLength(10) — use short prefix + last 6 digits
  test('TC-MD-031: POST /admin/master-data/wards — Admin creates ward', async ({ request }) => {
    test.skip(!districtCode, 'District not set up');
    const code = `W${String(Date.now()).slice(-7)}`;
    wardCode = code;
    const res = await request.post(`${API_BASE}/api/v1/admin/master-data/wards`, {
      headers: { ...authHeaders(adminToken), 'Content-Type': 'application/json' },
      data: {
        code,
        districtCode,
        nameVi: 'Phường Kiểm Thử',
        nameEn: 'Test Ward',
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

  // TC-MD-032: POST ward — duplicate code returns 409
  test('TC-MD-032: POST /admin/master-data/wards duplicate code — 409', async ({ request }) => {
    test.skip(!wardCode || !districtCode, 'Ward/district not created');
    const res = await request.post(`${API_BASE}/api/v1/admin/master-data/wards`, {
      headers: { ...authHeaders(adminToken), 'Content-Type': 'application/json' },
      data: {
        code: wardCode,
        districtCode,
        nameVi: 'Phường Kiểm Thử',
        nameEn: 'Test Ward',
        sortOrder: 999,
      },
    });
    expect([409, 422]).toContain(res.status());
  });

  // TC-MD-033: PUT ward — Admin updates existing ward
  test('TC-MD-033: PUT /admin/master-data/wards/{code} — Admin updates ward', async ({ request }) => {
    test.skip(!wardCode || !districtCode, 'Ward not created');
    const res = await request.put(
      `${API_BASE}/api/v1/admin/master-data/wards/${wardCode}`,
      {
        headers: { ...authHeaders(adminToken), 'Content-Type': 'application/json' },
        data: {
          code: wardCode,
          districtCode,
          nameVi: 'Phường Cập Nhật',
          nameEn: 'Updated Ward',
          sortOrder: 998,
        },
      },
    );
    expect([200, 204]).toContain(res.status());
  });

  // TC-MD-034: DELETE ward — soft-deletes successfully
  // Ward.Code is MaxLength(10) — use short prefix + last 7 digits
  test('TC-MD-034: DELETE /admin/master-data/wards/{code} — soft-deletes', async ({ request }) => {
    test.skip(!districtCode, 'District not set up');
    const code = `WD${String(Date.now()).slice(-6)}`;
    const createRes = await request.post(`${API_BASE}/api/v1/admin/master-data/wards`, {
      headers: { ...authHeaders(adminToken), 'Content-Type': 'application/json' },
      data: {
        code,
        districtCode,
        nameVi: 'Phường Xóa',
        nameEn: 'Delete Ward',
        sortOrder: 997,
      },
    });
    test.skip(![200, 201].includes(createRes.status()), 'Could not create ward to delete');

    const delRes = await request.delete(
      `${API_BASE}/api/v1/admin/master-data/wards/${code}`,
      { headers: authHeaders(adminToken) },
    );
    expect([200, 204]).toContain(delRes.status());
  });
});
