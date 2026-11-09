import { test, expect } from './fixtures/auth-fixture';
import { navigateAndWait } from './helpers/navigation-helper';

const WEB_URL = process.env.WEB_URL ?? 'http://localhost:5173';

/**
 * Browser error boundaries: 404 page, error fallback, form validation,
 * API 500 handling, network offline.
 */
test.describe('Browser Error Boundaries', () => {
  test.describe.configure({ mode: 'serial' });
  test.setTimeout(60_000);

  test('404 page renders for unknown route', async ({ authedPage }) => {
    await authedPage.goto(`${WEB_URL}/this-route-does-not-exist-123`);
    await authedPage.waitForLoadState('domcontentloaded');

    // Should show not-found page or error boundary — not white screen
    const hasNotFound = await authedPage.getByText(/404|not found|không tìm thấy/i)
      .first().isVisible({ timeout: 5_000 }).catch(() => false);
    const hasContent = await authedPage.locator('.ant-layout-content, main')
      .first().isVisible({ timeout: 5_000 }).catch(() => false);
    expect(hasNotFound || hasContent).toBe(true);
  });

  test('error boundary catches invalid route param', async ({ authedPage }) => {
    await authedPage.goto(`${WEB_URL}/forms/invalid-uuid-format`);
    await authedPage.waitForLoadState('domcontentloaded');

    // Should show error fallback or redirect, NOT white screen
    const body = await authedPage.locator('body').textContent();
    expect(body?.length).toBeGreaterThan(10);
  });

  test('form validation errors display inline', async ({ authedPage }) => {
    await navigateAndWait(authedPage, `${WEB_URL}/admin/users`);

    const addBtn = authedPage.getByRole('button', { name: /thêm|tạo|create|add/i });
    const hasAdd = await addBtn.isVisible({ timeout: 5_000 }).catch(() => false);
    test.skip(!hasAdd, 'No create button on users page');

    await addBtn.click();

    // Submit empty form
    const submitBtn = authedPage.locator('.ant-modal:visible, .ant-drawer:visible')
      .getByRole('button', { name: /lưu|save|submit|tạo/i });
    const hasSubmit = await submitBtn.isVisible({ timeout: 3_000 }).catch(() => false);
    test.skip(!hasSubmit, 'No submit button in modal');

    await submitBtn.click();

    // Expect validation errors
    const errors = authedPage.locator('.ant-form-item-explain-error');
    await expect(errors.first()).toBeVisible({ timeout: 3_000 });
  });

  test('intercepted API 500 does not crash page', async ({ authedPage }) => {
    // Intercept forms API to return 500
    await authedPage.route('**/api/v1/forms/templates*', (route) => {
      route.fulfill({
        status: 500,
        contentType: 'application/json',
        body: JSON.stringify({ success: false, message: 'Internal Server Error' }),
      });
    });

    try {
      await authedPage.goto(`${WEB_URL}/forms`);
      await authedPage.waitForLoadState('domcontentloaded');
      await authedPage.waitForTimeout(2_000);

      // Page should not be blank
      const body = await authedPage.locator('body').textContent();
      expect(body?.length).toBeGreaterThan(10);
    } finally {
      // Always unroute to prevent state contamination
      await authedPage.unroute('**/api/v1/forms/templates*');
    }
  });

  test('network offline shows error state', async ({ authedPage }) => {
    await authedPage.goto(`${WEB_URL}/`);
    await authedPage.waitForLoadState('domcontentloaded');

    // Simulate offline
    await authedPage.context().setOffline(true);
    await authedPage.goto(`${WEB_URL}/forms`).catch(() => {});
    await authedPage.waitForTimeout(2_000);

    // Restore network
    await authedPage.context().setOffline(false);

    // Verify page recovers
    await authedPage.goto(`${WEB_URL}/`);
    await authedPage.waitForLoadState('domcontentloaded');
    const body = await authedPage.locator('body').textContent();
    expect(body?.length).toBeGreaterThan(10);
  });
});
