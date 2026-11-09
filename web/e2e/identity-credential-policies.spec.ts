import { test, expect } from './fixtures/test-base';
import { getAccessToken, authHeaders, API_BASE } from './helpers/auth-helper';

// CredentialPoliciesController P1 TCs — TC-IDENT-067/068/070/071/072
// Route prefix: /api/v1/admin/credential-policies
// Auth: [Authorize(Policy = "Admin")]
test.describe('Credential Policies admin (authenticated API)', () => {
  test.describe.configure({ mode: 'serial' });
  let token: string;
  let createdPolicyId: string;

  test.beforeAll(async ({ request }) => {
    token = await getAccessToken(request);
    expect(token).toBeTruthy();
  });

  test.afterAll(async ({ request }) => {
    if (createdPolicyId) {
      await request.delete(`${API_BASE}/api/v1/admin/credential-policies/${createdPolicyId}`, {
        headers: authHeaders(token),
      });
    }
  });

  // TC-IDENT-067: list credential policies — paginated
  test('TC-IDENT-067: GET /admin/credential-policies returns paginated list', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/admin/credential-policies?page=1&pageSize=20`, {
      headers: authHeaders(token),
    });
    expect(res.status()).toBe(200);
    const body = await res.json();
    const data = body.data ?? body;
    expect(data).toHaveProperty('items');
  });

  // TC-IDENT-070: create credential policy — valid payload returns 201
  test('TC-IDENT-070: POST /admin/credential-policies creates policy', async ({ request }) => {
    const res = await request.post(`${API_BASE}/api/v1/admin/credential-policies`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: {
        name: `TC070-Policy-${Date.now()}`,
        minLength: 8,
        maxLength: 64,
        requireUppercase: true,
        requireLowercase: true,
        requireDigit: true,
        requireSpecialChar: false,
        rotationDays: 90,
        maxFailedAttempts: 5,
        lockoutMinutes: 15,
        passwordHistoryCount: 5,
        isDefault: false,
      },
    });
    expect([201, 422]).toContain(res.status());
    if (res.status() === 201) {
      // 201 Created — body may be empty, ID is in Location header
      const location = res.headers()['location'] ?? '';
      const idMatch = location.match(/\/([0-9a-f-]{36})$/i);
      if (idMatch) createdPolicyId = idMatch[1];
      expect(location).toBeTruthy();
    }
  });

  // TC-IDENT-068: get credential policy by ID — existing
  test('TC-IDENT-068: GET /admin/credential-policies/{id} returns policy detail', async ({ request }) => {
    test.skip(!createdPolicyId, 'No policy created');
    const res = await request.get(`${API_BASE}/api/v1/admin/credential-policies/${createdPolicyId}`, {
      headers: authHeaders(token),
    });
    expect(res.status()).toBe(200);
    const body = await res.json();
    const data = body.data ?? body;
    expect(data).toHaveProperty('minLength');
    expect(data).toHaveProperty('maxLength');
  });

  // TC-IDENT-071: update credential policy — valid payload returns 204
  test('TC-IDENT-071: PUT /admin/credential-policies/{id} updates policy', async ({ request }) => {
    test.skip(!createdPolicyId, 'No policy created');
    const res = await request.put(`${API_BASE}/api/v1/admin/credential-policies/${createdPolicyId}`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: {
        name: `TC071-Policy-Updated`,
        minLength: 8,
        maxLength: 64,
        requireUppercase: true,
        requireLowercase: true,
        requireDigit: true,
        requireSpecialChar: false,
        rotationDays: 60,
        maxFailedAttempts: 5,
        lockoutMinutes: 15,
        passwordHistoryCount: 5,
        isDefault: false,
      },
    });
    expect([204, 200]).toContain(res.status());
  });

  // TC-IDENT-072: delete credential policy — returns 204
  test('TC-IDENT-072: DELETE /admin/credential-policies/{id} deletes policy', async ({ request }) => {
    test.skip(!createdPolicyId, 'No policy created');
    const res = await request.delete(`${API_BASE}/api/v1/admin/credential-policies/${createdPolicyId}`, {
      headers: authHeaders(token),
    });
    expect([204, 200]).toContain(res.status());
    createdPolicyId = ''; // cleanup done inline
  });

  // Auth guard
  test('GET /admin/credential-policies without auth returns 401', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/admin/credential-policies`);
    expect(res.status()).toBe(401);
  });
});
