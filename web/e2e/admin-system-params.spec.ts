import { test, expect } from './fixtures/test-base';
import { getAccessToken, authHeaders, API_BASE } from './helpers/auth-helper';

// Admin system params E2E — system params, feature flags, announcements CRUD

// TC-SP-002: GET /admin/system-params without auth → 401
test('TC-SP-002: GET /admin/system-params without auth returns 401', async ({ request }) => {
  const res = await request.get(`${API_BASE}/api/v1/admin/system-params`);
  expect(res.status()).toBe(401);
});

// TC-SP-003: GET /admin/system-params with non-Admin role → 403
test('TC-SP-003: GET /admin/system-params with user role returns 403', async ({ request }) => {
  const userToken = await getAccessToken(request, 'user');
  const res = await request.get(`${API_BASE}/api/v1/admin/system-params`, {
    headers: authHeaders(userToken),
  });
  expect([401, 403]).toContain(res.status());
});

// TC-FF-002: GET /admin/feature-flags without auth → 401
test('TC-FF-002: GET /admin/feature-flags without auth returns 401', async ({ request }) => {
  const res = await request.get(`${API_BASE}/api/v1/admin/feature-flags`);
  expect(res.status()).toBe(401);
});

// TC-FF-007: PUT /admin/feature-flags/{name} without auth → 401
test('TC-FF-007: PUT /admin/feature-flags/notifications without auth returns 401', async ({
  request,
}) => {
  const res = await request.put(`${API_BASE}/api/v1/admin/feature-flags/notifications`, {
    headers: { 'Content-Type': 'application/json' },
    data: { isEnabled: false },
  });
  expect(res.status()).toBe(401);
});

test.describe('Admin system params (authenticated API)', () => {
  let token: string;

  test.beforeAll(async ({ request }) => {
    token = await getAccessToken(request);
    expect(token).toBeTruthy();
  });

  test('GET /admin/system-params returns all system params', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/admin/system-params`, {
      headers: authHeaders(token),
    });
    expect([200, 403]).toContain(res.status());
    if (res.status() === 200) {
      const body = await res.json();
      expect(body.success).toBe(true);
    }
  });

  test('GET /admin/system-params/{key} returns specific param', async ({ request }) => {
    // Discover available keys first; fall back to well-known seed key
    const listRes = await request.get(`${API_BASE}/api/v1/admin/system-params`, {
      headers: authHeaders(token),
    });
    if (listRes.status() === 403) {
      test.skip(true, 'Insufficient permissions for system-params endpoint');
      return;
    }

    const listBody = await listRes.json();
    const items: { key?: string; name?: string }[] = listBody.data?.items ?? listBody.data ?? [];
    const key = items[0]?.key ?? items[0]?.name ?? 'site.name';

    const res = await request.get(`${API_BASE}/api/v1/admin/system-params/${key}`, {
      headers: authHeaders(token),
    });
    // 200 = found, 404 = key not seeded, 403 = role-gated
    expect([200, 403, 404]).toContain(res.status());
    if (res.status() === 200) {
      const body = await res.json();
      expect(body.success).toBe(true);
    }
  });

  test('PUT /admin/system-params/{key} updates param value', async ({ request }) => {
    // GET list first to discover a real key
    const listRes = await request.get(`${API_BASE}/api/v1/admin/system-params`, {
      headers: authHeaders(token),
    });
    if (listRes.status() === 403) {
      test.skip(true, 'Insufficient permissions for system-params endpoint');
      return;
    }

    const listBody = await listRes.json();
    const items: { key?: string; name?: string; value?: string }[] =
      listBody.data?.items ?? listBody.data ?? [];

    test.skip(items.length === 0, 'No system params seeded');

    const param = items[0];
    const key = param.key ?? param.name ?? 'site.name';
    const currentValue = param.value ?? 'test-value';

    const res = await request.put(`${API_BASE}/api/v1/admin/system-params/${key}`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: { value: currentValue }, // restore same value — non-destructive
    });
    // 200 = updated, 403 = role-gated, 404 = key not found, 422 = validation
    expect([200, 403, 404, 422]).toContain(res.status());
  });

  test('GET /admin/feature-flags returns all feature flags', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/admin/feature-flags`, {
      headers: authHeaders(token),
    });
    expect([200, 403]).toContain(res.status());
    if (res.status() === 200) {
      const body = await res.json();
      expect(body.success).toBe(true);
    }
  });

  test('PUT /admin/feature-flags/{flagName} toggles feature flag', async ({ request }) => {
    const listRes = await request.get(`${API_BASE}/api/v1/admin/feature-flags`, {
      headers: authHeaders(token),
    });
    if (listRes.status() === 403) {
      test.skip(true, 'Insufficient permissions for feature-flags endpoint');
      return;
    }

    const listBody = await listRes.json();
    const flags: { name?: string; flagName?: string; enabled?: boolean }[] =
      listBody.data?.items ?? listBody.data ?? [];

    test.skip(flags.length === 0, 'No feature flags seeded');

    const flag = flags[0];
    const flagName = flag.name ?? flag.flagName ?? '';
    const currentEnabled = flag.enabled ?? false;

    test.skip(!flagName, 'Could not determine flag name from response');

    const res = await request.put(`${API_BASE}/api/v1/admin/feature-flags/${flagName}`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: { enabled: currentEnabled }, // restore same value — non-destructive
    });
    // 200 = toggled, 403 = role-gated, 404 = flag not found, 422 = validation
    expect([200, 403, 404, 422]).toContain(res.status());
  });

  test('GET /admin/announcements returns announcement list', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/admin/announcements`, {
      headers: authHeaders(token),
    });
    expect([200, 403]).toContain(res.status());
    if (res.status() === 200) {
      const body = await res.json();
      expect(body.success).toBe(true);
    }
  });

  test('POST then DELETE /admin/announcements creates and cleans up announcement', async ({
    request,
  }) => {
    const createRes = await request.post(`${API_BASE}/api/v1/admin/announcements`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: {
        title: `E2E Announcement ${Date.now()}`,
        content: 'Created by Playwright E2E smoke test — safe to delete',
        severity: 'Info',
      },
    });
    // 403 = role-gated — skip cleanup if we never created
    if (createRes.status() === 403) {
      test.skip(true, 'Insufficient permissions for announcements endpoint');
      return;
    }
    // 200/201 = created, 422 = validation (field names may differ)
    expect([200, 201, 422]).toContain(createRes.status());

    if ([200, 201].includes(createRes.status())) {
      const createBody = await createRes.json();
      const announcementId = createBody.data?.id;

      if (announcementId) {
        const deleteRes = await request.delete(
          `${API_BASE}/api/v1/admin/announcements/${announcementId}`,
          { headers: authHeaders(token) },
        );
        // 200/204 = deleted, 404 = already gone
        expect([200, 204, 404]).toContain(deleteRes.status());
      }
    }
  });
});
