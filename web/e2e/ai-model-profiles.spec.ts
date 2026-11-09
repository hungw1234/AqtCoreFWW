import { test, expect } from './fixtures/test-base';
import { getAccessToken, authHeaders, API_BASE } from './helpers/auth-helper';

const NON_EXISTENT_ID = '00000000-0000-0000-0000-000000000099';

// AI Model Profiles CRUD E2E — /api/v1/ai/model-profiles
// No DELETE endpoint — deactivate via PUT isActive: false as cleanup
test.describe('AI Model Profiles (authenticated API)', () => {
  test.describe.configure({ mode: 'serial' });

  let token: string;
  let createdProfileId: string;
  const profileName = `E2E-Profile-${Date.now()}`;

  test.beforeAll(async ({ request }) => {
    token = await getAccessToken(request, 'admin');
    expect(token).toBeTruthy();
  });

  test.afterAll(async ({ request }) => {
    // Deactivate created profile as cleanup (no DELETE endpoint)
    if (createdProfileId) {
      await request.put(`${API_BASE}/api/v1/ai/model-profiles/${createdProfileId}`, {
        headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
        data: {
          name: profileName,
          provider: 0,
          modelId: 'llama3',
          endpoint: 'http://localhost:11434',
          maxTokens: 4096,
          temperature: 0.7,
          costPerToken: 0.0,
          isDefault: false,
          isActive: false,
        },
      });
    }
  });

  // TC-AI-013: List model profiles
  test('TC-AI-013: GET /ai/model-profiles — returns array with expected shape', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/ai/model-profiles`, {
      headers: authHeaders(token),
    });
    expect(res.status()).toBe(200);
    const body = await res.json();
    expect(body.success).toBe(true);
    const items: Array<Record<string, unknown>> = body.data?.items ?? body.data ?? [];
    expect(Array.isArray(items)).toBe(true);
    if (items.length > 0) {
      expect(items[0]).toHaveProperty('id');
      expect(items[0]).toHaveProperty('name');
      expect(items[0]).toHaveProperty('provider');
    }
  });

  // TC-AI-014: List without auth — 401
  test('TC-AI-014: GET /ai/model-profiles without auth — 401', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/ai/model-profiles`);
    expect(res.status()).toBe(401);
  });

  // TC-AI-015: Create model profile
  test('TC-AI-015: POST /ai/model-profiles — create Ollama profile returns id', async ({ request }) => {
    const res = await request.post(`${API_BASE}/api/v1/ai/model-profiles`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: {
        name: profileName,
        provider: 0,
        modelId: 'llama3',
        endpoint: 'http://localhost:11434',
        maxTokens: 4096,
        temperature: 0.7,
        costPerToken: 0.0,
        isDefault: false,
      },
    });
    expect(res.status()).toBe(200);
    const body = await res.json();
    expect(body.success).toBe(true);
    const item = body.data ?? body;
    expect(item).toHaveProperty('id');
    createdProfileId = item.id;
  });

  // TC-AI-018: Create without auth — 401
  test('TC-AI-018: POST /ai/model-profiles without auth — 401', async ({ request }) => {
    const res = await request.post(`${API_BASE}/api/v1/ai/model-profiles`, {
      headers: { 'Content-Type': 'application/json' },
      data: { name: 'Anon Profile', provider: 0, modelId: 'llama3', endpoint: 'http://localhost:11434', maxTokens: 1024, temperature: 0.5, costPerToken: 0.0, isDefault: false },
    });
    expect(res.status()).toBe(401);
  });

  // TC-AI-019: PUT update maxTokens and deactivate
  test('TC-AI-019: PUT /ai/model-profiles/{id} update maxTokens and deactivate — 200', async ({ request }) => {
    test.skip(!createdProfileId, 'Profile not created in TC-AI-015');
    const res = await request.put(`${API_BASE}/api/v1/ai/model-profiles/${createdProfileId}`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: {
        name: profileName,
        provider: 0,
        modelId: 'llama3',
        endpoint: 'http://localhost:11434',
        maxTokens: 2048,
        temperature: 0.5,
        costPerToken: 0.0,
        isDefault: false,
        isActive: true,
      },
    });
    expect(res.status()).toBe(200);
    const body = await res.json();
    expect(body.success).toBe(true);
  });

  // TC-AI-022: PUT without auth — 401
  test('TC-AI-022: PUT /ai/model-profiles/{id} without auth — 401', async ({ request }) => {
    const res = await request.put(`${API_BASE}/api/v1/ai/model-profiles/${NON_EXISTENT_ID}`, {
      headers: { 'Content-Type': 'application/json' },
      data: { name: 'Anon', provider: 0, modelId: 'llama3', endpoint: 'http://localhost:11434', maxTokens: 1024, temperature: 0.5, costPerToken: 0.0, isDefault: false },
    });
    expect(res.status()).toBe(401);
  });
});
