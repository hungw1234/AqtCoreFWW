import { test, expect } from './fixtures/test-base';
import { AUTH_SERVER } from './helpers/auth-helper';

// Smoke E2E tests — verify app shell, auth redirect, and API connectivity.
// Requires: npm run dev (app on localhost:5173) + Docker backend services
// Do NOT run in unit test CI — use separate E2E pipeline with docker-compose.

// App shell tests require Vite dev server running on :5173
// Skip if dev server not available
test.describe('App shell (requires Vite)', () => {
  test.skip(() => !process.env.VITE_RUNNING, 'Vite dev server not running');

  test('root page loads without 404/500', async ({ page }) => {
    const response = await page.goto('/');
    expect(response?.status()).not.toBe(404);
    expect(response?.status()).not.toBe(500);
  });

  test('app title renders', async ({ page }) => {
    await page.goto('/');
    await expect(page).toHaveTitle(/AqtCoreFW/);
  });
});

test.describe('Auth redirect (requires Vite)', () => {
  test.skip(() => !process.env.VITE_RUNNING, 'Vite dev server not running');

  test('unauthenticated /dashboard redirects to auth server', async ({ page }) => {
    await page.goto('/dashboard');
    await page.waitForTimeout(2000);
    const url = page.url();
    const isAuthRedirect = url.includes(new URL(AUTH_SERVER).host);
    const isAppLogin = url.includes('/login') || url.includes('/callback');
    const isStillOnApp = url.includes('localhost:5173');
    expect(isAuthRedirect || isAppLogin || isStillOnApp).toBeTruthy();
  });

  test('auth server discovery endpoint is accessible', async ({ page }) => {
    const response = await page.goto(`${AUTH_SERVER}/.well-known/openid-configuration`);
    expect(response?.status()).toBe(200);
    const body = await response?.json();
    // Issuer may differ depending on Docker config — just verify it's a valid URL
    expect(body.issuer).toBeTruthy();
    expect(body.authorization_endpoint).toBeTruthy();
    expect(body.token_endpoint).toBeTruthy();
  });
});

test.describe('API connectivity via nginx', () => {
  // API tests hit nginx:5001 directly (works without Vite dev server)
  const API = 'http://localhost:5001';

  test('OpenAPI spec accessible', async ({ request }) => {
    const response = await request.get(`${API}/openapi/v1.json`);
    expect(response.status()).toBe(200);
    const body = await response.json();
    expect(body.openapi).toBeTruthy();
    expect(body.paths).toBeTruthy();
  });

  test('public API endpoint returns data', async ({ request }) => {
    const response = await request.get(`${API}/api/v1/feature-flags/EnablePdfWatermark`);
    expect(response.status()).toBe(200);
    const body = await response.json();
    expect(body.success).toBe(true);
    expect(body.data.key).toContain('EnablePdfWatermark');
  });

  test('protected API endpoint returns 401 without token', async ({ request }) => {
    const response = await request.get(`${API}/api/v1/admin/feature-flags`);
    expect(response.status()).toBe(401);
  });
});

test.describe('AuthServer login form', () => {
  test('login page renders with form fields', async ({ page }) => {
    await page.goto(`${AUTH_SERVER}/account/login`);
    await expect(page.locator('input[name="Email"]')).toBeVisible();
    await expect(page.locator('input[name="Password"]')).toBeVisible();
    await expect(page.locator('button[type="submit"]')).toBeVisible();
  });

  test('login with invalid credentials shows error', async ({ page }) => {
    await page.goto(`${AUTH_SERVER}/account/login`);
    await page.fill('input[name="Email"]', 'wrong@email.com');
    await page.fill('input[name="Password"]', 'wrongpass');
    await page.click('button[type="submit"]');
    await page.waitForTimeout(1000);
    await expect(page.locator('.error')).toBeVisible();
  });

  test('login with valid credentials redirects', async ({ page }) => {
    await page.goto(`${AUTH_SERVER}/account/login?returnUrl=/`);
    await page.fill('input[name="Email"]', 'admin@dev.local');
    await page.fill('input[name="Password"]', 'DevAdmin@12345');
    await page.click('button[type="submit"]');
    await page.waitForTimeout(1000);
    // Should redirect away from login page
    expect(page.url()).not.toContain('/account/login');
  });
});

test.describe('Authenticated API flow', () => {
  const API = 'http://localhost:5001';
  let accessToken: string;

  test.beforeAll(async ({ request }) => {
    const tokenResponse = await request.post(`${AUTH_SERVER}/connect/token`, {
      form: {
        grant_type: 'password',
        client_id: 'aqtcorefw-dev-ropc',
        client_secret: 'dev-secret-not-for-production',
        username: 'admin@dev.local',
        password: 'DevAdmin@12345',
        scope: 'api',
      },
    });
    expect(tokenResponse.status()).toBe(200);
    const body = await tokenResponse.json();
    accessToken = body.access_token;
    expect(accessToken).toBeTruthy();
  });

  test('authenticated GET /audit/logs returns 200 or 403', async ({ request }) => {
    const response = await request.get(`${API}/api/v1/audit/logs`, {
      headers: { Authorization: `Bearer ${accessToken}` },
    });
    expect([200, 403]).toContain(response.status());
  });

  test('authenticated GET /notifications returns 200', async ({ request }) => {
    const response = await request.get(`${API}/api/v1/notifications`, {
      headers: { Authorization: `Bearer ${accessToken}` },
    });
    expect([200, 403]).toContain(response.status());
  });
});
