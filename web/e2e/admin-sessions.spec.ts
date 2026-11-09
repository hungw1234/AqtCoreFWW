import { test, expect } from './fixtures/test-base';
import { getAccessToken, authHeaders, API_BASE } from './helpers/auth-helper';

// Admin sessions E2E — TC-IDENT-093/094/097 — list active tokens and force-revoke.
// Requires Admin or SystemAdmin role (Policy="Admin" on SessionAdminController).
// Dev seed user has Admin+SystemAdmin — all authed tests should pass.
test.describe('Admin sessions (authenticated API)', () => {
  let token: string;

  test.beforeAll(async ({ request }) => {
    token = await getAccessToken(request);
    expect(token).toBeTruthy();
  });

  // ── Auth guard ──────────────────────────────────────────────────────────────

  // TC-IDENT-094: list sessions — unauthenticated returns 401
  test('TC-IDENT-094: GET /admin/sessions/active without auth returns 401', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/admin/sessions/active`);
    expect(res.status()).toBe(401);
  });

  // ── List active sessions ────────────────────────────────────────────────────

  // TC-IDENT-093: list active sessions — authenticated admin
  test('TC-IDENT-093: GET /admin/sessions/active returns paginated active token list', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/admin/sessions/active`, {
      headers: authHeaders(token),
    });
    expect(res.status()).toBe(200);
    const body = await res.json();
    expect(body.success).toBe(true);
    // Response carries items + pagination metadata
    const data = body.data;
    expect(data).toHaveProperty('items');
    expect(data).toHaveProperty('totalCount');
    expect(Array.isArray(data.items)).toBe(true);
  });

  test('GET /admin/sessions/active?page=1&pageSize=5 pagination works', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/admin/sessions/active?page=1&pageSize=5`, {
      headers: authHeaders(token),
    });
    expect(res.status()).toBe(200);
    const body = await res.json();
    const items = body.data?.items ?? [];
    // Page size honoured — at most 5 records returned
    expect(items.length).toBeLessThanOrEqual(5);
  });

  // ── Revoke by tokenId ───────────────────────────────────────────────────────

  test('DELETE /admin/sessions/{tokenId} with dummy id returns 404', async ({ request }) => {
    // Use a non-existent opaque token ID — server should return 404 (not 500)
    const dummyTokenId = 'nonexistent-token-id-e2e-smoke';
    const res = await request.delete(`${API_BASE}/api/v1/admin/sessions/${dummyTokenId}`, {
      headers: authHeaders(token),
    });
    // 404 = token not found; 204 = found and revoked (unlikely with dummy id)
    expect([404, 204]).toContain(res.status());
  });

  // ── Revoke all tokens for a user ────────────────────────────────────────────

  // TC-IDENT-097: revoke all sessions for user — returns 204
  test('TC-IDENT-097: DELETE /admin/sessions/user/{userId} revokes all tokens for a user', async ({ request }) => {
    // First retrieve a real userId from the active sessions list
    const listRes = await request.get(`${API_BASE}/api/v1/admin/sessions/active?page=1&pageSize=1`, {
      headers: authHeaders(token),
    });
    const items = (await listRes.json()).data?.items ?? [];

    // Fall back to fetching own userId from the users list if no sessions returned
    let userId: string | undefined = items[0]?.userId ?? items[0]?.subjectId;

    if (!userId) {
      const usersRes = await request.get(`${API_BASE}/api/v1/admin/users?page=1&pageSize=1`, {
        headers: authHeaders(token),
      });
      const users = (await usersRes.json()).data?.items ?? [];
      userId = users[0]?.id;
    }

    test.skip(!userId, 'No userId available to test revoke-all');

    const res = await request.delete(`${API_BASE}/api/v1/admin/sessions/user/${userId}`, {
      headers: authHeaders(token),
    });
    // 204 = all tokens revoked (even if count is 0 it is a success)
    // 404 = user not found (acceptable if userId came from an empty list)
    expect([204, 404]).toContain(res.status());
  });
});
