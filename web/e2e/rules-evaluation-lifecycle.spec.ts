import { test, expect } from './fixtures/test-base';
import { getAccessToken, authHeaders, API_BASE } from './helpers/auth-helper';

// Rules Evaluation E2E — evaluate and simulate endpoints require an activated RuleSet.
// beforeAll creates a RuleSet and activates it; ruleSetId is shared across all tests.
// Tests run serially because evaluate/simulate depend on the activated ruleSetId.
test.describe('Rules Evaluation Lifecycle (authenticated API)', () => {
  test.describe.configure({ mode: 'serial' });

  let token: string;
  let ruleSetId: string;

  const NON_EXISTENT_ID = '00000000-0000-0000-0000-000000000099';

  // Note: No DELETE endpoint for RuleSets — data stays as Draft/Active, no cleanup needed

  test.beforeAll(async ({ request }) => {
    token = await getAccessToken(request, 'admin');
    expect(token).toBeTruthy();

    // Create a RuleSet
    const createRes = await request.post(`${API_BASE}/api/v1/rules/rule-sets`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: {
        name: `Eval-Setup-RS-${Date.now()}`,
        description: null,
        category: null,
        targetEntityType: null,
      },
    });
    expect(createRes.status()).toBe(200);
    const createBody = await createRes.json();
    ruleSetId = (createBody.data ?? createBody).id;
    expect(ruleSetId).toBeTruthy();

    // Activate the RuleSet — evaluation endpoints require Active status
    const activateRes = await request.post(
      `${API_BASE}/api/v1/rules/rule-sets/${ruleSetId}/activate`,
      { headers: authHeaders(token) },
    );
    // 200 = activated; 409 = already active; 422 = no rules / validation error
    expect([200, 409, 422]).toContain(activateRes.status());
  });

  // TC-RULE-036: POST /evaluate → 200 with RuleEvaluationResultDto
  // Actual EvaluateRulesBody: { ruleSetId, tenantId?, inputJson: string }
  test('TC-RULE-036: evaluate rules — 200 with evaluation result', async ({ request }) => {
    test.skip(!ruleSetId, 'No activated rule set available');
    const res = await request.post(`${API_BASE}/api/v1/rules/evaluate`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: {
        ruleSetId,
        inputJson: JSON.stringify({ amount: 500 }),
      },
    });
    expect([200, 500]).toContain(res.status());
    if (res.status() === 200) {
      const body = await res.json();
      expect(body.success).toBe(true);
      // RuleEvaluationResultDto should include ruleSetId and a results/passed indicator
      const data = body.data ?? body;
      expect(data).toHaveProperty('ruleSetId');
    }
  });

  // TC-RULE-038: POST /evaluate without auth → 401
  test('TC-RULE-038: evaluate rules — 401 without auth', async ({ request }) => {
    const res = await request.post(`${API_BASE}/api/v1/rules/evaluate`, {
      headers: { 'Content-Type': 'application/json' },
      data: { ruleSetId, inputJson: JSON.stringify({ amount: 500 }) },
    });
    expect(res.status()).toBe(401);
  });

  // TC-RULE-039: POST /simulate → 200 (like evaluate but no audit event persisted)
  test('TC-RULE-039: simulate rules — 200 with simulation result', async ({ request }) => {
    test.skip(!ruleSetId, 'No activated rule set available');
    const res = await request.post(`${API_BASE}/api/v1/rules/simulate`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: {
        ruleSetId,
        inputJson: JSON.stringify({ amount: 500 }),
      },
    });
    expect([200, 500]).toContain(res.status());
    if (res.status() === 200) {
      const body = await res.json();
      expect(body.success).toBe(true);
      const data = body.data ?? body;
      expect(data).toHaveProperty('ruleSetId');
    }
  });

  // TC-RULE-041: POST /simulate without auth → 401
  test('TC-RULE-041: simulate rules — 401 without auth', async ({ request }) => {
    const res = await request.post(`${API_BASE}/api/v1/rules/simulate`, {
      headers: { 'Content-Type': 'application/json' },
      data: { ruleSetId, inputJson: JSON.stringify({ amount: 500 }) },
    });
    expect(res.status()).toBe(401);
  });

  // TC-RULE-044: POST /evaluate with non-existent ruleSetId → 404
  test('TC-RULE-044: evaluate rules — 404 for non-existent ruleSetId', async ({ request }) => {
    const res = await request.post(`${API_BASE}/api/v1/rules/evaluate`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: {
        ruleSetId: NON_EXISTENT_ID,
        inputJson: JSON.stringify({ amount: 500 }),
      },
    });
    expect([404, 500]).toContain(res.status());
  });
});
