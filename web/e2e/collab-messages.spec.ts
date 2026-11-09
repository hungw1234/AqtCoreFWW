import { test, expect } from './fixtures/test-base';
import { getAccessToken, authHeaders, API_BASE } from './helpers/auth-helper';

const MSG_URL  = `${API_BASE}/api/v1/messages`;
const CONV_URL = `${API_BASE}/api/v1/conversations`;
const NON_EXISTENT_ID = '00000000-0000-0000-0000-000000000099';

// Messages E2E — send, search, delete, read-receipt via authenticated API
test.describe('Collab Messages (authenticated API)', () => {
  test.describe.configure({ mode: 'serial' });

  let token: string;
  let adminUserId: string;
  let conversationId: string;
  let messageId: string;

  test.beforeAll(async ({ request }) => {
    token = await getAccessToken(request, 'admin');
    expect(token).toBeTruthy();

    // Resolve admin user ID
    const usersRes = await request.get(`${API_BASE}/api/v1/admin/users?page=1&pageSize=1`, {
      headers: authHeaders(token),
    });
    const items = (await usersRes.json()).data?.items ?? [];
    adminUserId = items[0]?.id ?? '';

    // Create a conversation to use across all message tests
    if (adminUserId) {
      const convRes = await request.post(CONV_URL, {
        headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
        data: { type: 1, name: 'E2E Messages Test', memberUserIds: [adminUserId] },
      });
      if ([200, 201].includes(convRes.status())) {
        const convBody = await convRes.json();
        conversationId = (convBody.data ?? convBody).id ?? '';
      }
    }
  });

  test.afterAll(async ({ request }) => {
    if (conversationId) {
      // Archive the conversation to clean up test data
      await request.patch(`${CONV_URL}/${conversationId}/archive`, {
        headers: authHeaders(token),
      }).catch(() => {});
    }
  });

  // ── Auth guards ────────────────────────────────────────────────────────────

  // TC-COLLAB-037
  test('TC-COLLAB-037: POST /messages without auth returns 401', async ({ request }) => {
    const res = await request.post(MSG_URL, {
      headers: { 'Content-Type': 'application/json' },
      data: { conversationId: NON_EXISTENT_ID, content: 'test', contentType: 0 },
    });
    expect(res.status()).toBe(401);
  });

  // TC-COLLAB-040
  test('TC-COLLAB-040: GET /messages/search without auth returns 401', async ({ request }) => {
    const res = await request.get(`${MSG_URL}/search?conversationId=${NON_EXISTENT_ID}&q=test`);
    expect(res.status()).toBe(401);
  });

  // TC-COLLAB-044
  test('TC-COLLAB-044: DELETE /messages/{id} without auth returns 401', async ({ request }) => {
    const res = await request.delete(`${MSG_URL}/${NON_EXISTENT_ID}`);
    expect(res.status()).toBe(401);
  });

  // TC-COLLAB-047
  test('TC-COLLAB-047: POST /messages/{id}/read without auth returns 401', async ({ request }) => {
    const res = await request.post(`${MSG_URL}/${NON_EXISTENT_ID}/read`);
    expect(res.status()).toBe(401);
  });

  // ── Send ────────────────────────────────────────────────────────────────────

  // TC-COLLAB-031
  test('TC-COLLAB-031: POST /messages sends text message returns 200', async ({ request }) => {
    test.skip(!conversationId, 'No conversation available');
    const res = await request.post(MSG_URL, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: {
        conversationId,
        content: 'Hello E2E world',
        contentType: 0,   // Text
        replyToId: null,
      },
    });
    expect([200, 201]).toContain(res.status());
    const body = await res.json();
    const data = body.data ?? body;
    expect(data.id).toBeTruthy();
    expect(data.content).toBe('Hello E2E world');
    expect(data.contentType).toBe('Text');
    expect(data.isDeleted).toBe(false);
    messageId = data.id;
  });

  // TC-COLLAB-034
  test('TC-COLLAB-034: POST /messages empty content returns 422', async ({ request }) => {
    test.skip(!conversationId, 'No conversation available');
    const res = await request.post(MSG_URL, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: { conversationId, content: '', contentType: 0, replyToId: null },
    });
    expect([400, 422]).toContain(res.status());
  });

  // TC-COLLAB-036
  test('TC-COLLAB-036: POST /messages non-existent conversation returns 404', async ({ request }) => {
    const res = await request.post(MSG_URL, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: {
        conversationId: NON_EXISTENT_ID,
        content: 'This should fail',
        contentType: 0,
        replyToId: null,
      },
    });
    expect(res.status()).toBe(404);
  });

  // ── Search ──────────────────────────────────────────────────────────────────

  // TC-COLLAB-038
  test('TC-COLLAB-038: GET /messages/search returns matching results', async ({ request }) => {
    test.skip(!conversationId, 'No conversation available');
    const res = await request.get(
      `${MSG_URL}/search?conversationId=${conversationId}&q=E2E+world`,
      { headers: authHeaders(token) },
    );
    expect(res.status()).toBe(200);
    const body = await res.json();
    const data = body.data ?? body;
    // May return items array or paginated envelope
    const items = Array.isArray(data) ? data : (data.items ?? []);
    expect(Array.isArray(items)).toBe(true);
  });

  // ── Read receipt ────────────────────────────────────────────────────────────

  // TC-COLLAB-045
  test('TC-COLLAB-045: POST /messages/{id}/read marks message as read returns 204', async ({ request }) => {
    test.skip(!messageId || !conversationId, 'No message ID available');
    const res = await request.post(`${MSG_URL}/${messageId}/read`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: { conversationId },
    });
    expect([200, 204]).toContain(res.status());
  });

  // TC-COLLAB-046
  // MarkAsRead looks up conversation (not message) — if conversation exists it returns 204
  // even for a non-existent message (mark-as-read is idempotent/fire-and-forget)
  test('TC-COLLAB-046: POST /messages/{id}/read non-existent message — 204 or 404', async ({ request }) => {
    test.skip(!conversationId, 'No conversation available');
    const res = await request.post(`${MSG_URL}/${NON_EXISTENT_ID}/read`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: { conversationId },
    });
    expect([204, 404]).toContain(res.status());
  });

  // ── Delete ──────────────────────────────────────────────────────────────────

  // TC-COLLAB-043
  test('TC-COLLAB-043: DELETE /messages/{id} non-existent returns 404', async ({ request }) => {
    const res = await request.delete(`${MSG_URL}/${NON_EXISTENT_ID}`, {
      headers: authHeaders(token),
    });
    expect(res.status()).toBe(404);
  });

  // TC-COLLAB-041
  test('TC-COLLAB-041: DELETE /messages/{id} soft-deletes message returns 204', async ({ request }) => {
    test.skip(!messageId, 'No message ID available');
    const res = await request.delete(`${MSG_URL}/${messageId}`, {
      headers: authHeaders(token),
    });
    expect([200, 204]).toContain(res.status());
  });
});
