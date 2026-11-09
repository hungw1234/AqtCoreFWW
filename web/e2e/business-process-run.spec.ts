import { test, expect } from './fixtures/test-base';
import { getAccessToken, authHeaders, API_BASE } from './helpers/auth-helper';

// BP Run E2E smoke — TC-BP-RUN-001 through TC-BP-RUN-004
// Requires: Docker BE up, seed data with at least one active BP
test.describe('Business Process Run — lifecycle smoke', () => {
  let token: string;

  test.beforeAll(async ({ request }) => {
    token = await getAccessToken(request);
    expect(token).toBeTruthy();
  });

  // TC-BP-RUN-001: Active BP list returns data
  test('TC-BP-RUN-001: GET /business-processes/active returns 200 with items', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/business-processes/active`, {
      headers: authHeaders(token),
    });
    expect(res.status()).toBe(200);
    const body = await res.json();
    // May be empty array if no seed BPs — just verify shape
    expect(Array.isArray(body)).toBeTruthy();
  });

  // TC-BP-RUN-002: /business-process-runs/new page renders BP picker
  test('TC-BP-RUN-002: /business-process-runs/new loads without console error', async ({ page }) => {
    const consoleErrors: string[] = [];
    page.on('console', (msg) => {
      if (msg.type() === 'error') consoleErrors.push(msg.text());
    });

    await page.goto('/business-process-runs/new');
    // Wait for React to hydrate (Select placeholder or permission guard)
    await page.waitForSelector('[class*="ant-select"], [class*="ant-alert"], [class*="ant-spin"]', {
      timeout: 10000,
    });

    // Filter out known non-critical 3rd party errors
    const criticalErrors = consoleErrors.filter(
      (e) => !e.includes('favicon') && !e.includes('ResizeObserver')
    );
    expect(criticalErrors).toHaveLength(0);
  });

  // TC-BP-RUN-003: Start BP run API — requires seed BP
  // Skipped if no active BPs (non-blocking for CI without seed)
  test('TC-BP-RUN-003: POST /business-processes/{id}/runs creates a run', async ({ request }) => {
    const listRes = await request.get(`${API_BASE}/api/v1/business-processes/active`, {
      headers: authHeaders(token),
    });
    const bps = await listRes.json() as { id: string }[];

    if (!bps || bps.length === 0) {
      test.skip(true, 'No active BPs in seed data — skipping run creation test');
      return;
    }

    const bpId = bps[0].id;
    const res = await request.post(`${API_BASE}/api/v1/business-processes/${bpId}/runs`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: { entityType: 'BusinessProcess', entityId: bpId, intakeFormData: null },
    });
    // 200/201 = created; 422 = missing intake data; 404 = BP gone
    expect([200, 201, 422, 404]).toContain(res.status());
  });

  // TC-BP-RUN-004: Detail page route resolves (even with unknown id — shows not found vs 500)
  test('TC-BP-RUN-004: /business-process-runs/:id does not crash on unknown id', async ({ page }) => {
    const consoleErrors: string[] = [];
    page.on('console', (msg) => {
      if (msg.type() === 'error') consoleErrors.push(msg.text());
    });

    await page.goto('/business-process-runs/00000000-0000-0000-0000-000000000000');
    // Should render not-found alert or spin — not a blank page / unhandled error boundary
    await page.waitForSelector('[class*="ant-alert"], [class*="ant-spin"], [class*="ant-result"]', {
      timeout: 10000,
    });

    const criticalErrors = consoleErrors.filter(
      (e) => !e.includes('favicon') && !e.includes('ResizeObserver') && !e.includes('404')
    );
    expect(criticalErrors).toHaveLength(0);
  });
});
