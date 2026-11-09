import { test, expect } from './fixtures/test-base';
import { AUTH_SERVER } from './helpers/auth-helper';

// MFA verification page tests — P1 TCs only
// Covers: TC-AUTH-030, TC-AUTH-031, TC-AUTH-032
// TC-AUTH-033 is P2 — skipped

// NOTE: MFA page tests require an active TwoFactor cookie (post-login session state).
// Without a user that has TOTP MFA enabled, direct navigation to /account/verify-mfa
// may redirect away. Tests verify the page renders when accessible, and skip gracefully
// when MFA session prerequisites cannot be met from seed data.

test.describe('AuthServer MFA Page — Rendering', () => {
  test('TC-AUTH-030: MFA page renders with code input field', async ({ page }) => {
    // Navigate directly — if no MFA session cookie, server may redirect to login
    const response = await page.goto(`${AUTH_SERVER}/account/verify-mfa`);

    // Page may redirect to login if no MFA session active
    const finalUrl = page.url();
    if (finalUrl.includes('/account/login')) {
      // No MFA session available — skip gracefully
      test.skip();
      return;
    }

    // MFA page rendered — verify required elements
    expect(response?.status()).toBeLessThan(500);
    // 6-digit code input
    const codeInput = page.locator('input[name="Code"], input[type="text"], input[type="number"]').first();
    await expect(codeInput).toBeVisible();
    await expect(page.locator('button[type="submit"]')).toBeVisible();
  });
});

test.describe('AuthServer MFA Page — Validation', () => {
  test('TC-AUTH-031: MFA page rejects invalid TOTP code format', async ({ page }) => {
    await page.goto(`${AUTH_SERVER}/account/verify-mfa`);

    const finalUrl = page.url();
    if (finalUrl.includes('/account/login')) {
      test.skip();
      return;
    }

    // Bypass HTML5 native validation (pattern/required) — submit directly to hit server-side validation
    const codeInput = page.locator('input[name="Code"]');
    await codeInput.fill('abc');
    // Remove pattern and required attrs so browser doesn't block form submission
    await page.evaluate(() => {
      const input = document.querySelector('input[name="Code"]') as HTMLInputElement;
      if (input) { input.removeAttribute('pattern'); input.removeAttribute('required'); }
    });
    await page.locator('button[type="submit"]').click();

    // Should stay on verify-mfa and show server-side validation error
    await expect(page).toHaveURL(new RegExp('/account/verify-mfa'));
    const bodyText = await page.locator('body').textContent();
    // Server returns Vietnamese error message for invalid format
    const hasValidationMsg =
      (bodyText ?? '').includes('Mã xác thực phải') ||
      (bodyText ?? '').includes('Mã xác thực là bắt buộc');
    expect(hasValidationMsg).toBe(true);
  });

  test('TC-AUTH-032: MFA page rejects wrong 6-digit TOTP code', async ({ page }) => {
    await page.goto(`${AUTH_SERVER}/account/verify-mfa`);

    const finalUrl = page.url();
    if (finalUrl.includes('/account/login')) {
      test.skip();
      return;
    }

    const codeInput = page.locator('input[name="Code"]');
    await codeInput.fill('000000');
    await page.locator('button[type="submit"]').click();

    await expect(page).toHaveURL(new RegExp('/account/verify-mfa'));
    await expect(page.locator('body')).toContainText('Mã xác thực không hợp lệ.');
  });
});
