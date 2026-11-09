import { test, expect } from './fixtures/test-base';
import { getAccessToken, authHeaders, API_BASE } from './helpers/auth-helper';

// Rules — RuleSets CRUD + activate lifecycle.
// Serial mode: create → read → update rule → activate → cleanup share ruleSetId.
test.describe('Rules RuleSets CRUD (authenticated API)', () => {
  test.describe.configure({ mode: 'serial' });

  let token: string;
  let ruleSetId: string;
  let firstRuleId: string; // populated after GET detail reveals rules array

  const NON_EXISTENT_ID = '00000000-0000-0000-0000-000000000099';

  test.beforeAll(async ({ request }) => {
    token = await getAccessToken(request, 'admin');
    expect(token).toBeTruthy();
  });

  // Note: No DELETE endpoint for RuleSets — data stays as Draft, no cleanup needed

  // TC-RULE-001: GET list → 200 paginated with id/name/version/status per item
  test('TC-RULE-001: list rule sets — 200 paginated envelope', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/rules/rule-sets`, {
      headers: authHeaders(token),
    });
    expect(res.status()).toBe(200);
    const body = await res.json();
    expect(body.success).toBe(true);
    expect(body.data).toHaveProperty('items');
    expect(Array.isArray(body.data.items)).toBeTruthy();
  });

  // TC-RULE-005: GET list without auth → 401
  test('TC-RULE-005: list rule sets — 401 without auth', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/rules/rule-sets`);
    expect(res.status()).toBe(401);
  });

  // TC-RULE-006: POST create minimal body → 200 (name only, status=Draft, version=1)
  test('TC-RULE-006: create rule set — 200 with minimal body, status Draft', async ({ request }) => {
    const res = await request.post(`${API_BASE}/api/v1/rules/rule-sets`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: {
        name: `TC-RULE-E2E-${Date.now()}`,
        description: null,
        category: null,
        targetEntityType: null,
      },
    });
    expect(res.status()).toBe(200);
    const body = await res.json();
    expect(body.success).toBe(true);
    const data = body.data ?? body;
    expect(data.id).toBeTruthy();
    expect(data.id).toMatch(/^[0-9a-f-]{36}$/i);
    expect(data.name).toBeTruthy();
    // status should be Draft (0 or string)
    const status = data.status;
    expect(status === 'Draft' || status === 0).toBeTruthy();
    ruleSetId = data.id;
  });

  // TC-RULE-008: POST create empty name → 422
  test('TC-RULE-008: create rule set — 422 when name is empty', async ({ request }) => {
    const res = await request.post(`${API_BASE}/api/v1/rules/rule-sets`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: { name: '', description: null, category: null, targetEntityType: null },
    });
    expect([400, 422]).toContain(res.status());
  });

  // TC-RULE-010: POST create without auth → 401
  test('TC-RULE-010: create rule set — 401 without auth', async ({ request }) => {
    const res = await request.post(`${API_BASE}/api/v1/rules/rule-sets`, {
      headers: { 'Content-Type': 'application/json' },
      data: { name: 'Unauthorized', description: null, category: null, targetEntityType: null },
    });
    expect(res.status()).toBe(401);
  });

  // TC-RULE-011: GET /{id} → 200 full detail with rules array
  test('TC-RULE-011: get rule set by id — 200 with rules array', async ({ request }) => {
    test.skip(!ruleSetId, 'No rule set created');
    const res = await request.get(`${API_BASE}/api/v1/rules/rule-sets/${ruleSetId}`, {
      headers: authHeaders(token),
    });
    expect(res.status()).toBe(200);
    const body = await res.json();
    const data = body.data ?? body;
    expect(data).toHaveProperty('id', ruleSetId);
    expect(data).toHaveProperty('name');
    // Capture first rule ID if rules array is present
    const rules: Array<{ id: string }> = data.rules ?? [];
    if (rules.length > 0) {
      firstRuleId = rules[0].id;
    }
  });

  // TC-RULE-012: GET /{id} non-existent → 404
  test('TC-RULE-012: get rule set by id — 404 for non-existent id', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/rules/rule-sets/${NON_EXISTENT_ID}`, {
      headers: authHeaders(token),
    });
    expect(res.status()).toBe(404);
  });

  // TC-RULE-013: GET /{id} without auth → 401
  test('TC-RULE-013: get rule set by id — 401 without auth', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/rules/rule-sets/${NON_EXISTENT_ID}`);
    expect(res.status()).toBe(401);
  });

  // TC-RULE-014: PUT /{id}/rules/{ruleId} — update rule name/expression → 200
  test('TC-RULE-014: update rule in rule set — 200 success', async ({ request }) => {
    test.skip(!ruleSetId || !firstRuleId, 'No rule set or rule available for update');
    const res = await request.put(
      `${API_BASE}/api/v1/rules/rule-sets/${ruleSetId}/rules/${firstRuleId}`,
      {
        headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
        data: {
          name: 'E2E Updated Rule',
          expression: 'amount > 100',
          description: 'Updated by E2E test',
        },
      },
    );
    expect(res.status()).toBe(200);
  });

  // TC-RULE-016: PUT update rule — 404 for non-existent RuleSet
  test('TC-RULE-016: update rule — 404 for non-existent rule set id', async ({ request }) => {
    const res = await request.put(
      `${API_BASE}/api/v1/rules/rule-sets/${NON_EXISTENT_ID}/rules/${NON_EXISTENT_ID}`,
      {
        headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
        data: { name: 'Ghost', expression: 'true', description: null },
      },
    );
    expect(res.status()).toBe(404);
  });

  // TC-RULE-019: PUT update rule without auth → 401
  test('TC-RULE-019: update rule — 401 without auth', async ({ request }) => {
    const res = await request.put(
      `${API_BASE}/api/v1/rules/rule-sets/${NON_EXISTENT_ID}/rules/${NON_EXISTENT_ID}`,
      {
        headers: { 'Content-Type': 'application/json' },
        data: { name: 'NoAuth', expression: 'true', description: null },
      },
    );
    expect(res.status()).toBe(401);
  });

  // TC-RULE-020: POST /{id}/activate → 200 or 409/422 (already active / no rules)
  test('TC-RULE-020: activate rule set — returns Active status or 409/422', async ({ request }) => {
    test.skip(!ruleSetId, 'No rule set created');
    const res = await request.post(
      `${API_BASE}/api/v1/rules/rule-sets/${ruleSetId}/activate`,
      { headers: authHeaders(token) },
    );
    // 409 = already Active, 422 = validation error (no rules or deprecated)
    expect([200, 409, 422]).toContain(res.status());
    if (res.status() === 200) {
      const body = await res.json();
      const data = body.data ?? body;
      const status = data.status;
      expect(status === 'Active' || status === 1).toBeTruthy();
    }
  });

  // TC-RULE-023: POST activate non-existent → 404
  test('TC-RULE-023: activate rule set — 404 for non-existent id', async ({ request }) => {
    const res = await request.post(
      `${API_BASE}/api/v1/rules/rule-sets/${NON_EXISTENT_ID}/activate`,
      { headers: authHeaders(token) },
    );
    expect(res.status()).toBe(404);
  });

  // TC-RULE-024: POST activate without auth → 401
  test('TC-RULE-024: activate rule set — 401 without auth', async ({ request }) => {
    const res = await request.post(
      `${API_BASE}/api/v1/rules/rule-sets/${NON_EXISTENT_ID}/activate`,
    );
    expect(res.status()).toBe(401);
  });
});
