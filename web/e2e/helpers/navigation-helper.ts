import { type Page, expect } from '@playwright/test';

const WEB_URL = process.env.WEB_URL ?? 'http://localhost:5173';

/**
 * Navigate to a path and wait for content to render.
 * Collects JS errors during navigation for assertion.
 */
export async function navigateAndWait(
  page: Page,
  path: string,
): Promise<{ errors: string[] }> {
  const errors: string[] = [];
  const handler = (err: Error) => errors.push(err.message);
  page.on('pageerror', handler);

  const url = path.startsWith('http') ? path : `${WEB_URL}${path}`;
  await page.goto(url);
  await page.waitForLoadState('domcontentloaded');
  await page.locator('.ant-layout-content, main').first().waitFor({ timeout: 10_000 });

  page.off('pageerror', handler);
  return { errors };
}

/**
 * Run an action and assert no JS errors are thrown during it.
 */
export async function assertNoJsErrors(page: Page, action: () => Promise<void>) {
  const errors: string[] = [];
  const handler = (err: Error) => errors.push(err.message);
  page.on('pageerror', handler);
  await action();
  page.off('pageerror', handler);
  expect(errors).toHaveLength(0);
}
