import { test, expect } from './fixtures/test-base';
import { getAccessToken, authHeaders, API_BASE } from './helpers/auth-helper';

const MSG_URL = `${API_BASE}/api/v1/messages`;

// Unread counts E2E — minimal spec covering GET /messages/unread-count auth guard + shape
test.describe('Collab Unread Counts (authenticated API)', () => {
  let token: string;

  test.beforeAll(async ({ request }) => {
    token = await getAccessToken(request, 'admin');
    expect(token).toBeTruthy();
  });

  // TC-COLLAB-050
  test('TC-COLLAB-050: GET /messages/unread-count without auth returns 401', async ({ request }) => {
    const res = await request.get(`${MSG_URL}/unread-count`);
    expect(res.status()).toBe(401);
  });

  // TC-COLLAB-048
  test('TC-COLLAB-048: GET /messages/unread-count returns 200 with per-conversation counts', async ({ request }) => {
    const res = await request.get(`${MSG_URL}/unread-count`, {
      headers: authHeaders(token),
    });
    expect(res.status()).toBe(200);
    const body = await res.json();
    // Response is either { data: [...] } or a direct array
    const items = Array.isArray(body) ? body : (body.data ?? []);
    expect(Array.isArray(items)).toBe(true);
    // If items present, verify shape of first element
    if (items.length > 0) {
      const entry = items[0];
      expect(entry).toHaveProperty('conversationId');
      expect(entry).toHaveProperty('unreadCount');
      expect(typeof entry.unreadCount).toBe('number');
    }
  });
});
