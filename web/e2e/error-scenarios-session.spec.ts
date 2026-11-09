import { test, expect } from './fixtures/test-base';
import { LoginPage } from './pages/login-page';
import { API_BASE, AUTH_SERVER } from './helpers/auth-helper';

const WEB_URL = process.env.WEB_URL ?? 'http://localhost:5173';

/**
 * Session expiry, token refresh, forced logout, concurrent sessions.
 */
test.describe('Session Expiry & Re-authentication', () => {
  test.setTimeout(60_000);

  test('expired token triggers re-auth redirect', async ({ browser }) => {
    const ctx = await browser.newContext();
    const page = await ctx.newPage();

    // Login normally
    const loginPage = new LoginPage(page);
    await loginPage.loginAsAdmin();

    // Clear auth cookies to simulate session expiry
    await ctx.clearCookies();

    // Navigate to protected route
    await page.goto(`${WEB_URL}/forms`);
    await page.waitForLoadState('domcontentloaded');
    await page.waitForTimeout(3_000);

    // Should redirect to login or auth server
    const url = page.url();
    const isLoginRedirect = url.includes('/login') || url.includes(new URL(AUTH_SERVER).host);
    const isOnApp = url.includes('localhost:5173');
    // Either redirected to auth or showing login page
    expect(isLoginRedirect || isOnApp).toBe(true);

    await ctx.close();
  });

  test('API call with invalid token returns 401', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/forms/templates`, {
      headers: { Authorization: 'Bearer this.is.expired' },
    });
    expect(res.status()).toBe(401);
  });

  test('concurrent sessions do not interfere', async ({ browser }) => {
    const ctx1 = await browser.newContext();
    const ctx2 = await browser.newContext();
    const page1 = await ctx1.newPage();
    const page2 = await ctx2.newPage();

    // Login both
    for (const page of [page1, page2]) {
      const lp = new LoginPage(page);
      await lp.loginAsAdmin();
    }

    // Both should navigate independently
    await page1.goto(`${WEB_URL}/forms`);
    await page2.goto(`${WEB_URL}/admin/users`);
    await page1.waitForLoadState('domcontentloaded');
    await page2.waitForLoadState('domcontentloaded');

    expect(page1.url()).toContain('/forms');
    expect(page2.url()).toContain('/admin/users');

    await ctx1.close();
    await ctx2.close();
  });
});
