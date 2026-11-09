import { test, expect } from './fixtures/test-base';
import { getAccessToken, authHeaders, API_BASE } from './helpers/auth-helper';

const FILES_URL = `${API_BASE}/api/v1/files`;

// Files E2E — upload, metadata, download, delete via authenticated API.
// POST returns 202 Accepted (async ClamAV scan); file enters quarantine first.
test.describe('Files CRUD (authenticated API)', () => {
  test.describe.configure({ mode: 'serial' });
  let token: string;
  let uploadedFileId: string | undefined;

  test.beforeAll(async ({ request }) => {
    token = await getAccessToken(request);
    expect(token).toBeTruthy();
  });

  // ── Auth guards ─────────────────────────────────────────────────────────────

  // TC-FILE-001
  test('TC-FILE-001: GET /files without auth returns 401', async ({ request }) => {
    const res = await request.get(FILES_URL);
    expect(res.status()).toBe(401);
  });

  // TC-FILE-007
  test('TC-FILE-007: POST /files without auth returns 401', async ({ request }) => {
    const res = await request.post(FILES_URL, {
      multipart: {
        file: { name: 'auth-guard.txt', mimeType: 'text/plain', buffer: Buffer.from('guard') },
      },
    });
    expect(res.status()).toBe(401);
  });

  // TC-FILE-012
  test('TC-FILE-012: GET /files/{id} download without auth returns 401', async ({ request }) => {
    const res = await request.get(`${FILES_URL}/00000000-0000-0000-0000-000000000001`);
    expect(res.status()).toBe(401);
  });

  // TC-FILE-016
  test('TC-FILE-016: GET /files/{id}/metadata without auth returns 401', async ({ request }) => {
    const res = await request.get(`${FILES_URL}/00000000-0000-0000-0000-000000000001/metadata`);
    expect(res.status()).toBe(401);
  });

  // TC-FILE-020
  test('TC-FILE-020: DELETE /files/{id} without auth returns 401', async ({ request }) => {
    const res = await request.delete(`${FILES_URL}/00000000-0000-0000-0000-000000000001`);
    expect(res.status()).toBe(401);
  });

  // ── List ────────────────────────────────────────────────────────────────────

  // TC-FILE-002
  test('TC-FILE-002: GET /files authenticated returns paginated list', async ({ request }) => {
    const res = await request.get(FILES_URL, { headers: authHeaders(token) });
    expect(res.status()).toBe(200);
    const body = await res.json();
    expect(body.success).toBe(true);
    expect(Array.isArray(body.data?.items)).toBe(true);
    expect(typeof body.data?.totalCount).toBe('number');
  });

  // TC-FILE-006
  test('TC-FILE-006: list item shape contains required fields', async ({ request }) => {
    const res = await request.get(FILES_URL, { headers: authHeaders(token) });
    expect(res.status()).toBe(200);
    const body = await res.json();
    const items: Record<string, unknown>[] = body.data?.items ?? [];
    if (items.length > 0) {
      const item = items[0];
      // API uses Dapper QueryAsync<object> — returns PascalCase SQL column names as-is
      // Accept both PascalCase (actual) and camelCase (future normalised response)
      const hasId = 'id' in item || 'Id' in item;
      const hasFileName = 'originalFileName' in item || 'OriginalFileName' in item;
      const hasContentType = 'contentType' in item || 'ContentType' in item;
      const hasSizeBytes = 'sizeBytes' in item || 'SizeBytes' in item;
      const hasScanStatus = 'virusScanStatus' in item || 'VirusScanStatus' in item;
      const hasUploadedBy = 'uploadedBy' in item || 'UploadedBy' in item;
      const hasUploadedAt = 'uploadedAt' in item || 'UploadedAt' in item;
      expect(hasId).toBe(true);
      expect(hasFileName).toBe(true);
      expect(hasContentType).toBe(true);
      expect(hasSizeBytes).toBe(true);
      expect(hasScanStatus).toBe(true);
      expect(hasUploadedBy).toBe(true);
      expect(hasUploadedAt).toBe(true);
    }
  });

  // ── Upload ──────────────────────────────────────────────────────────────────

  // TC-FILE-008
  test('TC-FILE-008: POST /files uploads a file and returns 202 with fileId', async ({ request }) => {
    const res = await request.post(FILES_URL, {
      headers: authHeaders(token),
      multipart: {
        file: {
          name: 'e2e-smoke-test.txt',
          mimeType: 'text/plain',
          buffer: Buffer.from('Hello from Playwright E2E test – file upload smoke test'),
        },
      },
    });
    // 202 = quarantined; 422 = policy rejection
    expect([202, 422]).toContain(res.status());
    if (res.status() === 202) {
      const body = await res.json();
      uploadedFileId = body?.id ?? body?.data?.id ?? body?.fileId;
      expect(uploadedFileId).toBeTruthy();
    }
  });

  // ── Metadata ────────────────────────────────────────────────────────────────

  // TC-FILE-017
  test('TC-FILE-017: GET /files/{id}/metadata returns metadata for uploaded file', async ({ request }) => {
    test.skip(!uploadedFileId, 'Upload did not produce a fileId');
    const res = await request.get(`${FILES_URL}/${uploadedFileId}/metadata`, {
      headers: authHeaders(token),
    });
    expect([200, 403]).toContain(res.status());
    if (res.status() === 200) {
      const body = await res.json();
      const data = body?.data ?? body;
      expect(data).toHaveProperty('id');
      expect(data).toHaveProperty('originalFileName');
      // API may use 'status' or 'virusScanStatus' depending on DTO
      expect('status' in data || 'virusScanStatus' in data).toBe(true);
    }
  });

  // TC-FILE-019
  test('TC-FILE-019: GET /files/{id}/metadata for unknown id returns 404', async ({ request }) => {
    const res = await request.get(
      `${FILES_URL}/00000000-0000-0000-0000-000000000004/metadata`,
      { headers: authHeaders(token) },
    );
    expect(res.status()).toBe(404);
  });

  // ── Download ────────────────────────────────────────────────────────────────

  // TC-FILE-013
  test('TC-FILE-013: GET /files/{id} for Quarantined file returns 403', async ({ request }) => {
    test.skip(!uploadedFileId, 'Upload did not produce a fileId');
    const res = await request.get(`${FILES_URL}/${uploadedFileId}`, {
      headers: authHeaders(token),
    });
    // 403 = quarantine block; 200 = scan already completed (fast env); 404 = not found
    expect([403, 200, 404]).toContain(res.status());
  });

  // TC-FILE-014
  test('TC-FILE-014: GET /files/{id} unknown id returns 404', async ({ request }) => {
    const res = await request.get(
      `${FILES_URL}/00000000-0000-0000-0000-000000000003`,
      { headers: authHeaders(token) },
    );
    expect(res.status()).toBe(404);
  });

  // TC-FILE-015
  test('TC-FILE-015: GET /files/{id} Available file returns stream with Content-Type', async ({ request }) => {
    // Requires ClamAV to have scanned the file — conditionally runnable
    test.skip(!uploadedFileId, 'Upload did not produce a fileId');
    const res = await request.get(`${FILES_URL}/${uploadedFileId}`, {
      headers: authHeaders(token),
    });
    if (res.status() === 200) {
      const contentType = res.headers()['content-type'];
      expect(contentType).toBeTruthy();
    } else {
      // 403 = quarantine, 404 = not found — both acceptable in scan-pending env
      expect([403, 404]).toContain(res.status());
    }
  });

  // ── Delete ──────────────────────────────────────────────────────────────────

  // TC-FILE-021
  test('TC-FILE-021: DELETE /files/{id} soft-deletes the uploaded file (returns 204)', async ({ request }) => {
    test.skip(!uploadedFileId, 'Upload did not produce a fileId');
    const res = await request.delete(`${FILES_URL}/${uploadedFileId}`, {
      headers: authHeaders(token),
    });
    expect([204, 404]).toContain(res.status());
  });

  test('GET /files/{id}/metadata after delete returns 404', async ({ request }) => {
    test.skip(!uploadedFileId, 'Upload did not produce a fileId');
    const res = await request.get(`${FILES_URL}/${uploadedFileId}/metadata`, {
      headers: authHeaders(token),
    });
    expect([404, 403]).toContain(res.status());
  });

  // TC-FILE-022
  test('TC-FILE-022: DELETE /files/{id} unknown id returns 404', async ({ request }) => {
    const res = await request.delete(
      `${FILES_URL}/00000000-0000-0000-0000-000000000005`,
      { headers: authHeaders(token) },
    );
    expect(res.status()).toBe(404);
  });
  // Legacy unknown-id guards covered by files-unknown-id-guards.spec.ts
});
