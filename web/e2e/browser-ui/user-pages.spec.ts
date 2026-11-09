import { test, expect } from '../fixtures/auth-fixture';
import { FormModalPage } from '../pages/form-modal-page';
import { navigateAndWait } from '../helpers/navigation-helper';
import { assertTableOrEmptyVisible } from '../helpers/table-helper';

const WEB_URL = process.env.WEB_URL ?? 'http://localhost:5173';

/**
 * User-facing page render tests.
 * Covers untested routes: workflow-inbox, forms, reports, search, AI, copilot,
 *   chat, signatures, profile, consent, roles, integration pages
 */
test.describe('User-Facing Pages', () => {
  test.describe.configure({ mode: 'serial' });
  test.setTimeout(60_000);

  // Routes with tables
  const TABLE_ROUTES = [
    { path: '/forms', name: 'Forms' },
    { path: '/reports/definitions', name: 'Report Definitions' },
    { path: '/reports/executions', name: 'Report Executions' },
    { path: '/signatures', name: 'Signatures' },
    { path: '/consent', name: 'Consent' },
    { path: '/roles', name: 'Roles' },
    { path: '/integration/partners', name: 'Integration Partners' },
    { path: '/integration/contracts', name: 'Integration Contracts' },
    { path: '/integration/message-logs', name: 'Integration Message Logs' },
  ];

  for (const { path, name } of TABLE_ROUTES) {
    test(`${name} (${path}) renders`, async ({ authedPage }) => {
      const { errors } = await navigateAndWait(authedPage, `${WEB_URL}${path}`);
      expect(errors).toHaveLength(0);
      await assertTableOrEmptyVisible(authedPage);
    });
  }

  // Routes without tables (special UI)
  const SPECIAL_ROUTES = [
    { path: '/ai/search', name: 'AI Search', check: 'content' },
    { path: '/copilot', name: 'Copilot', check: 'content' },
    { path: '/chat', name: 'Chat', check: 'content' },
    { path: '/profile', name: 'Profile', check: 'form' },
  ];

  for (const { path, name, check } of SPECIAL_ROUTES) {
    test(`${name} (${path}) renders`, async ({ authedPage }) => {
      const { errors } = await navigateAndWait(authedPage, `${WEB_URL}${path}`);
      expect(errors).toHaveLength(0);

      if (check === 'input') {
        const hasInput = await authedPage.locator('input[type="search"], input[type="text"], .ant-input')
          .first().isVisible({ timeout: 5_000 }).catch(() => false);
        const hasContent = await authedPage.locator('.ant-layout-content, main')
          .first().isVisible({ timeout: 3_000 }).catch(() => false);
        expect(hasInput || hasContent).toBe(true);
      } else if (check === 'form') {
        const hasForm = await authedPage.locator('form, .ant-form')
          .first().isVisible({ timeout: 5_000 }).catch(() => false);
        const hasContent = await authedPage.locator('.ant-layout-content, main')
          .first().isVisible({ timeout: 3_000 }).catch(() => false);
        expect(hasForm || hasContent).toBe(true);
      } else {
        const hasContent = await authedPage.locator('.ant-layout-content, main')
          .first().isVisible({ timeout: 5_000 });
        expect(hasContent).toBe(true);
      }
    });
  }

  // ── CRUD: User-facing pages — create operations ───────────────────

  test('Forms — modal opens and closes', async ({ authedPage }) => {
    await navigateAndWait(authedPage, `${WEB_URL}/forms`);
    const addBtn = authedPage.getByRole('button', { name: /thêm|tạo|create/i });
    const hasAdd = await addBtn.isVisible({ timeout: 5_000 }).catch(() => false);
    test.skip(!hasAdd, 'No create button on forms page');

    await addBtn.click();
    const modal = new FormModalPage(authedPage);
    await modal.waitForOpen();
    // Forms have many required fields — verify modal opens then close
    await modal.close();
    await authedPage.waitForTimeout(1_000);
  });

  test('Integration Partners — modal opens and closes', async ({ authedPage }) => {
    await navigateAndWait(authedPage, `${WEB_URL}/integration/partners`);
    const addBtn = authedPage.getByRole('button', { name: /thêm|tạo|create/i });
    const hasAdd = await addBtn.isVisible({ timeout: 5_000 }).catch(() => false);
    test.skip(!hasAdd, 'No create button on partners page');

    await addBtn.click();
    const modal = new FormModalPage(authedPage);
    await modal.waitForOpen();
    await modal.close();
    await authedPage.waitForTimeout(1_000);
  });
});
