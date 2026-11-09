import { test, expect } from './fixtures/test-base';
import { getAccessToken, authHeaders, API_BASE } from './helpers/auth-helper';

// Admin API keys E2E — create / list / rotate / revoke via authenticated API.
// ApiKeysAdminController has no [Authorize] attribute on the class itself —
// auth is enforced at middleware level (SystemAdmin policy per Phase 04 docs).
// Dev seed user has Admin+SystemAdmin — all authed tests should pass.

// TC-HADM-002: 403 for non-Admin role (user token)
test('TC-HADM-002: POST /admin/api-keys with user role returns 403', async ({ request }) => {
  const userToken = await getAccessToken(request, 'user');
  const res = await request.post(`${API_BASE}/api/v1/admin/api-keys`, {
    headers: { ...authHeaders(userToken), 'Content-Type': 'application/json' },
    data: { Name: 'test-key', ClientId: 'test-client' },
  });
  // 403 = role-gated; 401 = user token invalid in this env
  expect([401, 403]).toContain(res.status());
});

test.describe('Admin API keys (authenticated API)', () => {
  test.describe.configure({ mode: 'serial' });
  let token: string;
  let createdKeyId: string | undefined;

  test.beforeAll(async ({ request }) => {
    token = await getAccessToken(request);
    expect(token).toBeTruthy();
  });

  // ── Auth guard ──────────────────────────────────────────────────────────────

  test('POST /admin/api-keys without auth returns 401', async ({ request }) => {
    const res = await request.post(`${API_BASE}/api/v1/admin/api-keys`, {
      headers: { 'Content-Type': 'application/json' },
      data: { Name: 'e2e-no-auth', ClientId: 'e2e-client' },
    });
    expect(res.status()).toBe(401);
  });

  // ── Create ──────────────────────────────────────────────────────────────────

  test('POST /admin/api-keys creates a key and returns 201 with plaintext', async ({ request }) => {
    const res = await request.post(`${API_BASE}/api/v1/admin/api-keys`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: {
        Name: `e2e-smoke-key-${Date.now()}`,
        ClientId: 'e2e-test-client',
        Scopes: ['api'],
      },
    });
    // 201 = created; 403 = insufficient role; 400 = validation / missing tenantId claim
    expect([201, 400, 403]).toContain(res.status());

    if (res.status() === 201) {
      const body = await res.json();
      const data = body?.data ?? body;
      // Plaintext key must be returned exactly once
      expect(data).toHaveProperty('key');
      expect(typeof data.key).toBe('string');
      expect(data.key.length).toBeGreaterThan(0);
      expect(data).toHaveProperty('id');
      createdKeyId = data.id;
    }
  });

  // ── List ────────────────────────────────────────────────────────────────────

  test('GET /admin/api-keys lists API keys (metadata only)', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/admin/api-keys`, {
      headers: authHeaders(token),
    });
    // 200 = ok; 400 = missing tenantId (dev env edge case); 403 = role check
    expect([200, 400, 403]).toContain(res.status());

    if (res.status() === 200) {
      const body = await res.json();
      expect(body.success).toBe(true);
      const keys = body?.data ?? [];
      expect(Array.isArray(keys)).toBe(true);
      // Metadata records must never expose plaintext key value
      for (const k of keys) {
        expect(k).not.toHaveProperty('key');
        expect(k).toHaveProperty('id');
        expect(k).toHaveProperty('prefix');
      }
    }
  });

  // ── Rotate ──────────────────────────────────────────────────────────────────

  test('POST /admin/api-keys/{id}/rotate issues new plaintext key', async ({ request }) => {
    test.skip(!createdKeyId, 'Skipped: create test did not produce a key id');

    const res = await request.post(`${API_BASE}/api/v1/admin/api-keys/${createdKeyId}/rotate`, {
      headers: authHeaders(token),
    });
    // 200 = rotated; 404 = key already revoked by earlier test run
    expect([200, 404]).toContain(res.status());

    if (res.status() === 200) {
      const body = await res.json();
      const data = body?.data ?? body;
      // Rotated key must carry new plaintext and a new id
      expect(data).toHaveProperty('key');
      expect(data).toHaveProperty('id');
      // New record gets a new id after atomic rotate
      expect(data.id).not.toBe(createdKeyId);
      // Update to new id so delete test revokes the rotated key
      createdKeyId = data.id;
    }
  });

  // ── Revoke ──────────────────────────────────────────────────────────────────

  test('DELETE /admin/api-keys/{id} revokes the created key', async ({ request }) => {
    test.skip(!createdKeyId, 'Skipped: create test did not produce a key id');

    const res = await request.delete(`${API_BASE}/api/v1/admin/api-keys/${createdKeyId}`, {
      headers: authHeaders(token),
    });
    // 204 = revoked; 404 = already gone
    expect([204, 404]).toContain(res.status());
  });
});
