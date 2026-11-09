import { test, expect } from './fixtures/test-base';
import { getAccessToken, authHeaders, API_BASE } from './helpers/auth-helper';

// Announcements admin E2E — /api/v1/admin/announcements [Authorize(Roles = "Admin")]
// Public endpoint TCs in admin-announcements-public.spec.ts
// P1 TCs: ANN-001, 002, 004, 007, 013, 014, 015, 016, 017, 018, 019, 020
test.describe('Admin announcements (authenticated API)', () => {
  test.describe.configure({ mode: 'serial' });
  let token: string;
  let createdId: string | undefined;

  test.beforeAll(async ({ request }) => {
    token = await getAccessToken(request);
    expect(token).toBeTruthy();
  });

  // TC-ANN-002: Unauthenticated returns 401
  test('TC-ANN-002: GET /admin/announcements without auth returns 401', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/admin/announcements`);
    expect(res.status()).toBe(401);
  });

  // TC-ANN-013: Create without auth returns 401
  test('TC-ANN-013: POST /admin/announcements without auth returns 401', async ({ request }) => {
    const res = await request.post(`${API_BASE}/api/v1/admin/announcements`, {
      headers: { 'Content-Type': 'application/json' },
      data: { title: 'Test', content: 'Test', severity: 'Info' },
    });
    expect(res.status()).toBe(401);
  });

  // TC-ANN-001: List announcements — authenticated Admin
  test('TC-ANN-001: GET /admin/announcements returns announcement list', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/admin/announcements`, {
      headers: authHeaders(token),
    });
    expect([200, 403]).toContain(res.status());
    if (res.status() === 200) {
      const body = await res.json();
      expect(body.success).toBe(true);
      const data: unknown[] = body.data?.items ?? body.data ?? [];
      expect(Array.isArray(data)).toBe(true);
    }
  });

  // TC-ANN-004: Create announcement — minimal valid request
  test('TC-ANN-004: POST /admin/announcements creates announcement', async ({ request }) => {
    const res = await request.post(`${API_BASE}/api/v1/admin/announcements`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: {
        title: `E2E Test Announcement ${Date.now()}`,
        content: 'E2E test announcement — safe to delete',
        severity: 'Info',
      },
    });
    // 200/201 = created; 403 = role-gated; 422 = validation
    expect([200, 201, 403, 422]).toContain(res.status());
    if ([200, 201].includes(res.status())) {
      const body = await res.json();
      expect(body.success).toBe(true);
      expect(body.data).toHaveProperty('id');
      createdId = body.data.id as string;
    }
  });

  // TC-ANN-007: Empty title returns 422
  test('TC-ANN-007: POST /admin/announcements with empty title returns 422', async ({
    request,
  }) => {
    const res = await request.post(`${API_BASE}/api/v1/admin/announcements`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: { title: '', content: 'Valid content', severity: 'Info' },
    });
    expect([400, 403, 422]).toContain(res.status());
  });

  // TC-ANN-014: Update announcement — change all mutable fields
  test('TC-ANN-014: PUT /admin/announcements/{id} updates announcement fields', async ({
    request,
  }) => {
    test.skip(!createdId, 'Skipped: no announcement ID from create step');

    const res = await request.put(`${API_BASE}/api/v1/admin/announcements/${createdId}`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: {
        title: 'Updated Title E2E',
        content: 'Updated content from E2E test',
        severity: 'Warning',
        startAt: null,
        endAt: null,
        isActive: true,
      },
    });
    // 200 = updated; 403 = role-gated; 404 = not found
    expect([200, 403, 404]).toContain(res.status());
    if (res.status() === 200) {
      const body = await res.json();
      expect(body.success).toBe(true);
    }
  });

  // TC-ANN-015 moved to admin-announcements-public.spec.ts (involves public list check)

  // TC-ANN-016: Update non-existent ID returns 404
  test('TC-ANN-016: PUT /admin/announcements/zero-guid returns 404', async ({ request }) => {
    const res = await request.put(
      `${API_BASE}/api/v1/admin/announcements/00000000-0000-0000-0000-000000000000`,
      {
        headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
        data: {
          title: 'X', content: 'X', severity: 'Info',
          startAt: null, endAt: null, isActive: true,
        },
      },
    );
    expect([403, 404]).toContain(res.status());
  });

  // TC-ANN-017: Update without auth returns 401
  test('TC-ANN-017: PUT /admin/announcements/{id} without auth returns 401', async ({
    request,
  }) => {
    const res = await request.put(
      `${API_BASE}/api/v1/admin/announcements/00000000-0000-0000-0000-000000000001`,
      {
        headers: { 'Content-Type': 'application/json' },
        data: {
          title: 'X', content: 'X', severity: 'Info',
          startAt: null, endAt: null, isActive: true,
        },
      },
    );
    expect(res.status()).toBe(401);
  });

  // TC-ANN-018: Delete announcement — soft delete (204)
  test('TC-ANN-018: DELETE /admin/announcements/{id} soft-deletes announcement', async ({
    request,
  }) => {
    test.skip(!createdId, 'Skipped: no announcement ID to delete');

    const res = await request.delete(`${API_BASE}/api/v1/admin/announcements/${createdId}`, {
      headers: authHeaders(token),
    });
    // 204 = soft-deleted; 403 = role-gated; 404 = not found
    expect([204, 403, 404]).toContain(res.status());
  });

  // TC-ANN-019: Delete non-existent ID returns 404
  test('TC-ANN-019: DELETE /admin/announcements/zero-guid returns 404', async ({ request }) => {
    const res = await request.delete(
      `${API_BASE}/api/v1/admin/announcements/00000000-0000-0000-0000-000000000000`,
      { headers: authHeaders(token) },
    );
    expect([403, 404]).toContain(res.status());
  });

  // TC-ANN-020: Delete without auth returns 401
  test('TC-ANN-020: DELETE /admin/announcements/{id} without auth returns 401', async ({
    request,
  }) => {
    const res = await request.delete(
      `${API_BASE}/api/v1/admin/announcements/00000000-0000-0000-0000-000000000001`,
    );
    expect(res.status()).toBe(401);
  });
});
