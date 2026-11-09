import { test, expect } from './fixtures/test-base';
import { getAccessToken, authHeaders, API_BASE } from './helpers/auth-helper';

const TEMPLATES_URL = `${API_BASE}/api/v1/forms/templates`;

// Forms template lifecycle E2E — publish, duplicate (P1 TCs: 017, 018, 021)
test.describe('Forms Template Lifecycle (publish / duplicate)', () => {
  test.describe.configure({ mode: 'serial' });
  let token: string;
  let userToken: string;
  let draftId: string | undefined;
  let duplicateId: string | undefined;

  test.beforeAll(async ({ request }) => {
    token = await getAccessToken(request);
    userToken = await getAccessToken(request, 'user');

    // Create a Draft template for lifecycle tests
    const ts = Date.now();
    const res = await request.post(TEMPLATES_URL, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: {
        name: 'E2E Lifecycle Form',
        nameVi: 'Form vòng đời E2E',
        code: `e2e-lifecycle-${ts}`,
        storageMode: 'Json',
      },
    });
    if (res.status() === 200 || res.status() === 201) {
      const body = await res.json();
      draftId = body.data?.id;
    }
  });

  test.afterAll(async ({ request }) => {
    for (const id of [draftId, duplicateId].filter(Boolean)) {
      await request.delete(`${TEMPLATES_URL}/${id}`, { headers: authHeaders(token) });
    }
  });

  // TC-FORMS-018: publish non-Admin returns 403
  test('TC-FORMS-018: non-Admin publish template returns 403', async ({ request }) => {
    test.skip(!draftId, 'No draft template available');
    const res = await request.post(`${TEMPLATES_URL}/${draftId}/publish`, {
      headers: authHeaders(userToken),
    });
    expect(res.status()).toBe(403);
  });

  // TC-FORMS-017: Admin publishes Draft to Active
  test('TC-FORMS-017: Admin publishes Draft template to Active', async ({ request }) => {
    test.skip(!draftId, 'No draft template available');

    // Add a field first (publish may require at least one field)
    await request.post(`${TEMPLATES_URL}/${draftId}/fields`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: {
        fieldKey: 'fullName',
        type: 'Text',
        labelVi: 'Họ và tên',
        labelEn: 'Full Name',
        displayOrder: 1,
        required: true,
        validationRulesJson: null,
      },
    });

    const res = await request.post(`${TEMPLATES_URL}/${draftId}/publish`, {
      headers: authHeaders(token),
    });
    expect([200, 204]).toContain(res.status());

    // Verify status changed
    const detail = await request.get(`${TEMPLATES_URL}/${draftId}`, {
      headers: authHeaders(token),
    });
    if (detail.status() === 200) {
      const body = await detail.json();
      const status = body.data?.status ?? body.data?.Status;
      expect(['Active', 'Materializing', 1]).toContain(status);
    }
  });

  // TC-FORMS-021: non-Admin duplicate returns 403
  test('TC-FORMS-021: non-Admin duplicate template returns 403', async ({ request }) => {
    test.skip(!draftId, 'No template available');
    const res = await request.post(`${TEMPLATES_URL}/${draftId}/duplicate`, {
      headers: { ...authHeaders(userToken), 'Content-Type': 'application/json' },
      data: { name: 'Copy', code: 'copy-x-403' },
    });
    expect(res.status()).toBe(403);
  });
});
