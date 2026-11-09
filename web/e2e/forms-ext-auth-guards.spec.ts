import { test, expect } from './fixtures/test-base';
import { API_BASE } from './helpers/auth-helper';

const TEMPLATES_URL = `${API_BASE}/api/v1/forms/templates`;
const SUBMISSIONS_URL = `${API_BASE}/api/v1/forms/submissions`;
// Seeded Active template ID
const SEEDED_TEMPLATE_ID = '30000000-0000-0000-0000-000000000001';

// Forms ext-controller auth guards — anonymous access returns 401 (P1 TCs: 040, 042, 063)
test.describe('Forms Ext Controller — unauthenticated returns 401', () => {
  // TC-FORMS-040: analytics unauthenticated returns 401
  test('TC-FORMS-040: get template analytics without auth returns 401', async ({ request }) => {
    const res = await request.get(
      `${TEMPLATES_URL}/${SEEDED_TEMPLATE_ID}/analytics`,
    );
    expect(res.status()).toBe(401);
  });

  // TC-FORMS-042: version diff unauthenticated returns 401
  test('TC-FORMS-042: get version diff without auth returns 401', async ({ request }) => {
    const res = await request.get(
      `${TEMPLATES_URL}/${SEEDED_TEMPLATE_ID}/diff?fromVersion=1&toVersion=2`,
    );
    expect(res.status()).toBe(401);
  });

  // TC-FORMS-063: export-pdf without auth returns 401
  test('TC-FORMS-063: export submission as PDF without auth returns 401', async ({ request }) => {
    const res = await request.get(
      `${SUBMISSIONS_URL}/00000000-0000-0000-0000-000000000001/export-pdf`,
    );
    expect(res.status()).toBe(401);
  });
});
