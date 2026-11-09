import { type Page } from '@playwright/test';

/** Fill a text input by its label */
export async function fillFormField(page: Page, label: string, value: string) {
  const field = page.getByLabel(label, { exact: false });
  await field.fill(value);
}

/** Select an option from Ant Design Select by label */
export async function selectOption(page: Page, label: string, option: string) {
  const formItem = page.locator('.ant-form-item').filter({ hasText: label });
  await formItem.locator('.ant-select-selector').click();
  await page.locator('.ant-select-dropdown:visible').getByTitle(option).click();
}

/** Fill an Ant Design DatePicker by label */
export async function fillDatePicker(page: Page, label: string, date: string) {
  const formItem = page.locator('.ant-form-item').filter({ hasText: label });
  await formItem.locator('.ant-picker-input input').click();
  await formItem.locator('.ant-picker-input input').fill(date);
  await page.keyboard.press('Enter');
}

/** Click the "Add/Create" button on a page (matches Vietnamese and English labels) */
export async function clickCreateButton(page: Page) {
  const btn = page.getByRole('button', { name: /thêm|tạo|create|add new/i });
  await btn.click();
}
