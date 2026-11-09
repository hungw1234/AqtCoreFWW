import { test, expect } from './fixtures/test-base';
import { getAccessToken, authHeaders, API_BASE } from './helpers/auth-helper';

// Notifications preferences E2E — notification list filters and pagination
// P1 TCs: TC-NOTIF-003 (isRead filter), TC-NOTIF-009/010 (unread-count auth guard)
// These are user-preference-level concerns: filter, channel, pagination behaviour
test.describe('Notifications list filters and preferences', () => {
  let token: string;

  test.beforeAll(async ({ request }) => {
    token = await getAccessToken(request);
    expect(token).toBeTruthy();
  });

  // GET /notifications?isRead=false — only unread items returned
  test('TC-NOTIF-003: GET /notifications?isRead=false — all items have isRead=false', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/notifications?isRead=false`, {
      headers: authHeaders(token),
    });
    expect(res.status()).toBe(200);
    const body = await res.json();
    expect(body.success).toBe(true);
    const items: { isRead?: boolean }[] = body.data?.items ?? body.data ?? [];
    for (const item of items) {
      if (item.isRead !== undefined) expect(item.isRead).toBe(false);
    }
  });

  // GET /notifications?channel=inapp — all returned items have channel=inapp
  test('TC-NOTIF-005: GET /notifications?channel=inapp — all items have channel=inapp', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/notifications?channel=inapp`, {
      headers: authHeaders(token),
    });
    expect(res.status()).toBe(200);
    const body = await res.json();
    expect(body.success).toBe(true);
    const items: { channel?: string }[] = body.data?.items ?? body.data ?? [];
    for (const item of items) {
      if (item.channel !== undefined) expect(item.channel).toBe('inapp');
    }
  });

  // GET /notifications?page=1&pageSize=5 — meta reflects requested page/pageSize
  test('TC-NOTIF-007: GET /notifications?page=1&pageSize=5 — pagination meta correct', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/notifications?page=1&pageSize=5`, {
      headers: authHeaders(token),
    });
    expect(res.status()).toBe(200);
    const body = await res.json();
    expect(body.success).toBe(true);
    const items: unknown[] = body.data?.items ?? body.data ?? [];
    expect(items.length).toBeLessThanOrEqual(5);
    const meta = body.data?.meta;
    if (meta) {
      expect(meta.page).toBe(1);
      expect(meta.pageSize).toBe(5);
    }
  });

  // GET /notifications/unread-count — numeric, non-negative
  test('TC-NOTIF-009: GET /notifications/unread-count — returns number >= 0', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/notifications/unread-count`, {
      headers: authHeaders(token),
    });
    expect(res.status()).toBe(200);
    const body = await res.json();
    expect(body.success).toBe(true);
    const count = body.data?.count ?? body.data;
    expect(typeof count).toBe('number');
    expect(count).toBeGreaterThanOrEqual(0);
  });

  // GET /notifications/unread-count without auth — 401
  test('TC-NOTIF-010: GET /notifications/unread-count without auth — 401', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/notifications/unread-count`);
    expect(res.status()).toBe(401);
  });

  // POST /notifications/read-all — idempotent (second call also 204)
  test('TC-NOTIF-019: POST /notifications/read-all with no unread — still 204', async ({ request }) => {
    // First call marks all read
    await request.post(`${API_BASE}/api/v1/notifications/read-all`, {
      headers: authHeaders(token),
    });
    // Second call on already-read set must not error
    const res = await request.post(`${API_BASE}/api/v1/notifications/read-all`, {
      headers: authHeaders(token),
    });
    expect([200, 204]).toContain(res.status());
  });
});
