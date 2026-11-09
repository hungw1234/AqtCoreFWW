import { test, expect } from '../fixtures/auth-fixture';
import { FormModalPage } from '../pages/form-modal-page';
import { navigateAndWait } from '../helpers/navigation-helper';
import { assertTableOrEmptyVisible } from '../helpers/table-helper';

const WEB_URL = process.env.WEB_URL ?? 'http://localhost:5173';

/**
 * Admin Integration routes — render + CRUD tests.
 * Covers: Workflow (defs, instances, assignments), Webhooks, AI Config, Rules
 */
test.describe('Admin Integration Routes', () => {
  test.describe.configure({ mode: 'serial' });
  test.setTimeout(60_000);

  const INTEGRATION_ROUTES = [
    { path: '/admin/workflow', name: 'Workflow Definitions' },
    { path: '/admin/workflow/instances', name: 'Workflow Instances' },
    { path: '/admin/workflow-assignments', name: 'Workflow Assignments' },
    { path: '/admin/webhooks', name: 'Webhook Deliveries' },
    { path: '/admin/ai', name: 'AI Config' },
    { path: '/admin/rules', name: 'Rules' },
  ];

  for (const { path, name } of INTEGRATION_ROUTES) {
    test(`${name} (${path}) renders`, async ({ authedPage }) => {
      const { errors } = await navigateAndWait(authedPage, `${WEB_URL}${path}`);
      expect(errors).toHaveLength(0);
      await assertTableOrEmptyVisible(authedPage);
    });
  }

  // ── CRUD: Rules — verify modal opens (complex form with lifecycle) ──

  test('Rules — modal opens and closes', async ({ authedPage }) => {
    await navigateAndWait(authedPage, `${WEB_URL}/admin/rules`);
    const addBtn = authedPage.getByRole('button', { name: /thêm|tạo|create/i });
    const hasAdd = await addBtn.isVisible({ timeout: 5_000 }).catch(() => false);
    test.skip(!hasAdd, 'No create button');

    await addBtn.click();
    const modal = new FormModalPage(authedPage);
    await modal.waitForOpen();
    // Rules form has lifecycle management — verify modal opens then close
    await modal.close();
    await authedPage.waitForTimeout(1_000);
  });
});
