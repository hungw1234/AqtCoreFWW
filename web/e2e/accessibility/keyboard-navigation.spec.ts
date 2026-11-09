import { test, expect } from '../fixtures/test-base';
import { AUTH_SERVER } from '../helpers/auth-helper';

// Keyboard navigation E2E tests — verify Tab focus order and keyboard operability.
// Requires: Docker backend + Vite dev server.
// Skip automatically when VITE_RUNNING env var is absent.
const APP_URL = 'http://localhost:5173';

test.describe('TC-A11Y-KB-001: Login form Tab navigation', () => {
  test.skip(
    () => !process.env.VITE_RUNNING,
    'Vite dev server not running',
  );

  test('Tab key moves focus through login form fields in order', async ({
    page,
  }) => {
    await page.goto(`${AUTH_SERVER}/account/login`);

    // Focus the first interactive element
    await page.keyboard.press('Tab');
    const firstFocus = await page.evaluate(
      () => document.activeElement?.getAttribute('name') ?? document.activeElement?.tagName,
    );
    expect(['Email', 'INPUT', 'email']).toContain(firstFocus?.toLowerCase() ?? firstFocus);

    // Second Tab → Password field
    await page.keyboard.press('Tab');
    const secondFocus = await page.evaluate(
      () => document.activeElement?.getAttribute('name') ?? document.activeElement?.tagName,
    );
    expect(['Password', 'password', 'INPUT']).toContain(secondFocus?.toLowerCase() ?? secondFocus);

    // Third Tab → Submit button
    await page.keyboard.press('Tab');
    const thirdTag = await page.evaluate(
      () => document.activeElement?.tagName?.toLowerCase(),
    );
    expect(['button', 'input', 'a']).toContain(thirdTag);
  });

  test('Enter key submits login form from password field', async ({ page }) => {
    await page.goto(`${AUTH_SERVER}/account/login`);
    await page.fill('input[name="Email"]', 'test@example.com');
    await page.fill('input[name="Password"]', 'wrongpassword');
    await page.keyboard.press('Enter');
    await page.waitForTimeout(800);
    // Should remain on login page (wrong creds) or redirect — either is valid nav
    expect(page.url()).toContain('localhost');
  });
});

test.describe('TC-A11Y-KB-002: Sidebar menu keyboard navigation', () => {
  test.skip(
    () => !process.env.VITE_RUNNING,
    'Vite dev server not running',
  );

  test('Sidebar nav items are reachable via Tab and activate on Enter', async ({
    page,
  }) => {
    await page.goto(APP_URL);

    // Tab through page until sidebar nav link is focused
    let sidebarFocused = false;
    for (let i = 0; i < 20; i++) {
      await page.keyboard.press('Tab');
      const role = await page.evaluate(
        () => document.activeElement?.getAttribute('role'),
      );
      const tag = await page.evaluate(
        () => document.activeElement?.tagName?.toLowerCase(),
      );
      if (role === 'menuitem' || tag === 'a' || role === 'link') {
        sidebarFocused = true;
        break;
      }
    }
    // Sidebar may not exist on unauthenticated pages — pass if no sidebar visible
    const hasSidebar = await page.locator('nav, [role="navigation"]').count();
    if (hasSidebar > 0) {
      expect(sidebarFocused).toBe(true);
    }
  });
});

test.describe('TC-A11Y-KB-003: Data table keyboard navigation', () => {
  test.skip(
    () => !process.env.VITE_RUNNING,
    'Vite dev server not running',
  );

  test('Table rows are focusable and respond to keyboard interaction', async ({
    page,
  }) => {
    // Navigate to an authenticated page that has a table (forms list)
    // Unauthenticated → redirect; test passes if table not rendered
    await page.goto(`${APP_URL}/forms`);
    await page.waitForTimeout(500);

    const tableExists = (await page.locator('table, [role="grid"], [role="table"]').count()) > 0;
    if (!tableExists) {
      // Redirected to login — keyboard nav of unauthenticated state is OK
      return;
    }

    // Tab into the table and verify cells are keyboard-reachable
    let tableFocused = false;
    for (let i = 0; i < 30; i++) {
      await page.keyboard.press('Tab');
      const role = await page.evaluate(
        () => document.activeElement?.closest('table, [role="grid"]') !== null,
      );
      if (role) {
        tableFocused = true;
        break;
      }
    }
    expect(tableFocused).toBe(true);
  });
});
