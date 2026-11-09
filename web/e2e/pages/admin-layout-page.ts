import { type Page, expect } from '@playwright/test';

const WEB_URL = process.env.WEB_URL ?? 'http://localhost:5173';

/**
 * Page Object Model for the admin layout shell.
 * Encapsulates sidebar navigation, topbar, and breadcrumb interactions.
 */
export class AdminLayoutPage {
  constructor(private page: Page) {}

  /** Navigate to a route by clicking sidebar menu item matching label text */
  async navigateToMenu(menuLabel: string) {
    const menuItem = this.page.locator('.ant-menu-item, [class*="menu-item"]')
      .filter({ hasText: new RegExp(menuLabel, 'i') });
    await menuItem.first().click();
    await this.page.waitForLoadState('domcontentloaded');
  }

  /** Navigate directly to a path and wait for content */
  async navigateToRoute(path: string) {
    await this.page.goto(`${WEB_URL}${path}`);
    await this.page.waitForLoadState('domcontentloaded');
    await this.page.locator('.ant-layout-content, main').first().waitFor({ timeout: 10_000 });
  }

  async getCurrentBreadcrumb(): Promise<string[]> {
    const items = this.page.locator('.ant-breadcrumb-link, .ant-breadcrumb span');
    return items.allTextContents();
  }

  async isSidebarVisible(): Promise<boolean> {
    return this.page.locator('.ant-layout-sider, [class*="sidebar"], nav')
      .first().isVisible({ timeout: 5_000 }).catch(() => false);
  }

  async getPageTitle(): Promise<string> {
    const heading = this.page.locator('h1, h2, .ant-page-header-heading-title').first();
    if (await heading.isVisible({ timeout: 3_000 }).catch(() => false)) {
      return (await heading.textContent()) ?? '';
    }
    return '';
  }

  /** Check content area is visible (not white screen) */
  async assertContentVisible() {
    await expect(this.page.locator('.ant-layout-content, main').first())
      .toBeVisible({ timeout: 10_000 });
  }
}
