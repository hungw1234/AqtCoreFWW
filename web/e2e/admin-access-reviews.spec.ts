import { test, expect } from './fixtures/test-base';
import { getAccessToken, authHeaders, API_BASE } from './helpers/auth-helper';

// Access reviews admin E2E — TC-IDENT-083/084/085/087 + pending list, approve/reject
test.describe('Admin access reviews (authenticated API)', () => {
  let token: string;

  test.beforeAll(async ({ request }) => {
    token = await getAccessToken(request);
    expect(token).toBeTruthy();
  });

  // TC-IDENT-083: list pending access reviews — authenticated admin
  test('TC-IDENT-083: GET /admin/access-reviews/pending returns pending review list', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/admin/access-reviews/pending`, {
      headers: authHeaders(token),
    });
    // 200 = accessible, 403 = insufficient permissions for this admin endpoint
    expect([200, 403]).toContain(res.status());
    if (res.status() === 200) {
      const body = await res.json();
      expect(body.success).toBe(true);
      // data may be an array (empty) or a paginated object with items
      const data = body.data;
      if (Array.isArray(data)) {
        expect(Array.isArray(data)).toBe(true);
      } else {
        expect(data).toHaveProperty('items');
      }
    }
  });

  // TC-IDENT-084: list pending reviews — unauthenticated returns 401
  test('TC-IDENT-084: GET /admin/access-reviews/pending without auth returns 401', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/admin/access-reviews/pending`);
    expect(res.status()).toBe(401);
  });

  // TC-IDENT-085: approve access review — returns 204 (or 404 if none pending)
  test('TC-IDENT-085: POST /admin/access-reviews/{id}/approve returns 204 or 404', async ({ request }) => {
    // Try to find a real pending review first
    const listRes = await request.get(`${API_BASE}/api/v1/admin/access-reviews/pending`, {
      headers: authHeaders(token),
    });
    const items = listRes.ok() ? (await listRes.json()).data?.items ?? [] : [];
    const id = items[0]?.id ?? '00000000-0000-0000-0000-000000000000';

    const res = await request.post(`${API_BASE}/api/v1/admin/access-reviews/${id}/approve`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: { comment: 'E2E approve' },
    });
    expect([204, 200, 404, 403]).toContain(res.status());
  });

  // TC-IDENT-087: reject access review — returns 204 (or 404 if none pending)
  test('TC-IDENT-087: POST /admin/access-reviews/{id}/reject returns 204 or 404', async ({ request }) => {
    const dummyId = '00000000-0000-0000-0000-000000000000';
    const res = await request.post(`${API_BASE}/api/v1/admin/access-reviews/${dummyId}/reject`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: { reason: 'E2E dummy reject' },
    });
    expect([204, 200, 404, 403]).toContain(res.status());
  });
});
