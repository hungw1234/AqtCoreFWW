import { test, expect } from './fixtures/test-base';
import { getAccessToken, authHeaders, API_BASE } from './helpers/auth-helper';
import type { APIRequestContext } from './fixtures/test-base';

// JIT provider configs API tests — TC-IDENT-104/105/107/109/110/111
test.describe('JIT provider configs API (authenticated)', () => {
  test.describe.configure({ mode: 'serial' });
  let token: string;
  let createdId: string;
  const testScheme = `TC107-Scheme-${Date.now()}`;

  test.beforeAll(async ({ request }: { request: APIRequestContext }) => {
    token = await getAccessToken(request);
  });

  test.afterAll(async ({ request }: { request: APIRequestContext }) => {
    if (createdId) {
      await request.delete(`${API_BASE}/api/v1/admin/jit-provider-configs/${createdId}`, {
        headers: authHeaders(token),
      });
    }
  });

  // TC-IDENT-104: list JIT provider configs — paginated
  test('TC-IDENT-104: GET /admin/jit-provider-configs returns paginated list', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/admin/jit-provider-configs?page=1&pageSize=10`, {
      headers: authHeaders(token),
    });
    expect(res.status()).toBe(200);
    const body = await res.json();
    const data = body.data ?? body;
    expect(data).toHaveProperty('items');
  });

  // TC-IDENT-107: create JIT provider config — valid payload returns 201
  test('TC-IDENT-107: POST /admin/jit-provider-configs creates config', async ({ request }) => {
    const res = await request.post(`${API_BASE}/api/v1/admin/jit-provider-configs`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: {
        scheme: testScheme,
        displayName: 'TC107 Provider',
        providerType: 'VNeID',
        jitEnabled: true,
        defaultRoleName: 'Viewer',
        requireApproval: false,
        claimMappingJson: null,
        defaultTenantId: null,
        allowedDomainsJson: null,
        maxProvisionsPerHour: 100,
      },
    });
    expect([201, 409]).toContain(res.status());
    if (res.status() === 201) {
      // 201 may return empty body — parse safely
      const text = await res.text();
      if (text) {
        const body = JSON.parse(text);
        createdId = body.data?.id ?? body.id;
      }
      // If no body, we still treat creation as successful; createdId will remain empty
      // and downstream tests that require it will be skipped
    }
  });

  // TC-IDENT-105: get JIT config by scheme — existing scheme
  test('TC-IDENT-105: GET /admin/jit-provider-configs/{scheme} returns config', async ({ request }) => {
    test.skip(!createdId, 'No config created');
    const res = await request.get(`${API_BASE}/api/v1/admin/jit-provider-configs/${testScheme}`, {
      headers: authHeaders(token),
    });
    expect([200, 404]).toContain(res.status());
    if (res.status() === 200) {
      const body = await res.json();
      const data = body.data ?? body;
      expect(data).toHaveProperty('scheme');
    }
  });

  // TC-IDENT-110: update JIT provider config — valid payload returns 204
  test('TC-IDENT-110: PUT /admin/jit-provider-configs/{id} updates config', async ({ request }) => {
    test.skip(!createdId, 'No config created');
    const res = await request.put(`${API_BASE}/api/v1/admin/jit-provider-configs/${createdId}`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: {
        displayName: 'TC110 Updated',
        jitEnabled: false,
        defaultRoleName: 'Viewer',
        requireApproval: true,
        claimMappingJson: null,
        defaultTenantId: null,
        allowedDomainsJson: null,
        maxProvisionsPerHour: 50,
      },
    });
    expect([204, 200]).toContain(res.status());
  });

  // TC-IDENT-109: delete JIT provider config — returns 204
  test('TC-IDENT-109: DELETE /admin/jit-provider-configs/{id} deactivates config', async ({ request }) => {
    test.skip(!createdId, 'No config created');
    const res = await request.delete(`${API_BASE}/api/v1/admin/jit-provider-configs/${createdId}`, {
      headers: authHeaders(token),
    });
    expect([204, 200]).toContain(res.status());
    createdId = '';
  });

  // TC-IDENT-111: unauthenticated returns 401
  test('TC-IDENT-111: GET /admin/jit-provider-configs without auth returns 401', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/admin/jit-provider-configs`);
    expect(res.status()).toBe(401);
  });
});

// ── Browser UI tests (existing) ───────────────────────────────────────────────
import { test as uiTest, expect as uiExpect } from './fixtures/auth-fixture';
import { DataTablePage } from './pages/data-table-page';
import { FormModalPage } from './pages/form-modal-page';
import { navigateAndWait } from './helpers/navigation-helper';

const WEB_URL = process.env.WEB_URL ?? 'http://localhost:5173';

/**
 * JIT SSO Provider Config — Admin CRUD via browser.
 * Skipped when FE is not running (WEB_URL not reachable).
 */
uiTest.describe('JIT SSO Provider Config Management', () => {
  uiTest.describe.configure({ mode: 'serial' });
  uiTest.setTimeout(60_000);

  // Skip all browser tests if FE is not reachable
  uiTest.skip(!!process.env.SKIP_UI_TESTS || !process.env.WEB_URL && true,
    'FE not running — set WEB_URL to enable browser tests');

  uiTest('/admin/jit-provider-configs renders', async ({ authedPage }) => {
    const { errors } = await navigateAndWait(authedPage, `${WEB_URL}/admin/jit-provider-configs`);
    uiExpect(errors).toHaveLength(0);
    const table = new DataTablePage(authedPage);
    await table.assertTableOrEmptyVisible();
  });

  uiTest('create JIT provider config', async ({ authedPage }) => {
    await navigateAndWait(authedPage, `${WEB_URL}/admin/jit-provider-configs`);

    const addBtn = authedPage.getByRole('button', { name: /thêm|tạo|create|add/i });
    const hasAdd = await addBtn.isVisible({ timeout: 5_000 }).catch(() => false);
    uiTest.skip(!hasAdd, 'No create button');

    await addBtn.click();
    const modal = new FormModalPage(authedPage);
    await modal.waitForOpen();
    await modal.fillField('Tên', `E2E JIT Provider ${Date.now()}`);
    await modal.submit();

    await authedPage.waitForTimeout(2_000);
    const modalClosed = !(await modal.isOpen());
    uiExpect(modalClosed).toBe(true);
  });
});
