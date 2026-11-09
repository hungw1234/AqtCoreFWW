import { test, expect } from './fixtures/test-base';
import { AUTH_SERVER } from './helpers/auth-helper';

// Authorization Code + PKCE flow tests — P1 TCs only
// Covers: TC-AUTH-040, TC-AUTH-041, TC-AUTH-042, TC-AUTH-043, TC-AUTH-044
// TC-AUTH-045, TC-AUTH-046 are P2 — skipped

// PKCE test values (static for deterministic tests)
// CODE_CHALLENGE is S256 of the RFC 7636 Appendix B verifier 'dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk'.
const CODE_CHALLENGE = 'E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM';
// Must use localhost (not 127.0.0.1) — only localhost:5173 is registered as RedirectUri in OpenIddict seeder
const REDIRECT_URI = 'http://localhost:5173/callback';
const SPA_CLIENT = 'aqtcorefw-spa-dev';
const STATE = 'test123';

function buildAuthorizeUrl(): string {
  const params = new URLSearchParams({
    client_id: SPA_CLIENT,
    redirect_uri: REDIRECT_URI,
    response_type: 'code',
    scope: 'openid profile email api offline_access',
    state: STATE,
    code_challenge: CODE_CHALLENGE,
    code_challenge_method: 'S256',
  });
  return `${AUTH_SERVER}/connect/authorize?${params.toString()}`;
}

test.describe('AuthServer Authorization Code Flow', () => {
  test('TC-AUTH-040: unauthenticated /connect/authorize redirects to login', async ({ page }) => {
    await page.goto(buildAuthorizeUrl());

    // Should redirect to /account/login with returnUrl containing the original authorize URL
    await expect(page).toHaveURL(new RegExp('/account/login'));
    expect(page.url()).toContain('returnUrl');

    // Login form visible
    await expect(page.locator('input[name="Email"]')).toBeVisible();
    await expect(page.locator('input[name="Password"]')).toBeVisible();
  });

  test('TC-AUTH-041: full auth code + PKCE flow returns authorization code', async ({ page }) => {
    // Navigate to authorize endpoint — expect redirect to login
    await page.goto(buildAuthorizeUrl());
    await expect(page).toHaveURL(new RegExp('/account/login'));

    // Fill credentials and submit
    await page.locator('input[name="Email"]').fill('admin@dev.local');
    await page.locator('input[name="Password"]').fill('DevAdmin@12345');

    // Capture the authorization code from the redirect URL before FE loads
    // FE (localhost:5173) is not running — navigation will fail but the URL contains the code
    let capturedUrl = '';
    page.on('request', (req) => {
      const url = req.url();
      if (url.includes('/callback') && url.includes('code=')) {
        capturedUrl = url;
      }
    });

    // Submit and wait for navigation to callback (FE may not be running)
    await Promise.all([
      page.waitForURL(
        (url) => url.href.includes('/callback') || url.href.includes('code='),
        { timeout: 15000, waitUntil: 'commit' },
      ).catch(() => null),
      page.locator('button[type="submit"]').click(),
    ]);

    // Use captured URL or current page URL
    const rawUrl = capturedUrl || page.url();
    let finalUrl: URL;
    try {
      finalUrl = new URL(rawUrl);
    } catch {
      // URL may not be parseable if navigation failed — skip
      test.skip(true, `Could not parse redirect URL: ${rawUrl}`);
      return;
    }

    const code = finalUrl.searchParams.get('code');
    const returnedState = finalUrl.searchParams.get('state');
    const error = finalUrl.searchParams.get('error');

    expect(error).toBeNull();
    expect(code).toBeTruthy();
    expect(returnedState).toBe(STATE);
  });

  test('TC-AUTH-042: /connect/authorize with invalid client_id returns error page', async ({ request }) => {
    const params = new URLSearchParams({
      client_id: 'nonexistent-client',
      redirect_uri: 'http://evil.com/cb',
      response_type: 'code',
      scope: 'openid',
      state: 'x',
      code_challenge: CODE_CHALLENGE,
      code_challenge_method: 'S256',
    });
    const res = await request.get(`${AUTH_SERVER}/connect/authorize?${params.toString()}`, {
      maxRedirects: 0,
    });

    // Must NOT redirect to evil.com — should return error page or 400
    expect(res.status()).not.toBe(302);
    // If 400 or error page, that's correct
    expect([400, 200]).toContain(res.status());
    // Ensure no redirect to untrusted origin
    const location = res.headers()['location'] ?? '';
    expect(location).not.toContain('evil.com');
  });

  test('TC-AUTH-043: /connect/authorize with unregistered redirect_uri returns error', async ({ request }) => {
    const params = new URLSearchParams({
      client_id: SPA_CLIENT,
      redirect_uri: 'http://attacker.com/steal',
      response_type: 'code',
      scope: 'openid',
      state: 'x',
      code_challenge: CODE_CHALLENGE,
      code_challenge_method: 'S256',
    });
    const res = await request.get(`${AUTH_SERVER}/connect/authorize?${params.toString()}`, {
      maxRedirects: 0,
    });

    // Must NOT redirect to attacker.com
    const location = res.headers()['location'] ?? '';
    expect(location).not.toContain('attacker.com');
    // OpenIddict renders error page or returns 400 for invalid redirect_uri
    expect([400, 200]).toContain(res.status());
  });

  test('TC-AUTH-044: /connect/authorize without code_challenge is rejected (PKCE required)', async ({ request }) => {
    const params = new URLSearchParams({
      client_id: SPA_CLIENT,
      redirect_uri: REDIRECT_URI,
      response_type: 'code',
      scope: 'openid',
      state: 'x',
      // code_challenge intentionally omitted
    });
    const res = await request.get(`${AUTH_SERVER}/connect/authorize?${params.toString()}`, {
      maxRedirects: 0,
    });

    // Either error page (200 with error body) or redirect to callback with error param
    if (res.status() === 302) {
      const location = res.headers()['location'] ?? '';
      expect(location).toContain('error=invalid_request');
    } else {
      expect([400, 200]).toContain(res.status());
    }
  });
});
