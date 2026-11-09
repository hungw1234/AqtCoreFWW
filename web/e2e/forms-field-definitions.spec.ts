import { test, expect } from './fixtures/test-base';
import { getAccessToken, authHeaders, API_BASE } from './helpers/auth-helper';

const TEMPLATES_URL = `${API_BASE}/api/v1/forms/templates`;

// Forms field definitions E2E — add/update/remove fields on Draft templates (P1 TCs)
// TC-FORMS-023, 025, 029, 030, 031, 032, 035, 037
test.describe('Forms Field Definitions (add / update / remove)', () => {
  test.describe.configure({ mode: 'serial' });
  let token: string;
  let userToken: string;
  let templateId: string | undefined;
  let fieldId: string | undefined;

  test.beforeAll(async ({ request }) => {
    token = await getAccessToken(request);
    userToken = await getAccessToken(request, 'user');

    // Create a Draft template to work with
    const ts = Date.now();
    const res = await request.post(TEMPLATES_URL, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: {
        name: 'E2E Field Test Form',
        nameVi: 'Form kiểm tra trường E2E',
        code: `e2e-fields-${ts}`,
        storageMode: 'Json',
      },
    });
    if (res.status() === 200 || res.status() === 201) {
      const body = await res.json();
      templateId = body.data?.id;
    }
  });

  test.afterAll(async ({ request }) => {
    if (templateId) {
      await request.delete(`${TEMPLATES_URL}/${templateId}`, {
        headers: authHeaders(token),
      });
    }
  });

  // TC-FORMS-025: non-Admin add field returns 403
  test('TC-FORMS-025: non-Admin add field returns 403', async ({ request }) => {
    test.skip(!templateId, 'No draft template available');
    const res = await request.post(`${TEMPLATES_URL}/${templateId}/fields`, {
      headers: { ...authHeaders(userToken), 'Content-Type': 'application/json' },
      data: {
        fieldKey: 'x',
        type: 'Text',
        labelVi: 'X',
        labelEn: 'X',
        displayOrder: 1,
        required: false,
        validationRulesJson: null,
      },
    });
    expect(res.status()).toBe(403);
  });

  // TC-FORMS-023: Admin adds text field
  test('TC-FORMS-023: Admin adds text field to Draft template', async ({ request }) => {
    test.skip(!templateId, 'No draft template available');
    const res = await request.post(`${TEMPLATES_URL}/${templateId}/fields`, {
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
    expect([200, 201]).toContain(res.status());
    const body = await res.json();
    const field = body.data;
    expect(field).toHaveProperty('id');
    expect(field.fieldKey ?? field.FieldKey).toBe('fullName');
    fieldId = field.id ?? field.Id;
  });

  // TC-FORMS-032: non-Admin update field returns 403
  test('TC-FORMS-032: non-Admin update field returns 403', async ({ request }) => {
    test.skip(!templateId || !fieldId, 'No field available');
    const res = await request.put(`${TEMPLATES_URL}/${templateId}/fields/${fieldId}`, {
      headers: { ...authHeaders(userToken), 'Content-Type': 'application/json' },
      data: { labelVi: 'X' },
    });
    expect(res.status()).toBe(403);
  });

  // TC-FORMS-031: Admin updates field labels
  test('TC-FORMS-031: Admin updates field labels and required flag', async ({ request }) => {
    test.skip(!templateId || !fieldId, 'No field available');
    const res = await request.put(`${TEMPLATES_URL}/${templateId}/fields/${fieldId}`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: { labelVi: 'Tên mới', labelEn: 'New Label', required: true },
    });
    expect([200, 204]).toContain(res.status());
  });

  // TC-FORMS-035: non-Admin reorder field returns 403
  test('TC-FORMS-035: non-Admin reorder field returns 403', async ({ request }) => {
    test.skip(!templateId || !fieldId, 'No field available');
    const res = await request.patch(
      `${TEMPLATES_URL}/${templateId}/fields/${fieldId}/order`,
      {
        headers: { ...authHeaders(userToken), 'Content-Type': 'application/json' },
        data: { newOrder: 2 },
      },
    );
    expect(res.status()).toBe(403);
  });

  // TC-FORMS-037: non-Admin bulk reorder returns 403
  test('TC-FORMS-037: non-Admin bulk reorder fields returns 403', async ({ request }) => {
    test.skip(!templateId, 'No template available');
    const res = await request.put(`${TEMPLATES_URL}/${templateId}/fields/reorder`, {
      headers: { ...authHeaders(userToken), 'Content-Type': 'application/json' },
      data: { items: [] },
    });
    expect(res.status()).toBe(403);
  });

  // TC-FORMS-030: non-Admin delete field returns 403
  test('TC-FORMS-030: non-Admin delete field returns 403', async ({ request }) => {
    test.skip(!templateId || !fieldId, 'No field available');
    const res = await request.delete(
      `${TEMPLATES_URL}/${templateId}/fields/${fieldId}`,
      { headers: authHeaders(userToken) },
    );
    expect(res.status()).toBe(403);
  });

  // TC-FORMS-029: Admin deactivates field
  test('TC-FORMS-029: Admin deactivates field from Draft template', async ({ request }) => {
    test.skip(!templateId || !fieldId, 'No field available');
    const res = await request.delete(
      `${TEMPLATES_URL}/${templateId}/fields/${fieldId}`,
      { headers: authHeaders(token) },
    );
    expect([204, 200]).toContain(res.status());
    fieldId = undefined;
  });
});
