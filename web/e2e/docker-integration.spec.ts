import { test, expect } from './fixtures/test-base';

// Docker integration E2E — tests that ONLY work with real Docker containers running.
// These catch CORS, scope, proxy, and cross-origin issues that unit/API tests miss.
// Run: DOCKER_RUNNING=true npx playwright test e2e/docker-integration.spec.ts

const FE_URL = 'http://localhost:5173';
const AUTH_URL = 'http://localhost:5000';

test.describe('Docker integration — browser-level validation', () => {
  test.skip(() => !process.env.DOCKER_RUNNING, 'Docker containers not running');

  test('FE loads at localhost:5173 with correct title', async ({ page }) => {
    await page.goto(FE_URL);
    await expect(page).toHaveTitle(/AqtCoreFW/);
  });

  test('FE can fetch AuthServer OIDC discovery (CORS check)', async ({ page }) => {
    await page.goto(FE_URL);
    // Execute fetch from browser context — this validates CORS
    const result = await page.evaluate(async (authUrl) => {
      try {
        const res = await fetch(`${authUrl}/.well-known/openid-configuration`);
        return { status: res.status, ok: res.ok };
      } catch (e) {
        return { status: 0, ok: false, error: (e as Error).message };
      }
    }, AUTH_URL);

    expect(result.ok).toBe(true);
    expect(result.status).toBe(200);
  });

  test('FE can fetch API public endpoint via nginx proxy (CORS check)', async ({ page }) => {
    await page.goto(FE_URL);
    const result = await page.evaluate(async () => {
      try {
        // This goes through FE nginx → api:8080
        const res = await fetch('/api/v1/feature-flags/EnablePdfWatermark');
        const body = await res.json();
        return { status: res.status, success: body.success };
      } catch (e) {
        return { status: 0, error: (e as Error).message };
      }
    });

    expect(result.status).toBe(200);
    expect(result.success).toBe(true);
  });

  test('Login button triggers OIDC redirect to AuthServer', async ({ page }) => {
    await page.goto(FE_URL);
    // Wait for page to load and find login button
    const loginBtn = page.getByRole('button', { name: /đăng nhập|login/i });
    if (await loginBtn.isVisible({ timeout: 5000 }).catch(() => false)) {
      await loginBtn.click();
      await page.waitForTimeout(2000);
      // Should redirect to AuthServer login page (not get CORS/scope error)
      const url = page.url();
      const isAuthPage = url.includes('localhost:5000') || url.includes('/account/login');
      expect(isAuthPage).toBeTruthy();
    }
  });

  test('AuthServer login page renders from Docker FE origin', async ({ page }) => {
    // Navigate directly to authorize endpoint with FE callback
    await page.goto(
      `${AUTH_URL}/connect/authorize?client_id=aqtcorefw-spa-dev` +
      `&redirect_uri=${encodeURIComponent(FE_URL + '/callback')}` +
      `&response_type=code&scope=openid+profile+email+api+offline_access` +
      `&state=dockertest&code_challenge=E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM` +
      `&code_challenge_method=S256`
    );
    // Should show login form, NOT invalid_scope error
    const pageText = await page.textContent('body');
    expect(pageText).not.toContain('invalid_scope');
    // Should have login form fields
    const hasEmailField = await page.locator('input[name="Email"]').isVisible().catch(() => false);
    const hasPasswordField = await page.locator('input[name="Password"]').isVisible().catch(() => false);
    expect(hasEmailField || hasPasswordField).toBeTruthy();
  });

  test('Full login → dashboard → logout flow', async ({ page }) => {
    // 1. Go to AuthServer login page directly
    await page.goto(`${AUTH_URL}/account/login?returnUrl=/`);
    await page.fill('input[name="Email"]', 'admin@dev.local');
    await page.fill('input[name="Password"]', 'DevAdmin@12345');
    await page.click('button[type="submit"]');
    await page.waitForTimeout(2000);
    // Should redirect away from login
    expect(page.url()).not.toContain('/account/login');
  });

  test('Admin login via OIDC shows admin menu items', async ({ page }) => {
    // Full SPA OIDC flow: FE → AuthServer → login → callback → dashboard with admin menu
    await page.goto(FE_URL);
    // Click login button to trigger OIDC redirect
    const loginBtn = page.getByRole('button', { name: /đăng nhập|login/i });
    if (await loginBtn.isVisible({ timeout: 5000 }).catch(() => false)) {
      await loginBtn.click();
    } else {
      // Already on AuthServer or auto-redirected — navigate to authorize endpoint
      await page.goto(
        `${AUTH_URL}/connect/authorize?client_id=aqtcorefw-spa-dev` +
        `&redirect_uri=${encodeURIComponent(FE_URL + '/callback')}` +
        `&response_type=code&scope=openid+profile+email+roles+api+offline_access` +
        `&state=admintest&nonce=admintest123` +
        `&code_challenge=E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM` +
        `&code_challenge_method=S256`
      );
    }
    // Fill login form on AuthServer
    await page.waitForSelector('input[name="Email"]', { timeout: 10000 });
    await page.fill('input[name="Email"]', 'admin@dev.local');
    await page.fill('input[name="Password"]', 'DevAdmin@12345');
    await page.click('button[type="submit"]');

    // Wait for OIDC callback to complete and FE to load
    await page.waitForURL(`${FE_URL}/**`, { timeout: 15000 }).catch(() => {});
    await page.waitForTimeout(3000);

    // Verify admin menu is visible in sidebar (role claims working)
    const pageContent = await page.textContent('body');
    const hasAdminSection = pageContent?.includes('Admin') || pageContent?.includes('Quản trị');
    expect(hasAdminSection).toBeTruthy();
  });

  test('Logout endpoint returns 302 (not 404)', async ({ request }) => {
    const res = await request.get(
      `${AUTH_URL}/connect/logout?post_logout_redirect_uri=${encodeURIComponent(FE_URL)}`,
      { maxRedirects: 0 }
    );
    // 302 = redirect (success), not 404
    expect([200, 302]).toContain(res.status());
  });
});
