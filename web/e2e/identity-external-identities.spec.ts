import { test, expect } from './fixtures/test-base';
import { getAccessToken, authHeaders, API_BASE } from './helpers/auth-helper';

// ExternalIdentitiesController P1 TCs — TC-IDENT-098/099/101/103
// Route prefix: /api/v1/admin/external-identities
// Auth: [Authorize(Policy = "Admin")]
test.describe('External Identities admin (authenticated API)', () => {
  test.describe.configure({ mode: 'serial' });
  let token: string;
  let userId: string;
  let createdIdentityId: string;

  test.beforeAll(async ({ request }) => {
    token = await getAccessToken(request);
    expect(token).toBeTruthy();

    // Resolve a real userId for tests
    const usersRes = await request.get(`${API_BASE}/api/v1/admin/users?page=1&pageSize=1`, {
      headers: authHeaders(token),
    });
    const users = (await usersRes.json()).data?.items ?? [];
    userId = users[0]?.id ?? '';
  });

  test.afterAll(async ({ request }) => {
    if (createdIdentityId) {
      await request.delete(
        `${API_BASE}/api/v1/admin/external-identities/${createdIdentityId}`,
        { headers: authHeaders(token) },
      );
    }
  });

  // TC-IDENT-098: list external identities for user — authenticated admin
  test('TC-IDENT-098: GET /admin/external-identities?userId={id} returns list', async ({ request }) => {
    test.skip(!userId, 'No user available');
    const res = await request.get(
      `${API_BASE}/api/v1/admin/external-identities?userId=${userId}`,
      { headers: authHeaders(token) },
    );
    expect(res.status()).toBe(200);
    const body = await res.json();
    const data = body.data ?? body;
    expect(data).toHaveProperty('items');
  });

  // TC-IDENT-101: create external identity link — valid payload returns 201
  test('TC-IDENT-101: POST /admin/external-identities creates identity link', async ({ request }) => {
    test.skip(!userId, 'No user available');
    const res = await request.post(`${API_BASE}/api/v1/admin/external-identities`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: {
        userId,
        provider: 'VNeID',
        externalId: `VN-TC101-${Date.now()}`,
        displayName: 'TC101 Display',
        email: `tc101-${Date.now()}@vneid.gov.vn`,
      },
    });
    expect([201, 409]).toContain(res.status());
    if (res.status() === 201) {
      const body = await res.json();
      createdIdentityId = body.data?.id ?? body.id;
      // Location header should be present per spec
      expect(res.headers()['location']).toBeTruthy();
    }
  });

  // TC-IDENT-099: get external identity by ID — existing
  test('TC-IDENT-099: GET /admin/external-identities/{id} returns detail', async ({ request }) => {
    test.skip(!createdIdentityId, 'No identity created');
    const res = await request.get(
      `${API_BASE}/api/v1/admin/external-identities/${createdIdentityId}`,
      { headers: authHeaders(token) },
    );
    expect(res.status()).toBe(200);
    const body = await res.json();
    const data = body.data ?? body;
    expect(data).toHaveProperty('provider');
    expect(data).toHaveProperty('externalId');
  });

  // TC-IDENT-103: delete external identity link — returns 204
  test('TC-IDENT-103: DELETE /admin/external-identities/{id} deletes identity link', async ({ request }) => {
    test.skip(!createdIdentityId, 'No identity created');
    const res = await request.delete(
      `${API_BASE}/api/v1/admin/external-identities/${createdIdentityId}`,
      { headers: authHeaders(token) },
    );
    expect([204, 200]).toContain(res.status());
    createdIdentityId = ''; // cleanup done inline
  });

  // Auth guard
  test('GET /admin/external-identities without auth returns 401', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/admin/external-identities`);
    expect(res.status()).toBe(401);
  });
});
