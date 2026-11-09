import { test, expect } from './fixtures/test-base';
import { getAccessToken, authHeaders, API_BASE } from './helpers/auth-helper';

// Integration Message Logs E2E — read-heavy module with create and status-update.
// beforeAll creates a partner for foreign key; afterAll cleans it up.
// Tests run serially because create/update share messageLogId.
test.describe('Integration Message Logs (authenticated API)', () => {
  test.describe.configure({ mode: 'serial' });

  let token: string;
  let partnerId: string;
  let messageLogId: string;

  const NON_EXISTENT_ID = '00000000-0000-0000-0000-000000000099';

  test.beforeAll(async ({ request }) => {
    token = await getAccessToken(request, 'admin');
    expect(token).toBeTruthy();

    // Create a partner to satisfy the partnerId FK on message logs
    const res = await request.post(`${API_BASE}/api/v1/integration/partners`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: {
        name: 'E2E MsgLog Partner',
        code: `E2E-MLOG-${Date.now()}`,
        contactEmail: 'msglog@example.com',
        authScheme: 'BearerToken',
      },
    });
    const body = await res.json();
    partnerId = (body.data ?? body).id;
    expect(partnerId).toBeTruthy();
  });

  test.afterAll(async ({ request }) => {
    if (partnerId) {
      await request.delete(`${API_BASE}/api/v1/integration/partners/${partnerId}`, {
        headers: authHeaders(token),
      });
    }
  });

  // TC-INTG-037: GET list → 200 paginated
  test('TC-INTG-037: list message logs — 200 paginated envelope', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/integration/message-logs`, {
      headers: authHeaders(token),
    });
    expect(res.status()).toBe(200);
    const body = await res.json();
    expect(body.success).toBe(true);
    expect(body.data).toHaveProperty('items');
    expect(Array.isArray(body.data.items)).toBeTruthy();
  });

  // TC-INTG-040: GET list without auth → 401
  test('TC-INTG-040: list message logs — 401 without auth', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/integration/message-logs`);
    expect(res.status()).toBe(401);
  });

  // TC-INTG-044: POST create → 200 with partnerId/direction/messageType/payload/status=Pending
  test('TC-INTG-044: create message log — 200 with Inbound direction', async ({ request }) => {
    test.skip(!partnerId, 'No partner available');
    const res = await request.post(`${API_BASE}/api/v1/integration/message-logs`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: {
        partnerPublicId: partnerId,
        direction: 0, // 0=Inbound
        messageType: 'E2E-Test',
        payload: '{"test":true}',
      },
    });
    expect(res.status()).toBe(200);
    const body = await res.json();
    expect(body.success).toBe(true);
    const data = body.data ?? body;
    expect(data.id).toBeTruthy();
    expect(data.id).toMatch(/^[0-9a-f-]{36}$/i);
    expect(data.partnerId).toBe(partnerId);
    // direction is serialized as string enum ("Inbound") or numeric (0)
    expect(data.direction === 0 || data.direction === 'Inbound').toBeTruthy();
    expect(data.messageType).toBe('E2E-Test');
    messageLogId = data.id;
  });

  // TC-INTG-041: GET /{id} → 200
  test('TC-INTG-041: get message log by id — 200 with correct fields', async ({ request }) => {
    test.skip(!messageLogId, 'No message log created');
    const res = await request.get(`${API_BASE}/api/v1/integration/message-logs/${messageLogId}`, {
      headers: authHeaders(token),
    });
    expect(res.status()).toBe(200);
    const body = await res.json();
    const data = body.data ?? body;
    expect(data).toHaveProperty('id', messageLogId);
    expect(data).toHaveProperty('partnerId');
    expect(data).toHaveProperty('direction');
    expect(data).toHaveProperty('messageType');
  });

  // TC-INTG-042: GET /{id} non-existent → 404
  test('TC-INTG-042: get message log by id — 404 for non-existent id', async ({ request }) => {
    const res = await request.get(
      `${API_BASE}/api/v1/integration/message-logs/${NON_EXISTENT_ID}`,
      { headers: authHeaders(token) },
    );
    expect(res.status()).toBe(404);
  });

  // TC-INTG-043: GET /{id} without auth → 401
  test('TC-INTG-043: get message log by id — 401 without auth', async ({ request }) => {
    const res = await request.get(
      `${API_BASE}/api/v1/integration/message-logs/${NON_EXISTENT_ID}`,
    );
    expect(res.status()).toBe(401);
  });

  // TC-INTG-046: POST create without auth → 401
  test('TC-INTG-046: create message log — 401 without auth', async ({ request }) => {
    const res = await request.post(`${API_BASE}/api/v1/integration/message-logs`, {
      headers: { 'Content-Type': 'application/json' },
      data: { partnerId, direction: 1, messageType: 'NoAuth', payload: '{}' },
    });
    expect(res.status()).toBe(401);
  });

  // TC-INTG-047: PUT update status → 200
  // Actual endpoint: PUT /{id}/status with body { newStatus: MessageLogStatus }
  // MessageLogStatus: 0=Sent, 1=Delivered, 2=Failed, 3=Acknowledged
  test('TC-INTG-047: update message log status — 200 success', async ({ request }) => {
    test.skip(!messageLogId, 'No message log created');
    const res = await request.put(
      `${API_BASE}/api/v1/integration/message-logs/${messageLogId}/status`,
      {
        headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
        data: {
          newStatus: 1, // 1=Delivered
        },
      },
    );
    expect(res.status()).toBe(200);
  });

  // TC-INTG-048: PUT update non-existent → 404
  test('TC-INTG-048: update message log — 404 for non-existent id', async ({ request }) => {
    const res = await request.put(
      `${API_BASE}/api/v1/integration/message-logs/${NON_EXISTENT_ID}/status`,
      {
        headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
        data: { newStatus: 1 },
      },
    );
    expect(res.status()).toBe(404);
  });
});
