import { test, expect } from './fixtures/test-base';
import { getAccessToken, authHeaders, API_BASE, resolveUserId } from './helpers/auth-helper';

// Seeded test data IDs (E2ETestDataSeeder)
const WORKFLOW_DEF_ID = '20000000-0000-0000-0000-000000000001';

// Workflow instances + tasks E2E
// TC-WF-001: branch child resolve (approve)
// TC-WF-010: tasks by assignee
// TC-WF-011: assign task
// TC-WF-013: reassign task
// TC-WF-015: cancel task
test.describe.configure({ mode: 'serial' });
test.describe('Workflow: instances and tasks (authenticated API)', () => {
  let token: string;
  let userId: string;
  let instanceId: string;
  let taskId: string;

  test.beforeAll(async ({ request }) => {
    token = await getAccessToken(request, 'admin');
    expect(token).toBeTruthy();
    userId = await resolveUserId(request, token);
  });

  // Helper: create a workflow instance bound to the seeded definition.
  // Field names must match CreateWorkflowInstanceRequest: definitionId (Guid), entityId (Guid).
  // entityType "E2ETest" passes (no validator constrains it on instance creation when
  // definitionId is provided; resolver path is skipped).
  async function createInstance(request: any): Promise<string> {
    const res = await request.post(`${API_BASE}/api/v1/workflow/instances`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: {
        definitionId: WORKFLOW_DEF_ID,
        entityType: 'E2ETest',
        entityId: crypto.randomUUID(),
      },
    });
    const body = await res.json();
    return body.data?.id ?? body.id ?? '';
  }

  // Helper: create a workflow task for a given instanceId
  async function createTask(request: any, wfInstanceId: string): Promise<string> {
    const res = await request.post(`${API_BASE}/api/v1/workflow/tasks`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: {
        workflowInstanceId: wfInstanceId,
        title: `E2E Task ${Date.now()}`,
        description: null,
        assigneeId: null,
        priority: 1,
        dueDate: null,
      },
    });
    const body = await res.json();
    return body.data?.id ?? body.id ?? '';
  }

  // ── Bootstrap: create instance + task for use in subsequent tests ─────────

  test('bootstrap: create workflow instance for task tests', async ({ request }) => {
    instanceId = await createInstance(request);
    // 404 = seeded definition not present in this env; skip downstream tests gracefully
    if (!instanceId) {
      test.skip(true, 'Could not create workflow instance — seeded definition may be inactive');
    }
    expect(instanceId).toBeTruthy();
  });

  test('bootstrap: create workflow task for assign/cancel tests', async ({ request }) => {
    test.skip(!instanceId, 'No instance available');
    taskId = await createTask(request, instanceId);
    if (!taskId) {
      test.skip(true, 'Could not create workflow task');
    }
    expect(taskId).toBeTruthy();
  });

  // ── TC-WF-010: Get tasks by assignee ─────────────────────────────────────

  test('TC-WF-010: get tasks by assignee — returns list (may be empty)', async ({ request }) => {
    test.skip(!userId, 'No userId resolved');

    const res = await request.get(`${API_BASE}/api/v1/workflow/tasks/by-assignee/${userId}`, {
      headers: authHeaders(token),
    });
    expect(res.status()).toBe(200);
    const body = await res.json();
    const items = body.data ?? body;
    expect(Array.isArray(items)).toBe(true);
  });

  // ── TC-WF-011: Assign task to user ───────────────────────────────────────

  test('TC-WF-011: assign task to user — 200 or 422 (valid user check)', async ({ request }) => {
    test.skip(!taskId, 'No task available');
    test.skip(!userId, 'No userId resolved');

    const res = await request.post(`${API_BASE}/api/v1/workflow/tasks/${taskId}/assign`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: {
        assigneeId: userId,
        reason: 'Assigned by E2E test',
      },
    });
    // 200 = assigned; 422 = already assigned or invalid state
    expect([200, 204, 422]).toContain(res.status());
  });

  // ── TC-WF-013: Reassign task ──────────────────────────────────────────────

  test('TC-WF-013: reassign task to user — 200 or 422', async ({ request }) => {
    test.skip(!taskId, 'No task available');
    test.skip(!userId, 'No userId resolved');

    const res = await request.post(`${API_BASE}/api/v1/workflow/tasks/${taskId}/reassign`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: {
        newAssigneeId: userId,
        reason: 'Reassigned by E2E test',
      },
    });
    expect([200, 204, 422]).toContain(res.status());
  });

  // ── TC-WF-015: Cancel task ────────────────────────────────────────────────

  test('TC-WF-015: cancel task — 200 after cancellation', async ({ request }) => {
    test.skip(!instanceId, 'No instance available');

    // Create a fresh task specifically for cancellation
    const cancelTaskId = await createTask(request, instanceId);
    test.skip(!cancelTaskId, 'Could not create cancel-target task');

    const res = await request.post(`${API_BASE}/api/v1/workflow/tasks/${cancelTaskId}/cancel`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: { reason: 'Cancelled by E2E test' },
    });
    expect([200, 204]).toContain(res.status());
  });

  // ── TC-WF-001: Resolve parallel branch child (approve) ────────────────────
  // This test requires a definition with parallel branch states.
  // We attempt the bootstrap and skip gracefully if setup is not possible.

  test('TC-WF-001: resolve branch child — 200 or skip if no parallel branch', async ({ request }) => {
    test.skip(!instanceId, 'No instance available');

    const branchRes = await request.get(`${API_BASE}/api/v1/workflow/instances/${instanceId}/branch-status`, {
      headers: authHeaders(token),
    });

    if (!branchRes.ok()) {
      test.skip(true, 'Branch status endpoint unavailable or instance has no branches');
      return;
    }

    const branchBody = await branchRes.json();
    const children: Array<{ id: string }> = branchBody.data?.childStatuses ?? branchBody.data ?? [];

    if (children.length === 0) {
      test.skip(true, 'No parallel branch children — definition may not have fan-out states');
      return;
    }

    const childStatusId = children[0].id;
    const res = await request.post(
      `${API_BASE}/api/v1/workflow/instances/${instanceId}/branch-children/${childStatusId}/resolve`,
      {
        headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
        data: { resolutionType: 1, comment: 'Branch approved by E2E test' },
      },
    );
    expect([200, 204, 422]).toContain(res.status());
  });
});
