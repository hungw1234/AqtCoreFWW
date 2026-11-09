import { type APIRequestContext } from '@playwright/test';

const API_BASE = process.env.API_BASE ?? 'http://127.0.0.1:5001';

interface TrackedEntity {
  type: string;
  id: string;
  deleteUrl: string;
}

/**
 * Test data factory for non-Identity modules (Cases, Files, Notifications, etc).
 * Follows same pattern as TestDataFactory — tracks entities for reverse-order cleanup.
 * Split from TestDataFactory to keep each file under 200 lines.
 */
export class TestDataModulesFactory {
  private created: TrackedEntity[] = [];
  private static readonly MAX_ENTITIES = 50;

  constructor(private request: APIRequestContext) {}

  // ── Files ─────────────────────────────────────────────────────────────

  async createFileRecord(overrides?: Record<string, unknown>): Promise<{ id: string }> {
    const data = {
      fileName: `e2e-file-${Date.now()}.txt`,
      contentType: 'text/plain',
      description: 'E2E test file',
      ...overrides,
    };
    return this.create('/api/v1/files', 'file', data);
  }

  // ── Notifications ─────────────────────────────────────────────────────

  async createNotificationTemplate(overrides?: Record<string, unknown>): Promise<{ id: string }> {
    const data = {
      name: `E2E Notification ${Date.now()}`,
      subject: 'E2E Test Subject',
      body: 'E2E test notification body',
      channel: 'InApp',
      ...overrides,
    };
    return this.create('/api/v1/admin/notification-templates', 'notification-template', data);
  }

  // ── Organization ──────────────────────────────────────────────────────

  async createOrgUnit(overrides?: Record<string, unknown>): Promise<{ id: string }> {
    const data = {
      name: `E2E Org ${Date.now()}`,
      nameEn: `E2E Org EN ${Date.now()}`,
      code: `E2E-ORG-${Date.now()}`,
      level: 1,
      ...overrides,
    };
    return this.create('/api/v1/admin/org/units', 'org-unit', data);
  }

  // ── Workflow ──────────────────────────────────────────────────────────

  async createWorkflowDefinition(overrides?: Record<string, unknown>): Promise<{ id: string }> {
    const data = {
      name: `E2E Workflow ${Date.now()}`,
      description: 'E2E test workflow',
      ...overrides,
    };
    return this.create('/api/v1/workflow/definitions', 'workflow-definition', data);
  }

  // ── Rules ─────────────────────────────────────────────────────────────

  async createRuleSet(overrides?: Record<string, unknown>): Promise<{ id: string }> {
    const data = {
      name: `E2E RuleSet ${Date.now()}`,
      description: 'E2E test rule set',
      ...overrides,
    };
    return this.create('/api/v1/rules/rule-sets', 'rule-set', data);
  }

  // ── Document Templates ────────────────────────────────────────────────

  async createDocumentTemplate(overrides?: Record<string, unknown>): Promise<{ id: string }> {
    const data = {
      name: `E2E DocTmpl ${Date.now()}`,
      description: 'E2E test document template',
      ...overrides,
    };
    return this.create('/api/v1/document-templates', 'document-template', data);
  }

  // ── Reporting Widgets ─────────────────────────────────────────────────

  async createWidget(overrides?: Record<string, unknown>): Promise<{ id: string }> {
    const data = {
      name: `E2E Widget ${Date.now()}`,
      type: 'Chart',
      ...overrides,
    };
    return this.create('/api/v1/widgets', 'widget', data);
  }

  // ── Cleanup ───────────────────────────────────────────────────────────

  /** Cleanup all created entities in reverse order (respects FK constraints) */
  async cleanup() {
    for (const entity of this.created.reverse()) {
      try {
        await this.request.delete(`${API_BASE}${entity.deleteUrl}`);
      } catch {
        console.warn(`[TestDataModulesFactory] Failed to cleanup ${entity.type}/${entity.id}`);
      }
    }
    this.created = [];
  }

  // ── Internal ──────────────────────────────────────────────────────────

  private async create(
    endpoint: string,
    type: string,
    data: Record<string, unknown>,
  ): Promise<{ id: string }> {
    if (this.created.length >= TestDataModulesFactory.MAX_ENTITIES) {
      throw new Error(
        `TestDataModulesFactory: entity cap reached (${TestDataModulesFactory.MAX_ENTITIES}). Call cleanup() first.`,
      );
    }

    const res = await this.request.post(`${API_BASE}${endpoint}`, { data });
    let id = '';

    // Try Location header first (201 Created with empty body)
    const location = res.headers()['location'] ?? '';
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
