import { test, expect } from './fixtures/test-base';
import { getAccessToken, authHeaders, API_BASE } from './helpers/auth-helper';

const NEGOTIATE_URL = `${API_BASE}/hubs/chat/negotiate?negotiateVersion=1`;

// ChatHub SignalR negotiate E2E — HTTP POST negotiate endpoint auth guards + response shape
test.describe('Collab ChatHub Negotiate (SignalR)', () => {
  let token: string;

  test.beforeAll(async ({ request }) => {
    token = await getAccessToken(request, 'admin');
    expect(token).toBeTruthy();
  });

  // TC-COLLAB-063
  test('TC-COLLAB-063: POST /hubs/chat/negotiate without auth returns 401', async ({ request }) => {
    const res = await request.post(NEGOTIATE_URL);
    expect(res.status()).toBe(401);
  });

  // TC-COLLAB-064
  test('TC-COLLAB-064: POST /hubs/chat/negotiate with invalid token returns 401', async ({ request }) => {
    const res = await request.post(NEGOTIATE_URL, {
      headers: { Authorization: 'Bearer invalid-token-e2e-test' },
    });
    expect(res.status()).toBe(401);
  });

  // TC-COLLAB-061
  test('TC-COLLAB-061: POST /hubs/chat/negotiate authenticated returns 200 with connectionId', async ({ request }) => {
    const res = await request.post(NEGOTIATE_URL, {
      headers: authHeaders(token),
    });
    expect(res.status()).toBe(200);
    const body = await res.json();
    // SignalR negotiate response must include connectionId or connectionToken
    expect(body.connectionId ?? body.connectionToken).toBeTruthy();
    expect(body.availableTransports).toBeDefined();
    expect(Array.isArray(body.availableTransports)).toBe(true);
  });
});
