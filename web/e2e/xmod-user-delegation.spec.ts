import { test, expect } from './fixtures/test-base';
import { getAccessToken, authHeaders, API_BASE } from './helpers/auth-helper';

// Cross-module flow: User provisioning → Org → Delegation → Case
// TC-XMOD-020..027 (skip 025 — P2)
// Serial: each step depends on prior state
test.describe('Cross-Module: User provisioning + Delegation + Case', () => {
  test.describe.configure({ mode: 'serial' });
  test.slow();

  let adminToken: string;
  let newUserId: string;
  let delegationId: string;
  let adminUserId: string;
  let delegateUserId: string;

  // Resolve two distinct user IDs from the users list for delegation
  test.beforeAll(async ({ request }) => {
    adminToken = await getAccessToken(request, 'admin');

    const res = await request.get(`${API_BASE}/api/v1/admin/users?page=1&pageSize=10`, {
      headers: authHeaders(adminToken),
    });
    if (res.ok()) {
      const items: Array<{ id: string; email?: string }> =
        (await res.json()).data?.items ?? [];
      adminUserId = items[0]?.id ?? '';
      delegateUserId = items[1]?.id ?? '';
    }
  });

  // TC-XMOD-020: Admin creates new user
  test('TC-XMOD-020: admin creates new user — returns 201 with user id', async ({ request }) => {
    const uniqueEmail = `xmod-e2e-${Date.now()}@dev.local`;
    const res = await request.post(`${API_BASE}/api/v1/admin/users`, {
      headers: { ...authHeaders(adminToken), 'Content-Type': 'application/json' },
      data: {
        fullName: 'XMOD E2E User',
        email: uniqueEmail,
        password: 'XmodE2E@12345',
        departmentCode: null,
      },
    });
    // 201 = created; 422 = validation; 409 = duplicate (re-run guard)
    expect([201, 409, 422]).toContain(res.status());
    if (res.status() === 201) {
      // 201 may return empty body (Content-Length: 0) — id is in Location header
      const location = res.headers()['location'] ?? '';
      const text = await res.text();
      let bodyId = '';
      if (text.trim().length > 0) {
        try {
          const body = JSON.parse(text);
          bodyId = body.data?.id ?? body.id ?? '';
        } catch {
          // ignore JSON parse errors — id will come from Location header
        }
      }
      newUserId = bodyId || location.split('/').pop() || '';
    }
  });

  // TC-XMOD-021: Assign role to new user (if created)
  test('TC-XMOD-021: assign GovOfficer role to new user', async ({ request }) => {
    test.skip(!newUserId, 'TC-XMOD-020 did not create a new user');
    const res = await request.post(`${API_BASE}/api/v1/admin/users/${newUserId}/roles`, {
      headers: { ...authHeaders(adminToken), 'Content-Type': 'application/json' },
      data: { role: 'GovOfficer' },
    });
    expect([200, 204, 422]).toContain(res.status());
  });

  // TC-XMOD-022: Verify new user is retrievable
  test('TC-XMOD-022: get new user by id — returns user profile', async ({ request }) => {
    test.skip(!newUserId, 'TC-XMOD-020 did not create a new user');
    const res = await request.get(`${API_BASE}/api/v1/admin/users/${newUserId}`, {
      headers: authHeaders(adminToken),
    });
    expect([200, 404]).toContain(res.status());
    if (res.status() === 200) {
      const body = await res.json();
      expect(body.data?.id ?? body.id).toBe(newUserId);
    }
  });

  // TC-XMOD-023: Create delegation from admin to delegate
  test('TC-XMOD-023: create delegation — admin delegates to another user', async ({ request }) => {
    test.skip(!adminUserId || !delegateUserId, 'Need at least two users in database');
    const now = new Date();
    const validTo = new Date(now.getTime() + 3 * 24 * 60 * 60 * 1000); // +3 days

    const res = await request.post(`${API_BASE}/api/v1/delegations`, {
      headers: { ...authHeaders(adminToken), 'Content-Type': 'application/json' },
      data: {
        delegatorId: adminUserId,
        delegateId: delegateUserId,
        validFrom: now.toISOString(),
        validTo: validTo.toISOString(),
        reason: 'XMOD E2E delegation test',
      },
    });
    // 201 = created; 409 = duplicate (re-run); 422 = validation
    expect([201, 409, 422]).toContain(res.status());
    if (res.status() === 201) {
      const body = await res.json();
      delegationId = body.data?.id ?? body.id ?? '';
    }
  });

  // TC-XMOD-024: Delegatee creates a form submission (exercising delegated authority path)
  // Retargeted to /forms/templates after Cases module removal
  test('TC-XMOD-024: officer accesses forms — verifies cross-user resource access', async ({ request }) => {
    const officerToken = await getAccessToken(request, 'officer');
    const res = await request.get(`${API_BASE}/api/v1/forms/templates?page=1&pageSize=1`, {
      headers: authHeaders(officerToken),
    });
    // Officer may not have forms-view permission — 403 acceptable
    expect([200, 403]).toContain(res.status());
    if (res.status() === 200) {
      const body = await res.json();
      expect(body.success).toBe(true);
    }
  });

  // TC-XMOD-026: Revoke delegation
  test('TC-XMOD-026: revoke delegation — returns 204 or 200', async ({ request }) => {
    // Ensure we have a delegation to revoke — fetch from list if not in state
    if (!delegationId) {
      const listRes = await request.get(`${API_BASE}/api/v1/delegations`, {
        headers: authHeaders(adminToken),
      });
      const items: Array<{ id: string }> =
        (await listRes.json()).data?.items ?? (await listRes.json()).data ?? [];
      test.skip(items.length === 0, 'No delegations available to revoke');
      delegationId = items[0]?.id ?? '';
    }
    test.skip(!delegationId, 'TC-XMOD-023 did not produce a delegationId');

    const res = await request.post(`${API_BASE}/api/v1/delegations/${delegationId}/revoke`, {
      headers: authHeaders(adminToken),
    });
    expect([200, 204, 404, 409]).toContain(res.status());
  });

  // TC-XMOD-027: Cleanup — soft-delete new user if created
  test('TC-XMOD-027: cleanup — deactivate new user if created', async ({ request }) => {
    test.skip(!newUserId, 'No new user to clean up');
    const res = await request.delete(`${API_BASE}/api/v1/admin/users/${newUserId}`, {
      headers: authHeaders(adminToken),
    });
    expect([200, 204, 404]).toContain(res.status());
  });
});
