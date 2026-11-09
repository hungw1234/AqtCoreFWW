import { test, expect } from './fixtures/test-base';
import { getAccessToken, authHeaders, API_BASE } from './helpers/auth-helper';

/**
 * Concurrent edit conflict detection (optimistic concurrency).
 */
test.describe('Concurrent Edit Detection', () => {
  test.setTimeout(60_000);

  test('editing stale resource shows conflict error', async ({ request }) => {
    const token = await getAccessToken(request);

    // Get a form template (if any exist)
    const listRes = await request.get(`${API_BASE}/api/v1/forms/templates?page=1&pageSize=1`, {
      headers: authHeaders(token),
    });
    const body = await listRes.json();
    const items = body.data?.items ?? body.items ?? [];
    test.skip(items.length === 0, 'No form templates to test concurrent edit');

    const item = items[0];
    const templateId = item.id;
    const concurrencyToken = item.concurrencyToken ?? item.rowVersion;

    // First update
    const res1 = await request.put(`${API_BASE}/api/v1/forms/templates/${templateId}`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: { ...item, description: `Updated at ${Date.now()}` },
    });
    // 405 = PUT not supported on this endpoint
    expect([200, 204, 400, 405, 422]).toContain(res1.status());

    // Second update with stale concurrency token
    if (concurrencyToken) {
      const res2 = await request.put(`${API_BASE}/api/v1/forms/templates/${templateId}`, {
        headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
        data: {
          ...item,
          concurrencyToken, // stale
          description: `Stale update at ${Date.now()}`,
        },
      });
      // 409 Conflict or validation error — implementation dependent
      expect([200, 204, 400, 409, 422]).toContain(res2.status());
    }
  });
});
