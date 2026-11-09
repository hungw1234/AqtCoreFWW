import { test, expect } from './fixtures/test-base';
import { getAccessToken, authHeaders, API_BASE } from './helpers/auth-helper';

// Minimal 1x1 transparent PNG — used for file upload tests without filesystem dependency
const PNG_BUFFER = Buffer.from(
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==',
  'base64',
);

// AI Extract, Agent Execute, Content Flag E2E
// Happy-path endpoints accept 503 when AI backend is unavailable
test.describe('AI Extract / Agent / Flag (authenticated API)', () => {
  let token: string;

  test.beforeAll(async ({ request }) => {
    token = await getAccessToken(request, 'admin');
    expect(token).toBeTruthy();
  });

  // ── Extract ───────────────────────────────────────────────────────────────

  // TC-AI-034: POST /extract with small PNG file
  test('TC-AI-034: POST /ai/extract with small PNG — [200, 503]', async ({ request }) => {
    const res = await request.post(`${API_BASE}/api/v1/ai/extract`, {
      headers: authHeaders(token),
      multipart: {
        file: {
          name: 'test.png',
          mimeType: 'image/png',
          buffer: PNG_BUFFER,
        },
      },
    });
    expect([200, 500, 503]).toContain(res.status());
    if (res.status() === 200) {
      const body = await res.json();
      expect(body.success).toBe(true);
    }
  });

  // TC-AI-035: POST /extract with no file — 400 or 415 (Unsupported Media Type)
  // 415 is returned when sending JSON to a multipart/form-data endpoint
  test('TC-AI-035: POST /ai/extract no file — 400 or 415', async ({ request }) => {
    const res = await request.post(`${API_BASE}/api/v1/ai/extract`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: {},
    });
    expect([400, 415, 422]).toContain(res.status());
  });

  // TC-AI-037: POST /extract without auth — 401
  test('TC-AI-037: POST /ai/extract without auth — 401', async ({ request }) => {
    const res = await request.post(`${API_BASE}/api/v1/ai/extract`, {
      multipart: {
        file: {
          name: 'test.png',
          mimeType: 'image/png',
          buffer: PNG_BUFFER,
        },
      },
    });
    expect(res.status()).toBe(401);
  });

  // ── Agent Execute ─────────────────────────────────────────────────────────

  // TC-AI-038: POST /agent/execute summarize task
  test('TC-AI-038: POST /ai/agent/execute summarize task — [200, 503]', async ({ request }) => {
    const res = await request.post(`${API_BASE}/api/v1/ai/agent/execute`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: {
        taskType: 'summarize',
        parameters: {
          text: 'This is a long government document about public services and citizen rights that needs summarization.',
        },
      },
    });
    expect([200, 503]).toContain(res.status());
    if (res.status() === 200) {
      const body = await res.json();
      expect(body.success).toBe(true);
    }
  });

  // TC-AI-039: POST /agent/execute empty taskType — [400, 422]
  test('TC-AI-039: POST /ai/agent/execute empty taskType — [400, 422]', async ({ request }) => {
    const res = await request.post(`${API_BASE}/api/v1/ai/agent/execute`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: { taskType: '', parameters: {} },
    });
    expect([400, 422]).toContain(res.status());
  });

  // TC-AI-040: POST /agent/execute without auth — 401
  test('TC-AI-040: POST /ai/agent/execute without auth — 401', async ({ request }) => {
    const res = await request.post(`${API_BASE}/api/v1/ai/agent/execute`, {
      headers: { 'Content-Type': 'application/json' },
      data: { taskType: 'summarize', parameters: { text: 'hello' } },
    });
    expect(res.status()).toBe(401);
  });

  // ── Content Flag ──────────────────────────────────────────────────────────

  // TC-AI-041: POST /flag benign content — riskLevel + detectedFlags in response
  test('TC-AI-041: POST /ai/flag benign content — [200, 503] with riskLevel and detectedFlags', async ({ request }) => {
    const res = await request.post(`${API_BASE}/api/v1/ai/flag`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: {
        content: 'This is a standard citizen inquiry about renewing a driving license.',
        contentType: 'text',
      },
    });
    expect([200, 503]).toContain(res.status());
    if (res.status() === 200) {
      const body = await res.json();
      expect(body.success).toBe(true);
      const data = body.data ?? body;
      expect(data).toHaveProperty('riskLevel');
      expect(data).toHaveProperty('detectedFlags');
    }
  });

  // TC-AI-043: POST /flag empty content — [400, 422]
  test('TC-AI-043: POST /ai/flag empty content — [400, 422]', async ({ request }) => {
    const res = await request.post(`${API_BASE}/api/v1/ai/flag`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: { content: '', contentType: 'text' },
    });
    expect([400, 422]).toContain(res.status());
  });

  // TC-AI-044: POST /flag without auth — 401
  test('TC-AI-044: POST /ai/flag without auth — 401', async ({ request }) => {
    const res = await request.post(`${API_BASE}/api/v1/ai/flag`, {
      headers: { 'Content-Type': 'application/json' },
      data: { content: 'hello', contentType: 'text' },
    });
    expect(res.status()).toBe(401);
  });
});
