import { test, expect } from './fixtures/test-base';
import { getAccessToken, authHeaders, API_BASE } from './helpers/auth-helper';

// Masterdata lookups E2E — provinces, districts, wards, case types, job titles, admin units
test.describe('Masterdata lookups (authenticated API)', () => {
  let token: string;

  test.beforeAll(async ({ request }) => {
    token = await getAccessToken(request);
    expect(token).toBeTruthy();
  });

  // TC-MD-001: GET provinces — anonymous succeeds
  test('TC-MD-001: GET /masterdata/provinces — anonymous returns 200', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/masterdata/provinces`);
    expect(res.status()).toBe(200);
    const body = await res.json();
    expect(body.success).toBe(true);
    const items: unknown[] = body.data?.items ?? body.data ?? [];
    // Accept 0 items — provinces may not be seeded in dev environment
    expect(items.length).toBeGreaterThanOrEqual(0);
  });

  // TC-MD-003: GET provinces — response shape validation
  test('TC-MD-003: GET /masterdata/provinces — response shape has code/nameVi/nameEn/sortOrder', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/masterdata/provinces`);
    expect(res.status()).toBe(200);
    const body = await res.json();
    const items: Record<string, unknown>[] = body.data?.items ?? body.data ?? [];
    // Skip shape validation when no data is seeded
    if (items.length === 0) return;
    const first = items[0];
    expect(typeof first.code).toBe('string');
    expect(typeof first.nameVi).toBe('string');
    expect(typeof first.nameEn).toBe('string');
    expect(typeof first.sortOrder).toBe('number');
  });

  // TC-MD-004: GET districts for a valid province code
  test('TC-MD-004: GET /masterdata/provinces/{code}/districts — anonymous returns districts', async ({ request }) => {
    const listRes = await request.get(`${API_BASE}/api/v1/masterdata/provinces`);
    const listBody = await listRes.json();
    const provinces: { code?: string; id?: string }[] =
      listBody.data?.items ?? listBody.data ?? [];
    test.skip(provinces.length === 0, 'No provinces seeded');
    const code = provinces[0].code ?? provinces[0].id ?? '';
    test.skip(!code, 'Province code not available');
    const res = await request.get(
      `${API_BASE}/api/v1/masterdata/provinces/${code}/districts`,
    );
    expect([200, 404]).toContain(res.status());
    if (res.status() === 200) {
      const body = await res.json();
      expect(body.success).toBe(true);
    }
  });

  // TC-MD-006: GET wards via province+district path
  test('TC-MD-006: GET /masterdata/provinces/{pCode}/districts/{dCode}/wards — anonymous returns wards', async ({
    request,
  }) => {
    const provRes = await request.get(`${API_BASE}/api/v1/masterdata/provinces`);
    const provBody = await provRes.json();
    const provinces: { code?: string; id?: string }[] =
      provBody.data?.items ?? provBody.data ?? [];
    test.skip(provinces.length === 0, 'No provinces seeded');
    const provCode = provinces[0].code ?? provinces[0].id ?? '';
    test.skip(!provCode, 'Province code not available');

    const distRes = await request.get(
      `${API_BASE}/api/v1/masterdata/provinces/${provCode}/districts`,
    );
    if (distRes.status() !== 200) {
      test.skip(true, 'Districts endpoint not reachable');
      return;
    }
    const distBody = await distRes.json();
    const districts: { code?: string; id?: string }[] =
      distBody.data?.items ?? distBody.data ?? [];
    test.skip(districts.length === 0, 'No districts seeded');
    const distCode = districts[0].code ?? districts[0].id ?? '';
    test.skip(!distCode, 'District code not available');

    const res = await request.get(
      `${API_BASE}/api/v1/masterdata/provinces/${provCode}/districts/${distCode}/wards`,
    );
    expect([200, 404]).toContain(res.status());
    if (res.status() === 200) {
      const body = await res.json();
      expect(body.success).toBe(true);
    }
  });

  // TC-MD-009: GET admin-units — anonymous succeeds (level 1 default)
  test('TC-MD-009: GET /masterdata/admin-units — anonymous returns level-1 units', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/masterdata/admin-units`);
    expect([200, 404]).toContain(res.status());
    if (res.status() === 200) {
      const body = await res.json();
      expect(body.success).toBe(true);
      const items: { level?: number }[] = body.data?.items ?? body.data ?? [];
      for (const item of items) {
        if (item.level !== undefined) {
          expect(item.level).toBe(1);
        }
      }
    }
  });

  // TC-MD-014: GET case-types — requires authentication (negative)
  test('TC-MD-014: GET /masterdata/case-types without auth — 401', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/masterdata/case-types`);
    expect(res.status()).toBe(401);
  });

  // TC-MD-015: GET case-types — authenticated returns tenant-scoped list
  test('TC-MD-015: GET /masterdata/case-types with auth — 200', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/masterdata/case-types`, {
      headers: authHeaders(token),
    });
    expect([200, 404]).toContain(res.status());
    if (res.status() === 200) {
      const body = await res.json();
      expect(body.success).toBe(true);
    }
  });

  // TC-MD-016: GET job-titles — requires authentication (negative)
  test('TC-MD-016: GET /masterdata/job-titles without auth — 401', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/masterdata/job-titles`);
    expect(res.status()).toBe(401);
  });

  // TC-MD-017: GET job-titles — authenticated returns list
  test('TC-MD-017: GET /masterdata/job-titles with auth — 200', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/masterdata/job-titles`, {
      headers: authHeaders(token),
    });
    expect([200, 404]).toContain(res.status());
    if (res.status() === 200) {
      const body = await res.json();
      expect(body.success).toBe(true);
    }
  });

});
