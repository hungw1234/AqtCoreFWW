import { type Page } from '@playwright/test';

export interface PageMetrics {
  domContentLoaded: number;
  loadComplete: number;
  lcp: number | null;
  cls: number | null;
  resourceCount: number;
  transferSize: number;
}

/**
 * Measure page load performance via the Performance API.
 * Call AFTER page.goto() and page.waitForLoadState('networkidle').
 */
export async function measurePageMetrics(page: Page): Promise<PageMetrics> {
  return page.evaluate(() => {
    const nav = performance.getEntriesByType('navigation')[0] as PerformanceNavigationTiming;
    const resources = performance.getEntriesByType('resource') as PerformanceResourceTiming[];

    // LCP — last paint entry
    let lcp: number | null = null;
    const lcpEntries = performance.getEntriesByType('largest-contentful-paint');
    if (lcpEntries.length > 0) {
      lcp = lcpEntries[lcpEntries.length - 1].startTime;
    }

    // CLS — sum of layout shift values (excluding user-input-related shifts)
    let cls: number | null = null;
    const layoutShifts = performance.getEntriesByType('layout-shift') as any[];
    if (layoutShifts.length > 0) {
      cls = layoutShifts
        .filter((e: any) => !e.hadRecentInput)
        .reduce((sum: number, e: any) => sum + e.value, 0);
    }

    const transferSize = resources.reduce((sum, r) => sum + (r.transferSize || 0), 0);

    return {
      domContentLoaded: nav ? nav.domContentLoadedEventEnd - nav.startTime : 0,
      loadComplete: nav ? nav.loadEventEnd - nav.startTime : 0,
      lcp,
      cls,
      resourceCount: resources.length,
      transferSize,
    };
  });
}

/**
 * Measure API response time for a single GET request.
 */
export async function measureApiResponseTime(
  request: any,
  url: string,
  headers: Record<string, string>,
): Promise<{ status: number; durationMs: number }> {
  const start = Date.now();
  const res = await request.get(url, { headers });
  const durationMs = Date.now() - start;
  return { status: res.status(), durationMs };
}
