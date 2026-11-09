import { test, expect } from './fixtures/test-base';
import { getAccessToken, authHeaders, API_BASE } from './helpers/auth-helper';

// Report schedules / dashboards E2E — /api/v1/dashboards
// P1 TCs: TC-RPT-031 to TC-RPT-034, TC-RPT-036 to TC-RPT-038
test.describe('Report dashboards CRUD (authenticated API)', () => {
  test.describe.configure({ mode: 'serial' });

  let adminToken: string;
  let dashboardId: string;

  test.beforeAll(async ({ request }) => {
    adminToken = await getAccessToken(request, 'admin');
    expect(adminToken).toBeTruthy();
  });

  // TC-RPT-031: POST create dashboard — valid name
  test('TC-RPT-031: POST /dashboards — creates dashboard with valid name', async ({ request }) => {
    const res = await request.post(`${API_BASE}/api/v1/dashboards`, {
      headers: { ...authHeaders(adminToken), 'Content-Type': 'application/json' },
      data: {
        name: `My E2E Dashboard ${Date.now()}`,
        isDefault: false,
      },
    });
    expect([200, 201]).toContain(res.status());
    if ([200, 201].includes(res.status())) {
      const body = await res.json();
      dashboardId = body.data?.id ?? body.id ?? body.data;
      expect(dashboardId).toBeTruthy();
    }
  });

  // TC-RPT-032: POST create dashboard — missing name returns 422
  test('TC-RPT-032: POST /dashboards empty name — 422', async ({ request }) => {
    const res = await request.post(`${API_BASE}/api/v1/dashboards`, {
      headers: { ...authHeaders(adminToken), 'Content-Type': 'application/json' },
      data: { name: '', isDefault: false },
    });
    expect([400, 422]).toContain(res.status());
  });

  // TC-RPT-033: POST create dashboard — unauthenticated returns 401
  test('TC-RPT-033: POST /dashboards without auth — 401', async ({ request }) => {
    const res = await request.post(`${API_BASE}/api/v1/dashboards`, {
      headers: { 'Content-Type': 'application/json' },
      data: { name: 'Anon Dashboard', isDefault: false },
    });
    expect(res.status()).toBe(401);
  });

  // TC-RPT-034: GET single dashboard by ID
  test('TC-RPT-034: GET /dashboards/{id} — returns dashboard by ID', async ({ request }) => {
    test.skip(!dashboardId, 'Dashboard not created in TC-RPT-031');
    const res = await request.get(`${API_BASE}/api/v1/dashboards/${dashboardId}`, {
      headers: authHeaders(adminToken),
    });
    expect(res.status()).toBe(200);
    const body = await res.json();
    const data = body.data ?? body;
    expect(data.id ?? data.name).toBeTruthy();
  });

  // TC-RPT-036: GET list dashboards — returns user's dashboards
  test('TC-RPT-036: GET /dashboards — returns list of dashboards', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/dashboards`, {
      headers: authHeaders(adminToken),
    });
    expect(res.status()).toBe(200);
    const body = await res.json();
    const items = body.data?.items ?? body.data ?? body;
    expect(Array.isArray(items)).toBe(true);
  });

  // TC-RPT-037: GET list dashboards — unauthenticated returns 401
  test('TC-RPT-037: GET /dashboards without auth — 401', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/dashboards`);
    expect(res.status()).toBe(401);
  });

  // TC-RPT-038: PUT update dashboard — rename and set as default
  test('TC-RPT-038: PUT /dashboards/{id} — renames and sets default', async ({ request }) => {
    test.skip(!dashboardId, 'Dashboard not created');
    const res = await request.put(`${API_BASE}/api/v1/dashboards/${dashboardId}`, {
      headers: { ...authHeaders(adminToken), 'Content-Type': 'application/json' },
      data: {
        name: `My E2E Dashboard Renamed ${Date.now()}`,
        widgetPositions: null,
        isDefault: true,
      },
    });
    expect([200, 204]).toContain(res.status());
  });
});
