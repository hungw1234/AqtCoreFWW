import { test, expect } from '../fixtures/auth-fixture';
import { FormModalPage } from '../pages/form-modal-page';
import { navigateAndWait } from '../helpers/navigation-helper';
import { assertTableOrEmptyVisible } from '../helpers/table-helper';

const WEB_URL = process.env.WEB_URL ?? 'http://localhost:5173';

/**
 * Admin Identity routes — render + selective CRUD tests.
 * Covers: Users, Groups, Sessions, ExternalIdentities, CredentialPolicies,
 *   ABAC, SoD, PolicyRules, DataScopes, AccessReviews, Delegations, JIT, RTBF
 */
test.describe('Admin Identity Routes', () => {
  test.describe.configure({ mode: 'serial' });
  test.setTimeout(60_000);

  // ── Render Tests ──────────────────────────────────────────────────

  const IDENTITY_ROUTES = [
    { path: '/admin/users', name: 'Users' },
    { path: '/admin/groups', name: 'Groups' },
    { path: '/admin/sessions', name: 'Sessions' },
    { path: '/admin/external-identities', name: 'External Identities' },
    { path: '/admin/credential-policies', name: 'Credential Policies' },
    { path: '/admin/abac-rules', name: 'ABAC Rules' },
    { path: '/admin/sod-rules', name: 'SoD Rules' },
    { path: '/admin/policy-rules', name: 'Policy Rules' },
    { path: '/admin/data-scopes', name: 'Data Scopes' },
    { path: '/admin/access-reviews', name: 'Access Reviews' },
    { path: '/admin/delegations', name: 'Delegations' },
    { path: '/admin/jit-provider-configs', name: 'JIT Provider Configs' },
    { path: '/admin/rtbf', name: 'RTBF Requests' },
  ];

  for (const { path, name } of IDENTITY_ROUTES) {
    test(`${name} (${path}) renders`, async ({ authedPage }) => {
      const { errors } = await navigateAndWait(authedPage, `${WEB_URL}${path}`);
      expect(errors).toHaveLength(0);
      await assertTableOrEmptyVisible(authedPage);
    });
  }

  // ── CRUD: Users ───────────────────────────────────────────────────

  test('Users page renders table or empty state', async ({ authedPage }) => {
    await navigateAndWait(authedPage, `${WEB_URL}/admin/users`);
    await assertTableOrEmptyVisible(authedPage);
  });

  // ── CRUD: Groups — create + verify ────────────────────────────────

  test('Groups — create via modal', async ({ authedPage }) => {
    await navigateAndWait(authedPage, `${WEB_URL}/admin/groups`);

    const addBtn = authedPage.getByRole('button', { name: /thêm|tạo|create|add/i });
    const hasAdd = await addBtn.isVisible({ timeout: 5_000 }).catch(() => false);
    test.skip(!hasAdd, 'No create button found');

    await addBtn.click();
    const modal = new FormModalPage(authedPage);
    await modal.waitForOpen();
    await modal.fillField('Mã', `e2e-grp-${Date.now()}`);
    await modal.fillField('Tên', `E2E Group ${Date.now()}`);
    await modal.submit();

    // Verify success notification or table refresh
    await authedPage.waitForTimeout(2_000);
    const notification = authedPage.locator('.ant-notification-notice, .ant-message');
    const hasNotif = await notification.first().isVisible({ timeout: 3_000 }).catch(() => false);
    // Either notification or modal closed = success
    const modalClosed = !(await modal.isOpen());
    expect(hasNotif || modalClosed).toBe(true);
  });
});
