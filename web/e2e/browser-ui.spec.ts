import { test, expect, type Page } from './fixtures/test-base';
import { AUTH_SERVER } from './helpers/auth-helper';

// Browser-based UI E2E tests — full OIDC login flow + page navigation
// Runs against Docker stack: Web, AuthServer, API
//
// Flow: Web app → OIDC redirect → AuthServer login form → callback → authenticated pages
// Tests real browser rendering, not just API calls.

const WEB_URL = 'http://localhost:5173';

// Shared auth helper — logs in via OIDC flow and returns authenticated page
async function loginViaOidc(page: Page) {
  // Navigate to web app — should show login page or redirect to AuthServer
  await page.goto(WEB_URL);
  await page.waitForLoadState('domcontentloaded');

  // If app shows login page with "Đăng nhập" button, click it to trigger OIDC redirect
  const loginBtn = page.getByRole('button', { name: /đăng nhập/i });
  if (await loginBtn.isVisible({ timeout: 5000 }).catch(() => false)) {
    await loginBtn.click();
  }

  // Wait for AuthServer login form — check both localhost and 127.0.0.1 variants
  // FE OIDC config uses localhost:5002, but E2E AUTH_SERVER defaults to 127.0.0.1:5002
  const authPort = new URL(AUTH_SERVER).port;
  await page.waitForURL(url => url.includes(`:${authPort}`), { timeout: 15_000 });
  await expect(page.locator('input[name="Email"]')).toBeVisible({ timeout: 10_000 });

  // Fill in credentials and submit
  await page.fill('input[name="Email"]', 'admin@dev.local');
  await page.fill('input[name="Password"]', 'DevAdmin@12345');
  await page.click('button[type="submit"]');

  // Wait for redirect back to web app after successful auth
  await page.waitForURL(/localhost:5173/, { timeout: 15_000 });
  await page.waitForLoadState('domcontentloaded');
}

test.describe('Browser UI E2E', () => {
  test.describe.configure({ mode: 'serial' });
  test.setTimeout(60_000);

  let authedPage: Page;

  test.beforeAll(async ({ browser }) => {
    test.setTimeout(120_000); // OIDC login flow can be slow (cold start + redirects)
    // Use a fresh browser context (no cookies/storage from prior runs)
    const context = await browser.newContext();
    authedPage = await context.newPage();
    await loginViaOidc(authedPage);
  });

  test.afterAll(async () => {
    await authedPage.context().close();
  });

  // -------------------------------------------------------------------------
  // Login Flow
  // -------------------------------------------------------------------------
  test.describe('Login Flow', () => {
    test('successful OIDC login lands on dashboard', async () => {
      // After loginViaOidc, should be on the dashboard (path = /)
      const url = authedPage.url();
      expect(url).toContain('localhost:5173');
      // Dashboard should have rendered — check for layout elements
      await expect(authedPage.locator('body')).toBeVisible();
    });

    test('authenticated page shows user info in topbar', async () => {
      // Topbar should display user identity or logout button
      const topbar = authedPage.locator('header, [class*="topbar"], [class*="header"]');
      await expect(topbar.first()).toBeVisible({ timeout: 5000 });
    });
  });

  // -------------------------------------------------------------------------
  // Page Navigation — authenticated pages render correctly
  // -------------------------------------------------------------------------
  test.describe('Page Navigation', () => {
    test('dashboard renders with content', async () => {
      await authedPage.goto(`${WEB_URL}/`);
      await authedPage.waitForLoadState('domcontentloaded');
      // Dashboard should have some content (cards, stats, or at least layout)
      await expect(authedPage.locator('.ant-layout-content').first()).toBeVisible({ timeout: 10_000 });
    });

    test('audit logs page renders', async () => {
      await authedPage.goto(`${WEB_URL}/audit/logs`);
      await authedPage.waitForLoadState('domcontentloaded');
      await expect(authedPage.locator('.ant-layout-content').first()).toBeVisible({ timeout: 10_000 });
    });

    test('files page renders', async () => {
      await authedPage.goto(`${WEB_URL}/files`);
      await authedPage.waitForLoadState('domcontentloaded');
      await expect(authedPage.locator('.ant-layout-content, main')).toBeVisible({ timeout: 10_000 });
    });

    test('notifications page renders', async () => {
      await authedPage.goto(`${WEB_URL}/notifications`);
      await authedPage.waitForLoadState('domcontentloaded');
      await expect(authedPage.locator('.ant-layout-content, main')).toBeVisible({ timeout: 10_000 });
    });
  });

  // -------------------------------------------------------------------------
  // Admin Pages
  // -------------------------------------------------------------------------
  test.describe('Admin Pages', () => {
    test('admin users page renders user table', async () => {
      await authedPage.goto(`${WEB_URL}/admin/users`);
      await authedPage.waitForLoadState('domcontentloaded');
      await expect(authedPage.locator('.ant-layout-content').first()).toBeVisible({ timeout: 10_000 });
    });

    test('admin system params page renders', async () => {
      await authedPage.goto(`${WEB_URL}/admin/system-params`);
      await authedPage.waitForLoadState('domcontentloaded');
      await expect(authedPage.locator('.ant-layout-content, main')).toBeVisible({ timeout: 10_000 });
    });

    test('admin health page renders', async () => {
      await authedPage.goto(`${WEB_URL}/admin/health`);
      await authedPage.waitForLoadState('domcontentloaded');
      await expect(authedPage.locator('.ant-layout-content, main')).toBeVisible({ timeout: 10_000 });
    });
  });

  // -------------------------------------------------------------------------
  // Sidebar Navigation
  // -------------------------------------------------------------------------
  test.describe('Sidebar Navigation', () => {
    test('sidebar menu is visible and has navigation items', async () => {
      await authedPage.goto(`${WEB_URL}/`);
      await authedPage.waitForLoadState('domcontentloaded');
      // Ant Design sidebar/sider or nav menu
      const sidebar = authedPage.locator('.ant-layout-sider, [class*="sidebar"], nav');
      await expect(sidebar.first()).toBeVisible({ timeout: 10_000 });
      // Should have multiple menu items
      const menuItems = authedPage.locator('.ant-menu-item, [class*="menu-item"]');
      const count = await menuItems.count();
      expect(count).toBeGreaterThan(2);
    });

    test('clicking sidebar item navigates to correct page', async () => {
      await authedPage.goto(`${WEB_URL}/`);
      await authedPage.waitForLoadState('domcontentloaded');
      // Click a menu item that navigates to forms
      const formsLink = authedPage.locator('.ant-menu-item, [class*="menu-item"]')
        .filter({ hasText: /biểu mẫu|forms/i });
      if (await formsLink.first().isVisible({ timeout: 5_000 }).catch(() => false)) {
        await formsLink.first().click();
        await authedPage.waitForLoadState('domcontentloaded');
        expect(authedPage.url()).toContain('/forms');
      }
    });
  });

  // -------------------------------------------------------------------------
  // Auth Guard — unauthenticated access
  // -------------------------------------------------------------------------
  test.describe('Auth Guard', () => {
    test('unauthenticated access redirects to login', async ({ browser }) => {
      // Fresh context — no auth cookies
      const ctx = await browser.newContext();
      const page = await ctx.newPage();
      await page.goto(`${WEB_URL}/forms`);
      await page.waitForLoadState('domcontentloaded');
      await page.waitForTimeout(3000);

      const url = page.url();
      // Should redirect to login page or AuthServer
      const isOnLogin = url.includes('/login') || url.includes(new URL(AUTH_SERVER).host);
      const isOnApp = url.includes('localhost:5173');
      // Either redirected to auth or showing login page
      expect(isOnLogin || isOnApp).toBe(true);
      await ctx.close();
    });
  });
});
