import { test, expect } from './fixtures/test-base';
import { getAccessToken, authHeaders, API_BASE } from './helpers/auth-helper';

// AbacRulesAdminController P1 TCs — TC-IDENT-057/058/059/060/061
// Route prefix: /api/v1/admin/abac-rules
// Auth: [Authorize(Policy = "Admin")]
test.describe('ABAC Rules admin (authenticated API)', () => {
  test.describe.configure({ mode: 'serial' });
  let token: string;
  let abacRuleId: string;

  test.beforeAll(async ({ request }) => {
    token = await getAccessToken(request);
    expect(token).toBeTruthy();
  });

  test.afterAll(async ({ request }) => {
    if (abacRuleId) {
      await request.delete(`${API_BASE}/api/v1/admin/abac-rules/${abacRuleId}`, {
        headers: authHeaders(token),
      });
    }
  });

  // TC-IDENT-057: list ABAC rules — authenticated admin
  test('TC-IDENT-057: GET /admin/abac-rules returns rule list', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/admin/abac-rules`, {
      headers: authHeaders(token),
    });
    expect(res.status()).toBe(200);
    const body = await res.json();
    const items = body.data?.items ?? body.data ?? body;
    expect(Array.isArray(items)).toBe(true);
  });

  // TC-IDENT-061: unauthenticated returns 401
  test('TC-IDENT-061: GET /admin/abac-rules without auth returns 401', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/admin/abac-rules`);
    expect(res.status()).toBe(401);
  });

  // TC-IDENT-058: create ABAC rule — valid payload returns 201
  test('TC-IDENT-058: POST /admin/abac-rules creates rule', async ({ request }) => {
    const res = await request.post(`${API_BASE}/api/v1/admin/abac-rules`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: {
        resource: 'forms',
        action: 'read',
        attributeKey: 'department',
        attributeValue: 'IT',
        effect: 'Allow',
      },
    });
    expect([201, 409]).toContain(res.status());
    if (res.status() === 201) {
      const body = await res.json();
      abacRuleId = body.data?.id ?? body.id;
    }
  });

  // TC-IDENT-059: update ABAC rule — valid payload returns 204
  test('TC-IDENT-059: PUT /admin/abac-rules/{id} updates rule', async ({ request }) => {
    test.skip(!abacRuleId, 'No ABAC rule created');
    const res = await request.put(`${API_BASE}/api/v1/admin/abac-rules/${abacRuleId}`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: {
        resource: 'forms',
        action: 'write',
        attributeKey: 'department',
        attributeValue: 'HR',
        effect: 'Deny',
      },
    });
    expect([204, 200]).toContain(res.status());
  });

  // TC-IDENT-060: delete ABAC rule — returns 204
  test('TC-IDENT-060: DELETE /admin/abac-rules/{id} deletes rule', async ({ request }) => {
    test.skip(!abacRuleId, 'No ABAC rule created');
    const res = await request.delete(`${API_BASE}/api/v1/admin/abac-rules/${abacRuleId}`, {
      headers: authHeaders(token),
    });
    expect([204, 200]).toContain(res.status());
    abacRuleId = ''; // cleanup done inline
  });
});

// PolicyRulesAdminController P1 TCs — TC-IDENT-062/064/066
// Route prefix: /api/v1/admin/policy-rules
// Auth: [Authorize(Policy = "Admin")]
test.describe('Policy Rules admin (authenticated API)', () => {
  test.describe.configure({ mode: 'serial' });
  let token: string;
  let policyRuleId: string;
  const ruleCode = `TC064-RULE-${Date.now()}`;

  test.beforeAll(async ({ request }) => {
    token = await getAccessToken(request);
    expect(token).toBeTruthy();
  });

  test.afterAll(async ({ request }) => {
    if (policyRuleId) {
      await request.delete(`${API_BASE}/api/v1/admin/policy-rules/${policyRuleId}`, {
        headers: authHeaders(token),
      });
    }
  });

  // TC-IDENT-062: list policy rules — authenticated admin
  test('TC-IDENT-062: GET /admin/policy-rules returns rule list', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/admin/policy-rules`, {
      headers: authHeaders(token),
    });
    expect(res.status()).toBe(200);
    const body = await res.json();
    const items = body.data?.items ?? body.data ?? body;
    expect(Array.isArray(items)).toBe(true);
  });

  // TC-IDENT-064: create policy rule — valid payload returns 201
  test('TC-IDENT-064: POST /admin/policy-rules creates rule', async ({ request }) => {
    const res = await request.post(`${API_BASE}/api/v1/admin/policy-rules`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: {
        code: ruleCode,
        permissionCode: 'forms.read',
        conditionExpression: "user.department == 'IT'",
        effect: 'Allow',
        priority: 100,
        logOnDeny: false,
        description: 'IT dept read access',
      },
    });
    expect([201, 409]).toContain(res.status());
    if (res.status() === 201) {
      const body = await res.json();
      policyRuleId = body.data?.id ?? body.id;
    }
  });

  // TC-IDENT-066: delete policy rule — returns 204
  test('TC-IDENT-066: DELETE /admin/policy-rules/{id} deletes rule', async ({ request }) => {
    test.skip(!policyRuleId, 'No policy rule created');
    const res = await request.delete(`${API_BASE}/api/v1/admin/policy-rules/${policyRuleId}`, {
      headers: authHeaders(token),
    });
    expect([204, 200]).toContain(res.status());
    policyRuleId = ''; // cleanup done inline
  });

  // Auth guard
  test('GET /admin/policy-rules without auth returns 401', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/admin/policy-rules`);
    expect(res.status()).toBe(401);
  });
});
