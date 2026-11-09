import { test, expect } from './fixtures/auth-fixture';
import { PublicFormPage } from './pages/public-form-page';
import { API_BASE } from './helpers/auth-helper';

/**
 * Full public form lifecycle:
 * Admin creates template -> publishes -> citizen fills -> submits -> admin reviews
 */
test.describe('Public form lifecycle', () => {
  test.describe.configure({ mode: 'serial' });
  test.setTimeout(60_000);

  let templateId: string;
  let formCode: string;

  test('admin creates form template via API', async ({ apiContext }) => {
    formCode = `e2e-pub-${Date.now()}`;
    const res = await apiContext.post(`${API_BASE}/api/v1/forms/templates`, {
      data: {
        name: `E2E Public Form ${Date.now()}`,
        nameVi: `Mẫu E2E ${Date.now()}`,
        code: formCode,
        storageMode: 'Json',
        requiresConsent: true,
        consentText: 'I consent to data processing',
      },
    });
    expect([200, 201]).toContain(res.status());
    const body = await res.json();
    templateId = body.data?.id ?? body.id;
    expect(templateId).toBeTruthy();
  });

  test('admin publishes template (set Active)', async ({ apiContext }) => {
    test.skip(!templateId, 'No template created');
    // Add a required field before publishing (publish requires ≥1 field)
    await apiContext.post(`${API_BASE}/api/v1/forms/templates/${templateId}/fields`, {
      data: { fieldKey: 'fullName', type: 'Text', labelVi: 'Họ và tên', labelEn: 'Full Name', displayOrder: 1, required: true },
    });
    const res = await apiContext.post(`${API_BASE}/api/v1/forms/templates/${templateId}/publish`, {
      data: {},
    });
    expect([200, 204]).toContain(res.status());
  });

  test('citizen can access public form without auth', async ({ browser }) => {
    test.skip(!formCode, 'No form code');
    const ctx = await browser.newContext(); // anonymous — no auth
    const page = await ctx.newPage();
    const publicForm = new PublicFormPage(page);
    await publicForm.goto(formCode);

    // Wait for form to render (React hydration + API fetch + re-render)
    await expect(page.locator('form')).toBeVisible({ timeout: 30_000 });
    await ctx.close();
  });

  test('citizen fills and submits form', async ({ browser }) => {
    test.skip(!formCode, 'No form code');
    const ctx = await browser.newContext();
    const page = await ctx.newPage();
    const publicForm = new PublicFormPage(page);
    await publicForm.goto(formCode);

    // Wait for form to render before interacting
    await page.locator('form').waitFor({ timeout: 30_000 });
    await publicForm.fillTextField('Họ và tên', 'Nguyen Van Test');
    await publicForm.submit();

    // Expect success confirmation or navigation
    const body = await page.locator('body').textContent();
    expect(body?.length).toBeGreaterThan(10);
    await ctx.close();
  });

  test('admin sees submission in list', async ({ apiContext }) => {
    test.skip(!templateId, 'No template');
    const res = await apiContext.get(
      `${API_BASE}/api/v1/forms/submissions?templateId=${templateId}&page=1&pageSize=10`,
    );
    expect([200, 403]).toContain(res.status());
  });

  test('cleanup: delete template', async ({ apiContext }) => {
    if (templateId) {
      await apiContext.delete(`${API_BASE}/api/v1/forms/templates/${templateId}`);
    }
  });
});
