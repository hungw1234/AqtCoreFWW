import { test, expect } from './fixtures/test-base';
import { getAccessToken, authHeaders, API_BASE } from './helpers/auth-helper';

// Notification template CRUD lifecycle E2E — /api/v1/admin/notification-templates
// P1 TCs: TC-NOTIF-028 to TC-NOTIF-055 (list, get, create, update, delete + auth guards)
test.describe('Notification templates admin CRUD (admin API)', () => {
  test.describe.configure({ mode: 'serial' });

  let adminToken: string;
  let userToken: string;
  let templateId: string;

  const TENANT_ID = '00000000-0000-0000-0000-000000000001';
  const templateKey = `e2e_lifecycle_${Date.now()}`;

  test.beforeAll(async ({ request }) => {
    adminToken = await getAccessToken(request, 'admin');
    userToken  = await getAccessToken(request, 'user');
    expect(adminToken).toBeTruthy();
    expect(userToken).toBeTruthy();
  });

  test.afterAll(async ({ request }) => {
    if (templateId) {
      await request.delete(
        `${API_BASE}/api/v1/admin/notification-templates/${templateId}`,
        { headers: authHeaders(adminToken) },
      );
    }
  });

  // TC-NOTIF-028: List notification templates — happy path
  test('TC-NOTIF-028: GET /admin/notification-templates — returns 200 list', async ({ request }) => {
    const res = await request.get(
      `${API_BASE}/api/v1/admin/notification-templates?tenantId=${TENANT_ID}`,
      { headers: authHeaders(adminToken) },
    );
    expect(res.status()).toBe(200);
    const body = await res.json();
    expect(body.success).toBe(true);
    expect(body.data).toBeDefined();
  });

  // TC-NOTIF-029: List notification templates — unauthenticated returns 401
  test('TC-NOTIF-029: GET /admin/notification-templates without auth — 401', async ({ request }) => {
    const res = await request.get(
      `${API_BASE}/api/v1/admin/notification-templates?tenantId=${TENANT_ID}`,
    );
    expect(res.status()).toBe(401);
  });

  // TC-NOTIF-030: List notification templates — insufficient role returns 403
  test('TC-NOTIF-030: GET /admin/notification-templates with user token — 403', async ({ request }) => {
    const res = await request.get(
      `${API_BASE}/api/v1/admin/notification-templates?tenantId=${TENANT_ID}`,
      { headers: authHeaders(userToken) },
    );
    expect(res.status()).toBe(403);
  });

  // TC-NOTIF-036: Create notification template — happy path (inapp channel)
  test('TC-NOTIF-036: POST /admin/notification-templates — creates inapp template', async ({ request }) => {
    const res = await request.post(`${API_BASE}/api/v1/admin/notification-templates`, {
      headers: { ...authHeaders(adminToken), 'Content-Type': 'application/json' },
      data: {
        tenantId: TENANT_ID,
        templateKey,
        subjectTemplate: 'Welcome {{name}}',
        bodyTemplate: 'Hello {{name}}, welcome to the platform.',
        channel: 'inapp',
      },
    });
    expect([200, 201]).toContain(res.status());
    if ([200, 201].includes(res.status())) {
      const body = await res.json();
      templateId = body.data ?? body.data?.id ?? body.id;
      expect(templateId).toBeTruthy();
    }
  });

  // TC-NOTIF-037: Create notification template — happy path (email channel)
  test('TC-NOTIF-037: POST /admin/notification-templates — creates email template', async ({ request }) => {
    const emailKey = `e2e_email_${Date.now()}`;
    const res = await request.post(`${API_BASE}/api/v1/admin/notification-templates`, {
      headers: { ...authHeaders(adminToken), 'Content-Type': 'application/json' },
      data: {
        tenantId: TENANT_ID,
        templateKey: emailKey,
        subjectTemplate: 'Password reset for {{username}}',
        bodyTemplate: 'Click {{reset_link}} to reset your password.',
        channel: 'email',
      },
    });
    expect([200, 201]).toContain(res.status());
    if ([200, 201].includes(res.status())) {
      // cleanup this extra template
      const body = await res.json();
      const id: string = body.data ?? body.data?.id ?? body.id;
      if (id) {
        await request.delete(
          `${API_BASE}/api/v1/admin/notification-templates/${id}`,
          { headers: authHeaders(adminToken) },
        );
      }
    }
  });

  // TC-NOTIF-039: Create notification template — unauthenticated returns 401
  test('TC-NOTIF-039: POST /admin/notification-templates without auth — 401', async ({ request }) => {
    const res = await request.post(`${API_BASE}/api/v1/admin/notification-templates`, {
      headers: { 'Content-Type': 'application/json' },
      data: {
        tenantId: TENANT_ID,
        templateKey: 'e2e_anon',
        subjectTemplate: 's',
        bodyTemplate: 'b',
        channel: 'inapp',
      },
    });
    expect(res.status()).toBe(401);
  });

  // TC-NOTIF-040: Create notification template — insufficient role returns 403
  test('TC-NOTIF-040: POST /admin/notification-templates with user token — 403', async ({ request }) => {
    const res = await request.post(`${API_BASE}/api/v1/admin/notification-templates`, {
      headers: { ...authHeaders(userToken), 'Content-Type': 'application/json' },
      data: {
        tenantId: TENANT_ID,
        templateKey: 'e2e_user_test',
        subjectTemplate: 's',
        bodyTemplate: 'b',
        channel: 'inapp',
      },
    });
    expect(res.status()).toBe(403);
  });

  // TC-NOTIF-041: Create notification template — invalid templateKey pattern returns 422
  test('TC-NOTIF-041: POST /admin/notification-templates invalid templateKey — 422', async ({ request }) => {
    const res = await request.post(`${API_BASE}/api/v1/admin/notification-templates`, {
      headers: { ...authHeaders(adminToken), 'Content-Type': 'application/json' },
      data: {
        tenantId: TENANT_ID,
        templateKey: 'Invalid Key With Spaces!',
        subjectTemplate: 'Subject',
        bodyTemplate: 'Body',
        channel: 'inapp',
      },
    });
    expect([400, 422]).toContain(res.status());
  });

  // TC-NOTIF-042: Create notification template — invalid channel returns 422
  test('TC-NOTIF-042: POST /admin/notification-templates invalid channel — 422', async ({ request }) => {
    const res = await request.post(`${API_BASE}/api/v1/admin/notification-templates`, {
      headers: { ...authHeaders(adminToken), 'Content-Type': 'application/json' },
      data: {
        tenantId: TENANT_ID,
        templateKey: 'e2e_bad_channel',
        subjectTemplate: 'Subject',
        bodyTemplate: 'Body',
        channel: 'webhook',
      },
    });
    expect([400, 422]).toContain(res.status());
  });

  // TC-NOTIF-033: Get template by ID — happy path
  test('TC-NOTIF-033: GET /admin/notification-templates/{id} — returns template', async ({ request }) => {
    test.skip(!templateId, 'Template not created in TC-NOTIF-036');
    const res = await request.get(
      `${API_BASE}/api/v1/admin/notification-templates/${templateId}`,
      { headers: authHeaders(adminToken) },
    );
    expect(res.status()).toBe(200);
    const body = await res.json();
    expect(body.success).toBe(true);
    const data = body.data ?? body;
    expect(data.templateKey ?? data.id).toBeTruthy();
  });

  // TC-NOTIF-034: Get template by ID — not found (404)
  test('TC-NOTIF-034: GET /admin/notification-templates/nonexistent — 404', async ({ request }) => {
    const res = await request.get(
      `${API_BASE}/api/v1/admin/notification-templates/00000000-0000-0000-0000-000000000002`,
      { headers: authHeaders(adminToken) },
    );
    expect(res.status()).toBe(404);
  });

  // TC-NOTIF-035: Get template by ID — unauthenticated returns 401
  test('TC-NOTIF-035: GET /admin/notification-templates/{id} without auth — 401', async ({ request }) => {
    const res = await request.get(
      `${API_BASE}/api/v1/admin/notification-templates/00000000-0000-0000-0000-000000000002`,
    );
    expect(res.status()).toBe(401);
  });

  // TC-NOTIF-045 to TC-NOTIF-058: Template update/delete/lifecycle — see notifications-template-mutations.spec.ts
});
