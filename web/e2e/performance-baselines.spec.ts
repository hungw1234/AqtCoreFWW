import { test, expect } from './fixtures/test-base';
import { getAccessToken, authHeaders, API_BASE } from './helpers/auth-helper';
import { measurePageMetrics, measureApiResponseTime } from './helpers/performance-helper';
import { LoginPage } from './pages/login-page';

const WEB_URL = process.env.WEB_URL ?? 'http://localhost:5173';

/**
 * Performance baselines: page load times, API response times, Core Web Vitals.
 * Thresholds are generous for dev environment — tighten after baseline collection.
 */
test.describe('Performance Baselines', () => {
  test.setTimeout(60_000);

  // ── Page Load Performance ──────────────────────────────────────────

  test.describe('Page Load Times', () => {
    test.describe.configure({ mode: 'serial' });

    let authedPage: any;

    test.beforeAll(async ({ browser }) => {
      test.setTimeout(120_000);
      const ctx = await browser.newContext();
      authedPage = await ctx.newPage();
      const loginPage = new LoginPage(authedPage);
      await loginPage.loginAsAdmin();
    });

    test.afterAll(async () => {
      await authedPage?.context().close();
    });

    const PAGES_TO_MEASURE = [
      { name: 'Dashboard', path: '/' },
      { name: 'Admin Users', path: '/admin/users' },
      { name: 'Forms', path: '/forms' },
      { name: 'Audit Logs', path: '/audit/logs' },
      { name: 'Admin System Params', path: '/admin/system-params' },
    ];

    for (const { name, path } of PAGES_TO_MEASURE) {
      test(`${name} loads within threshold`, async () => {
        await authedPage.goto(`${WEB_URL}${path}`);
        await authedPage.waitForLoadState('networkidle').catch(() => {});

        const metrics = await measurePageMetrics(authedPage);

        console.log(`[PERF] ${name}:`, {
          domContentLoaded: `${metrics.domContentLoaded.toFixed(0)}ms`,
          loadComplete: `${metrics.loadComplete.toFixed(0)}ms`,
          lcp: metrics.lcp ? `${metrics.lcp.toFixed(0)}ms` : 'N/A',
          cls: metrics.cls?.toFixed(3) ?? 'N/A',
          resources: metrics.resourceCount,
          transferred: `${(metrics.transferSize / 1024).toFixed(0)}KB`,
        });

        expect(metrics.domContentLoaded).toBeLessThan(3000);
        expect(metrics.loadComplete).toBeLessThan(8000);

        if (metrics.lcp !== null) {
          expect(metrics.lcp).toBeLessThan(4000);
        }
        if (metrics.cls !== null) {
          expect(metrics.cls).toBeLessThan(0.25);
        }
      });
    }
  });

  // ── API Response Time ──────────────────────────────────────────────

  test.describe('API Response Times', () => {
    let token: string;

    test.beforeAll(async ({ request }) => {
      token = await getAccessToken(request);
    });

    const LIST_ENDPOINTS = [
      '/forms/templates?page=1&pageSize=10',
      '/notifications?page=1&pageSize=10',
      '/admin/users?page=1&pageSize=10',
      '/admin/system-params',
      '/audit/logs?page=1&pageSize=10',
    ];

    for (const ep of LIST_ENDPOINTS) {
      test(`API ${ep.split('?')[0]} responds within 500ms`, async ({ request }) => {
        const { status, durationMs } = await measureApiResponseTime(
          request,
          `${API_BASE}/api/v1${ep}`,
          authHeaders(token),
        );
        console.log(`[PERF] API ${ep.split('?')[0]}: ${durationMs}ms (${status})`);
        expect(status).toBeLessThan(300); // must be 2xx
        expect(durationMs).toBeLessThan(500);
      });
    }

    const DETAIL_ENDPOINTS = [
      '/notifications/unread-count',
      '/admin/data-scopes/types',
      '/announcements/active',
    ];

    for (const ep of DETAIL_ENDPOINTS) {
      test(`API ${ep} responds within 300ms`, async ({ request }) => {
        const { status, durationMs } = await measureApiResponseTime(
          request,
          `${API_BASE}/api/v1${ep}`,
          authHeaders(token),
        );
        console.log(`[PERF] API ${ep}: ${durationMs}ms (${status})`);
        expect(status).toBeLessThan(300); // must be 2xx
        expect(durationMs).toBeLessThan(300);
      });
    }
  });

  // ── Auth Token Performance ─────────────────────────────────────────

  test.describe('Auth Performance', () => {
    test('ROPC token acquisition under 1s', async ({ request }) => {
      const start = Date.now();
      const token = await getAccessToken(request);
      const elapsed = Date.now() - start;

      console.log(`[PERF] Token acquisition: ${elapsed}ms`);
      expect(token).toBeTruthy();
      expect(elapsed).toBeLessThan(1000);
    });
  });
});
