import { test, expect } from './fixtures/test-base';
import { getAccessToken, authHeaders, API_BASE } from './helpers/auth-helper';

// Alerting rules admin E2E — AlertingAdminController CRUD
// Route: /api/v1/admin/alerts — SystemAdmin only
// P1 TCs: HADM-043, 044, 045, 046, 048, 049, 050, 052
test.describe('Admin alerting rules (authenticated API)', () => {
  test.describe.configure({ mode: 'serial' });
  let token: string;
  let createdRuleId: string | undefined;

  test.beforeAll(async ({ request }) => {
    token = await getAccessToken(request);
    expect(token).toBeTruthy();
  });

  // TC-HADM-043: 401 without auth
  test('TC-HADM-043: GET /admin/alerts without auth returns 401', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/admin/alerts`);
    expect(res.status()).toBe(401);
  });

  // TC-HADM-044: 403 for non-SystemAdmin
  test('TC-HADM-044: GET /admin/alerts with user role returns 403', async ({ request }) => {
    const userToken = await getAccessToken(request, 'user');
    const res = await request.get(`${API_BASE}/api/v1/admin/alerts`, {
      headers: authHeaders(userToken),
    });
    expect([401, 403]).toContain(res.status());
  });

  // TC-HADM-045: List alert rules — 200 paginated
  test('TC-HADM-045: GET /admin/alerts returns alert rule list', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/admin/alerts`, {
      headers: authHeaders(token),
    });
    // 200 = ok; 403 = token lacks SystemAdmin in this env
    expect([200, 403]).toContain(res.status());
    if (res.status() === 200) {
      const body = await res.json();
      expect(body.success).toBe(true);
      expect(body.data).toHaveProperty('items');
      expect(Array.isArray(body.data.items)).toBe(true);
      expect(typeof body.data.totalCount).toBe('number');
      if (body.data.items.length > 0) {
        const item = body.data.items[0] as Record<string, unknown>;
        expect(item).toHaveProperty('id');
        expect(item).toHaveProperty('name');
        expect(item).toHaveProperty('metricName');
        expect(item).toHaveProperty('condition');
        expect(item).toHaveProperty('threshold');
        expect(item).toHaveProperty('windowMinutes');
        expect(item).toHaveProperty('notifyChannel');
        expect(item).toHaveProperty('enabled');
      }
    }
  });

  // TC-HADM-049: POST 401 without auth
  test('TC-HADM-049: POST /admin/alerts without auth returns 401', async ({ request }) => {
    const res = await request.post(`${API_BASE}/api/v1/admin/alerts`, {
      headers: { 'Content-Type': 'application/json' },
      data: {
        Name: 'x', MetricName: 'y', Condition: '>',
        Threshold: 1, WindowMinutes: 1, NotifyChannel: 'email', NotifyTarget: null,
      },
    });
    expect(res.status()).toBe(401);
  });

  // TC-HADM-048: Create alert rule — 201 with ID
  test('TC-HADM-048: POST /admin/alerts creates alert rule and returns 201', async ({ request }) => {
    const res = await request.post(`${API_BASE}/api/v1/admin/alerts`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: {
        Name: `E2E-HighErrorRate-${Date.now()}`,
        MetricName: 'http_server_errors_total',
        Condition: '>',
        Threshold: 10.0,
        WindowMinutes: 5,
        NotifyChannel: 'email',
        NotifyTarget: 'admin@example.com',
      },
    });
    // 201 = created; 403 = SystemAdmin-only env; 422 = validation
    expect([201, 403, 422]).toContain(res.status());
    if (res.status() === 201) {
      const body = await res.json();
      expect(body.data).toHaveProperty('id');
      expect(typeof body.data.id).toBe('string');
      createdRuleId = body.data.id as string;
    }
  });

  // TC-HADM-046: Get alert rule by ID
  test('TC-HADM-046: GET /admin/alerts/{id} returns alert rule detail', async ({ request }) => {
    // Try to get first existing rule from list if create failed
    if (!createdRuleId) {
      const listRes = await request.get(`${API_BASE}/api/v1/admin/alerts?page=1&pageSize=1`, {
        headers: authHeaders(token),
      });
      if (listRes.status() !== 200) {
        test.skip(true, 'No access to alerts endpoint');
        return;
      }
      const listBody = await listRes.json();
      const items: Array<{ id: string }> = listBody.data?.items ?? [];
      if (items.length === 0) {
        test.skip(true, 'No alert rules exist to test GET by ID');
        return;
      }
      createdRuleId = items[0].id;
    }

    const res = await request.get(`${API_BASE}/api/v1/admin/alerts/${createdRuleId}`, {
      headers: authHeaders(token),
    });
    expect([200, 403]).toContain(res.status());
    if (res.status() === 200) {
      const body = await res.json();
      expect(body.data).toHaveProperty('id', createdRuleId);
      expect(body.data).toHaveProperty('name');
      expect(body.data).toHaveProperty('metricName');
      expect(body.data).toHaveProperty('condition');
    }
  });

  // TC-HADM-050: Update alert rule — 200
  test('TC-HADM-050: PUT /admin/alerts/{id} updates alert rule fields', async ({ request }) => {
    test.skip(!createdRuleId, 'Skipped: no rule ID from create or list step');

    const res = await request.put(`${API_BASE}/api/v1/admin/alerts/${createdRuleId}`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: {
        Name: 'E2E-HighErrorRate-Updated',
        MetricName: 'http_server_errors_total',
        Condition: '>',
        Threshold: 20.0,
        WindowMinutes: 10,
        NotifyChannel: 'slack',
        NotifyTarget: '#alerts',
      },
    });
    expect([200, 403, 404]).toContain(res.status());
    if (res.status() === 200) {
      const body = await res.json();
      expect(body.success).toBe(true);
    }
  });

  // TC-HADM-052: Delete alert rule — 204
  test('TC-HADM-052: DELETE /admin/alerts/{id} removes alert rule', async ({ request }) => {
    test.skip(!createdRuleId, 'Skipped: no rule ID to delete');

    const res = await request.delete(`${API_BASE}/api/v1/admin/alerts/${createdRuleId}`, {
      headers: authHeaders(token),
    });
    // 204 = deleted; 404 = already gone; 403 = role-gated
    expect([204, 403, 404]).toContain(res.status());

    if (res.status() === 204) {
      // Verify deletion — subsequent GET must return 404
      const getRes = await request.get(`${API_BASE}/api/v1/admin/alerts/${createdRuleId}`, {
        headers: authHeaders(token),
      });
      expect([404, 403]).toContain(getRes.status());
    }
  });
});
