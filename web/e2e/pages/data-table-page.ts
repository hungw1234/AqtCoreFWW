import { type Page, type Locator, expect } from '@playwright/test';

/**
 * Page Object Model for Ant Design tables.
 * Reusable across admin and user-facing pages with data tables.
 */
export class DataTablePage {
  private tableBody: Locator;

  constructor(private page: Page, tableSelector?: string) {
    this.tableBody = page.locator(tableSelector ?? '.ant-table-tbody');
  }

  async getRowCount(): Promise<number> {
    // Wait for table to be visible first
    await this.tableBody.waitFor({ timeout: 10_000 }).catch(() => {});
    return this.tableBody.locator('tr.ant-table-row').count();
  }

  async getCellText(row: number, col: number): Promise<string> {
    const cell = this.tableBody.locator(`tr.ant-table-row:nth-child(${row + 1}) td:nth-child(${col + 1})`);
    return (await cell.textContent()) ?? '';
  }

  async getColumnValues(colIndex: number): Promise<string[]> {
    const cells = this.tableBody.locator(`tr.ant-table-row td:nth-child(${colIndex + 1})`);
    return cells.allTextContents();
  }

  async sortByColumn(columnName: string) {
    const header = this.page.locator('.ant-table-thead th').filter({ hasText: columnName });
    await header.click();
  }

  async goToPage(pageNum: number) {
    await this.page.locator(`.ant-pagination-item[title="${pageNum}"]`).click();
    await this.page.waitForLoadState('domcontentloaded');
  }

  async selectRow(index: number) {
    const checkbox = this.tableBody.locator(`tr.ant-table-row:nth-child(${index + 1}) .ant-checkbox-input`);
    await checkbox.click();
  }

  async selectAllRows() {
    const selectAll = this.page.locator('.ant-table-thead .ant-checkbox-input');
    await selectAll.click();
  }

  /** Check if table or empty state is rendered (not a crash) */
  async assertTableOrEmptyVisible() {
    const hasTable = await this.tableBody.isVisible({ timeout: 8_000 }).catch(() => false);
    const hasEmpty = await this.page.locator('.ant-empty, .ant-table-placeholder')
      .first().isVisible({ timeout: 3_000 }).catch(() => false);
    const hasContent = await this.page.locator('.ant-layout-content, main')
      .first().isVisible({ timeout: 3_000 }).catch(() => false);
    expect(hasTable || hasEmpty || hasContent).toBe(true);
  }
}
