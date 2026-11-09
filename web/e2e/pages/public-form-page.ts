import { type Page } from '@playwright/test';

const WEB_URL = process.env.WEB_URL ?? 'http://localhost:5173';

/**
 * Page Object Model for public-facing citizen form pages.
 * These pages are outside the authenticated layout (anonymous access).
 */
export class PublicFormPage {
  constructor(private page: Page) {}

  async goto(formCode: string) {
    await this.page.goto(`${WEB_URL}/public/forms/${formCode}`);
    await this.page.waitForLoadState('domcontentloaded');
  }

  async fillTextField(label: string, value: string) {
    const field = this.page.getByLabel(label, { exact: false });
    await field.fill(value);
  }

  async fillSelectField(label: string, option: string) {
    const formItem = this.page.locator('.ant-form-item').filter({ hasText: label });
    await formItem.locator('.ant-select-selector').click();
    await this.page.locator('.ant-select-dropdown:visible').getByTitle(option).click();
  }

  async uploadFile(label: string, filePath: string) {
    const formItem = this.page.locator('.ant-form-item').filter({ hasText: label });
    const input = formItem.locator('input[type="file"]');
    await input.setInputFiles(filePath);
  }

  async nextStep() {
    const btn = this.page.getByRole('button', { name: /tiếp|next|tiếp tục/i });
    await btn.click();
  }

  async previousStep() {
    const btn = this.page.getByRole('button', { name: /quay lại|back|previous/i });
    await btn.click();
  }

  async getCurrentStep(): Promise<number> {
    const items = this.page.locator('.ant-steps-item');
    const count = await items.count();
    for (let i = 0; i < count; i++) {
      if (await items.nth(i).evaluate(el => el.classList.contains('ant-steps-item-active'))) {
        return i;
      }
    }
    return 0;
  }

  async submit() {
    const btn = this.page.getByRole('button', { name: /gửi|submit|nộp/i });
    await btn.click();
  }

  async getConfirmationMessage(): Promise<string> {
    const msg = this.page.locator('.ant-result-title, .ant-alert-message, .ant-notification-notice-message');
    await msg.first().waitFor({ timeout: 10_000 });
    return (await msg.first().textContent()) ?? '';
  }

  async getValidationErrors(): Promise<string[]> {
    const errors = this.page.locator('.ant-form-item-explain-error');
    return errors.allTextContents();
  }
}
