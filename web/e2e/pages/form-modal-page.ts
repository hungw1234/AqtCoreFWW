import { type Page, type Locator, expect } from '@playwright/test';

/**
 * Page Object Model for Ant Design modal/drawer forms.
 * Handles: open detection, field fill, submit, validation errors, close.
 */
export class FormModalPage {
  private container: Locator;

  constructor(private page: Page) {
    // Match both modal and drawer
    this.container = page.locator('.ant-modal:visible, .ant-drawer:visible').first();
  }

  async waitForOpen() {
    await expect(this.container).toBeVisible({ timeout: 5_000 });
  }

  async isOpen(): Promise<boolean> {
    return this.container.isVisible({ timeout: 2_000 }).catch(() => false);
  }

  async fillField(label: string, value: string) {
    // Try getByLabel first (works when <label for="..."> is properly set)
    const field = this.container.getByLabel(label, { exact: false });
    if (await field.isVisible({ timeout: 2_000 }).catch(() => false)) {
      await field.fill(value);
      return;
    }
    // Fallback: Ant Design form-item pattern (label in .ant-form-item-label)
    const formItem = this.container.locator('.ant-form-item').filter({ hasText: label });
    const input = formItem.locator('input, textarea').first();
    await input.fill(value);
  }

  async fillSelect(label: string, option: string) {
    // Click the select to open dropdown
    const formItem = this.container.locator('.ant-form-item').filter({ hasText: label });
    await formItem.locator('.ant-select-selector').click();
    // Select option from dropdown
    await this.page.locator('.ant-select-dropdown:visible').getByTitle(option).click();
  }

  async fillDatePicker(label: string, date: string) {
    const formItem = this.container.locator('.ant-form-item').filter({ hasText: label });
    await formItem.locator('.ant-picker-input input').click();
    await formItem.locator('.ant-picker-input input').fill(date);
    await this.page.keyboard.press('Enter');
  }

  /** Fill a RangePicker (e.g., delegation valid range) */
  async fillRangePicker(label: string, startDate: string, endDate: string) {
    const formItem = this.container.locator('.ant-form-item').filter({ hasText: label });
    const inputs = formItem.locator('.ant-picker-input input');
    await inputs.first().click();
    await inputs.first().fill(startDate);
    await this.page.keyboard.press('Enter');
    await inputs.last().fill(endDate);
    await this.page.keyboard.press('Enter');
  }

  /** Toggle an Ant Design Switch by its label */
  async fillSwitch(label: string, checked: boolean) {
    const formItem = this.container.locator('.ant-form-item').filter({ hasText: label });
    const switchEl = formItem.locator('.ant-switch');
    const isChecked = await switchEl.getAttribute('aria-checked') === 'true';
    if (isChecked !== checked) {
      await switchEl.click();
    }
  }

  /** Fill an InputNumber by label */
  async fillNumber(label: string, value: number) {
    const field = this.container.getByLabel(label, { exact: false });
    await field.fill(String(value));
  }

  async submit() {
    // Matches: Lưu, Save, Submit, Tạo, Tạo key, OK, Xác nhận, Thêm, Thêm mới
    const submitBtn = this.container.getByRole('button', { name: /lưu|save|submit|tạo|ok|xác nhận|thêm/i });
    await submitBtn.click();
  }

  async cancel() {
    const cancelBtn = this.container.getByRole('button', { name: /hủy|cancel|đóng|close/i });
    await cancelBtn.click();
  }

  async getValidationErrors(): Promise<string[]> {
    const errors = this.container.locator('.ant-form-item-explain-error');
    return errors.allTextContents();
  }

  async close() {
    const closeBtn = this.container.locator('.ant-modal-close, .ant-drawer-close');
    if (await closeBtn.isVisible({ timeout: 2_000 }).catch(() => false)) {
      await closeBtn.click();
    }
  }
}
