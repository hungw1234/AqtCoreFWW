import { test, expect } from './fixtures/test-base';
import { getAccessToken, authHeaders, API_BASE } from './helpers/auth-helper';

const DUMMY_ID = '00000000-0000-0000-0000-000000000001';

// Workflow auth guard E2E — 401 unauthenticated + 403 role guards
// TC-WF-100 through TC-WF-112 (P1 only)
test.describe('Workflow: auth guards (401 + 403)', () => {
  let userToken: string;

  test.beforeAll(async ({ request }) => {
    // 'user' role = viewer@dev.local — no Admin/SystemAdmin role
    userToken = await getAccessToken(request, 'user');
    expect(userToken).toBeTruthy();
  });

  // ── 401 Unauthenticated tests — no Authorization header ───────────────────

  test('TC-WF-100: GET /definitions without token — 401', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/workflow/definitions`);
    expect(res.status()).toBe(401);
  });

  test('TC-WF-101: POST /definitions without token — 401', async ({ request }) => {
    const res = await request.post(`${API_BASE}/api/v1/workflow/definitions`, {
      headers: { 'Content-Type': 'application/json' },
      data: { name: 'test', description: 'test', states: [], transitions: [] },
    });
    expect(res.status()).toBe(401);
  });

  test('TC-WF-102: PUT /definitions/{id}/graph without token — 401', async ({ request }) => {
    const res = await request.put(`${API_BASE}/api/v1/workflow/definitions/${DUMMY_ID}/graph`, {
      headers: { 'Content-Type': 'application/json' },
      data: { states: [], transitions: [] },
    });
    expect(res.status()).toBe(401);
  });

  test('TC-WF-103: POST /definitions/{id}/activate without token — 401', async ({ request }) => {
    const res = await request.post(
      `${API_BASE}/api/v1/workflow/definitions/${DUMMY_ID}/activate`,
    );
    expect(res.status()).toBe(401);
  });

  test('TC-WF-104: POST /instances without token — 401', async ({ request }) => {
    const res = await request.post(`${API_BASE}/api/v1/workflow/instances`, {
      headers: { 'Content-Type': 'application/json' },
      data: { entityType: 'Test', entityId: DUMMY_ID },
    });
    expect(res.status()).toBe(401);
  });

  test('TC-WF-105: POST /instances/{id}/transitions without token — 401', async ({ request }) => {
    const res = await request.post(
      `${API_BASE}/api/v1/workflow/instances/${DUMMY_ID}/transitions`,
      {
        headers: { 'Content-Type': 'application/json' },
        data: { transitionId: DUMMY_ID, comment: null, metadata: null },
      },
    );
    expect(res.status()).toBe(401);
  });

  test('TC-WF-106: GET /tasks/by-instance/{id} without token — 401', async ({ request }) => {
    const res = await request.get(
      `${API_BASE}/api/v1/workflow/tasks/by-instance/${DUMMY_ID}`,
    );
    expect(res.status()).toBe(401);
  });

  test('TC-WF-107: POST /tasks without token — 401', async ({ request }) => {
    const res = await request.post(`${API_BASE}/api/v1/workflow/tasks`, {
      headers: { 'Content-Type': 'application/json' },
      data: {
        workflowInstanceId: DUMMY_ID,
        title: 'test',
        description: null,
        assigneeId: null,
        priority: 1,
        dueDate: null,
      },
    });
    expect(res.status()).toBe(401);
  });

  test('TC-WF-108: POST /tasks/{id}/cancel without token — 401', async ({ request }) => {
    const res = await request.post(`${API_BASE}/api/v1/workflow/tasks/${DUMMY_ID}/cancel`, {
      headers: { 'Content-Type': 'application/json' },
      data: { reason: null },
    });
    expect(res.status()).toBe(401);
  });

  test('TC-WF-109: GET /assignments without token — 401', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/workflow/assignments`);
    expect(res.status()).toBe(401);
  });

  test('TC-WF-110: POST /assignments without token — 401', async ({ request }) => {
    const res = await request.post(`${API_BASE}/api/v1/workflow/assignments`, {
      headers: { 'Content-Type': 'application/json' },
      data: {
        workflowDefinitionId: DUMMY_ID,
        entityType: 'Test',
        entitySubType: null,
        priority: 10,
        description: null,
      },
    });
    expect(res.status()).toBe(401);
  });

  // ── 403 Role guard tests — authenticated but insufficient role ────────────

  test('TC-WF-111: POST /definitions without Admin role — 403', async ({ request }) => {
    const res = await request.post(`${API_BASE}/api/v1/workflow/definitions`, {
      headers: { ...authHeaders(userToken), 'Content-Type': 'application/json' },
      data: {
        name: 'Unauthorized create attempt',
        description: 'Should be rejected',
        states: [],
        transitions: [],
      },
    });
    expect(res.status()).toBe(403);
  });

  test('TC-WF-112: POST /assignments without Admin role — 403', async ({ request }) => {
    const res = await request.post(`${API_BASE}/api/v1/workflow/assignments`, {
      headers: { ...authHeaders(userToken), 'Content-Type': 'application/json' },
      data: {
        workflowDefinitionId: DUMMY_ID,
        entityType: 'Test',
        entitySubType: null,
        priority: 10,
        description: null,
      },
    });
    expect(res.status()).toBe(403);
  });
});
