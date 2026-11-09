import { test, expect } from './fixtures/test-base';
import { getAccessToken, authHeaders, API_BASE } from './helpers/auth-helper';

// Ollama availability probe — runs once in beforeAll.
// If Ollama is unreachable, all tests that depend on it are skipped via beforeEach gate.
// Auth-only tests (401 checks) are unaffected by Ollama state.
const OLLAMA = process.env.OLLAMA_BASE_URL ?? 'http://localhost:11434';
let ollamaUp = false;

// AI chat, stream, document ingest, session history E2E
test.describe('AI Chat Sessions (authenticated API)', () => {
  test.describe.configure({ mode: 'serial' });

  let token: string;
  let sessionId: string;

  test.beforeAll(async ({ request }) => {
    token = await getAccessToken(request, 'admin');
    expect(token).toBeTruthy();

    // Probe Ollama availability — 5s timeout.
    // If unreachable, Ollama-dependent tests skip rather than accepting 503 as PASS.
    try {
      const r = await fetch(`${OLLAMA}/api/tags`, { signal: AbortSignal.timeout(5000) });
      ollamaUp = r.ok;
    } catch {
      ollamaUp = false;
    }

    if (!ollamaUp) {
      console.warn(`[ai-chat] Ollama not reachable at ${OLLAMA} — Ollama-dependent tests will be skipped.`);
    }
  });

  // TC-AI-001: Start a new chat session
  // Requires Ollama — skip if not available.
  test('TC-AI-001: POST /ai/chat new session — 200', async ({ request }) => {
    test.skip(!ollamaUp, 'Ollama not available');
    const res = await request.post(`${API_BASE}/api/v1/ai/chat`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: { message: 'Hello, what can you help with?', sessionId: null, modelProfileId: null },
    });
    expect(res.status()).toBe(200);
    const body = await res.json();
    expect(body.success).toBe(true);
    const data = body.data ?? body;
    expect(data).toHaveProperty('sessionId');
    expect(data).toHaveProperty('messageId');
    expect(data).toHaveProperty('response');
    sessionId = data.sessionId;
  });

  // TC-AI-002: Continue existing session
  // Skip if no sessionId from TC-AI-001 OR Ollama unavailable.
  test('TC-AI-002: POST /ai/chat continue session — sessionId matches', async ({ request }) => {
    test.skip(!ollamaUp, 'Ollama not available');
    test.skip(!sessionId, 'No sessionId from TC-AI-001 (AI backend unavailable)');
    const res = await request.post(`${API_BASE}/api/v1/ai/chat`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: { message: 'Tell me more about case management.', sessionId, modelProfileId: null },
    });
    // 409 may occur if the AI session has a concurrent request conflict
    expect([200, 409]).toContain(res.status());
    if (res.status() === 200) {
      const body = await res.json();
      if (body.success === true) {
        const data = body.data ?? body;
        expect(data.sessionId).toBe(sessionId);
      }
    }
  });

  // TC-AI-003: POST /chat without auth — 401 (auth-only, no Ollama dependency)
  test('TC-AI-003: POST /ai/chat without auth — 401', async ({ request }) => {
    const res = await request.post(`${API_BASE}/api/v1/ai/chat`, {
      headers: { 'Content-Type': 'application/json' },
      data: { message: 'Hello' },
    });
    expect(res.status()).toBe(401);
  });

  // TC-AI-005: Stream endpoint — requires Ollama
  test('TC-AI-005: POST /ai/chat/stream — 200', async ({ request }) => {
    test.skip(!ollamaUp, 'Ollama not available');
    const res = await request.post(`${API_BASE}/api/v1/ai/chat/stream`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: { message: 'Summarize government services.', sessionId: null, modelProfileId: null },
    });
    expect(res.status()).toBe(200);
    // Stream responses use text/event-stream or application/json
    const contentType = res.headers()['content-type'] ?? '';
    expect(contentType.length).toBeGreaterThan(0);
  });

  // TC-AI-006: Stream without auth — 401 (auth-only, no Ollama dependency)
  test('TC-AI-006: POST /ai/chat/stream without auth — 401', async ({ request }) => {
    const res = await request.post(`${API_BASE}/api/v1/ai/chat/stream`, {
      headers: { 'Content-Type': 'application/json' },
      data: { message: 'Hello' },
    });
    expect(res.status()).toBe(401);
  });

  // TC-AI-007: Document ingest — requires Ollama for embedding; 400 if fileId not found
  test('TC-AI-007: POST /ai/documents/ingest — [200, 204, 400]', async ({ request }) => {
    test.skip(!ollamaUp, 'Ollama not available');
    const res = await request.post(`${API_BASE}/api/v1/ai/documents/ingest`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: {
        fileId: '00000000-0000-0000-0000-000000000001',
        text: 'This is a test document for AI ingestion during E2E testing.',
      },
    });
    // 400 if fileId not found in DB; 204/200 on success — 503 is a real regression now
    expect([200, 204, 400]).toContain(res.status());
  });

  // TC-AI-008: Document ingest without auth — 401 (auth-only, no Ollama dependency)
  test('TC-AI-008: POST /ai/documents/ingest without auth — 401', async ({ request }) => {
    const res = await request.post(`${API_BASE}/api/v1/ai/documents/ingest`, {
      headers: { 'Content-Type': 'application/json' },
      data: { title: 'Anon Doc', content: 'Content.' },
    });
    expect(res.status()).toBe(401);
  });

  // TC-AI-009: Get chat session history — requires Ollama (needs sessionId from TC-AI-001)
  test('TC-AI-009: GET /ai/chat/{sessionId}/history — 200', async ({ request }) => {
    test.skip(!ollamaUp, 'Ollama not available');
    test.skip(!sessionId, 'No sessionId from TC-AI-001 (AI backend unavailable)');
    const res = await request.get(`${API_BASE}/api/v1/ai/chat/${sessionId}/history`, {
      headers: authHeaders(token),
    });
    expect(res.status()).toBe(200);
    const body = await res.json();
    expect(body.success).toBe(true);
  });

  // TC-AI-012: Session history without auth — 401 (auth-only, no Ollama dependency)
  test('TC-AI-012: GET /ai/chat/{sessionId}/history without auth — 401', async ({ request }) => {
    // AiSession is T1 (long Id) — sessionId route constraint `:long`. Use numeric so route matches and auth runs.
    const fakeSession = 999999;
    const res = await request.get(`${API_BASE}/api/v1/ai/chat/${fakeSession}/history`);
    expect(res.status()).toBe(401);
  });
});
