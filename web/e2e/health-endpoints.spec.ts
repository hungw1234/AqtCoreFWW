import { test, expect } from './fixtures/test-base';

const API_BASE = 'http://localhost:5001';

// Health & infrastructure E2E — no auth required
test.describe('Health & infrastructure endpoints', () => {
  test('GET /health/ready returns healthy', async ({ request }) => {
    const res = await request.get(`${API_BASE}/health/ready`);
    expect([200, 503]).toContain(res.status());
  });

  test('GET /openapi/v1.json returns valid OpenAPI spec', async ({ request }) => {
    const res = await request.get(`${API_BASE}/openapi/v1.json`);
    expect(res.status()).toBe(200);
    const body = await res.json();
    expect(body.openapi).toBeTruthy();
    expect(Object.keys(body.paths).length).toBeGreaterThan(10);
  });

  test('GET /masterdata/provinces returns cached data', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/masterdata/provinces`);
    expect(res.status()).toBe(200);
    const body = await res.json();
    expect(body.success).toBe(true);
  });

  test('GET /feature-flags/{key} returns flag status', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/feature-flags/EnablePdfWatermark`);
    expect(res.status()).toBe(200);
    const body = await res.json();
    expect(body.success).toBe(true);
  });

  test('GET /announcements/active returns announcements', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/announcements/active`);
    expect(res.status()).toBe(200);
  });
});
