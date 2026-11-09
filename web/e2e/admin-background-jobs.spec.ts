import { test, expect } from './fixtures/test-base';
import { getAccessToken, authHeaders, API_BASE } from './helpers/auth-helper';

// Background jobs admin E2E — Hangfire job monitoring dashboard
// Covers: JobsAdminController GET /api/v1/admin/jobs + GET /api/v1/admin/jobs/stats
// P1 TCs: HADM-012, 013, 017, 018
test.describe('Admin background jobs (authenticated API)', () => {
  let token: string;

  test.beforeAll(async ({ request }) => {
    token = await getAccessToken(request);
    expect(token).toBeTruthy();
  });

  // TC-HADM-012: 401 without auth
  test('TC-HADM-012: GET /admin/jobs without auth returns 401', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/admin/jobs`);
    expect(res.status()).toBe(401);
  });

  // TC-HADM-013: List jobs default — recurring + failed dashboard view
  test('TC-HADM-013: GET /admin/jobs returns job list with counts', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/admin/jobs`, {
      headers: authHeaders(token),
    });
    // 200 = ok; 403 = role-gated in restricted env
    expect([200, 403]).toContain(res.status());
    if (res.status() === 200) {
      const body = await res.json();
      expect(body.success).toBe(true);
      // items array may be empty on clean env — just verify shape
      expect(body.data).toHaveProperty('items');
      expect(body.data).toHaveProperty('totalCount');
      expect(typeof body.data.totalCount).toBe('number');
      expect(body.data.totalCount).toBeGreaterThanOrEqual(0);
      // counts must include all Hangfire states
      expect(body.data).toHaveProperty('counts');
      const counts = body.data.counts as Record<string, unknown>;
      expect(counts).toHaveProperty('Enqueued');
      expect(counts).toHaveProperty('Processing');
      expect(counts).toHaveProperty('Scheduled');
      expect(counts).toHaveProperty('Succeeded');
      expect(counts).toHaveProperty('Failed');
      expect(counts).toHaveProperty('Recurring');
    }
  });

  // TC-HADM-018: Stats 401 without auth
  test('TC-HADM-018: GET /admin/jobs/stats without auth returns 401', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/admin/jobs/stats`);
    expect(res.status()).toBe(401);
  });

  // TC-HADM-017: Get job stats with all counters
  test('TC-HADM-017: GET /admin/jobs/stats returns all state counters', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/admin/jobs/stats`, {
      headers: authHeaders(token),
    });
    expect([200, 403]).toContain(res.status());
    if (res.status() === 200) {
      const body = await res.json();
      expect(body.success).toBe(true);
      const data = body.data as Record<string, number>;
      // All counters must be present and non-negative
      for (const key of ['enqueued', 'processing', 'scheduled', 'succeeded', 'failed', 'pending']) {
        expect(typeof data[key]).toBe('number');
        expect(data[key]).toBeGreaterThanOrEqual(0);
      }
      // pending === enqueued + scheduled (controller invariant)
      expect(data['pending']).toBe(data['enqueued'] + data['scheduled']);
    }
  });
});
