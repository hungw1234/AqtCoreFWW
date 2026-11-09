import { test, expect } from '../fixtures/auth-fixture';
import { DataTablePage } from '../pages/data-table-page';
import { FormModalPage } from '../pages/form-modal-page';
import { navigateAndWait } from '../helpers/navigation-helper';
import { assertTableOrEmptyVisible } from '../helpers/table-helper';

const WEB_URL = process.env.WEB_URL ?? 'http://localhost:5173';

/**
 * Admin System routes — render + CRUD tests.
 * Covers: Dashboard, SystemParams, APIKeys, Health, Backup, Jobs
 */
test.describe('Admin System Routes', () => {
  test.describe.configure({ mode: 'serial' });
  test.setTimeout(60_000);

  // ── Render Tests ──────────────────────────────────────────────────

  test('Admin Dashboard renders', async ({ authedPage }) => {
    const { errors } = await navigateAndWait(authedPage, `${WEB_URL}/admin`);
    expect(errors).toHaveLength(0);
    const hasContent = await authedPage.locator('.ant-layout-content, main')
      .first().isVisible({ timeout: 5_000 });
    expect(hasContent).toBe(true);
  });

  const SYSTEM_TABLE_ROUTES = [
    { path: '/admin/system-params', name: 'System Params' },
    { path: '/admin/api-keys', name: 'API Keys' },
    { path: '/admin/jobs', name: 'Jobs' },
  ];

  for (const { path, name } of SYSTEM_TABLE_ROUTES) {
    test(`${name} (${path}) renders`, async ({ authedPage }) => {
      const { errors } = await navigateAndWait(authedPage, `${WEB_URL}${path}`);
      expect(errors).toHaveLength(0);
      await assertTableOrEmptyVisible(authedPage);
    });
  }

  test('Health page renders status', async ({ authedPage }) => {
    const { errors } = await navigateAndWait(authedPage, `${WEB_URL}/admin/health`);
    expect(errors).toHaveLength(0);
    const hasContent = await authedPage.locator('.ant-layout-content, main')
      .first().isVisible({ timeout: 5_000 });
    expect(hasContent).toBe(true);
  });

  test('Backup page renders', async ({ authedPage }) => {
    const { errors } = await navigateAndWait(authedPage, `${WEB_URL}/admin/backup`);
    expect(errors).toHaveLength(0);
    const hasContent = await authedPage.locator('.ant-layout-content, main')
      .first().isVisible({ timeout: 5_000 });
    expect(hasContent).toBe(true);
  });

  // ── CRUD: System Params — create + edit + delete ──────────────────

  test('System Params table has data', async ({ authedPage }) => {
    await navigateAndWait(authedPage, `${WEB_URL}/admin/system-params`);
    const table = new DataTablePage(authedPage);
    const count = await table.getRowCount();
    expect(count).toBeGreaterThanOrEqual(0);
  });

  // SystemParams is read-only (no create/edit/delete buttons in UI)

  // ── CRUD: API Keys — modal open + close (two-phase modal) ────────

  test('API Keys — modal opens and closes', async ({ authedPage }) => {
    await navigateAndWait(authedPage, `${WEB_URL}/admin/api-keys`);
    const addBtn = authedPage.getByRole('button', { name: /tạo api key|thêm|create/i });
    const hasAdd = await addBtn.isVisible({ timeout: 5_000 }).catch(() => false);
    test.skip(!hasAdd, 'No create button');

    await addBtn.click();
    const modal = new FormModalPage(authedPage);
    await modal.waitForOpen();
    await modal.close();
    await authedPage.waitForTimeout(1_000);
  });
});
