/**
 * @smoke
 *
 * TC-WF-E2E-CURSOR-001: Workflow dashboard cursor advance — forward-only pagination.
 *
 * Spec: RT-10 — forward-only MVP (no back-nav). Verifies:
 *   1. First page returns up to 50 instances with hasNextPage indicator.
 *   2. Load-more: passing nextCursor returns second page (cursor advance forward).
 *   3. Summary endpoint returns total >= 0 (KPI card populated).
 *   4. No back-cursor step — MVP is forward-only.
 *
 * NOTE: This spec tests the API layer directly (no FE browser navigation).
 * Reason: Dev server (port 5173) and auth server (port 5002) are not spun up
 * automatically in CI without webServer config. The cursor pagination logic is
 * fully covered at the API level. FE rendering is covered by vitest
 * (use-workflow-instances-infinite.test.ts, instance-monitoring-dashboard.test.tsx).
 *
 * If/when CI starts a dev server, upgrade this spec to use page.goto('/admin/workflow/instances')
 * and assert DOM row count via getByRole('row').
 */

import { test, expect } from './fixtures/test-base';
import { getAccessToken, authHeaders, API_BASE } from './helpers/auth-helper';

const INSTANCES_URL = `${API_BASE}/api/v1/workflow/instances`;
const SUMMARY_URL   = `${API_BASE}/api/v1/workflow/instances/summary`;

test.describe('Workflow dashboard cursor advance @smoke', () => {
  let token: string;

  test.beforeAll(async ({ request }) => {
    token = await getAccessToken(request, 'admin');
    expect(token).toBeTruthy();
  });

  // ── TC-WF-E2E-CURSOR-001: First page renders up to 50 rows ───────────────────

  test('first page — returns items array with hasNextPage indicator', async ({ request }) => {
    const res = await request.get(`${INSTANCES_URL}?pageSize=50`, {
      headers: authHeaders(token),
    });

    // Expect 200 (may be 403 if token lacks WORKFLOW.INSTANCE.VIEW — E2E seeder must assign it)
    expect(res.status()).toBe(200);

    const body = await res.json();
    expect(body).toHaveProperty('success', true);

    const data = body.data;
    expect(data).toHaveProperty('items');
    expect(Array.isArray(data.items)).toBe(true);
    expect(data.items.length).toBeLessThanOrEqualTo(50);

    expect(data).toHaveProperty('meta');
    expect(data.meta).toHaveProperty('hasNextPage');
    expect(typeof data.meta.hasNextPage).toBe('boolean');
  });

  // ── TC-WF-E2E-CURSOR-002: Load-more — cursor advances forward ────────────────

  test('load-more — nextCursor from page 1 fetches page 2', async ({ request }) => {
    // Page 1
    const page1Res = await request.get(`${INSTANCES_URL}?pageSize=50`, {
      headers: authHeaders(token),
    });
    expect(page1Res.status()).toBe(200);

    const page1Body = await page1Res.json();
    const meta = page1Body.data?.meta;

    // Only proceed if there IS a next page (requires seeded data)
    if (!meta?.hasNextPage) {
      test.skip();
      return;
    }

    const cursor = meta.nextCursor as string;
    expect(cursor).toBeTruthy();

    // Page 2 via cursor
    const page2Res = await request.get(
      `${INSTANCES_URL}?pageSize=50&cursor=${encodeURIComponent(cursor)}`,
      { headers: authHeaders(token) },
    );
    expect(page2Res.status()).toBe(200);

    const page2Body = await page2Res.json();
    expect(page2Body).toHaveProperty('success', true);
    expect(page2Body.data.items).toBeDefined();
    expect(Array.isArray(page2Body.data.items)).toBe(true);

    // No ID duplication across pages — forward-only invariant
    const page1Ids = new Set<string>(
      (page1Body.data.items as Array<{ id: string }>).map((i) => i.id),
    );
    const page2Ids = (page2Body.data.items as Array<{ id: string }>).map((i) => i.id);
    for (const id of page2Ids) {
      expect(page1Ids.has(id)).toBe(false);
    }
  });

  // ── TC-WF-E2E-CURSOR-003: KPI card — summary returns total >= 0 ───────────────

  test('KPI card — summary endpoint returns valid counts', async ({ request }) => {
    const res = await request.get(SUMMARY_URL, {
      headers: authHeaders(token),
    });
    expect(res.status()).toBe(200);

    const body = await res.json();
    expect(body).toHaveProperty('success', true);

    const data = body.data;
    expect(typeof data.active).toBe('number');
    expect(typeof data.completedToday).toBe('number');
    expect(typeof data.total).toBe('number');

    // total >= 0 (KPI card can show 0 when no instances seeded — valid state)
    expect(data.total).toBeGreaterThanOrEqual(0);
    expect(data.active).toBeGreaterThanOrEqual(0);
  });

  // ── No back-nav step — MVP is forward-only (RT-10) ───────────────────────────
  // Negative assertion: the cursor API has no prevCursor / prev page endpoint.
  // The BE meta always returns prevCursor: null.

  test('no back-nav — prevCursor is null (forward-only MVP)', async ({ request }) => {
    const res = await request.get(`${INSTANCES_URL}?pageSize=50`, {
      headers: authHeaders(token),
    });
    expect(res.status()).toBe(200);

    const body = await res.json();
    const meta = body.data?.meta;

    // prevCursor must be null — forward-only MVP
    expect(meta?.prevCursor ?? null).toBeNull();
  });
});
