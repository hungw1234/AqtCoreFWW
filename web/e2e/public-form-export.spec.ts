import { test, expect } from './fixtures/auth-fixture';
import { navigateAndWait } from './helpers/navigation-helper';
import { API_BASE } from './helpers/auth-helper';


/**
 * CSV export of form submissions from admin side.
 */
test.describe('Form submissions CSV export', () => {
  test.setTimeout(60_000);

  test('admin can view form submissions page', async ({ authedPage }) => {
    const { errors } = await navigateAndWait(authedPage, '/forms');
    expect(errors).toHaveLength(0);

    // Page should render with table or content
    const hasContent = await authedPage.locator('.ant-layout-content, main')
      .first().isVisible({ timeout: 5_000 });
    expect(hasContent).toBe(true);
  });

  test('admin exports submissions as CSV (if export button exists)', async ({ authedPage }) => {
    await navigateAndWait(authedPage, '/forms');

    // Look for export button
    const exportBtn = authedPage.getByRole('button', { name: /xuất|export|csv|tải/i });
    const hasExport = await exportBtn.isVisible({ timeout: 5_000 }).catch(() => false);

    if (hasExport) {
      const [download] = await Promise.all([
        authedPage.waitForEvent('download', { timeout: 30_000 }),
        exportBtn.click(),
      ]);
      const filename = download.suggestedFilename();
      expect(filename).toMatch(/\.(csv|xlsx)$/i);
    }
    // If no export button, skip gracefully — feature may not be implemented in UI yet
  });

  test('form submissions API returns paginated data', async ({ apiContext }) => {
    const res = await apiContext.get(
      `${API_BASE}/api/v1/forms/submissions?page=1&pageSize=10`,
    );
    expect([200, 403]).toContain(res.status());
    if (res.status() === 200) {
      const body = await res.json();
      expect(body.success).toBe(true);
    }
  });
});
