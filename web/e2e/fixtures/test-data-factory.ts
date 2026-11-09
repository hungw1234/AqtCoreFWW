import { type APIRequestContext } from '@playwright/test';

const API_BASE = process.env.API_BASE ?? 'http://127.0.0.1:5001';

interface TrackedEntity {
  type: string;
  id: string;
  deleteUrl: string;
}

/**
 * API-driven test data factory.
 * Creates entities via API and tracks them for reverse-order cleanup.
 * Max 50 entities per factory instance to prevent runaway creation.
 */
export class TestDataFactory {
  private created: TrackedEntity[] = [];
  private static readonly MAX_ENTITIES = 50;

  constructor(private request: APIRequestContext) {}

  async createUser(overrides?: Record<string, unknown>): Promise<{ id: string }> {
    const data = {
      email: `e2e-${Date.now()}@test.local`,
      fullName: 'E2E TestUser',
      password: 'E2eTest@12345',
      ...overrides,
    };
    return this.create('/api/v1/admin/users', 'user', data);
  }

  async createGroup(overrides?: Record<string, unknown>): Promise<{ id: string }> {
    const data = {
      name: `E2E Group ${Date.now()}`,
      description: 'Created by E2E test',
      ...overrides,
    };
    return this.create('/api/v1/admin/groups', 'group', data);
  }

  async createFormTemplate(overrides?: Record<string, unknown>): Promise<{ id: string }> {
    const data = {
      name: `E2E Form ${Date.now()}`,
      code: `e2e-form-${Date.now()}`,
      description: 'E2E test form template',
      ...overrides,
    };
    return this.create('/api/v1/forms/templates', 'form-template', data);
  }

  async createSystemParam(overrides?: Record<string, unknown>): Promise<{ id: string }> {
    const data = {
      key: `E2E_PARAM_${Date.now()}`,
      value: 'e2e-test-value',
      description: 'Created by E2E test',
      ...overrides,
    };
    return this.create('/api/v1/admin/system-params', 'system-param', data);
  }

  async createJitProviderConfig(overrides?: Record<string, unknown>): Promise<{ id: string }> {
    const data = {
      name: `E2E JIT Provider ${Date.now()}`,
      issuerUrl: 'https://mock-idp.test',
      clientId: 'e2e-test-client',
      clientSecret: 'e2e-secret',
      domainWhitelist: ['allowed-domain.com'],
      autoApprove: false,
      ...overrides,
    };
    return this.create('/api/v1/admin/jit-provider-configs', 'jit-provider-config', data);
  }

  /** Cleanup all created entities in reverse order (respects FK constraints) */
  async cleanup() {
    for (const entity of this.created.reverse()) {
      try {
        await this.request.delete(`${API_BASE}${entity.deleteUrl}`);
      } catch {
        // Best-effort cleanup — don't fail tests if cleanup fails
        console.warn(`[TestDataFactory] Failed to cleanup ${entity.type}/${entity.id}`);
      }
    }
    this.created = [];
  }

  private async create(endpoint: string, type: string, data: Record<string, unknown>): Promise<{ id: string }> {
    if (this.created.length >= TestDataFactory.MAX_ENTITIES) {
      throw new Error(`TestDataFactory: entity cap reached (${TestDataFactory.MAX_ENTITIES}). Call cleanup() first.`);
    }

    const res = await this.request.post(`${API_BASE}${endpoint}`, { data });
    let id = '';

    // Try Location header first (201 Created with empty body)
    const headers = res.headers();
    const location = headers['location'] ?? '';
    const locMatch = location.match(/\/([0-9a-f-]{36})$/i);
    if (locMatch) {
      id = locMatch[1];
    } else {
      // Fallback: parse JSON body
      const text = await res.text();
      if (text) {
        try {
          const body = JSON.parse(text);
          id = body.data?.id ?? body.id ?? '';
        } catch { /* non-JSON response */ }
      }
    }

    if (id) {
      this.created.push({ type, id, deleteUrl: `${endpoint}/${id}` });
    }

    return { id };
  }
}
