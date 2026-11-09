import { type Page, expect } from '@playwright/test';

const WEB_URL = process.env.WEB_URL ?? 'http://localhost:5173';
const AUTH_SERVER = process.env.AUTH_SERVER ?? 'http://127.0.0.1:5002';

/** Extract port pattern for URL matching — matches both localhost and 127.0.0.1 */
function urlPattern(url: string): RegExp {
  const { port } = new URL(url);
  return new RegExp(`(localhost|127\\.0\\.0\\.1):${port}`);
}

/**
 * Page Object Model for OIDC login flow.
 * Handles: Web app -> AuthServer redirect -> credentials -> callback -> authenticated app.
 */
export class LoginPage {
  constructor(private page: Page) {}

  async goto() {
    await this.page.goto(WEB_URL);
    await this.page.waitForLoadState('domcontentloaded');
  }

  async login(email: string, password: string) {
    // Click "Dang nhap" button if app shows login page first
    const loginBtn = this.page.getByRole('button', { name: /dang nhap|đăng nhập/i });
    if (await loginBtn.isVisible({ timeout: 5_000 }).catch(() => false)) {
      await loginBtn.click();
    }

    // Wait for AuthServer login form
    await this.page.waitForURL(urlPattern(AUTH_SERVER), { timeout: 15_000 });
    await expect(this.page.locator('input[name="Email"]')).toBeVisible({ timeout: 10_000 });

    // Fill credentials and submit
    await this.page.fill('input[name="Email"]', email);
    await this.page.fill('input[name="Password"]', password);
    await this.page.click('button[type="submit"]');
  }

  async waitForAuthenticated() {
    await this.page.waitForURL(urlPattern(WEB_URL), { timeout: 15_000 });
    await this.page.waitForLoadState('domcontentloaded');
    // Wait for app layout to render
    await this.page.locator('.ant-layout-content, main').first().waitFor({ timeout: 10_000 });
  }

  /** Full login shortcut with default dev credentials */
  async loginAsAdmin() {
    await this.goto();
    await this.login('admin@dev.local', 'DevAdmin@12345');
    await this.waitForAuthenticated();
  }
}
