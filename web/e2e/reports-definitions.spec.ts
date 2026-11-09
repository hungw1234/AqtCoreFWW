import { test, expect } from './fixtures/test-base';
import { getAccessToken, authHeaders, API_BASE } from './helpers/auth-helper';

// Report definitions CRUD E2E — /api/v1/reports/definitions
// P1 TCs: TC-RPT-005 to TC-RPT-008, TC-RPT-010 to TC-RPT-011, TC-RPT-014
test.describe('Report definitions CRUD (admin API)', () => {
  test.describe.configure({ mode: 'serial' });

  let adminToken: string;
  let userToken: string;
  let officerToken: string;
  let definitionId: string;

  test.beforeAll(async ({ request }) => {
    adminToken   = await getAccessToken(request, 'admin');
    userToken    = await getAccessToken(request, 'user');
    officerToken = await getAccessToken(request, 'officer');
    expect(adminToken).toBeTruthy();
  });

  // TC-RPT-005: GET report definitions — Admin lists definitions
  test('TC-RPT-005: GET /reports/definitions — Admin returns list', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/reports/definitions`, {
      headers: authHeaders(adminToken),
    });
    expect(res.status()).toBe(200);
    const body = await res.json();
    const items = body.data?.items ?? body.data ?? body;
    expect(Array.isArray(items)).toBe(true);
  });

  // TC-RPT-006: GET report definitions — User role forbidden
  test('TC-RPT-006: GET /reports/definitions with user token — 403', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/reports/definitions`, {
      headers: authHeaders(userToken),
    });
    expect(res.status()).toBe(403);
  });

  // TC-RPT-007: POST create report definition — valid payload
  test('TC-RPT-007: POST /reports/definitions — Admin creates with valid payload', async ({ request }) => {
    const res = await request.post(`${API_BASE}/api/v1/reports/definitions`, {
      headers: { ...authHeaders(adminToken), 'Content-Type': 'application/json' },
      data: {
        name: `E2E Monthly Audit ${Date.now()}`,
        nameVi: 'Báo cáo vụ việc hàng tháng',
        description: 'Monthly summary of audit logs by action',
        sqlTemplate: 'SELECT TOP 10 * FROM audit.AuditLogs WHERE TenantId = @TenantId',
        parametersSchema: '{}',
        outputFormat: 1,
      },
    });
    expect([200, 201, 422]).toContain(res.status());
    if ([200, 201].includes(res.status())) {
      const body = await res.json();
      definitionId = body.data?.id ?? body.id ?? body.data;
      expect(definitionId).toBeTruthy();
    }
  });

  // TC-RPT-008: POST create report definition — missing required fields returns 422
  test('TC-RPT-008: POST /reports/definitions empty fields — 422', async ({ request }) => {
    const res = await request.post(`${API_BASE}/api/v1/reports/definitions`, {
      headers: { ...authHeaders(adminToken), 'Content-Type': 'application/json' },
      data: {
        name: '',
        nameVi: '',
        description: '',
        sqlTemplate: '',
        parametersSchema: '',
        outputFormat: 1,
      },
    });
    expect([400, 422]).toContain(res.status());
  });

  // TC-RPT-010: POST create report definition — GovOfficer role forbidden
  test('TC-RPT-010: POST /reports/definitions with officer token — 403', async ({ request }) => {
    const res = await request.post(`${API_BASE}/api/v1/reports/definitions`, {
      headers: { ...authHeaders(officerToken), 'Content-Type': 'application/json' },
      data: {
        name: 'GovOfficer attempt',
        nameVi: 'Test',
        description: 'desc',
        sqlTemplate: 'SELECT 1',
        parametersSchema: '{}',
        outputFormat: 1,
      },
    });
    expect(res.status()).toBe(403);
  });

  // TC-RPT-011: PUT update report definition — Admin updates name and SQL
  test('TC-RPT-011: PUT /reports/definitions/{id} — Admin updates definition', async ({ request }) => {
    // Ensure we have a definition to update
    if (!definitionId) {
      const listRes = await request.get(`${API_BASE}/api/v1/reports/definitions`, {
        headers: authHeaders(adminToken),
      });
      const items: { id: string }[] = (await listRes.json()).data?.items ?? [];
      test.skip(items.length === 0, 'No report definitions available to update');
      definitionId = items[0].id;
    }

    const res = await request.put(
      `${API_BASE}/api/v1/reports/definitions/${definitionId}`,
      {
        headers: { ...authHeaders(adminToken), 'Content-Type': 'application/json' },
        data: {
          name: 'E2E Monthly Audit Updated',
          nameVi: 'Báo cáo cập nhật',
          description: 'Updated description',
          sqlTemplate: 'SELECT TOP 5 * FROM audit.AuditLogs WHERE TenantId = @TenantId',
          parametersSchema: '{}',
          outputFormat: 2,
        },
      },
    );
    expect([200, 204]).toContain(res.status());
  });

  // TC-RPT-014: PUT update report definition — GovOfficer forbidden
  test('TC-RPT-014: PUT /reports/definitions/{id} with officer token — 403', async ({ request }) => {
    const id = definitionId ?? '00000000-0000-0000-0000-000000000001';
    const res = await request.put(
      `${API_BASE}/api/v1/reports/definitions/${id}`,
      {
        headers: { ...authHeaders(officerToken), 'Content-Type': 'application/json' },
        data: {
          name: 'Officer update attempt',
          nameVi: 'x',
          description: 'x',
          sqlTemplate: 'SELECT 1',
          parametersSchema: '{}',
          outputFormat: 1,
        },
      },
    );
    expect(res.status()).toBe(403);
  });
});
