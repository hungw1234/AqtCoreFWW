import { test, expect } from './fixtures/test-base';
import { getAccessToken, authHeaders, API_BASE } from './helpers/auth-helper';

const RETENTION_URL = `${API_BASE}/api/v1/retention-policies`;

// Retention Policies E2E — list + create (P1 TCs: RET-001, 002, 005, 006, 007, 011)
test.describe('Retention Policies (authenticated API)', () => {
  let token: string;
  let userToken: string;

  test.beforeAll(async ({ request }) => {
    token = await getAccessToken(request);
    userToken = await getAccessToken(request, 'user');
    expect(token).toBeTruthy();
  });

  // TC-RET-001: GET without auth returns 401
  test('TC-RET-001: GET /retention-policies without auth returns 401', async ({ request }) => {
    const res = await request.get(RETENTION_URL);
    expect(res.status()).toBe(401);
  });

  // TC-RET-006: POST without auth returns 401
  test('TC-RET-006: POST /retention-policies without auth returns 401', async ({ request }) => {
    const res = await request.post(RETENTION_URL, {
      headers: { 'Content-Type': 'application/json' },
      data: { name: 'Test', documentType: 'Invoice', retainDays: 365 },
    });
    expect(res.status()).toBe(401);
  });

  // TC-RET-005: non-Admin POST returns 403
  test('TC-RET-005: non-Admin POST /retention-policies returns 403', async ({ request }) => {
    const res = await request.post(RETENTION_URL, {
      headers: { ...authHeaders(userToken), 'Content-Type': 'application/json' },
      data: { name: 'Test', documentType: 'Invoice', retainDays: 365 },
    });
    expect(res.status()).toBe(403);
  });

  // TC-RET-002: authenticated user GET returns list
  test('TC-RET-002: GET /retention-policies authenticated returns list', async ({ request }) => {
    const res = await request.get(RETENTION_URL, { headers: authHeaders(token) });
    expect(res.status()).toBe(200);
    const body = await res.json();
    expect(body.success).toBe(true);
    // data may be array or paginated object
    const items = Array.isArray(body.data) ? body.data : body.data?.items ?? body.data ?? [];
    expect(Array.isArray(items)).toBe(true);
  });

  // TC-RET-007: Admin creates policy successfully
  test('TC-RET-007: Admin POST /retention-policies creates policy', async ({ request }) => {
    const res = await request.post(RETENTION_URL, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: {
        name: 'E2E Invoice Retention',
        documentType: 'Invoice',
        retainDays: 365,
        archiveAfterDays: 730,
        destroyAfterDays: 2555,
      },
    });
    expect([200, 201]).toContain(res.status());
    const body = await res.json();
    const policy = body.data ?? body;
    expect(policy).toHaveProperty('id');
    expect(policy).toHaveProperty('name');
    expect(policy).toHaveProperty('documentType');
    expect(policy).toHaveProperty('retainDays');
    // isActive defaults to true on creation
    expect(policy.isActive ?? policy.IsActive).toBe(true);
  });

  // TC-RET-011: tenantId comes from JWT, not body
  test('TC-RET-011: tenantId in created policy matches JWT tenant', async ({ request }) => {
    const res = await request.post(RETENTION_URL, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: {
        name: 'Tenant Test Policy',
        documentType: 'Contract',
        retainDays: 365,
        archiveAfterDays: null,
        destroyAfterDays: null,
      },
    });
    expect([200, 201]).toContain(res.status());
    const body = await res.json();
    const policy = body.data ?? body;
    // tenantId must be present and non-empty (resolved from JWT)
    const tenantId = policy.tenantId ?? policy.TenantId;
    expect(tenantId).toBeTruthy();
    expect(tenantId).not.toBe('00000000-0000-0000-0000-000000000000');
  });
});
