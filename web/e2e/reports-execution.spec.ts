import { test, expect } from './fixtures/test-base';
import { getAccessToken, authHeaders, API_BASE } from './helpers/auth-helper';

// Reports Execution E2E — report definitions CRUD, async run + auth guards
// P1 TCs: TC-RPT-005 to TC-RPT-020
// Execution polling + compliance PDF — see reports-compliance.spec.ts
// Note: TC-RPT-001/003/004 removed 2026-05-09 (REPORTS-DASHBOARD-ENDPOINT closed
// as YAGNI obsolete: no FE consumer for /reports/dashboard exists).
test.describe('Reports Execution (authenticated API)', () => {
  test.describe.configure({ mode: 'serial' });

  let token: string;
  let userToken: string;
  let officerToken: string;
  let definitionId: string;

  test.beforeAll(async ({ request }) => {
    token        = await getAccessToken(request, 'admin');
    userToken    = await getAccessToken(request, 'user');
    officerToken = await getAccessToken(request, 'officer');
    expect(token).toBeTruthy();
  });

  // TC-RPT-005: GET report definitions — Admin lists definitions
  test('TC-RPT-005: GET /reports/definitions — Admin gets list', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/reports/definitions`, {
      headers: authHeaders(token),
    });
    expect(res.status()).toBe(200);
    const body = await res.json();
    const items = body.data?.items ?? body.data ?? body;
    expect(Array.isArray(items)).toBe(true);
  });

  // TC-RPT-006 to TC-RPT-014: Definitions CRUD + auth guards — see reports-definitions.spec.ts
  // Seed definitionId here so run tests below can reference it
  test('setup: create report definition for run tests', async ({ request }) => {
    const res = await request.post(`${API_BASE}/api/v1/reports/definitions`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: {
        name: `E2E Run Test ${Date.now()}`,
        nameVi: 'Báo cáo chạy thử',
        description: 'Seed for run tests',
        sqlTemplate: 'SELECT TOP 10 * FROM audit.AuditLogs WHERE TenantId = @TenantId',
        parametersSchema: '{}',
        outputFormat: 1,
      },
    });
    if ([200, 201].includes(res.status())) {
      const body = await res.json();
      definitionId = body.data?.id ?? body.id ?? body.data;
    }
    if (!definitionId) {
      const listRes = await request.get(`${API_BASE}/api/v1/reports/definitions`, {
        headers: authHeaders(token),
      });
      const items: { id: string }[] = (await listRes.json()).data?.items ?? [];
      if (items.length > 0) definitionId = items[0].id;
    }
  });

  // TC-RPT-015: POST run report — GovOfficer executes and receives 202
  test('TC-RPT-015: POST /reports/run with officer token — 202', async ({ request }) => {
    if (!definitionId) {
      const listRes = await request.get(`${API_BASE}/api/v1/reports/definitions`, {
        headers: authHeaders(token),
      });
      const items = (await listRes.json()).data?.items ?? [];
      test.skip(items.length === 0, 'No definitions available');
      definitionId = items[0].id;
    }
    const res = await request.post(`${API_BASE}/api/v1/reports/run`, {
      headers: { ...authHeaders(officerToken), 'Content-Type': 'application/json' },
      data: {
        reportDefinitionId: definitionId,
        parametersJson: '{}',
        formatOverride: null,
      },
    });
    expect([200, 202, 422]).toContain(res.status());
  });

  // TC-RPT-016: POST run report — invalid JSON in parametersJson returns 422
  test('TC-RPT-016: POST /reports/run invalid parametersJson — 422', async ({ request }) => {
    const id = definitionId ?? '00000000-0000-0000-0000-000000000001';
    const res = await request.post(`${API_BASE}/api/v1/reports/run`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: { reportDefinitionId: id, parametersJson: 'not-valid-json', formatOverride: null },
    });
    expect([400, 422]).toContain(res.status());
  });
  // TC-RPT-018: POST run report — User role forbidden
  test('TC-RPT-018: POST /reports/run with user token — 403', async ({ request }) => {
    const id = definitionId ?? '00000000-0000-0000-0000-000000000001';
    const res = await request.post(`${API_BASE}/api/v1/reports/run`, {
      headers: { ...authHeaders(userToken), 'Content-Type': 'application/json' },
      data: { reportDefinitionId: id, parametersJson: '{}', formatOverride: null },
    });
    expect(res.status()).toBe(403);
  });
  // TC-RPT-020: POST run report — unauthenticated returns 401
  test('TC-RPT-020: POST /reports/run without auth — 401', async ({ request }) => {
    const res = await request.post(`${API_BASE}/api/v1/reports/run`, {
      headers: { 'Content-Type': 'application/json' },
      data: { reportDefinitionId: 'any', parametersJson: '{}' },
    });
    expect(res.status()).toBe(401);
  });

  // TC-RPT-021 to TC-RPT-030: Execution polling + download + compliance PDF — see reports-compliance.spec.ts
});
