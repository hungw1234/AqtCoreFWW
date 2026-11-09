import { describe, it, expect, vi } from 'vitest';
import { render } from '@testing-library/react';
import { createElement, type ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

// ──────────────────────────────────────────────────────────────────────────────
// Global Mocks
// ──────────────────────────────────────────────────────────────────────────────

vi.mock('react-i18next', () => ({
  useTranslation: () => ({ t: (key: string) => key, i18n: { language: 'vi' } }),
}));

vi.mock('@tanstack/react-router', () => ({
  useNavigate: () => vi.fn(),
  useParams: () => ({}),
  useLocation: () => ({ pathname: '/' }),
  useSearch: () => ({}),
  Link: ({ children }: { children: ReactNode }) => children,
}));

vi.mock('@/features/auth/use-auth', () => ({
  useAuth: () => ({
    user: { profile: { tenant_id: 'test-tenant', sub: 'test-user', name: 'Test Admin' } },
    isAuthenticated: true,
    login: vi.fn(),
    logout: vi.fn(),
  }),
}));

vi.mock('@/features/auth/use-permissions', () => ({
  usePermissions: () => ({
    permissions: ['admin.read', 'admin.write', 'audit.read', 'roles.read'],
    roles: ['SystemAdmin'],
  }),
}));

vi.mock('@/core/hooks/use-server-pagination', () => ({
  useServerPagination: () => ({
    antPagination: { current: 1, pageSize: 20 },
    toQueryParams: () => ({ pageNumber: 1, pageSize: 20 }),
  }),
}));

// ──────────────────────────────────────────────────────────────────────────────
// Feature-Specific Mocks
// ──────────────────────────────────────────────────────────────────────────────

// Session Admin Page
vi.mock('@/features/sessions/session-api', () => ({
  useActiveSessions: () => ({ data: { items: [], totalCount: 0 }, isFetching: false }),
  useRevokeSession: () => ({ mutate: vi.fn(), isPending: false }),
  useRevokeUserSessions: () => ({ mutate: vi.fn(), isPending: false }),
}));

// System Params Page
vi.mock('@/features/system-params/system-params-api', () => ({
  useSystemParams: () => ({ data: [], isFetching: false }),
  useUpdateSystemParam: () => ({ mutateAsync: vi.fn(), isPending: false }),
  useFeatureFlags: () => ({ data: { items: [] }, isFetching: false }),
  useUpdateFeatureFlag: () => ({ mutateAsync: vi.fn(), isPending: false }),
  useAnnouncements: () => ({ data: { items: [] }, isFetching: false }),
  useCreateAnnouncement: () => ({ mutate: vi.fn(), isPending: false }),
  useDeleteAnnouncement: () => ({ mutate: vi.fn(), isPending: false }),
}));

vi.mock('@/features/system-params/params-table', () => ({
  ParamsTable: () => createElement('div', { 'data-testid': 'params-table' }),
}));

vi.mock('@/features/system-params/feature-flags-tab', () => ({
  FeatureFlagsTab: () => createElement('div', { 'data-testid': 'feature-flags-tab' }),
}));

vi.mock('@/features/system-params/announcements-tab', () => ({
  AnnouncementsTab: () => createElement('div', { 'data-testid': 'announcements-tab' }),
}));

// Master Data Page
vi.mock('@/features/master-data/master-data-api', () => ({
  useProvinces: () => ({ data: [], isFetching: false }),
  useDistricts: () => ({ data: [], isFetching: false }),
  useWards: () => ({ data: [], isFetching: false }),
  useCreateProvince: () => ({ mutateAsync: vi.fn(), isPending: false }),
  useUpdateProvince: () => ({ mutateAsync: vi.fn(), isPending: false }),
  useDeleteProvince: () => ({ mutateAsync: vi.fn(), isPending: false }),
  useCreateDistrict: () => ({ mutateAsync: vi.fn(), isPending: false }),
  useUpdateDistrict: () => ({ mutateAsync: vi.fn(), isPending: false }),
  useDeleteDistrict: () => ({ mutateAsync: vi.fn(), isPending: false }),
  useCreateWard: () => ({ mutateAsync: vi.fn(), isPending: false }),
  useUpdateWard: () => ({ mutateAsync: vi.fn(), isPending: false }),
  useDeleteWard: () => ({ mutateAsync: vi.fn(), isPending: false }),
}));

// API Keys Page
vi.mock('@/features/api-keys/api-key-api', () => ({
  useApiKeys: () => ({ data: [], isFetching: false }),
  useRevokeApiKey: () => ({ mutate: vi.fn(), isPending: false }),
}));

vi.mock('@/features/api-keys/api-key-create-modal', () => ({
  ApiKeyCreateModal: () => null,
}));

// ABAC Rules Page
vi.mock('@/features/abac-rules/abac-rules-api', () => ({
  useAbacRules: () => ({ data: [], isLoading: false }),
  useCreateAbacRule: () => ({ mutateAsync: vi.fn(), isPending: false }),
  useUpdateAbacRule: () => ({ mutateAsync: vi.fn(), isPending: false }),
  useDeleteAbacRule: () => ({ mutate: vi.fn(), isPending: false }),
}));

// Access Review Page
vi.mock('@/features/access-reviews/access-review-api', () => ({
  usePendingAccessReviews: () => ({ data: { items: [], totalCount: 0 }, isLoading: false }),
  useApproveAccessReview: () => ({ mutate: vi.fn(), isPending: false }),
  useRejectAccessReview: () => ({ mutate: vi.fn(), isPending: false }),
}));

// Health Check Page — no API mocks needed, uses fetch
vi.stubGlobal('fetch', vi.fn());

// Backup Admin Page
vi.mock('@/features/backup/backup-api', () => ({
  useBackupRecords: () => ({ data: { items: [], totalCount: 0 }, isFetching: false, refetch: vi.fn() }),
  useTriggerBackup: () => ({ mutateAsync: vi.fn(), isPending: false }),
  useTriggerRestoreDrill: () => ({ mutateAsync: vi.fn(), isPending: false }),
}));

// Webhook Deliveries Page
vi.mock('@/features/webhooks/webhook-api', () => ({
  useWebhookSubscriptions: () => ({ data: [], isLoading: false }),
  useWebhookDeliveries: () => ({ data: { items: [], totalCount: 0 }, isFetching: false, refetch: vi.fn() }),
  useTestWebhook: () => ({ mutateAsync: vi.fn(), isPending: false }),
}));

// Delegation List Page (also in admin section)
vi.mock('@/features/delegations/delegation-api', () => ({
  useDelegations: () => ({ data: [], isLoading: false }),
  useCreateDelegation: () => ({ mutate: vi.fn(), isPending: false }),
  useRevokeDelegation: () => ({ mutate: vi.fn(), isPending: false }),
}));

// Notification Templates Admin Page (also in admin section)
vi.mock('@/features/notifications/notification-templates-api', () => ({
  useNotificationTemplates: () => ({ data: { items: [], totalCount: 0 }, isFetching: false }),
  useCreateNotificationTemplate: () => ({ mutate: vi.fn(), isPending: false }),
  useUpdateNotificationTemplate: () => ({ mutateAsync: vi.fn(), isPending: false }),
  useDeleteNotificationTemplate: () => ({ mutate: vi.fn(), isPending: false }),
}));

// ──────────────────────────────────────────────────────────────────────────────
// Utility: QueryClient wrapper
// ──────────────────────────────────────────────────────────────────────────────

function makeWrapper() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return ({ children }: { children: ReactNode }) =>
    createElement(QueryClientProvider, { client: qc }, children);
}

// ──────────────────────────────────────────────────────────────────────────────
// Import components — these need to wait for mocks to be registered
// ──────────────────────────────────────────────────────────────────────────────

import { SessionAdminPage } from '@/features/sessions/session-admin-page';
import { SystemParamsPage } from '@/features/system-params/system-params-page';
import { MasterDataPage } from '@/features/master-data/master-data-page';
import { ApiKeyListPage } from '@/features/api-keys/api-key-list-page';
import { AbacRulesPage } from '@/features/abac-rules/abac-rules-page';
import { AccessReviewPage } from '@/features/access-reviews/access-review-page';
import { HealthCheckPage } from '@/features/admin/health-check-page';
import { BackupAdminPage } from '@/features/backup/backup-admin-page';
import { WebhookDeliveriesPage } from '@/features/webhooks/webhook-deliveries-page';

// ──────────────────────────────────────────────────────────────────────────────
// Tests
// ──────────────────────────────────────────────────────────────────────────────

describe('SessionAdminPage', () => {
  it('renders without crashing', () => {
    const { container } = render(<SessionAdminPage />, { wrapper: makeWrapper() });
    expect(container.firstChild).toBeTruthy();
  });
});

describe('SystemParamsPage', () => {
  it('renders without crashing', () => {
    const { container } = render(<SystemParamsPage />, { wrapper: makeWrapper() });
    expect(container.firstChild).toBeTruthy();
  });
});

describe('MasterDataPage', () => {
  it('renders without crashing', () => {
    const { container } = render(<MasterDataPage />, { wrapper: makeWrapper() });
    expect(container.firstChild).toBeTruthy();
  });
});

describe('ApiKeyListPage', () => {
  it('renders without crashing', () => {
    const { container } = render(<ApiKeyListPage />, { wrapper: makeWrapper() });
    expect(container.firstChild).toBeTruthy();
  });
});

describe('AbacRulesPage', () => {
  it('renders without crashing', () => {
    const { container } = render(<AbacRulesPage />, { wrapper: makeWrapper() });
    expect(container.firstChild).toBeTruthy();
  });
});

describe('AccessReviewPage', () => {
  it('renders without crashing', () => {
    const { container } = render(<AccessReviewPage />, { wrapper: makeWrapper() });
    expect(container.firstChild).toBeTruthy();
  });
});

describe('HealthCheckPage', () => {
  it('renders without crashing', () => {
    const { container } = render(<HealthCheckPage />, { wrapper: makeWrapper() });
    expect(container.firstChild).toBeTruthy();
  });
});

describe('BackupAdminPage', () => {
  it('renders without crashing', () => {
    const { container } = render(<BackupAdminPage />, { wrapper: makeWrapper() });
    expect(container.firstChild).toBeTruthy();
  });
});

describe('WebhookDeliveriesPage', () => {
  it('renders without crashing', () => {
    const { container } = render(<WebhookDeliveriesPage />, { wrapper: makeWrapper() });
    expect(container.firstChild).toBeTruthy();
  });
});
