import { test, expect } from './fixtures/test-base';
import { getAccessToken, authHeaders, API_BASE } from './helpers/auth-helper';

const FILES_URL = `${API_BASE}/api/v1/files`;

// Files unknown-id guards — 404 responses for nonexistent file IDs (backward compat)
test.describe('Files — unknown id returns 404', () => {
  let token: string;

  test.beforeAll(async ({ request }) => {
    token = await getAccessToken(request);
  });

  test('GET /files/{id}/metadata for unknown id returns 404', async ({ request }) => {
    const res = await request.get(
      `${FILES_URL}/00000000-0000-0000-0000-000000000001/metadata`,
      { headers: authHeaders(token) },
    );
    expect(res.status()).toBe(404);
  });

  test('DELETE /files/{id} for unknown id returns 404', async ({ request }) => {
    const res = await request.delete(
      `${FILES_URL}/00000000-0000-0000-0000-000000000002`,
      { headers: authHeaders(token) },
    );
    expect(res.status()).toBe(404);
  });
});
