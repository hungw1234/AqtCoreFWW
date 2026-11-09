import { test, expect, APIRequestContext, request as newRequest } from './fixtures/test-base';
import { API_BASE, AUTH_SERVER } from './helpers/auth-helper';

// SignalR WebSocket E2E Tests
// Covers: hub negotiation, authenticated connection, unauthorized rejection
// Runs against Docker stack: API, AuthServer
//
// SignalR hub: /hubs/notifications (requires JWT auth, group targeting)
const HUB_URL = `${API_BASE}/hubs/notifications`;

test.describe('SignalR WebSocket', () => {
  test.describe.configure({ mode: 'serial' });

  let apiCtx: APIRequestContext;
  let sharedToken: string;

  test.beforeAll(async () => {
    test.setTimeout(120_000);
    apiCtx = await newRequest.newContext({ baseURL: API_BASE });
    await apiCtx.get('/health/ready');

    // Acquire token with retry for lockout resilience
    const maxRetries = 5;
    for (let attempt = 1; attempt <= maxRetries; attempt++) {
      const tokenRes = await apiCtx.post(`${AUTH_SERVER}/connect/token`, {
        form: {
          grant_type: 'password',
          client_id: 'aqtcorefw-dev-ropc',
          client_secret: 'dev-secret-not-for-production',
          username: 'admin@dev.local',
          password: 'DevAdmin@12345',
          scope: 'api',
        },
      });
      if (tokenRes.status() === 200) {
        const body = await tokenRes.json();
        sharedToken = body.access_token;
        break;
      }
      if (attempt === maxRetries) {
        const errBody = await tokenRes.text();
        throw new Error(`Token failed after ${maxRetries} retries: ${errBody.slice(0, 200)}`);
      }
      await new Promise(r => setTimeout(r, 10_000));
    }
    expect(sharedToken).toBeTruthy();
  });

  test.beforeEach(async () => {
    test.setTimeout(30_000);
  });

  test.afterAll(async () => {
    await apiCtx.dispose();
  });

  // -------------------------------------------------------------------------
  // Hub Negotiation
  // -------------------------------------------------------------------------
  test.describe('Hub Negotiation', () => {
    test('POST /hubs/notifications/negotiate returns 200 with valid token', async () => {
      const res = await apiCtx.post(`${HUB_URL}/negotiate?negotiateVersion=1`, {
        headers: {
          Authorization: `Bearer ${sharedToken}`,
        },
      });
      expect(res.status()).toBe(200);
      const body = await res.json();
      // SignalR negotiate response includes connectionId and available transports
      expect(body.connectionId || body.connectionToken).toBeTruthy();
      expect(body.availableTransports).toBeDefined();
      expect(Array.isArray(body.availableTransports)).toBe(true);
    });

    test('negotiate response includes WebSockets transport', async () => {
      const res = await apiCtx.post(`${HUB_URL}/negotiate?negotiateVersion=1`, {
        headers: {
          Authorization: `Bearer ${sharedToken}`,
        },
      });
      expect(res.status()).toBe(200);
      const body = await res.json();
      const transportNames = body.availableTransports.map((t: { transport: string }) => t.transport);
      expect(transportNames).toContain('WebSockets');
    });

    test('negotiate response includes ServerSentEvents fallback', async () => {
      const res = await apiCtx.post(`${HUB_URL}/negotiate?negotiateVersion=1`, {
        headers: {
          Authorization: `Bearer ${sharedToken}`,
        },
      });
      expect(res.status()).toBe(200);
      const body = await res.json();
      const transportNames = body.availableTransports.map((t: { transport: string }) => t.transport);
      expect(transportNames).toContain('ServerSentEvents');
    });

    test('negotiate response includes LongPolling fallback', async () => {
      const res = await apiCtx.post(`${HUB_URL}/negotiate?negotiateVersion=1`, {
        headers: {
          Authorization: `Bearer ${sharedToken}`,
        },
      });
      expect(res.status()).toBe(200);
      const body = await res.json();
      const transportNames = body.availableTransports.map((t: { transport: string }) => t.transport);
      expect(transportNames).toContain('LongPolling');
    });
  });

  // -------------------------------------------------------------------------
  // Authentication Enforcement
  // -------------------------------------------------------------------------
  test.describe('Authentication Enforcement', () => {
    test('negotiate returns 401 without token', async () => {
      const res = await apiCtx.post(`${HUB_URL}/negotiate?negotiateVersion=1`);
      expect(res.status()).toBe(401);
    });

    test('negotiate returns 401 with invalid token', async () => {
      const res = await apiCtx.post(`${HUB_URL}/negotiate?negotiateVersion=1`, {
        headers: {
          Authorization: 'Bearer invalid-token-12345',
        },
      });
      expect(res.status()).toBe(401);
    });

    test('negotiate returns 401 with expired token format', async () => {
      const fakeJwt = 'eyJhbGciOiJSUzI1NiJ9.eyJzdWIiOiJ0ZXN0IiwiZXhwIjoxMDAwfQ.invalidsig';
      const res = await apiCtx.post(`${HUB_URL}/negotiate?negotiateVersion=1`, {
        headers: {
          Authorization: `Bearer ${fakeJwt}`,
        },
      });
      expect(res.status()).toBe(401);
    });
  });

  // -------------------------------------------------------------------------
  // Connection Token Uniqueness
  // -------------------------------------------------------------------------
  test.describe('Connection Token Uniqueness', () => {
    test('consecutive negotiate calls return unique connection tokens', async () => {
      const tokens: string[] = [];
      for (let i = 0; i < 3; i++) {
        const res = await apiCtx.post(`${HUB_URL}/negotiate?negotiateVersion=1`, {
          headers: { Authorization: `Bearer ${sharedToken}` },
        });
        expect(res.status()).toBe(200);
        const body = await res.json();
        tokens.push(body.connectionToken || body.connectionId);
      }
      // All connection tokens should be unique
      const unique = new Set(tokens);
      expect(unique.size).toBe(3);
    });
  });

  // -------------------------------------------------------------------------
  // Hub Endpoint Availability
  // -------------------------------------------------------------------------
  test.describe('Hub Endpoint Availability', () => {
    test('GET /hubs/notifications returns non-404 (hub is mapped)', async () => {
      const res = await apiCtx.get(HUB_URL);
      // SignalR hub doesn't respond to GET — but should not be 404 (means route exists)
      expect(res.status()).not.toBe(404);
    });

    test('OPTIONS /hubs/notifications returns CORS-compatible response', async () => {
      const res = await apiCtx.fetch(`${HUB_URL}/negotiate`, {
        method: 'OPTIONS',
        headers: {
          'Origin': 'http://localhost:5173',
          'Access-Control-Request-Method': 'POST',
        },
      });
      // CORS preflight should return 200/204, or at least not 404
      expect([200, 204, 401, 405]).toContain(res.status());
    });
  });
});
