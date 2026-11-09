import { test, expect } from '../fixtures/test-base';
import { AUTH_SERVER } from '../helpers/auth-helper';

// Responsive / compatibility layout tests — TC-COMPAT-001/002/003.
// Verifies sidebar visibility, nav collapse, and table layout at three viewports.
// Requires: Vite dev server.

const APP_URL = 'http://localhost:5173';

const VIEWPORTS = {
  desktop: { width: 1920, height: 1080 },
  tablet:  { width: 768,  height: 1024 },
  mobile:  { width: 375,  height: 667  },
} as const;

test.describe('Responsive layout', () => {
  test.skip(
    () => !process.env.VITE_RUNNING,
    'Vite dev server not running',
  );

  // ── TC-COMPAT-001 ─────────────────────────────────────────────────────────────

  test('TC-COMPAT-001: Desktop layout (1920x1080) — sidebar visible, table full width', async ({
    page,
  }) => {
    await page.setViewportSize(VIEWPORTS.desktop);
    await page.goto(APP_URL);
    await page.waitForLoadState('domcontentloaded');

    // App shell should render without horizontal scroll overflow
    const bodyScrollWidth = await page.evaluate(
      () => document.body.scrollWidth <= window.innerWidth,
    );
    expect(bodyScrollWidth).toBe(true);

    // On desktop the sidebar / nav menu should not be collapsed into a hamburger
    const sidebar = page.locator('aside, [data-testid="sidebar"], nav.sidebar, .ant-layout-sider');
    const hamburger = page.locator('[data-testid="menu-toggle"], .hamburger, button[aria-label*="menu"]');

    const sidebarCount = await sidebar.count();
    if (sidebarCount > 0) {
      // Sidebar present — expect it visible (not display:none)
      await expect(sidebar.first()).toBeVisible();
      // Hamburger should NOT be the primary nav on desktop
      const hamburgerVisible = await hamburger.first().isVisible().catch(() => false);
      expect(hamburgerVisible).toBe(false);
    }
  });

  // ── TC-COMPAT-002 ─────────────────────────────────────────────────────────────

  test('TC-COMPAT-002: Tablet layout (768x1024) — navigation adapts correctly', async ({
    page,
  }) => {
    await page.setViewportSize(VIEWPORTS.tablet);
    await page.goto(APP_URL);
    await page.waitForLoadState('domcontentloaded');

    // No horizontal overflow at tablet width
    const noOverflow = await page.evaluate(
      () => document.body.scrollWidth <= window.innerWidth + 2, // 2px tolerance for borders
    );
    expect(noOverflow).toBe(true);

    // Page should have at least one navigable landmark
    const navLandmarks = await page
      .locator('nav, [role="navigation"], header')
      .count();
    expect(navLandmarks).toBeGreaterThan(0);
  });

  // ── TC-COMPAT-003 ─────────────────────────────────────────────────────────────

  test('TC-COMPAT-003: Mobile layout (375x667) — sidebar collapsed, content fits', async ({
    page,
  }) => {
    await page.setViewportSize(VIEWPORTS.mobile);
    await page.goto(APP_URL);
    await page.waitForLoadState('domcontentloaded');

    // No horizontal overflow on mobile
    const noOverflow = await page.evaluate(
      () => document.body.scrollWidth <= window.innerWidth + 2,
    );
    expect(noOverflow).toBe(true);

    // Sidebar (if present) should be hidden or collapsed on mobile
    const sidebar = page.locator(
      'aside, [data-testid="sidebar"], .ant-layout-sider',
    );
    const sidebarCount = await sidebar.count();
    if (sidebarCount > 0) {
      const sidebarVisible = await sidebar.first().isVisible().catch(() => false);
      // On mobile, sidebar is either hidden or collapsed behind a toggle
      if (sidebarVisible) {
        const sidebarWidth = await sidebar.first().evaluate(
          el => (el as HTMLElement).getBoundingClientRect().width,
        );
        // Collapsed sider in Ant Design is ~80px; fully hidden is 0
        expect(sidebarWidth).toBeLessThanOrEqual(80);
      }
    }

    // Tables should have horizontal scroll or stack — not overflow the viewport
    const tables = page.locator('table, [role="grid"]');
    const tableCount = await tables.count();
    if (tableCount > 0) {
      const firstTable = tables.first();
      const tableBox = await firstTable.boundingBox();
      if (tableBox) {
        // Table container must not be wider than viewport
        expect(tableBox.width).toBeLessThanOrEqual(VIEWPORTS.mobile.width + 2);
      }
    }
  });

  // ── Login page responsive ─────────────────────────────────────────────────────

  test('Login page renders without overflow at all three viewports', async ({
    page,
  }) => {
    for (const [name, size] of Object.entries(VIEWPORTS)) {
      await page.setViewportSize(size);
      await page.goto(`${AUTH_SERVER}/account/login`);
      await page.waitForLoadState('domcontentloaded');

      const noOverflow = await page.evaluate(
        () => document.body.scrollWidth <= window.innerWidth + 2,
      );
      expect(noOverflow, `Horizontal overflow at ${name} (${size.width}x${size.height})`).toBe(true);

      await expect(page.locator('input[name="Email"]')).toBeVisible();
    }
  });
});
