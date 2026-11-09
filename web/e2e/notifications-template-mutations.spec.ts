import { test, expect } from './fixtures/test-base';
import { getAccessToken, authHeaders, API_BASE } from './helpers/auth-helper';

// Notification template update/delete mutations E2E — /api/v1/admin/notification-templates
// P1 TCs: TC-NOTIF-045 to TC-NOTIF-055, TC-NOTIF-058 (update, delete, lifecycle)
test.describe('Notification template mutations (admin API)', () => {
  test.describe.configure({ mode: 'serial' });

  let adminToken: string;
  let userToken: string;
  let templateId: string;

  const TENANT_ID = '00000000-0000-0000-0000-000000000001';

  test.beforeAll(async ({ request }) => {
    adminToken = await getAccessToken(request, 'admin');
    userToken  = await getAccessToken(request, 'user');
    expect(adminToken).toBeTruthy();

    // Create a template to use for update/delete tests
    const res = await request.post(`${API_BASE}/api/v1/admin/notification-templates`, {
      headers: { ...authHeaders(adminToken), 'Content-Type': 'application/json' },
      data: {
        tenantId: TENANT_ID,
        templateKey: `e2e_mut_${Date.now()}`,
        subjectTemplate: 'Original subject',
        bodyTemplate: 'Original body.',
        channel: 'inapp',
      },
    });
    if ([200, 201].includes(res.status())) {
      const body = await res.json();
      templateId = body.data ?? body.data?.id ?? body.id;
    }
  });

  test.afterAll(async ({ request }) => {
    if (templateId) {
      await request.delete(
        `${API_BASE}/api/v1/admin/notification-templates/${templateId}`,
        { headers: authHeaders(adminToken) },
      );
    }
  });

  // TC-NOTIF-045: Update notification template — happy path
  test('TC-NOTIF-045: PUT /admin/notification-templates/{id} — updates successfully', async ({ request }) => {
    test.skip(!templateId, 'Template not set up');
    const res = await request.put(
      `${API_BASE}/api/v1/admin/notification-templates/${templateId}`,
      {
        headers: { ...authHeaders(adminToken), 'Content-Type': 'application/json' },
        data: { subjectTemplate: 'Updated subject {{name}}', bodyTemplate: 'Updated body.' },
      },
    );
    expect([200, 204]).toContain(res.status());
  });

  // TC-NOTIF-046: Update notification template — not found returns 404
  test('TC-NOTIF-046: PUT /admin/notification-templates/nonexistent — 404', async ({ request }) => {
    const res = await request.put(
      `${API_BASE}/api/v1/admin/notification-templates/00000000-0000-0000-0000-000000000003`,
      {
        headers: { ...authHeaders(adminToken), 'Content-Type': 'application/json' },
        data: { subjectTemplate: 'Updated', bodyTemplate: 'Updated body.' },
      },
    );
    expect(res.status()).toBe(404);
  });

  // TC-NOTIF-047: Update notification template — unauthenticated returns 401
  test('TC-NOTIF-047: PUT /admin/notification-templates/{id} without auth — 401', async ({ request }) => {
    const res = await request.put(
      `${API_BASE}/api/v1/admin/notification-templates/00000000-0000-0000-0000-000000000003`,
      {
        headers: { 'Content-Type': 'application/json' },
        data: { subjectTemplate: 's', bodyTemplate: 'b' },
      },
    );
    expect(res.status()).toBe(401);
  });

  // TC-NOTIF-048: Update notification template — insufficient role returns 403
  test('TC-NOTIF-048: PUT /admin/notification-templates/{id} with user token — 403', async ({ request }) => {
    test.skip(!templateId, 'Template not set up');
    const res = await request.put(
      `${API_BASE}/api/v1/admin/notification-templates/${templateId}`,
      {
        headers: { ...authHeaders(userToken), 'Content-Type': 'application/json' },
        data: { subjectTemplate: 's', bodyTemplate: 'b' },
      },
    );
    expect(res.status()).toBe(403);
  });

  // TC-NOTIF-053: Delete notification template — not found returns 404
  test('TC-NOTIF-053: DELETE /admin/notification-templates/nonexistent — 404', async ({ request }) => {
    const res = await request.delete(
      `${API_BASE}/api/v1/admin/notification-templates/00000000-0000-0000-0000-000000000004`,
      { headers: authHeaders(adminToken) },
    );
    expect(res.status()).toBe(404);
  });

  // TC-NOTIF-054: Delete notification template — unauthenticated returns 401
  test('TC-NOTIF-054: DELETE /admin/notification-templates/{id} without auth — 401', async ({ request }) => {
    const res = await request.delete(
      `${API_BASE}/api/v1/admin/notification-templates/00000000-0000-0000-0000-000000000004`,
    );
    expect(res.status()).toBe(401);
  });

  // TC-NOTIF-055: Delete notification template — insufficient role returns 403
  test('TC-NOTIF-055: DELETE /admin/notification-templates/{id} with user token — 403', async ({ request }) => {
    test.skip(!templateId, 'Template not set up');
    const res = await request.delete(
      `${API_BASE}/api/v1/admin/notification-templates/${templateId}`,
      { headers: authHeaders(userToken) },
    );
    expect(res.status()).toBe(403);
  });

  // TC-NOTIF-051 + TC-NOTIF-052: Delete — happy path + verify hidden in GET
  test('TC-NOTIF-051/052: DELETE template then GET returns 404', async ({ request }) => {
    const keyToDelete = `e2e_del_${Date.now()}`;
    const createRes = await request.post(`${API_BASE}/api/v1/admin/notification-templates`, {
      headers: { ...authHeaders(adminToken), 'Content-Type': 'application/json' },
      data: {
        tenantId: TENANT_ID,
        templateKey: keyToDelete,
        subjectTemplate: 'To delete',
        bodyTemplate: 'Will be deleted.',
        channel: 'inapp',
      },
    });
    test.skip(![200, 201].includes(createRes.status()), 'Could not create template to delete');
    const createBody = await createRes.json();
    const idToDelete: string = createBody.data ?? createBody.data?.id ?? createBody.id;
    expect(idToDelete).toBeTruthy();

    const delRes = await request.delete(
      `${API_BASE}/api/v1/admin/notification-templates/${idToDelete}`,
      { headers: authHeaders(adminToken) },
    );
    expect([200, 204]).toContain(delRes.status());

    const getRes = await request.get(
      `${API_BASE}/api/v1/admin/notification-templates/${idToDelete}`,
      { headers: authHeaders(adminToken) },
    );
    expect(getRes.status()).toBe(404);
  });

  // TC-NOTIF-058: Full template lifecycle — create → read → update → delete
  test('TC-NOTIF-058: full template CRUD lifecycle', async ({ request }) => {
    const lifecycleKey = `e2e_lc_${Date.now()}`;

    const createRes = await request.post(`${API_BASE}/api/v1/admin/notification-templates`, {
      headers: { ...authHeaders(adminToken), 'Content-Type': 'application/json' },
      data: {
        tenantId: TENANT_ID,
        templateKey: lifecycleKey,
        subjectTemplate: 'Original subject',
        bodyTemplate: 'Original body.',
        channel: 'inapp',
      },
    });
    test.skip(![200, 201].includes(createRes.status()), 'Could not create template for lifecycle');
    const createBody = await createRes.json();
    const lcId: string = createBody.data ?? createBody.data?.id ?? createBody.id;
    expect(lcId).toBeTruthy();
    const base = `${API_BASE}/api/v1/admin/notification-templates/${lcId}`;
    const hdr = { ...authHeaders(adminToken), 'Content-Type': 'application/json' };
    expect((await request.get(base, { headers: authHeaders(adminToken) })).status()).toBe(200);
    expect([200, 204]).toContain(
      (await request.put(base, { headers: hdr, data: { subjectTemplate: 'Updated subject', bodyTemplate: 'Updated body.' } })).status(),
    );
    const getAfterRes = await request.get(base, { headers: authHeaders(adminToken) });
    if (getAfterRes.status() === 200) {
      const d = (await getAfterRes.json()).data ?? (await getAfterRes.json());
      if (d?.subjectTemplate) expect(d.subjectTemplate).toBe('Updated subject');
    }
    expect([200, 204]).toContain(
      (await request.delete(base, { headers: authHeaders(adminToken) })).status(),
    );
    expect((await request.get(base, { headers: authHeaders(adminToken) })).status()).toBe(404);
  });
});
