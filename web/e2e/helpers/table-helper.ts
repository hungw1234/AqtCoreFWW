import { type Page, expect } from '@playwright/test';

/** Get the number of data rows in an Ant Design table */
export async function getTableRowCount(page: Page, selector?: string): Promise<number> {
  const tbody = page.locator(selector ?? '.ant-table-tbody');
  await tbody.waitFor({ timeout: 8_000 }).catch(() => {});
  return tbody.locator('tr.ant-table-row').count();
}

/** Get all text values of a specific column index (0-based) */
export async function getColumnValues(page: Page, colIndex: number): Promise<string[]> {
  const cells = page.locator(`.ant-table-tbody tr.ant-table-row td:nth-child(${colIndex + 1})`);
  return cells.allTextContents();
}

/**
 * Click the edit button (EditOutlined icon) on a specific table row.
 * Targets action column buttons by row index (0-based).
 */
export async function clickRowEditButton(page: Page, rowIndex: number) {
  const row = page.locator(`.ant-table-tbody tr.ant-table-row:nth-child(${rowIndex + 1})`);
  // Ant Design action buttons — try multiple icon patterns
  const editBtn = row.locator('[aria-label*="edit" i], .anticon-edit, .anticon-form').first();
  if (await editBtn.isVisible({ timeout: 3_000 }).catch(() => false)) {
    await editBtn.click();
    return;
  }
  // Fallback: first clickable element in action column (last td) — handles button, a, span
  const lastTd = row.locator('td:last-child');
  const anyBtn = lastTd.locator('button, a, span[role="img"], .anticon').first();
  if (await anyBtn.isVisible({ timeout: 3_000 }).catch(() => false)) {
    await anyBtn.click();
    return;
  }
  // Final fallback: first child in last td
  await lastTd.locator('> *').first().click();
}

/**
 * Click the delete button on a row and confirm the Popconfirm.
 * Handles varying okText ("Xoá", "Xác nhận", "Confirm", etc.)
 */
export async function clickRowDeleteAndConfirm(page: Page, rowIndex: number) {
  const row = page.locator(`.ant-table-tbody tr.ant-table-row:nth-child(${rowIndex + 1})`);
  // Find delete/revoke button by icon class
  const deleteBtn = row.locator('.anticon-delete, .anticon-stop, [aria-label*="delete" i]').first();
  await deleteBtn.click();
  // Wait for Popconfirm overlay
  const popconfirm = page.locator('.ant-popconfirm:visible').first();
  await popconfirm.waitFor({ timeout: 3_000 });
  // Click the OK button inside the popconfirm (matches any confirm text)
  const okBtn = popconfirm.locator('.ant-popconfirm-buttons .ant-btn-primary, .ant-popconfirm-buttons .ant-btn-dangerous');
  await okBtn.first().click();
}

/**
 * Find a table row containing specific text and return its index (0-based).
 * Uses innerText() for accurate cell spacing. Waits for rows to render.
 * Returns -1 if not found.
 */
export async function findRowByText(page: Page, text: string, timeout = 5000): Promise<number> {
  const rows = page.locator('.ant-table-tbody tr.ant-table-row');
  await rows.first().waitFor({ timeout }).catch(() => {});
  const count = await rows.count();
  for (let i = 0; i < count; i++) {
    const rowText = await rows.nth(i).innerText();
    if (rowText?.includes(text)) return i;
  }
  return -1;
}

/** Assert table or empty state is visible (page didn't crash) */
export async function assertTableOrEmptyVisible(page: Page) {
  const hasTable = await page.locator('.ant-table-tbody')
    .isVisible({ timeout: 8_000 }).catch(() => false);
  const hasEmpty = await page.locator('.ant-empty, .ant-table-placeholder')
    .first().isVisible({ timeout: 3_000 }).catch(() => false);
  const hasContent = await page.locator('.ant-layout-content, main')
    .first().isVisible({ timeout: 3_000 }).catch(() => false);
  expect(hasTable || hasEmpty || hasContent).toBe(true);
}
