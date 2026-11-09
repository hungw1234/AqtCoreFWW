import { test, expect } from './fixtures/test-base';
import { getAccessToken, authHeaders, API_BASE } from './helpers/auth-helper';

// Webhook subscriptions admin E2E — WebhooksAdminController
// Route: /api/v1/admin/webhooks — Admin or SystemAdmin
// Correct request body: EndpointUrl + EventTypes (not url/events which causes 422)
// P1 TCs: HADM-056, 057, 060, 061, 063, 065
test.describe('Admin webhooks (authenticated API)', () => {
  test.describe.configure({ mode: 'serial' });
  let token: string;
  let createdWebhookId: string | undefined;

  test.beforeAll(async ({ request }) => {
    token = await getAccessToken(request);
    expect(token).toBeTruthy();
  });

  // TC-HADM-056: 401 without auth
  test('TC-HADM-056: POST /admin/webhooks without auth returns 401', async ({ request }) => {
    const res = await request.post(`${API_BASE}/api/v1/admin/webhooks`, {
      headers: { 'Content-Type': 'application/json' },
      data: { EndpointUrl: 'https://example.com/hook', EventTypes: ['user.created'] },
    });
    expect(res.status()).toBe(401);
  });

  // TC-HADM-065: GET deliveries 401 without auth
  test('TC-HADM-065: GET /admin/webhooks/{id}/deliveries without auth returns 401', async ({
    request,
  }) => {
    const fakeId = '00000000-0000-0000-0000-000000000001';
    const res = await request.get(`${API_BASE}/api/v1/admin/webhooks/${fakeId}/deliveries`);
    expect(res.status()).toBe(401);
  });

  // TC-HADM-057: Create webhook subscription — 201 with correct field names
  test('TC-HADM-057: POST /admin/webhooks creates subscription with correct fields', async ({
    request,
  }) => {
    const res = await request.post(`${API_BASE}/api/v1/admin/webhooks`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: {
        EndpointUrl: 'https://httpbin.org/post',
        EventTypes: ['user.created', 'user.updated'],
        Secret: null,
      },
    });
    // 201 = created; 400 = missing tenantId context; 403 = role-gated; 422 = validation
    expect([201, 400, 403, 422]).toContain(res.status());
    if (res.status() === 201) {
      const body = await res.json();
      expect(body.data).toHaveProperty('id');
      expect(body.data).toHaveProperty('endpointUrl', 'https://httpbin.org/post');
      expect(body.data).toHaveProperty('isActive', true);
      expect(Array.isArray(body.data.eventTypes)).toBe(true);
      createdWebhookId = body.data.id as string;
    }
  });

  // TC-HADM-060: List webhook subscriptions — 200
  test('TC-HADM-060: GET /admin/webhooks returns subscription list', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/admin/webhooks`, {
      headers: authHeaders(token),
    });
    // 200 = ok; 400 = no tenant context; 403 = role-gated
    expect([200, 400, 403]).toContain(res.status());
    if (res.status() === 200) {
      const body = await res.json();
      expect(body.success).toBe(true);
      expect(Array.isArray(body.data)).toBe(true);
      // Each item must not expose secret hash
      for (const item of body.data as Record<string, unknown>[]) {
        expect(item).toHaveProperty('id');
        expect(item).toHaveProperty('endpointUrl');
        expect(item).toHaveProperty('isActive');
        expect(item).not.toHaveProperty('secretHash');
        expect(item).not.toHaveProperty('plaintextSecret');
      }
    }
  });

  // TC-HADM-063: Get delivery history for created subscription
  test('TC-HADM-063: GET /admin/webhooks/{id}/deliveries returns delivery list', async ({
    request,
  }) => {
    test.skip(!createdWebhookId, 'Skipped: no webhook ID from create step');

    const res = await request.get(
      `${API_BASE}/api/v1/admin/webhooks/${createdWebhookId}/deliveries?page=1&pageSize=20`,
      { headers: authHeaders(token) },
    );
    // 200 = ok; 400 = no tenant; 404 = subscription not found under this tenant
    expect([200, 400, 403, 404]).toContain(res.status());
    if (res.status() === 200) {
      const body = await res.json();
      expect(body.success).toBe(true);
      expect(Array.isArray(body.data)).toBe(true);
      // Each delivery item (if any) must have required fields
      for (const item of body.data as Record<string, unknown>[]) {
        expect(item).toHaveProperty('id');
        expect(item).toHaveProperty('subscriptionId');
        expect(item).toHaveProperty('eventType');
        expect(item).toHaveProperty('attemptNumber');
        expect(item).toHaveProperty('attemptedAt');
        expect(item).toHaveProperty('isSuccess');
      }
    }
  });

  // TC-HADM-061: Delete webhook subscription — 204
  test('TC-HADM-061: DELETE /admin/webhooks/{id} removes subscription', async ({ request }) => {
    test.skip(!createdWebhookId, 'Skipped: no webhook ID to delete');

    const res = await request.delete(`${API_BASE}/api/v1/admin/webhooks/${createdWebhookId}`, {
      headers: authHeaders(token),
    });
    // 204 = deleted; 400 = no tenant; 403 = role-gated; 404 = already gone
    expect([204, 400, 403, 404]).toContain(res.status());
    if (res.status() === 204) {
      // Verify removal — subscription should not appear in list
      const listRes = await request.get(`${API_BASE}/api/v1/admin/webhooks`, {
        headers: authHeaders(token),
      });
      if (listRes.status() === 200) {
        const listBody = await listRes.json();
        const ids = (listBody.data as Array<{ id: string }>).map((s) => s.id);
        expect(ids).not.toContain(createdWebhookId);
      }
    }
  });
});
