import { test, expect } from './fixtures/test-base';
import { getAccessToken, authHeaders, API_BASE } from './helpers/auth-helper';

// Rules Decision Tables E2E — requires an activated RuleSet as foreign key.
// beforeAll creates a RuleSet and activates it.
// Note: No DELETE endpoint for DecisionTables or RuleSets — data stays, no cleanup needed.
// Tests run serially: create decision table → read by id share decisionTableId.
test.describe('Rules Decision Tables (authenticated API)', () => {
  test.describe.configure({ mode: 'serial' });

  let token: string;
  let ruleSetId: string;
  let decisionTableId: string;

  const NON_EXISTENT_ID = '00000000-0000-0000-0000-000000000099';

  test.beforeAll(async ({ request }) => {
    token = await getAccessToken(request, 'admin');
    expect(token).toBeTruthy();

    // Create a RuleSet to use as FK for decision table tests
    const createRes = await request.post(`${API_BASE}/api/v1/rules/rule-sets`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: {
        name: `DT-Setup-RS-${Date.now()}`,
        description: null,
        category: null,
        targetEntityType: null,
      },
    });
    expect(createRes.status()).toBe(200);
    const createBody = await createRes.json();
    ruleSetId = (createBody.data ?? createBody).id;
    expect(ruleSetId).toBeTruthy();

    // Activate the RuleSet so decision tables can reference it
    const activateRes = await request.post(
      `${API_BASE}/api/v1/rules/rule-sets/${ruleSetId}/activate`,
      { headers: authHeaders(token) },
    );
    // Accept 200 (activated), 409 (already active), 422 (no rules / validation)
    expect([200, 409, 422]).toContain(activateRes.status());
  });

  // TC-RULE-025: POST create decision table → 200
  // Actual CreateDecisionTableBody: InputColumns/OutputColumns/Rows are JSON strings, not arrays
  test('TC-RULE-025: create decision table — 200 with inputColumns and outputColumns', async ({ request }) => {
    test.skip(!ruleSetId, 'No rule set available');
    const res = await request.post(`${API_BASE}/api/v1/decision-tables`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: {
        ruleSetId,
        name: `DT-${Date.now()}`,
        inputColumns: JSON.stringify([{ name: 'amount', dataType: 'decimal' }]),
        outputColumns: JSON.stringify([{ name: 'approved', dataType: 'boolean' }]),
        rows: JSON.stringify([]),
      },
    });
    expect(res.status()).toBe(200);
    const body = await res.json();
    expect(body.success).toBe(true);
    const data = body.data ?? body;
    expect(data.id).toBeTruthy();
    expect(data.id).toMatch(/^[0-9a-f-]{36}$/i);
    expect(data.ruleSetId).toBe(ruleSetId);
    decisionTableId = data.id;
  });

  // TC-RULE-030: GET /{id} → 200 with full detail
  test('TC-RULE-030: get decision table by id — 200 with full detail', async ({ request }) => {
    test.skip(!decisionTableId, 'No decision table created');
    const res = await request.get(`${API_BASE}/api/v1/decision-tables/${decisionTableId}`, {
      headers: authHeaders(token),
    });
    expect(res.status()).toBe(200);
    const body = await res.json();
    const data = body.data ?? body;
    expect(data).toHaveProperty('id', decisionTableId);
    expect(data).toHaveProperty('ruleSetId');
    expect(data).toHaveProperty('name');
    // inputColumns and outputColumns should be present
    expect(data).toHaveProperty('inputColumns');
    expect(data).toHaveProperty('outputColumns');
  });

  // TC-RULE-031: GET /{id} non-existent → 404
  test('TC-RULE-031: get decision table by id — 404 for non-existent id', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/decision-tables/${NON_EXISTENT_ID}`, {
      headers: authHeaders(token),
    });
    expect(res.status()).toBe(404);
  });

  // TC-RULE-032: GET /{id} without auth → 401
  test('TC-RULE-032: get decision table by id — 401 without auth', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/decision-tables/${NON_EXISTENT_ID}`);
    expect(res.status()).toBe(401);
  });

  // TC-RULE-027: POST create missing name → 422
  test('TC-RULE-027: create decision table — 422 when name is missing', async ({ request }) => {
    test.skip(!ruleSetId, 'No rule set available');
    const res = await request.post(`${API_BASE}/api/v1/decision-tables`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: {
        ruleSetId,
        name: '',
        inputColumns: JSON.stringify([{ name: 'amount', dataType: 'decimal' }]),
        outputColumns: JSON.stringify([{ name: 'approved', dataType: 'boolean' }]),
        rows: JSON.stringify([]),
      },
    });
    expect([400, 422]).toContain(res.status());
  });

  // TC-RULE-028: POST create missing ruleSetId → 422
  test('TC-RULE-028: create decision table — 422 when ruleSetId is missing', async ({ request }) => {
    const res = await request.post(`${API_BASE}/api/v1/decision-tables`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: {
        ruleSetId: '',
        name: `DT-NORULESET-${Date.now()}`,
        inputColumns: JSON.stringify([]),
        outputColumns: JSON.stringify([]),
        rows: JSON.stringify([]),
      },
    });
    expect([400, 422]).toContain(res.status());
  });

  // TC-RULE-029: POST create without auth → 401
  test('TC-RULE-029: create decision table — 401 without auth', async ({ request }) => {
    const res = await request.post(`${API_BASE}/api/v1/decision-tables`, {
      headers: { 'Content-Type': 'application/json' },
      data: {
        ruleSetId,
        name: 'NoAuth DT',
        inputColumns: JSON.stringify([]),
        outputColumns: JSON.stringify([]),
        rows: JSON.stringify([]),
      },
    });
    expect(res.status()).toBe(401);
  });
});
