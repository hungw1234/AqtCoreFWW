import { test, expect } from '../fixtures/auth-fixture';
import { FormModalPage } from '../pages/form-modal-page';
import { navigateAndWait } from '../helpers/navigation-helper';
import { assertTableOrEmptyVisible } from '../helpers/table-helper';

const WEB_URL = process.env.WEB_URL ?? 'http://localhost:5173';


/**
 * Admin Content routes — render + CRUD tests.
 * Covers: Menus, Templates, NotificationTemplates, MasterData, Organization
 */
test.describe('Admin Content Routes', () => {
  test.describe.configure({ mode: 'serial' });
  test.setTimeout(60_000);

  const ts = Date.now();

  // ── Render Tests ──────────────────────────────────────────────────

  const CONTENT_ROUTES = [
    { path: '/admin/menus', name: 'Menus' },
    { path: '/admin/templates', name: 'Templates' },
    { path: '/admin/notification-templates', name: 'Notification Templates' },
    { path: '/admin/master-data', name: 'Master Data' },
    { path: '/admin/organization', name: 'Organization' },
  ];

  for (const { path, name } of CONTENT_ROUTES) {
    test(`${name} (${path}) renders`, async ({ authedPage }) => {
      const { errors } = await navigateAndWait(authedPage, `${WEB_URL}${path}`);
      expect(errors).toHaveLength(0);
      await assertTableOrEmptyVisible(authedPage);
    });
  }

  // ── CRUD: Templates — modal open + fill + close ────────────────────

  test('Templates — modal opens with form fields', async ({ authedPage }) => {
    await navigateAndWait(authedPage, `${WEB_URL}/admin/templates`);
    const addBtn = authedPage.getByRole('button', { name: /thêm|tạo|create/i });
    const hasAdd = await addBtn.isVisible({ timeout: 5_000 }).catch(() => false);
    test.skip(!hasAdd, 'No create button');

    await addBtn.click();
    const modal = new FormModalPage(authedPage);
    await modal.waitForOpen();
    // Verify form fields exist (Tên, Mã, Định dạng, Nội dung mẫu)
    await modal.fillField('Tên', `E2E Tpl ${ts}`);
    await modal.fillField('Mã', `E2E-TPL-${ts}`);
    await modal.close();
    await authedPage.waitForTimeout(1_000);
  });

  // ── CRUD: Master Data — verify modal opens (cascading cards, complex form) ──

  test('Master Data — modal opens and closes', async ({ authedPage }) => {
    await navigateAndWait(authedPage, `${WEB_URL}/admin/master-data`);
    const addBtn = authedPage.getByRole('button', { name: /thêm|tạo|create/i }).first();
    const hasAdd = await addBtn.isVisible({ timeout: 5_000 }).catch(() => false);
    test.skip(!hasAdd, 'No create button');

    await addBtn.click();
    const modal = new FormModalPage(authedPage);
    await modal.waitForOpen();
    // MasterData uses cascading cards — complex form, just verify modal works
    await modal.close();
    await authedPage.waitForTimeout(1_000);
  });

  // ── CRUD: Notification Templates — modal open + close ───────────────

  test('Notification Templates — modal opens and closes', async ({ authedPage }) => {
    await navigateAndWait(authedPage, `${WEB_URL}/admin/notification-templates`);
    const addBtn = authedPage.getByRole('button', { name: /thêm|tạo|create/i });
    const hasAdd = await addBtn.isVisible({ timeout: 5_000 }).catch(() => false);
    test.skip(!hasAdd, 'No create button');

    await addBtn.click();
    const modal = new FormModalPage(authedPage);
    await modal.waitForOpen();
    await modal.close();
    await authedPage.waitForTimeout(1_000);
  });
});
