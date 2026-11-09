import { test, expect } from '../fixtures/test-base';
import AxeBuilder from '@axe-core/playwright';
import { AUTH_SERVER } from '../helpers/auth-helper';

// Accessibility axe-core audit tests — TC-A11Y-AXE-001/002/003.
// Requires: @axe-core/playwright  →  npm install --save-dev @axe-core/playwright
// Requires: Docker backend + Vite dev server.
// Tag: @accessibility
//
// Install dep if missing:  cd web && npm install --save-dev @axe-core/playwright
const APP_URL = 'http://localhost:5173';

// Skip entire suite when axe package or Vite is absent
const axeAvailable = (() => {
  try {
    require.resolve('@axe-core/playwright');
    return true;
  } catch {
    return false;
  }
})();

test.describe('Accessibility — axe-core audit', { tag: '@accessibility' }, () => {
  test.skip(!axeAvailable, '@axe-core/playwright not installed — run: npm i -D @axe-core/playwright');
  test.skip(
    () => !process.env.VITE_RUNNING,
    'Vite dev server not running',
  );

  // ── TC-A11Y-AXE-001 ──────────────────────────────────────────────────────────

  test('TC-A11Y-AXE-001: Login page passes axe-core audit', async ({ page }) => {
    await page.goto(`${AUTH_SERVER}/account/login`);
    await page.waitForLoadState('domcontentloaded');

    const results = await new AxeBuilder({ page })
      .withTags(['wcag2a', 'wcag2aa', 'wcag21aa'])
      .exclude('#cookie-banner')  // exclude third-party widgets that are not ours
      .analyze();

    // Report violations for debugging even when failing
    if (results.violations.length > 0) {
      const summary = results.violations
        .map(v => `[${v.impact}] ${v.id}: ${v.description}`)
        .join('\n');
      console.log('Axe violations on login page:\n' + summary);
    }

    expect(results.violations).toHaveLength(0);
  });

  // ── TC-A11Y-AXE-002 ──────────────────────────────────────────────────────────

  test('TC-A11Y-AXE-002: Dashboard page passes axe-core audit', async ({ page }) => {
    await page.goto(`${APP_URL}/dashboard`);
    await page.waitForLoadState('domcontentloaded');
    await page.waitForTimeout(500); // allow JS framework to hydrate

    const results = await new AxeBuilder({ page })
      .withTags(['wcag2a', 'wcag2aa', 'wcag21aa'])
      .exclude('#cookie-banner')
      .analyze();

    if (results.violations.length > 0) {
      const summary = results.violations
        .map(v => `[${v.impact}] ${v.id}: ${v.description}`)
        .join('\n');
      console.log('Axe violations on dashboard:\n' + summary);
    }

    expect(results.violations).toHaveLength(0);
  });

});
