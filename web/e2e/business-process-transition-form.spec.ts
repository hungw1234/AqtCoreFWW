import { test, expect } from './fixtures/test-base';
import { getAccessToken, authHeaders, API_BASE } from './helpers/auth-helper';

// BP transition confirm-modal E2E — the FE half of BP-TRANSITION-FORM-E2E.
//
// Covers the user journey the BE integration test (ExecuteBusinessProcessTransitionHttpTests) cannot:
// a designer-bound REQUIRED transition form makes the run's action open the confirm modal with the
// published form rendered inline. Deterministic contract coverage (requiresForms + inline snapshot)
// lives in the BE test; this asserts the modal actually wires up against a live stack.
//
// Live-backend + graceful-skip, matching the repo's e2e convention (business-process-run.spec.ts):
// every precondition (seeded BP, creatable run, a form-gated transition) is skip-guarded so the spec
// never false-fails when the dev seed does not configure a form-gated transition.
//
// Activation precondition: the demo BP binds a required form to its Submit transition but ships in
// Draft (IsActive=false). Precondition 1 below queries /active, so against the out-of-box seed this
// spec SKIPS until an operator activates the BP — a skipped (not asserted) result is expected then,
// and must not be read as passing coverage.
test.describe('Business Process transition — required-form confirm modal', () => {
  let token: string;

  test.beforeAll(async ({ request }) => {
    token = await getAccessToken(request);
    expect(token).toBeTruthy();
  });

  // Stable selector: the modal title's Vietnamese defaultValue in transition-confirm-modal.tsx
  // (i18n key page.businessProcessRun.confirmTransition; no locale override exists today).
  const MODAL_TITLE = 'Xác nhận chuyển trạng thái';

  /** Best-effort extraction of the created run's id (Orval types the create-run response as void). */
  function extractRunId(body: unknown): string | null {
    if (!body || typeof body !== 'object') return null;
    const b = body as Record<string, unknown>;
    const data = (b.data && typeof b.data === 'object' ? b.data : b) as Record<string, unknown>;
    for (const key of ['workflowInstanceId', 'workflowInstancePublicId', 'id', 'publicId', 'runId']) {
      const v = data[key];
      if (typeof v === 'string' && v.length > 0) return v;
    }
    return null;
  }

  test('a form-gated transition opens the confirm modal with the inline form', async ({ page, request }) => {
    // Precondition 1 — at least one active BP in the seed.
    const activeRes = await request.get(`${API_BASE}/api/v1/business-processes/active`, {
      headers: authHeaders(token),
    });
    expect(activeRes.status()).toBe(200);
    const bps = (await activeRes.json()) as { id: string }[];
    if (!bps?.length) {
      test.skip(true, 'No active BPs in seed — cannot reach a run');
      return;
    }

    // Precondition 2 — a run we can open. Try each BP; intake-gated BPs return 422 and are skipped.
    let runId: string | null = null;
    for (const bp of bps) {
      const runRes = await request.post(`${API_BASE}/api/v1/business-processes/${bp.id}/runs`, {
        headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
        data: { entityType: 'BusinessProcess', entityId: bp.id, intakeFormData: null },
      });
      if (runRes.status() === 200 || runRes.status() === 201) {
        runId = extractRunId(await runRes.json().catch(() => null));
        if (runId) break;
      }
    }
    if (!runId) {
      test.skip(true, 'No BP run could be created without intake data — skipping modal journey');
      return;
    }

    // Open the run detail and wait for the allowed-actions bar to hydrate.
    await page.goto(`/business-process-runs/${runId}`);
    await page.waitForSelector('[class*="ant-btn"], [class*="ant-spin"], [class*="ant-alert"]', {
      timeout: 10000,
    });

    // Precondition 3 — a form-gated action. Click each primary action; the gated one opens the modal,
    // a non-gated one fires a transition toast. Skip if none of the run's actions are form-gated.
    const actionButtons = page.locator('.ant-btn-primary:visible');
    const count = await actionButtons.count();
    if (count === 0) {
      test.skip(true, 'Run has no available actions in its current state — skipping');
      return;
    }

    const modalTitle = page.getByText(MODAL_TITLE, { exact: false });
    let modalOpened = false;
    for (let i = 0; i < count; i++) {
      await actionButtons.nth(i).click();
      try {
        await modalTitle.waitFor({ state: 'visible', timeout: 4000 });
        modalOpened = true;
        break;
      } catch {
        // Not form-gated (transitioned or no-op) — dismiss any open modal and try the next action.
        await page.keyboard.press('Escape').catch(() => {});
      }
    }
    if (!modalOpened) {
      test.skip(true, 'No form-gated transition reachable in the current seed — modal not triggered');
      return;
    }

    // Assert the confirm modal rendered the inline form: title + at least one interactive field.
    await expect(modalTitle).toBeVisible();
    const modalField = page.locator(
      '.ant-modal input:visible, .ant-modal textarea:visible, .ant-modal .ant-select:visible'
    );
    await expect(modalField.first()).toBeVisible();

    // The confirm action is present (full submit is asserted at the integration/handler layer).
    await expect(page.getByRole('button', { name: 'Xác nhận' })).toBeVisible();
  });
});
