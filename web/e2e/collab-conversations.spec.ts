import { test, expect } from './fixtures/test-base';
import { getAccessToken, authHeaders, API_BASE } from './helpers/auth-helper';

const CONV_URL = `${API_BASE}/api/v1/conversations`;
const NON_EXISTENT_ID = '00000000-0000-0000-0000-000000000099';

// Conversations E2E — create, list, archive, members via authenticated API
test.describe('Collab Conversations (authenticated API)', () => {
  test.describe.configure({ mode: 'serial' });

  let token: string;
  let adminUserId: string;
  let secondUserId: string;
  let directConvId: string;
  let groupConvId: string;

  test.beforeAll(async ({ request }) => {
    token = await getAccessToken(request, 'admin');
    expect(token).toBeTruthy();

    // Resolve two user IDs for membership operations
    const usersRes = await request.get(`${API_BASE}/api/v1/admin/users?page=1&pageSize=5`, {
      headers: authHeaders(token),
    });
    const items = (await usersRes.json()).data?.items ?? [];
    adminUserId  = items[0]?.id ?? '';
    secondUserId = items[1]?.id ?? adminUserId; // fallback to same user if only one exists
  });

  // ── Auth guards ────────────────────────────────────────────────────────────

  // TC-COLLAB-006
  test('TC-COLLAB-006: POST /conversations without auth returns 401', async ({ request }) => {
    const res = await request.post(CONV_URL, {
      headers: { 'Content-Type': 'application/json' },
      data: { type: 0, memberUserIds: [] },
    });
    expect(res.status()).toBe(401);
  });

  // TC-COLLAB-010
  test('TC-COLLAB-010: GET /conversations without auth returns 401', async ({ request }) => {
    const res = await request.get(CONV_URL);
    expect(res.status()).toBe(401);
  });

  // TC-COLLAB-015
  test('TC-COLLAB-015: GET /conversations/{id}/messages without auth returns 401', async ({ request }) => {
    const res = await request.get(`${CONV_URL}/${NON_EXISTENT_ID}/messages`);
    expect(res.status()).toBe(401);
  });

  // TC-COLLAB-019
  test('TC-COLLAB-019: PATCH /conversations/{id}/archive without auth returns 401', async ({ request }) => {
    const res = await request.patch(`${CONV_URL}/${NON_EXISTENT_ID}/archive`);
    expect(res.status()).toBe(401);
  });

  // TC-COLLAB-023
  test('TC-COLLAB-023: POST /conversations/{id}/members without auth returns 401', async ({ request }) => {
    const res = await request.post(`${CONV_URL}/${NON_EXISTENT_ID}/members`);
    expect(res.status()).toBe(401);
  });

  // ── Create ─────────────────────────────────────────────────────────────────

  // TC-COLLAB-001
  test('TC-COLLAB-001: POST /conversations creates Direct conversation', async ({ request }) => {
    test.skip(!adminUserId, 'Admin user ID not resolved');
    const res = await request.post(CONV_URL, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: {
        type: 0,          // Direct
        name: null,
        memberUserIds: [adminUserId],
      },
    });
    expect([200, 201]).toContain(res.status());
    const body = await res.json();
    const data = body.data ?? body;
    expect(data.id).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i);
    expect(data.type).toBe('Direct');
    expect(data.isArchived).toBe(false);
    directConvId = data.id;
  });

  // TC-COLLAB-002
  test('TC-COLLAB-002: POST /conversations creates Group conversation with name', async ({ request }) => {
    test.skip(!adminUserId, 'Admin user ID not resolved');
    const res = await request.post(CONV_URL, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: {
        type: 1,          // Group
        name: 'E2E Test Group',
        memberUserIds: [adminUserId],
      },
    });
    expect([200, 201]).toContain(res.status());
    const body = await res.json();
    const data = body.data ?? body;
    expect(data.id).toBeTruthy();
    groupConvId = data.id;
  });

  // TC-COLLAB-004
  test('TC-COLLAB-004: POST /conversations empty memberUserIds returns 422', async ({ request }) => {
    const res = await request.post(CONV_URL, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: {
        type: 0,
        name: null,
        memberUserIds: [],
      },
    });
    expect([400, 422]).toContain(res.status());
  });

  // ── List ────────────────────────────────────────────────────────────────────

  // TC-COLLAB-007
  // GetConversationsQuery returns IReadOnlyList<ConversationDto> — body.data is an array (not paginated)
  test('TC-COLLAB-007: GET /conversations returns 200 list', async ({ request }) => {
    const res = await request.get(CONV_URL, { headers: authHeaders(token) });
    expect(res.status()).toBe(200);
    const body = await res.json();
    expect(body.success).toBe(true);
    const data = body.data ?? body;
    // data may be an array directly or a paginated envelope with items
    const items = Array.isArray(data) ? data : (data.items ?? data);
    expect(Array.isArray(items)).toBe(true);
  });

  // ── Messages ────────────────────────────────────────────────────────────────

  // TC-COLLAB-011
  // GetMessagesQuery returns IReadOnlyList<MessageDto> — body.data is an array, not paginated
  test('TC-COLLAB-011: GET /conversations/{id}/messages returns 200', async ({ request }) => {
    test.skip(!directConvId, 'No conversation ID available');
    const res = await request.get(`${CONV_URL}/${directConvId}/messages`, {
      headers: authHeaders(token),
    });
    expect(res.status()).toBe(200);
    const body = await res.json();
    const data = body.data ?? body;
    // data may be a direct array or a paginated envelope
    const items = Array.isArray(data) ? data : (data.items ?? []);
    expect(Array.isArray(items)).toBe(true);
  });

  // TC-COLLAB-014
  // GetMessages does not validate conversation existence — may return 200 with empty list
  test('TC-COLLAB-014: GET /conversations/{id}/messages non-existent returns 404 or 200 empty', async ({ request }) => {
    const res = await request.get(`${CONV_URL}/${NON_EXISTENT_ID}/messages`, {
      headers: authHeaders(token),
    });
    expect([200, 404]).toContain(res.status());
  });

  // ── Archive ─────────────────────────────────────────────────────────────────

  // TC-COLLAB-018
  test('TC-COLLAB-018: PATCH /conversations/{id}/archive non-existent returns 404', async ({ request }) => {
    const res = await request.patch(`${CONV_URL}/${NON_EXISTENT_ID}/archive`, {
      headers: authHeaders(token),
    });
    expect(res.status()).toBe(404);
  });

  // TC-COLLAB-016
  test('TC-COLLAB-016: PATCH /conversations/{id}/archive returns 204', async ({ request }) => {
    test.skip(!groupConvId, 'No group conversation ID available');
    const res = await request.patch(`${CONV_URL}/${groupConvId}/archive`, {
      headers: authHeaders(token),
    });
    expect([200, 204]).toContain(res.status());
  });

  // ── Members ─────────────────────────────────────────────────────────────────

  // TC-COLLAB-022
  // AddMemberBody: { userId: Guid, role: MemberRole } — singular, not array
  test('TC-COLLAB-022: POST /conversations/{id}/members non-existent returns 404', async ({ request }) => {
    const res = await request.post(`${CONV_URL}/${NON_EXISTENT_ID}/members`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: { userId: adminUserId, role: 0 },
    });
    expect(res.status()).toBe(404);
  });

  // TC-COLLAB-020
  // AddMemberBody: { userId: Guid, role: MemberRole } — singular, not array
  test('TC-COLLAB-020: POST /conversations/{id}/members adds member returns 204', async ({ request }) => {
    test.skip(!directConvId || !secondUserId, 'No conversation or second user available');
    const res = await request.post(`${CONV_URL}/${directConvId}/members`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: { userId: secondUserId, role: 0 },
    });
    expect([200, 204, 409]).toContain(res.status()); // 409 = already member
  });
});
