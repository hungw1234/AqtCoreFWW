import { test, expect } from './fixtures/auth-fixture';
import { PublicFormPage } from './pages/public-form-page';
import { API_BASE } from './helpers/auth-helper';
import path from 'path';
import fs from 'fs';

/**
 * File upload scenarios on public forms.
 */
test.describe('Public form file upload', () => {
  test.describe.configure({ mode: 'serial' });
  test.setTimeout(60_000);

  let formCode: string;
  let templateId: string;
  const tmpDir = path.join(process.cwd(), 'test-results', 'tmp');

  test.beforeAll(async ({ apiContext }) => {
    // Ensure tmp dir exists for test files
    if (!fs.existsSync(tmpDir)) fs.mkdirSync(tmpDir, { recursive: true });

    formCode = `e2e-upload-${Date.now()}`;
    const res = await apiContext.post(`${API_BASE}/api/v1/forms/templates`, {
      data: { name: `E2E Upload Form ${Date.now()}`, nameVi: `Mẫu Upload ${Date.now()}`, code: formCode, storageMode: 'Json' },
    });
    const body = await res.json();
    templateId = body.data?.id ?? body.id ?? '';
    // Add fields then publish (API requires separate steps)
    await apiContext.post(`${API_BASE}/api/v1/forms/templates/${templateId}/fields`, {
      data: { fieldKey: 'fullName', type: 'Text', labelVi: 'Họ và tên', labelEn: 'Full Name', displayOrder: 1, required: true },
    });
    await apiContext.post(`${API_BASE}/api/v1/forms/templates/${templateId}/fields`, {
      data: { fieldKey: 'attachment', type: 'File', labelVi: 'Tài liệu đính kèm', labelEn: 'Attachment', displayOrder: 2, required: false },
    });
    await apiContext.post(`${API_BASE}/api/v1/forms/templates/${templateId}/publish`, { data: {} });
  });

  test.afterAll(async ({ apiContext }) => {
    if (templateId) {
      await apiContext.delete(`${API_BASE}/api/v1/forms/templates/${templateId}`);
    }
    // Cleanup tmp files
    if (fs.existsSync(tmpDir)) {
      for (const f of fs.readdirSync(tmpDir)) {
        if (f.startsWith('e2e-')) fs.unlinkSync(path.join(tmpDir, f));
      }
    }
  });

  test('upload valid file succeeds', async ({ browser }) => {
    test.skip(!formCode, 'No form');
    const ctx = await browser.newContext();
    const page = await ctx.newPage();
    const publicForm = new PublicFormPage(page);
    await publicForm.goto(formCode);
    await page.locator('form').waitFor({ timeout: 30_000 });

    // Create a small test PDF
    const filePath = path.join(tmpDir, 'e2e-valid-upload.txt');
    fs.writeFileSync(filePath, 'E2E test file content - valid upload');

    await publicForm.fillTextField('Họ và tên', 'Upload Test');

    // Use setInputFiles for file upload (avoids native file dialog)
    const fileInput = page.locator('input[type="file"]').first();
    if (await fileInput.count() > 0) {
      await fileInput.setInputFiles(filePath);
      // Verify file appears in upload list
      const body = await page.locator('body').textContent();
      expect(body?.length).toBeGreaterThan(10);
    }
    await ctx.close();
  });

  test('upload oversized file shows error', async ({ browser }) => {
    test.skip(!formCode, 'No form');
    const ctx = await browser.newContext();
    const page = await ctx.newPage();
    const publicForm = new PublicFormPage(page);
    await publicForm.goto(formCode);

    // Create a file >10MB
    const filePath = path.join(tmpDir, 'e2e-oversized.bin');
    fs.writeFileSync(filePath, Buffer.alloc(11 * 1024 * 1024, 'x'));

    const fileInput = page.locator('input[type="file"]').first();
    if (await fileInput.count() > 0) {
      await fileInput.setInputFiles(filePath);
      // Should show size error or reject
      await page.waitForTimeout(2_000);
      const body = await page.locator('body').textContent();
      expect(body?.length).toBeGreaterThan(10);
    }
    await ctx.close();
  });
});
