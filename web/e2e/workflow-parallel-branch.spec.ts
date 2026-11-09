import { test, expect } from './fixtures/test-base';
import { getAccessToken, authHeaders, API_BASE } from './helpers/auth-helper';

/**
 * Workflow Parallel-Branch E2E (API-contract level, through the live stack).
 *
 * Proves, at the REST layer, the parallel-branch author→save→reload→render loop, the clone
 * deep-copy contract, and the fork-diamond drag-persist contract. Real React-Flow drag is
 * eyeballed manually (the canvas gesture); here we lock the persistence + deep-copy contracts
 * that underpin it, as a durable non-flaky regression guard.
 *
 * Self-contained: authors its OWN throwaway Draft definition (mirroring the dev seeder's
 * known-valid AND-fork shape) and deletes it at the end, so it never mutates the seeded
 * demo-parallel-process (whose IsLatest flag a clone would otherwise flip).
 *
 * Graph (canonical "Generic" vocab — non-canonical names are rejected at save):
 *   Draft → Submitted [fork] ⇒ {UnderReview, Assigned} ⇒ Returned [AND join] → Closed [final]
 */
test.describe.serial('Workflow Parallel Branch (authenticated API) @smoke', () => {
  let token: string;
  let definitionId: string;
  let cloneId: string;

  const TENANT_ID = '00000000-0000-0000-0000-000000000001';
  const ACTOR_ID = '00000000-0000-0000-0000-000000000001';
  const POOL_PERM = 'WORKFLOW.TASK.PROCESS';

  // One Union/ByPermission clause reused by both transitions and both branch children —
  // ByPermission pools pass the save-time RB5 guard (which only rejects ByGroup/OrgUnit/etc.).
  const poolAssignment = {
    combineMode: 'Union',
    allowAssignorOverride: false,
    clauses: [{ mode: 'ByPermission', refsJson: `["${POOL_PERM}"]` }],
  };

  // Name-keyed graph builder. Only the single draggable fork-diamond position is parameterized,
  // so author + drag-persist reuse one shape. State names are stable across a clone, so the same
  // name-keyed payload re-applies to the clone with no id remapping (mirrors what the FE save does).
  const buildGraph = (forkX: number | null, forkY: number | null) => ({
    tenantId: TENANT_ID,
    modifiedBy: ACTOR_ID,
    states: [
      { name: 'Draft', displayNameVi: 'Nháp', displayNameEn: 'Draft', isInitial: true, isFinal: false, color: '#9E9E9E', sortOrder: 1, positionX: 80, positionY: 200 },
      { name: 'Submitted', displayNameVi: 'Đã nộp', displayNameEn: 'Submitted', isInitial: false, isFinal: false, color: '#FAAD14', sortOrder: 2, positionX: 300, positionY: 200 },
      { name: 'UnderReview', displayNameVi: 'Thẩm định A', displayNameEn: 'Review A', isInitial: false, isFinal: false, color: '#2196F3', sortOrder: 3, positionX: 540, positionY: 90 },
      { name: 'Assigned', displayNameVi: 'Thẩm định B', displayNameEn: 'Review B', isInitial: false, isFinal: false, color: '#2196F3', sortOrder: 4, positionX: 540, positionY: 310 },
      { name: 'Returned', displayNameVi: 'Tổng hợp', displayNameEn: 'Synthesis', isInitial: false, isFinal: false, color: '#1890FF', sortOrder: 5, positionX: 780, positionY: 200 },
      { name: 'Closed', displayNameVi: 'Đóng', displayNameEn: 'Closed', isInitial: false, isFinal: true, color: '#52C41A', sortOrder: 6, positionX: 1000, positionY: 200 },
    ],
    transitions: [
      { fromStateName: 'Draft', toStateName: 'Submitted', actionName: 'Submit', actionLabelVi: 'Nộp', actionLabelEn: 'Submit', conditionsJson: null, sortOrder: 1, assignment: poolAssignment },
      // Reachability edge for the continuation state; the join reaches Returned internally.
      { fromStateName: 'Submitted', toStateName: 'Returned', actionName: 'Continue', actionLabelVi: 'Tiếp tục', actionLabelEn: 'Continue', conditionsJson: null, sortOrder: 2, assignment: poolAssignment },
      { fromStateName: 'Returned', toStateName: 'Closed', actionName: 'Close', actionLabelVi: 'Đóng', actionLabelEn: 'Close', conditionsJson: null, sortOrder: 3, assignment: poolAssignment },
    ],
    branches: [
      {
        branchingStateName: 'Submitted',
        continuationStateName: 'Returned',
        branchType: 'And',
        allowCancelOnAnyReject: false,
        timeoutMinutes: 0,
        children: [
          { targetStateName: 'UnderReview', name: 'review-a', displayNameVi: 'Thẩm định A', sortOrder: 1, resolutions: [{ resolutionType: 'Rejected', resultingStateName: 'Closed' }], assignment: poolAssignment },
          { targetStateName: 'Assigned', name: 'review-b', displayNameVi: 'Thẩm định B', sortOrder: 2, resolutions: [], assignment: poolAssignment },
        ],
        positionX: forkX,
        positionY: forkY,
      },
    ],
  });

  type Assignment = { clauses?: Array<{ refsJson?: string | null }> };
  type BranchChild = { name: string; assignment?: Assignment | null; resolutions?: Array<{ resolutionType: string }> };
  type Branch = {
    branchType: string;
    branchingStateId: number;
    continuationStateId: number;
    positionX: number | null;
    positionY: number | null;
    children?: BranchChild[];
  };
  type Graph = {
    states?: Array<{ id: number }>;
    transitions?: Array<{ assignment?: unknown }>;
    branches?: Branch[];
  };

  const getGraph = async (request: import('@playwright/test').APIRequestContext, id: string): Promise<Graph> => {
    const res = await request.get(
      `${API_BASE}/api/v1/workflow/definitions/${id}?tenantId=${TENANT_ID}`,
      { headers: authHeaders(token) },
    );
    expect(res.status()).toBe(200);
    const body = await res.json();
    return body.data ?? body;
  };

  const putGraph = async (request: import('@playwright/test').APIRequestContext, id: string, forkX: number | null, forkY: number | null) => {
    const res = await request.put(
      `${API_BASE}/api/v1/workflow/definitions/${id}/graph`,
      { headers: { ...authHeaders(token), 'Content-Type': 'application/json' }, data: buildGraph(forkX, forkY) },
    );
    expect([200, 204]).toContain(res.status());
  };

  test.beforeAll(async ({ request }) => {
    token = await getAccessToken(request);
    expect(token).toBeTruthy();
  });

  // ── Author ────────────────────────────────────────────────────────────────────

  test('POST /workflow/definitions — create the parallel definition shell', async ({ request }) => {
    const res = await request.post(`${API_BASE}/api/v1/workflow/definitions`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: {
        name: `E2E Parallel ${Date.now()}`,
        description: 'Parallel-branch authoring + drag-persist E2E',
        tenantId: TENANT_ID,
        createdBy: ACTOR_ID,
        entityType: 'Generic',
        states: [
          { name: 'Draft', displayNameVi: 'Nháp', displayNameEn: 'Draft', isInitial: true, isFinal: false, color: '#9E9E9E', sortOrder: 0 },
        ],
        transitions: [],
      },
    });
    expect([200, 201]).toContain(res.status());
    const body = await res.json();
    definitionId = body.data?.id ?? body.id;
    expect(definitionId).toBeTruthy();
  });

  test('PUT /graph — save AND-fork; reload renders fork + pooled children', async ({ request }) => {
    test.skip(!definitionId, 'No definition created');
    await putGraph(request, definitionId, 410, 200);

    const def = await getGraph(request, definitionId);
    expect(def.branches?.length).toBe(1);
    const branch = def.branches![0];
    expect(branch.branchType).toBe('And');
    expect(branch.children?.length).toBe(2);
    for (const c of branch.children!) expect(c.assignment).toBeTruthy(); // per-child pools persisted
    expect((def.transitions ?? []).filter((t) => t.assignment).length).toBe(3); // transition pools persisted
    expect(branch.positionX).toBe(410);
    expect(branch.positionY).toBe(200);
    const reviewA = branch.children!.find((c) => c.name === 'review-a');
    expect(reviewA?.resolutions?.length).toBe(1);
    expect(reviewA?.resolutions?.[0].resolutionType).toBe('Rejected');
  });

  // ── Clone (deep-copy regression guard) ──────────────────────────────────────────

  test('POST /clone — deep-copies fork, child pools, transition pools, and resolutions', async ({ request }) => {
    test.skip(!definitionId, 'No definition created');
    const res = await request.post(
      `${API_BASE}/api/v1/workflow/definitions/${definitionId}/clone?tenantId=${TENANT_ID}&createdBy=${ACTOR_ID}`,
      { headers: authHeaders(token) },
    );
    expect([200, 201]).toContain(res.status());
    const body = await res.json();
    cloneId = body.data?.id ?? body.id;
    expect(cloneId).toBeTruthy();

    // Before the deep-copy fix, the clone copied only the definition aggregate (states + transitions)
    // and silently dropped the two satellite families — the parallel branch and every transition/child
    // pool — yielding an un-saveable, un-activatable clone. Assert all four families survive.
    const clone = await getGraph(request, cloneId);
    expect(clone.branches?.length).toBe(1); // fork survived
    const branch = clone.branches![0];
    expect(branch.children?.length).toBe(2);
    for (const c of branch.children!) expect(c.assignment).toBeTruthy(); // child pools survived
    expect((clone.transitions ?? []).filter((t) => t.assignment).length).toBe(3); // transition pools survived
    // Assert the deep-copy preserved pool CONTENT, not just empty assignment rows: a surviving
    // ByPermission clause ref and the resolution's type+target must come through the clone intact.
    expect(branch.children![0].assignment?.clauses?.[0]?.refsJson).toContain(POOL_PERM);
    const reviewA = branch.children!.find((c) => c.name === 'review-a');
    expect(reviewA?.resolutions?.length).toBe(1); // resolution survived
    expect(reviewA?.resolutions?.[0].resolutionType).toBe('Rejected');

    // Branch state refs resolve to the clone's OWN freshly-minted state ids, not the source's.
    const cloneStateIds = new Set((clone.states ?? []).map((s) => s.id));
    expect(cloneStateIds.has(branch.branchingStateId)).toBe(true);
    expect(cloneStateIds.has(branch.continuationStateId)).toBe(true);
  });

  // ── Drag-persist ────────────────────────────────────────────────────────────────

  test('PUT /graph — drag fork diamond → save → reload → position persists', async ({ request }) => {
    test.skip(!cloneId, 'No clone created');
    await putGraph(request, cloneId, 999.5, 444.25);
    const def = await getGraph(request, cloneId);
    expect(def.branches?.[0].positionX).toBe(999.5);
    expect(def.branches?.[0].positionY).toBe(444.25);
  });

  test('PUT /graph — null fork position persists (canvas auto-derives on reload)', async ({ request }) => {
    test.skip(!cloneId, 'No clone created');
    await putGraph(request, cloneId, null, null);
    const def = await getGraph(request, cloneId);
    expect(def.branches?.[0].positionX == null).toBe(true);
    expect(def.branches?.[0].positionY == null).toBe(true);
  });

  // ── Cleanup ─────────────────────────────────────────────────────────────────────
  // afterAll, NOT a trailing test: in serial mode a mid-suite failure skips every remaining test,
  // so a test-shaped cleanup silently leaks the authored definitions into the dev DB. An afterAll
  // hook runs even when the suite fails partway, deleting whatever was actually created.

  test.afterAll(async ({ request }) => {
    // Collect-then-assert: a failed first delete must not strand the second definition —
    // attempt every cleanup, then fail loudly if any returned an unexpected status.
    const failures: string[] = [];
    for (const id of [cloneId, definitionId]) {
      if (!id) continue;
      const res = await request.delete(
        `${API_BASE}/api/v1/workflow/definitions/${id}?tenantId=${TENANT_ID}&deletedBy=${ACTOR_ID}`,
        { headers: authHeaders(token) },
      );
      // Both stay Draft → delete must succeed. A 400 here is the Guid-vs-long PK regression
      // (the generic Find-by-PublicId bug) — assert success, don't tolerate it.
      if (![200, 204].includes(res.status())) failures.push(`${id} → ${res.status()}`);
    }
    expect(failures).toEqual([]);
  });
});
