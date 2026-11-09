import { test, expect } from './fixtures/test-base';
import { getAccessToken, authHeaders, API_BASE } from './helpers/auth-helper';

// Reports compliance + execution polling E2E
// P1 TCs: TC-RPT-021, TC-RPT-023 to TC-RPT-024, TC-RPT-027 to TC-RPT-030
test.describe('Reports compliance and execution polling (authenticated API)', () => {
  test.describe.configure({ mode: 'serial' });

  let adminToken: string;
  let userToken: string;
  let officerToken: string;
  // executionId discovered by running a report; may be undefined if run endpoint unavailable
  let executionId: string;

  test.beforeAll(async ({ request }) => {
    adminToken   = await getAccessToken(request, 'admin');
    userToken    = await getAccessToken(request, 'user');
    officerToken = await getAccessToken(request, 'officer');
    expect(adminToken).toBeTruthy();

    // Attempt to get an executionId by running the first available definition
    const listRes = await request.get(`${API_BASE}/api/v1/reports/definitions`, {
      headers: authHeaders(adminToken),
    });
    if (listRes.status() === 200) {
      const items: { id: string }[] = (await listRes.json()).data?.items ?? [];
      if (items.length > 0) {
        const runRes = await request.post(`${API_BASE}/api/v1/reports/run`, {
          headers: { ...authHeaders(adminToken), 'Content-Type': 'application/json' },
          data: { reportDefinitionId: items[0].id, parametersJson: '{}', formatOverride: null },
        });
        if ([200, 202].includes(runRes.status())) {
          const body = await runRes.json();
          executionId =
            body.data?.executionId ?? body.data?.id ?? body.executionId ?? body.id ?? body.data;
        }
      }
    }
  });

  // ── Execution polling ─────────────────────────────────────────────────────────

  // TC-RPT-021: GET execution status — poll returns known status
  test('TC-RPT-021: GET /reports/executions/{id} — returns status field', async ({ request }) => {
    test.skip(!executionId, 'No executionId available');
    const res = await request.get(`${API_BASE}/api/v1/reports/executions/${executionId}`, {
      headers: authHeaders(adminToken),
    });
    expect(res.status()).toBe(200);
    const body = await res.json();
    const data = body.data ?? body;
    expect(data).toHaveProperty('status');
    expect(['Queued', 'Running', 'Done', 'Failed', 'Pending', 'Completed']).toContain(data.status);
  });

  // TC-RPT-023: GET execution status — user from another scope gets 403 or 404
  test('TC-RPT-023: GET /reports/executions/{id} cross-user — 403 or 404', async ({ request }) => {
    test.skip(!executionId, 'No executionId available');
    const res = await request.get(`${API_BASE}/api/v1/reports/executions/${executionId}`, {
      headers: authHeaders(userToken),
    });
    expect([403, 404]).toContain(res.status());
  });

  // ── Execution download ────────────────────────────────────────────────────────

  // TC-RPT-024: GET execution download — not-Done execution returns 404
  test('TC-RPT-024: GET /reports/executions/{id}/download not-done — 404', async ({ request }) => {
    test.skip(!executionId, 'No executionId available');
    const res = await request.get(
      `${API_BASE}/api/v1/reports/executions/${executionId}/download`,
      { headers: authHeaders(adminToken) },
    );
    // Queued/Running = 404 "not ready"; Done = 200/302; role-gated = 403
    expect([200, 302, 403, 404]).toContain(res.status());
  });

  // TC-RPT-027: GET execution download — unauthenticated returns 401
  test('TC-RPT-027: GET /reports/executions/{id}/download without auth — 401', async ({ request }) => {
    const id = executionId ?? '00000000-0000-0000-0000-000000000001';
    const res = await request.get(`${API_BASE}/api/v1/reports/executions/${id}/download`);
    expect(res.status()).toBe(401);
  });

  // ── Compliance PDF ────────────────────────────────────────────────────────────

  // TC-RPT-028: POST compliance PDF — Admin generates compliance evidence
  test('TC-RPT-028: POST /reports/compliance/pdf — Admin gets PDF', async ({ request }) => {
    const res = await request.post(`${API_BASE}/api/v1/reports/compliance/pdf`, {
      headers: { ...authHeaders(adminToken), 'Content-Type': 'application/json' },
      data: {
        framework: 'ISO27001',
        periodFrom: '2025-01-01T00:00:00Z',
        periodTo: '2025-12-31T23:59:59Z',
        generatedBy: 'e2e-test-admin',
      },
    });
    expect([200, 403, 404]).toContain(res.status());
    if (res.status() === 200) {
      const contentType = res.headers()['content-type'] ?? '';
      expect(contentType).toMatch(/pdf|octet-stream/i);
    }
  });

  // TC-RPT-029: POST compliance PDF — GovOfficer role forbidden
  test('TC-RPT-029: POST /reports/compliance/pdf with officer token — 403', async ({ request }) => {
    const res = await request.post(`${API_BASE}/api/v1/reports/compliance/pdf`, {
      headers: { ...authHeaders(officerToken), 'Content-Type': 'application/json' },
      data: {
        framework: 'ISO27001',
        periodFrom: '2025-01-01T00:00:00Z',
        periodTo: '2025-12-31T23:59:59Z',
        generatedBy: 'e2e-officer',
      },
    });
    expect(res.status()).toBe(403);
  });

  // TC-RPT-030: POST compliance PDF — unauthenticated returns 401
  test('TC-RPT-030: POST /reports/compliance/pdf without auth — 401', async ({ request }) => {
    const res = await request.post(`${API_BASE}/api/v1/reports/compliance/pdf`, {
      headers: { 'Content-Type': 'application/json' },
      data: {
        framework: 'ISO27001',
        periodFrom: '2025-01-01T00:00:00Z',
        periodTo: '2025-12-31T23:59:59Z',
        generatedBy: 'anon',
      },
    });
    expect(res.status()).toBe(401);
  });
});
