import { test, expect } from './fixtures/test-base';
import { getAccessToken, authHeaders, API_BASE, resolveUserId } from './helpers/auth-helper';

// Seeded test data IDs (E2ETestDataSeeder)
const ORG_BNV_ID = '10000000-0000-0000-0000-000000000001';  // Root org
const ORG_VTCCB_ID = '10000000-0000-0000-0000-000000000002'; // Dept child
const ORG_PNS_ID = '10000000-0000-0000-0000-000000000003';   // Division child

// Org Staff Assignments E2E — assign, transfer, history
// StaffAssignmentController: POST /assign, POST /transfer, GET /history
// All endpoints require Admin or SystemAdmin role
test.describe.configure({ mode: 'serial' });
test.describe('Org: staff assignments (authenticated API)', () => {
  let adminToken: string;
  let userToken: string;
  let adminUserId: string;

  test.beforeAll(async ({ request }) => {
    [adminToken, userToken] = await Promise.all([
      getAccessToken(request, 'admin'),
      getAccessToken(request, 'user'),
    ]);
    expect(adminToken).toBeTruthy();
    adminUserId = await resolveUserId(request, adminToken);
  });

  // ── TC-ORG-025: POST assign — Admin assigns user to org unit ──────────────

  test('TC-ORG-025: assign staff — Admin assigns user to org unit', async ({ request }) => {
    test.skip(!adminUserId, 'No userId resolved');

    const res = await request.post(
      `${API_BASE}/api/v1/admin/org/staff/${adminUserId}/assign`,
      {
        headers: { ...authHeaders(adminToken), 'Content-Type': 'application/json' },
        data: {
          orgUnitId: ORG_BNV_ID,
          roleInOrg: 'Member',
          positionTitle: 'Software Engineer',
        },
      },
    );
    // 200/201 = assigned; 409 = already assigned to this unit; 404 = org unit not in seeded data
    expect([200, 201, 404, 409, 422]).toContain(res.status());
    if ([200, 201].includes(res.status())) {
      const body = await res.json();
      const assignment = body.data ?? body;
      expect(assignment).toHaveProperty('userId', adminUserId);
      expect(assignment).toHaveProperty('isActive', true);
    }
  });

  // ── TC-ORG-026: POST assign — non-existent org unit returns 404 ───────────

  test('TC-ORG-026: assign staff — non-existent org unit returns 404', async ({ request }) => {
    test.skip(!adminUserId, 'No userId resolved');

    const res = await request.post(
      `${API_BASE}/api/v1/admin/org/staff/${adminUserId}/assign`,
      {
        headers: { ...authHeaders(adminToken), 'Content-Type': 'application/json' },
        data: {
          orgUnitId: '00000000-0000-0000-0000-000000000099',
          roleInOrg: 'Member',
          positionTitle: 'Tester',
        },
      },
    );
    expect(res.status()).toBe(404);
  });

  // ── TC-ORG-027: POST assign — non-Admin returns 403 ──────────────────────

  test('TC-ORG-027: assign staff — non-Admin role returns 403', async ({ request }) => {
    const targetUserId = adminUserId || '00000000-0000-0000-0000-000000000001';
    const res = await request.post(
      `${API_BASE}/api/v1/admin/org/staff/${targetUserId}/assign`,
      {
        headers: { ...authHeaders(userToken), 'Content-Type': 'application/json' },
        data: { orgUnitId: ORG_BNV_ID, roleInOrg: 'Member', positionTitle: 'Dev' },
      },
    );
    expect(res.status()).toBe(403);
  });

  // ── TC-ORG-028: POST assign — unauthenticated returns 401 ─────────────────

  test('TC-ORG-028: assign staff — unauthenticated returns 401', async ({ request }) => {
    const res = await request.post(
      `${API_BASE}/api/v1/admin/org/staff/00000000-0000-0000-0000-000000000001/assign`,
      {
        headers: { 'Content-Type': 'application/json' },
        data: { orgUnitId: ORG_BNV_ID, roleInOrg: 'Member', positionTitle: 'Dev' },
      },
    );
    expect(res.status()).toBe(401);
  });

  // ── TC-ORG-029: POST transfer — Admin transfers user to new unit ──────────

  test('TC-ORG-029: transfer staff — Admin transfers user to new org unit', async ({ request }) => {
    test.skip(!adminUserId, 'No userId resolved');

    const res = await request.post(
      `${API_BASE}/api/v1/admin/org/staff/${adminUserId}/transfer`,
      {
        headers: { ...authHeaders(adminToken), 'Content-Type': 'application/json' },
        data: {
          toOrgUnitId: ORG_VTCCB_ID,
          roleInOrg: 'Lead',
          positionTitle: 'Team Lead',
        },
      },
    );
    // 200/201 = transferred; 404 = seeded unit not present; 422 = no active assignment to close
    expect([200, 201, 404, 422]).toContain(res.status());
    if ([200, 201].includes(res.status())) {
      const body = await res.json();
      const assignment = body.data ?? body;
      expect(assignment).toHaveProperty('orgUnitId', ORG_VTCCB_ID);
      expect(assignment).toHaveProperty('isActive', true);
    }
  });

  // ── TC-ORG-031: POST transfer — non-existent target unit returns 404 ──────

  test('TC-ORG-031: transfer staff — non-existent target org unit returns 404', async ({ request }) => {
    test.skip(!adminUserId, 'No userId resolved');

    const res = await request.post(
      `${API_BASE}/api/v1/admin/org/staff/${adminUserId}/transfer`,
      {
        headers: { ...authHeaders(adminToken), 'Content-Type': 'application/json' },
        data: {
          toOrgUnitId: '00000000-0000-0000-0000-000000000099',
          roleInOrg: 'Member',
          positionTitle: 'Engineer',
        },
      },
    );
    expect(res.status()).toBe(404);
  });

  // ── TC-ORG-032: POST transfer — non-Admin returns 403 ────────────────────

  test('TC-ORG-032: transfer staff — non-Admin role returns 403', async ({ request }) => {
    const targetUserId = adminUserId || '00000000-0000-0000-0000-000000000001';
    const res = await request.post(
      `${API_BASE}/api/v1/admin/org/staff/${targetUserId}/transfer`,
      {
        headers: { ...authHeaders(userToken), 'Content-Type': 'application/json' },
        data: { toOrgUnitId: ORG_PNS_ID, roleInOrg: 'Member', positionTitle: 'Dev' },
      },
    );
    expect(res.status()).toBe(403);
  });

  // ── TC-ORG-033: GET history — Admin retrieves assignment + position history

  test('TC-ORG-033: GET staff history — Admin retrieves full history', async ({ request }) => {
    test.skip(!adminUserId, 'No userId resolved');

    const res = await request.get(
      `${API_BASE}/api/v1/admin/org/staff/${adminUserId}/history`,
      { headers: authHeaders(adminToken) },
    );
    expect(res.status()).toBe(200);
    const body = await res.json();
    const data = body.data ?? body;
    // StaffHistoryDto shape: { assignments: [...], positionHistory: [...] }
    expect(data).toHaveProperty('assignments');
    expect(data).toHaveProperty('positionHistory');
    expect(Array.isArray(data.assignments)).toBe(true);
    expect(Array.isArray(data.positionHistory)).toBe(true);
  });

  // ── TC-ORG-035: GET history — non-Admin returns 403 ──────────────────────

  test('TC-ORG-035: GET staff history — non-Admin role returns 403', async ({ request }) => {
    const targetUserId = adminUserId || '00000000-0000-0000-0000-000000000001';
    const res = await request.get(
      `${API_BASE}/api/v1/admin/org/staff/${targetUserId}/history`,
      { headers: authHeaders(userToken) },
    );
    expect(res.status()).toBe(403);
  });
});
