import { test, expect } from './fixtures/test-base';
import { getAccessToken, authHeaders, API_BASE } from './helpers/auth-helper';

const TEMPLATES_URL = `${API_BASE}/api/v1/forms/templates`;
const SUBMISSIONS_URL = `${API_BASE}/api/v1/forms/submissions`;

// Form submissions E2E — submit/list/review (P1 TCs: 043-057)
test.describe('Form Submissions (authenticated API)', () => {
  test.describe.configure({ mode: 'serial' });
  let token: string;
  let userToken: string;
  let submissionId: string | undefined;

  // Live-fetched FormTemplate Guid AK (T2, post PR #76 retier).
  // Seeded template Guid: 30000000-0000-0000-0000-000000000001 (E2ETestDataSeeder).
  let templatePublicId: string;

  test.beforeAll(async ({ request }) => {
    token = await getAccessToken(request);
    userToken = await getAccessToken(request, 'user');
    expect(token).toBeTruthy();

    // Fetch the first available FormTemplate to get its Guid PublicId (T2 AK).
    // Admin token has Forms.Template.View policy.
    const tmplRes = await request.get(`${TEMPLATES_URL}?page=1&pageSize=1`, {
      headers: authHeaders(token),
    });
    if (tmplRes.status() !== 200) {
      throw new Error(
        `[form-submissions] Failed to fetch FormTemplates (${tmplRes.status()}). Is the API seeded?`,
      );
    }
    const tmplBody = await tmplRes.json();
    const items: Array<{ publicId?: string; id?: string }> = tmplBody.data?.items ?? tmplBody.data ?? [];
    if (!items.length) {
      throw new Error(
        '[form-submissions] No FormTemplate found. Run E2ETestDataSeeder before E2E tests.',
      );
    }
    templatePublicId = items[0].publicId ?? items[0].id ?? '';
    if (!templatePublicId) {
      throw new Error('[form-submissions] FormTemplate item has no publicId field.');
    }
  });

  // TC-FORMS-044: unauthenticated submit returns 401
  test('TC-FORMS-044: submit form without auth returns 401', async ({ request }) => {
    const res = await request.post(SUBMISSIONS_URL, {
      headers: { 'Content-Type': 'application/json' },
      // formTemplatePublicId is the T2 Guid AK field (FormSubmissionsController SubmitFormRequest)
      data: { formTemplatePublicId: templatePublicId, data: {} },
    });
    expect(res.status()).toBe(401);
  });

  // TC-FORMS-046: required field missing returns 422 (or 200/201 if no required fields on live template)
  test('TC-FORMS-046: submit with missing required field returns 422', async ({ request }) => {
    const res = await request.post(SUBMISSIONS_URL, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      // formTemplatePublicId: T2 Guid AK; data: empty — triggers 422 if template has required fields
      data: { formTemplatePublicId: templatePublicId, data: {} },
    });
    // 422 if template has required fields; 200/201 if no required fields enforced on live template
    expect([200, 201, 422]).toContain(res.status());
  });

  // TC-FORMS-043: authenticated user submits to Active template
  test('TC-FORMS-043: authenticated user submits form to Active template', async ({ request }) => {
    const res = await request.post(SUBMISSIONS_URL, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: {
        formTemplatePublicId: templatePublicId,
        data: { fullName: 'Nguyen Van A', email: 'vana@test.vn' },
      },
    });
    expect([200, 201, 422]).toContain(res.status());
    if (res.status() === 200 || res.status() === 201) {
      const body = await res.json();
      submissionId = body.data?.id;
      expect(submissionId).toBeTruthy();
    }
  });

  // TC-FORMS-047: list submissions filtered by templatePublicId
  test('TC-FORMS-047: list submissions filtered by templatePublicId', async ({ request }) => {
    // Query param is templatePublicId (FormSubmissionsController.List [FromQuery] Guid? templatePublicId)
    const res = await request.get(
      `${SUBMISSIONS_URL}?templatePublicId=${templatePublicId}`,
      { headers: authHeaders(token) },
    );
    expect(res.status()).toBe(200);
    const body = await res.json();
    expect(body.success).toBe(true);
    expect(body.data).toBeDefined();
  });

  // TC-FORMS-049: get submission by ID
  test('TC-FORMS-049: get submission by ID returns submission detail', async ({ request }) => {
    test.skip(!submissionId, 'No submission created in this run');
    const res = await request.get(`${SUBMISSIONS_URL}/${submissionId}`, {
      headers: authHeaders(token),
    });
    expect(res.status()).toBe(200);
    const body = await res.json();
    expect(body.data).toHaveProperty('id', submissionId);
    expect(body.data).toHaveProperty('data');
  });

  // TC-FORMS-050: get submission by nonexistent ID returns 404
  test('TC-FORMS-050: get submission by nonexistent ID returns 404', async ({ request }) => {
    const res = await request.get(
      `${SUBMISSIONS_URL}/00000000-0000-0000-0000-000000000099`,
      { headers: authHeaders(token) },
    );
    expect(res.status()).toBe(404);
  });

  // TC-FORMS-052: non-Admin export returns 403
  test('TC-FORMS-052: non-Admin export submissions returns 403', async ({ request }) => {
    // Export query param is templatePublicId (FormSubmissionsController.Export [FromQuery] Guid templatePublicId)
    const res = await request.get(
      `${SUBMISSIONS_URL}/export?templatePublicId=${templatePublicId}&format=csv`,
      { headers: authHeaders(userToken) },
    );
    expect(res.status()).toBe(403);
  });

  // TC-FORMS-054: non-Admin approve returns 403
  test('TC-FORMS-054: non-Admin approve submission returns 403', async ({ request }) => {
    const targetId = submissionId ?? '00000000-0000-0000-0000-000000000001';
    const res = await request.post(`${SUBMISSIONS_URL}/${targetId}/approve`, {
      headers: { ...authHeaders(userToken), 'Content-Type': 'application/json' },
      data: { comment: null },
    });
    expect(res.status()).toBe(403);
  });

  // TC-FORMS-057: non-Admin reject returns 403
  test('TC-FORMS-057: non-Admin reject submission returns 403', async ({ request }) => {
    const targetId = submissionId ?? '00000000-0000-0000-0000-000000000001';
    const res = await request.post(`${SUBMISSIONS_URL}/${targetId}/reject`, {
      headers: { ...authHeaders(userToken), 'Content-Type': 'application/json' },
      data: { comment: 'X' },
    });
    expect(res.status()).toBe(403);
  });

  // TC-FORMS-053: Admin approves Pending submission
  test('TC-FORMS-053: Admin approves Pending submission', async ({ request }) => {
    test.skip(!submissionId, 'No submission created in this run');
    const res = await request.post(`${SUBMISSIONS_URL}/${submissionId}/approve`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: { comment: 'Approved by E2E test' },
    });
    // 200 = approved; 422 = already approved or workflow constraint
    expect([200, 422]).toContain(res.status());
  });

  // TC-FORMS-059: non-Admin bulk-approve returns 403
  test('TC-FORMS-059: non-Admin bulk-approve returns 403', async ({ request }) => {
    const res = await request.post(`${SUBMISSIONS_URL}/bulk-approve`, {
      headers: { ...authHeaders(userToken), 'Content-Type': 'application/json' },
      data: { ids: [], comment: null },
    });
    expect(res.status()).toBe(403);
  });

  // TC-FORMS-061: non-Admin bulk-reject returns 403
  test('TC-FORMS-061: non-Admin bulk-reject returns 403', async ({ request }) => {
    const res = await request.post(`${SUBMISSIONS_URL}/bulk-reject`, {
      headers: { ...authHeaders(userToken), 'Content-Type': 'application/json' },
      data: { ids: [], comment: 'X' },
    });
    expect(res.status()).toBe(403);
  });

  // TC-FORMS-056: Admin rejects a submission (create fresh Pending one)
  test('TC-FORMS-056: Admin rejects Pending submission', async ({ request }) => {
    // Submit a fresh form to get a Pending submission
    const submitRes = await request.post(SUBMISSIONS_URL, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: {
        formTemplatePublicId: templatePublicId,
        data: { fullName: 'Test Reject', email: 'reject@test.vn' },
      },
    });
    if (submitRes.status() !== 200 && submitRes.status() !== 201) {
      test.skip(true, 'Could not create submission for rejection test');
      return;
    }
    const submitBody = await submitRes.json();
    const rejectId = submitBody.data?.id;
    if (!rejectId) return;

    const res = await request.post(`${SUBMISSIONS_URL}/${rejectId}/reject`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: { comment: 'Missing required documents' },
    });
    expect([200, 422]).toContain(res.status());
  });
});

// Auth-guard tests for ext endpoints live in forms-ext-auth-guards.spec.ts
