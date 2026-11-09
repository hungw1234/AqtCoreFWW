import { test, expect } from './fixtures/test-base';
import { getAccessToken, authHeaders, API_BASE } from './helpers/auth-helper';

// Workflow Definitions E2E — Definition CRUD, graph save, activate/deactivate, clone, delete.
// All tests run serially within the describe block because they share definitionId.
test.describe.serial('Workflow Definitions (authenticated API)', () => {
  let token: string;

  // IDs shared across chained tests
  let definitionId: string;
  let cloneId: string;

  // Fixed actor IDs — use a well-known dev tenant/user pair
  const TENANT_ID = '00000000-0000-0000-0000-000000000001';
  const ACTOR_ID = '00000000-0000-0000-0000-000000000001';

  // R2-strict at Save (WorkflowGraphValidator.ValidateForSaveAsync) requires every non-recall
  // transition out of a non-final state to carry an assignment rule. A ByPermission pool passes
  // the save-time clause-mode guard. Same string-enum shape the parallel-branch spec uses.
  const poolAssignment = {
    combineMode: 'Union',
    allowAssignorOverride: false,
    clauses: [{ mode: 'ByPermission', refsJson: '["WORKFLOW.TASK.PROCESS"]' }],
  };

  test.beforeAll(async ({ request }) => {
    token = await getAccessToken(request);
    expect(token).toBeTruthy();
  });

  // ── Create ──────────────────────────────────────────────────────────────────

  test('POST /workflow/definitions — create definition', async ({ request }) => {
    const res = await request.post(`${API_BASE}/api/v1/workflow/definitions`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: {
        name: `E2E Definition ${Date.now()}`,
        description: 'Created by E2E test',
        tenantId: TENANT_ID,
        createdBy: ACTOR_ID,
        entityType: 'Generic',
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

  // ── Read ────────────────────────────────────────────────────────────────────

  test('GET /workflow/definitions/{id} — get with states/transitions', async ({ request }) => {
    test.skip(!definitionId, 'No definition created');
    const res = await request.get(
      `${API_BASE}/api/v1/workflow/definitions/${definitionId}?tenantId=${TENANT_ID}`,
      { headers: authHeaders(token) },
    );
    expect(res.status()).toBe(200);
    const body = await res.json();
    const def = body.data ?? body;
    expect(def.id).toBe(definitionId);
  });

  // ── Update metadata ─────────────────────────────────────────────────────────

  test('PUT /workflow/definitions/{id} — update name and description', async ({ request }) => {
    test.skip(!definitionId, 'No definition created');
    const res = await request.put(`${API_BASE}/api/v1/workflow/definitions/${definitionId}`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: {
        name: `E2E Definition Updated ${Date.now()}`,
        description: 'Updated by E2E test',
        tenantId: TENANT_ID,
        modifiedBy: ACTOR_ID,
      },
    });
    expect([200, 204]).toContain(res.status());
  });

  // ── Save graph (3 states + 2 transitions) ───────────────────────────────────

  test('PUT /workflow/definitions/{id}/graph — save states + transitions', async ({ request }) => {
    test.skip(!definitionId, 'No definition created');
    const res = await request.put(
      `${API_BASE}/api/v1/workflow/definitions/${definitionId}/graph`,
      {
        headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
        data: {
          tenantId: TENANT_ID,
          modifiedBy: ACTOR_ID,
          states: [
            {
              name: 'Draft', displayNameVi: 'Nháp', displayNameEn: 'Draft',
              isInitial: true, isFinal: false, color: '#9E9E9E', sortOrder: 1,
            },
            {
              name: 'UnderReview', displayNameVi: 'Xem xét', displayNameEn: 'In Review',
              isInitial: false, isFinal: false, color: '#2196F3', sortOrder: 2,
            },
            {
              name: 'Approved', displayNameVi: 'Đã duyệt', displayNameEn: 'Approved',
              isInitial: false, isFinal: true, color: '#4CAF50', sortOrder: 3,
            },
          ],
          transitions: [
            {
              fromStateName: 'Draft', toStateName: 'UnderReview',
              actionName: 'Submit', actionLabelVi: 'Nộp', actionLabelEn: 'Submit',
              conditionsJson: null, sortOrder: 1, assignment: poolAssignment,
            },
            {
              fromStateName: 'UnderReview', toStateName: 'Approved',
              actionName: 'Approve', actionLabelVi: 'Duyệt', actionLabelEn: 'Approve',
              conditionsJson: null, sortOrder: 2, assignment: poolAssignment,
            },
          ],
        },
      },
    );
    expect([200, 204]).toContain(res.status());
  });

  // ── Activate ─────────────────────────────────────────────────────────────────

  test('POST /workflow/definitions/{id}/activate — activate definition', async ({ request }) => {
    test.skip(!definitionId, 'No definition created');
    const res = await request.post(
      `${API_BASE}/api/v1/workflow/definitions/${definitionId}/activate` +
      `?tenantId=${TENANT_ID}&modifiedBy=${ACTOR_ID}`,
      { headers: authHeaders(token) },
    );
    expect([200, 204]).toContain(res.status());
  });

  // ── Clone ─────────────────────────────────────────────────────────────────────

  test('POST /workflow/definitions/{id}/clone — clone as new version', async ({ request }) => {
    test.skip(!definitionId, 'No definition created');
    const res = await request.post(
      `${API_BASE}/api/v1/workflow/definitions/${definitionId}/clone` +
      `?tenantId=${TENANT_ID}&createdBy=${ACTOR_ID}`,
      { headers: authHeaders(token) },
    );
    expect([200, 201]).toContain(res.status());
    const body = await res.json();
    cloneId = body.data?.id ?? body.id;
    expect(cloneId).toBeTruthy();

    // BUG2-CLONE regression (2026-05-18): clone must deep-copy transitions with FKs remapped to
    // the cloned states' new ids. Previously every cloned transition was persisted with
    // FromStateId=0/ToStateId=0 (silent corruption → empty workflow visualization). The source
    // def has 3 states + 2 transitions from the graph-save test above.
    const cloneRes = await request.get(
      `${API_BASE}/api/v1/workflow/definitions/${cloneId}?tenantId=${TENANT_ID}`,
      { headers: authHeaders(token) },
    );
    expect(cloneRes.status()).toBe(200);
    const cloneBody = await cloneRes.json();
    const cloneDef = cloneBody.data ?? cloneBody;
    const stateIds = new Set((cloneDef.states ?? []).map((s: { id: number }) => s.id));
    expect(cloneDef.transitions?.length).toBe(2);
    for (const t of cloneDef.transitions as { fromStateId: number; toStateId: number }[]) {
      expect(t.fromStateId).toBeGreaterThan(0);
      expect(t.toStateId).toBeGreaterThan(0);
      expect(stateIds.has(t.fromStateId)).toBe(true);
      expect(stateIds.has(t.toStateId)).toBe(true);
    }
  });

  // ── Deactivate ────────────────────────────────────────────────────────────────

  test('POST /workflow/definitions/{id}/deactivate — deactivate original', async ({ request }) => {
    test.skip(!definitionId, 'No definition created');
    const res = await request.post(
      `${API_BASE}/api/v1/workflow/definitions/${definitionId}/deactivate` +
      `?tenantId=${TENANT_ID}&modifiedBy=${ACTOR_ID}`,
      { headers: authHeaders(token) },
    );
    expect([200, 204]).toContain(res.status());
  });

  // ── Delete clone ──────────────────────────────────────────────────────────────

  test('DELETE /workflow/definitions/{id} — soft delete the clone', async ({ request }) => {
    test.skip(!cloneId, 'No clone created');
    const res = await request.delete(
      `${API_BASE}/api/v1/workflow/definitions/${cloneId}` +
      `?tenantId=${TENANT_ID}&deletedBy=${ACTOR_ID}`,
      { headers: authHeaders(token) },
    );
    // The clone is a Draft (deactivated original, inactive clone) → delete must succeed. A 400 here
    // is the Guid-vs-long PK regression (generic Find resolved by EF PK instead of PublicId).
    expect([200, 204]).toContain(res.status());
  });

  // ── List ──────────────────────────────────────────────────────────────────────

  test('GET /workflow/definitions — list with pagination, original still exists', async ({ request }) => {
    test.skip(!definitionId, 'No definition created');
    const res = await request.get(
      `${API_BASE}/api/v1/workflow/definitions?tenantId=${TENANT_ID}&page=1&pageSize=20`,
      { headers: authHeaders(token) },
    );
    expect(res.status()).toBe(200);
    const body = await res.json();
    const items: Array<{ id: string }> = body.data?.items ?? body.items ?? body.data ?? [];
    expect(Array.isArray(items)).toBeTruthy();
    expect(items.some((d) => d.id === definitionId)).toBeTruthy();
  });
});
