import { test, expect } from './fixtures/test-base';
import { getAccessToken, authHeaders, API_BASE } from './helpers/auth-helper';

// Workflow Configs E2E — notification configs + assignment rules.
// Creates its own definition so this file is fully independent.
test.describe.serial('Workflow Notification Configs (authenticated API)', () => {
  let token: string;

  let definitionId: string;
  let notificationConfigId: string;

  const TENANT_ID = '00000000-0000-0000-0000-000000000001';
  const ACTOR_ID = '00000000-0000-0000-0000-000000000001';

  test.beforeAll(async ({ request }) => {
    token = await getAccessToken(request);
    expect(token).toBeTruthy();
  });

  // ── Bootstrap: create a definition to attach configs to ─────────────────────

  test('setup — create definition for notification config tests', async ({ request }) => {
    const res = await request.post(`${API_BASE}/api/v1/workflow/definitions`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: {
        name: `E2E Configs Def ${Date.now()}`,
        description: 'Bootstrap definition for notification config E2E tests',
        tenantId: TENANT_ID,
        createdBy: ACTOR_ID,
        states: [
          { name: 'Draft', displayNameVi: 'Nháp', displayNameEn: 'Draft', isInitial: true, isFinal: false, color: '#808080', sortOrder: 0 },
        ],
        transitions: [],
      },
    });
    expect([200, 201]).toContain(res.status());
    const body = await res.json();
    definitionId = body.data?.id ?? body.id;
    expect(definitionId).toBeTruthy();
  });

  // ── Create notification config ───────────────────────────────────────────────

  test('POST /workflow/definitions/{id}/notification-configs — create email config', async ({ request }) => {
    test.skip(!definitionId, 'No definition bootstrapped');
    const res = await request.post(
      `${API_BASE}/api/v1/workflow/definitions/${definitionId}/notification-configs`,
      {
        headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
        data: {
          actionName: 'Submit',
          channel: 'Email',
          recipientType: 'Assignee',
          recipientValue: '',
          subject: 'E2E notification subject',
          bodyTemplate: 'Instance {{instanceId}} was submitted.',
          tenantId: TENANT_ID,
          createdBy: ACTOR_ID,
        },
      },
    );
    expect([200, 201]).toContain(res.status());
    const body = await res.json();
    notificationConfigId = body.data?.id ?? body.id;
    expect(notificationConfigId).toBeTruthy();
  });

  // ── List notification configs ────────────────────────────────────────────────

  test('GET /workflow/definitions/{id}/notification-configs — list configs', async ({ request }) => {
    test.skip(!definitionId, 'No definition bootstrapped');
    const res = await request.get(
      `${API_BASE}/api/v1/workflow/definitions/${definitionId}/notification-configs` +
      `?tenantId=${TENANT_ID}`,
      { headers: authHeaders(token) },
    );
    expect(res.status()).toBe(200);
    const body = await res.json();
    const configs: Array<{ id: string }> = body.data ?? body ?? [];
    expect(Array.isArray(configs)).toBeTruthy();
    if (notificationConfigId) {
      expect(configs.some((c) => c.id === notificationConfigId)).toBeTruthy();
    }
  });

  // ── Delete notification config ───────────────────────────────────────────────

  test('DELETE /workflow/definitions/{id}/notification-configs/{configId}', async ({ request }) => {
    test.skip(!definitionId || !notificationConfigId, 'No config created');
    const res = await request.delete(
      `${API_BASE}/api/v1/workflow/definitions/${definitionId}` +
      `/notification-configs/${notificationConfigId}?tenantId=${TENANT_ID}`,
      { headers: authHeaders(token) },
    );
    expect([200, 204]).toContain(res.status());
  });
});

// ── Assignment Rules ─────────────────────────────────────────────────────────

test.describe.serial('Workflow Assignment Rules (authenticated API)', () => {
  let token: string;

  let definitionId: string;
  let assignmentRuleId: string;

  const TENANT_ID = '00000000-0000-0000-0000-000000000001';
  const ACTOR_ID = '00000000-0000-0000-0000-000000000001';
  const ENTITY_TYPE = 'E2EAssignmentEntity';

  test.beforeAll(async ({ request }) => {
    token = await getAccessToken(request);
    expect(token).toBeTruthy();
  });

  // ── Bootstrap: create a definition to assign ─────────────────────────────────

  test('setup — create definition for assignment rule tests', async ({ request }) => {
    const res = await request.post(`${API_BASE}/api/v1/workflow/definitions`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: {
        name: `E2E Assignment Def ${Date.now()}`,
        description: 'Bootstrap definition for assignment rule E2E tests',
        tenantId: TENANT_ID,
        createdBy: ACTOR_ID,
        states: [
          { name: 'Draft', displayNameVi: 'Nháp', displayNameEn: 'Draft', isInitial: true, isFinal: false, color: '#808080', sortOrder: 0 },
        ],
        transitions: [],
      },
    });
    expect([200, 201]).toContain(res.status());
    const body = await res.json();
    definitionId = body.data?.id ?? body.id;
    expect(definitionId).toBeTruthy();
  });

  // ── Create assignment rule ────────────────────────────────────────────────────

  test('POST /workflow/assignments — create assignment rule', async ({ request }) => {
    test.skip(!definitionId, 'No definition bootstrapped');
    const res = await request.post(`${API_BASE}/api/v1/workflow/assignments`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: {
        tenantId: TENANT_ID,
        workflowDefinitionId: definitionId,
        entityType: ENTITY_TYPE,
        entitySubType: null,
        priority: 10,
        description: 'E2E assignment rule',
        createdBy: ACTOR_ID,
        effectiveFrom: null,
        effectiveUntil: null,
      },
    });
    expect([200, 201]).toContain(res.status());
    const body = await res.json();
    assignmentRuleId = body.data?.id ?? body.id;
    expect(assignmentRuleId).toBeTruthy();
  });

  // ── List assignment rules ─────────────────────────────────────────────────────

  test('GET /workflow/assignments — list rules for tenant', async ({ request }) => {
    const res = await request.get(
      `${API_BASE}/api/v1/workflow/assignments?tenantId=${TENANT_ID}`,
      { headers: authHeaders(token) },
    );
    expect(res.status()).toBe(200);
    const body = await res.json();
    const rules: unknown[] = body.data ?? body ?? [];
    expect(Array.isArray(rules)).toBeTruthy();
  });

  // ── Resolve assignment ────────────────────────────────────────────────────────

  test('GET /workflow/assignments/resolve — resolve best match for entityType', async ({ request }) => {
    test.skip(!assignmentRuleId, 'No assignment rule created');
    const res = await request.get(
      `${API_BASE}/api/v1/workflow/assignments/resolve` +
      `?tenantId=${TENANT_ID}&entityType=${ENTITY_TYPE}`,
      { headers: authHeaders(token) },
    );
    // 200 = match found; 404 = no active rule matched (acceptable on re-run if rule was deleted)
    expect([200, 404]).toContain(res.status());
    if (res.status() === 200) {
      const body = await res.json();
      const resolved = body.data ?? body;
      expect(resolved).toBeTruthy();
    }
  });

  // ── Delete assignment rule ────────────────────────────────────────────────────

  test('DELETE /workflow/assignments/{id} — soft delete rule', async ({ request }) => {
    test.skip(!assignmentRuleId, 'No assignment rule created');
    const res = await request.delete(
      `${API_BASE}/api/v1/workflow/assignments/${assignmentRuleId}` +
      `?tenantId=${TENANT_ID}&deletedBy=${ACTOR_ID}`,
      { headers: authHeaders(token) },
    );
    expect([200, 204]).toContain(res.status());
  });
});
