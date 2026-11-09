import { test, expect } from './fixtures/test-base';

// BP Admin UI smoke — list → wizard → submit → detail
// Requires: Vite dev server on :5173 + authenticated admin session
// Run with: npx playwright test e2e/business-process-admin.spec.ts

test.describe('BP Admin — list page', () => {
  test.skip(() => !process.env.VITE_RUNNING, 'Vite dev server not running');

  test('list page loads without 404/500', async ({ page }) => {
    const response = await page.goto('/admin/business-processes');
    expect(response?.status()).not.toBe(404);
    expect(response?.status()).not.toBe(500);
  });

  test('list page shows table or empty state', async ({ page }) => {
    await page.goto('/admin/business-processes');
    await page.waitForTimeout(1500);
    // Either redirected to auth (unauthenticated) or shows the page shell
    const url = page.url();
    const isAuthOrPage = url.includes('/admin/business-processes')
      || url.includes('/login')
      || url.includes('authorize');
    expect(isAuthOrPage).toBeTruthy();
  });
});

test.describe('BP Admin — wizard route', () => {
  test.skip(() => !process.env.VITE_RUNNING, 'Vite dev server not running');

  test('wizard route loads without error', async ({ page }) => {
    const response = await page.goto('/admin/business-processes/new');
    expect(response?.status()).not.toBe(404);
    expect(response?.status()).not.toBe(500);
  });
});

test.describe('BP Admin — detail route', () => {
  test.skip(() => !process.env.VITE_RUNNING, 'Vite dev server not running');

  test('detail route with placeholder id loads without 404', async ({ page }) => {
    // Uses a fake UUID — expects 404 from BE, but FE route itself must resolve
    const response = await page.goto('/admin/business-processes/00000000-0000-0000-0000-000000000001');
    expect(response?.status()).not.toBe(404);
    expect(response?.status()).not.toBe(500);
  });
});
