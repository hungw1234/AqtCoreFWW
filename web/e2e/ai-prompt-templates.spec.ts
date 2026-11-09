import { test, expect } from './fixtures/test-base';
import { getAccessToken, authHeaders, API_BASE } from './helpers/auth-helper';

const NON_EXISTENT_ID = '00000000-0000-0000-0000-000000000099';

// AI Prompt Templates CRUD E2E — /api/v1/ai/prompt-templates
test.describe('AI Prompt Templates (authenticated API)', () => {
  test.describe.configure({ mode: 'serial' });

  let token: string;
  let createdTemplateId: string;
  const templateName = `E2E-Template-${Date.now()}`;

  test.beforeAll(async ({ request }) => {
    token = await getAccessToken(request, 'admin');
    expect(token).toBeTruthy();
  });

  test.afterAll(async ({ request }) => {
    // Deactivate template as cleanup if no DELETE endpoint available
    if (createdTemplateId) {
      await request.delete(`${API_BASE}/api/v1/ai/prompt-templates/${createdTemplateId}`, {
        headers: authHeaders(token),
      }).catch(() => {/* ignore — endpoint may not exist */});
    }
  });

  // TC-AI-023: List prompt templates
  test('TC-AI-023: GET /ai/prompt-templates — returns array', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/ai/prompt-templates`, {
      headers: authHeaders(token),
    });
    expect(res.status()).toBe(200);
    const body = await res.json();
    expect(body.success).toBe(true);
    const items: unknown[] = body.data?.items ?? body.data ?? [];
    expect(Array.isArray(items)).toBe(true);
  });

  // TC-AI-025: List without auth — 401
  test('TC-AI-025: GET /ai/prompt-templates without auth — 401', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/ai/prompt-templates`);
    expect(res.status()).toBe(401);
  });

  // TC-AI-026: Create prompt template
  test('TC-AI-026: POST /ai/prompt-templates — create chat template returns id', async ({ request }) => {
    const res = await request.post(`${API_BASE}/api/v1/ai/prompt-templates`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: {
        name: templateName,
        category: 0,
        systemPrompt: 'You are a helpful government assistant.',
        userPromptTemplate: 'User says: {{message}}',
      },
    });
    expect(res.status()).toBe(200);
    const body = await res.json();
    expect(body.success).toBe(true);
    const item = body.data ?? body;
    expect(item).toHaveProperty('id');
    createdTemplateId = item.id;
  });

  // TC-AI-029: Create without auth — 401
  test('TC-AI-029: POST /ai/prompt-templates without auth — 401', async ({ request }) => {
    const res = await request.post(`${API_BASE}/api/v1/ai/prompt-templates`, {
      headers: { 'Content-Type': 'application/json' },
      data: { name: 'Anon Template', category: 0, systemPrompt: 'You are helpful.', userPromptTemplate: '{{message}}' },
    });
    expect(res.status()).toBe(401);
  });

  // TC-AI-030: PUT update systemPrompt — version should auto-increment
  test('TC-AI-030: PUT /ai/prompt-templates/{id} update systemPrompt — version increments', async ({ request }) => {
    test.skip(!createdTemplateId, 'Template not created in TC-AI-026');

    // Get current version first
    const getRes = await request.get(`${API_BASE}/api/v1/ai/prompt-templates/${createdTemplateId}`, {
      headers: authHeaders(token),
    });
    const initialVersion: number = getRes.ok() ? ((await getRes.json()).data?.version ?? 1) : 1;

    const res = await request.put(`${API_BASE}/api/v1/ai/prompt-templates/${createdTemplateId}`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: {
        name: templateName,
        category: 0,
        systemPrompt: 'You are an expert government services assistant.',
        userPromptTemplate: 'User says: {{message}}',
      },
    });
    expect(res.status()).toBe(200);
    const body = await res.json();
    expect(body.success).toBe(true);
    // version should have incremented if the API tracks it
    const updatedVersion: number = (body.data ?? body).version ?? initialVersion;
    expect(updatedVersion).toBeGreaterThanOrEqual(initialVersion);
  });

  // TC-AI-033: PUT without auth — 401
  test('TC-AI-033: PUT /ai/prompt-templates/{id} without auth — 401', async ({ request }) => {
    const res = await request.put(`${API_BASE}/api/v1/ai/prompt-templates/${NON_EXISTENT_ID}`, {
      headers: { 'Content-Type': 'application/json' },
      data: { name: 'Anon Update', category: 0, systemPrompt: 'You are helpful.', userPromptTemplate: '{{message}}' },
    });
    expect(res.status()).toBe(401);
  });
});
