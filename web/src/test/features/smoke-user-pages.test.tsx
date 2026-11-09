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
    user: { profile: { tenant_id: 'test-tenant', sub: 'test-user', name: 'Test User', email: 'test@example.com' } },
    isAuthenticated: true,
    login: vi.fn(),
    logout: vi.fn(),
  }),
}));

// usePermissions imports useAuth via a relative path, so the alias mock above doesn't reach it —
// mock the hook directly (grant-all) so permission-gated UI like the inbox "create dossier" button renders.
vi.mock('@/features/auth/use-permissions', () => ({
  usePermissions: () => ({
    permissions: [],
    roles: [],
    hasPermission: () => true,
    hasRole: () => false,
    isPermissionsLoading: false,
    isPermissionsRefetching: false,
  }),
}));

// ──────────────────────────────────────────────────────────────────────────────
// Feature-Specific Mocks
// ──────────────────────────────────────────────────────────────────────────────

// AI Search Page
vi.mock('@/features/ai/ai-api', () => ({
  useNlqQuery: () => ({ mutate: vi.fn(), isPending: false }),
}));

// Delegation List Page
vi.mock('@/features/delegations/delegation-api', () => ({
  useDelegations: () => ({ data: [], isLoading: false }),
  useCreateDelegation: () => ({ mutate: vi.fn(), isPending: false }),
  useRevokeDelegation: () => ({ mutate: vi.fn(), isPending: false }),
  useApproveDelegation: () => ({ mutate: vi.fn(), mutateAsync: vi.fn(), isPending: false }),
}));

// Inbox Page — useMyInbox returns bare WorkflowTaskDto[] (no .items wrapper)
vi.mock('@/features/inbox/inbox-api', () => ({
  useMyInbox: () => ({ data: [], isFetching: false }),
}));

// My-processed View 2 tab — useMyProcessedInfinite (useInfiniteQuery wrapper)
vi.mock('@/features/inbox/my-processed-api', () => ({
  useMyProcessedInfinite: () => ({
    data: undefined,
    isFetching: false,
    isFetchingNextPage: false,
    hasNextPage: false,
    fetchNextPage: vi.fn(),
  }),
}));

// BP active list used by ProcessedDossiersList for BP name resolution
vi.mock('@/features/business-process/business-process-run-api', () => ({
  useActiveBps: () => ({ data: { items: [] }, isLoading: false }),
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

// Notification Templates Admin Page
vi.mock('@/features/notifications/notification-templates-api', () => ({
  useNotificationTemplates: () => ({ data: { items: [], totalCount: 0 }, isFetching: false }),
  useCreateNotificationTemplate: () => ({ mutate: vi.fn(), isPending: false }),
  useUpdateNotificationTemplate: () => ({ mutateAsync: vi.fn(), isPending: false }),
  useDeleteNotificationTemplate: () => ({ mutate: vi.fn(), isPending: false }),
}));

// Org Tree Page
vi.mock('@/features/organization/org-api', () => ({
  useOrgUnits: () => ({ data: [], isFetching: false }),
  useCreateOrgUnit: () => ({ mutateAsync: vi.fn(), isPending: false }),
  useUpdateOrgUnit: () => ({ mutateAsync: vi.fn(), isPending: false }),
  useOrgUnitStaff: () => ({ data: [], isFetching: false }),
}));

vi.mock('@/features/organization/org-unit-form-modal', () => ({
  OrgUnitFormModal: () => null,
}));

vi.mock('@/features/organization/staff-assignment-table', () => ({
  StaffAssignmentTable: () => null,
}));

// Profile Page
vi.mock('@/features/profile/profile-api', () => ({
  useChangePassword: () => ({ mutateAsync: vi.fn(), isPending: false }),
}));

// Report Definitions Page
vi.mock('@/features/reports/report-api', () => ({
  useReportDefinitions: () => ({ data: [], isLoading: false }),
  useCreateReportDefinition: () => ({ mutate: vi.fn(), isPending: false }),
  useReportExecution: () => ({ data: null, isLoading: false }),
  downloadReport: vi.fn(),
}));

vi.mock('@/features/reports/report-run-modal', () => ({
  ReportRunModal: () => null,
}));

// Roles Page — no API calls, static component

// ──────────────────────────────────────────────────────────────────────────────
// Utility: QueryClient wrapper
// ──────────────────────────────────────────────────────────────────────────────

function makeWrapper() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return ({ children }: { children: ReactNode }) =>
    createElement(QueryClientProvider, { client: qc }, children);
}

// ──────────────────────────────────────────────────────────────────────────────
// Tests
// ──────────────────────────────────────────────────────────────────────────────

// ──────────────────────────────────────────────────────────────────────────────
// Import components — these need to wait for mocks to be registered
// ──────────────────────────────────────────────────────────────────────────────

import { AiSearchPage } from '@/features/ai/ai-search-page';
import { DelegationListPage } from '@/features/delegations/delegation-list-page';
import { WorkflowInboxPage } from '@/features/inbox/inbox-page';
import { MasterDataPage } from '@/features/master-data/master-data-page';
import { NotificationTemplatesAdminPage } from '@/features/notifications/notification-templates-admin-page';
import { OrgTreePage } from '@/features/organization/org-tree-page';
import { ProfilePage } from '@/features/profile/profile-page';
import { ReportDefinitionsPage } from '@/features/reports/report-definitions-page';
import { ReportExecutionsPage } from '@/features/reports/report-executions-page';
import { RolesPage } from '@/features/roles/roles-page';

// ──────────────────────────────────────────────────────────────────────────────
// Tests
// ──────────────────────────────────────────────────────────────────────────────

describe('AiSearchPage', () => {
  it('renders without crashing', () => {
    const { container } = render(<AiSearchPage />, { wrapper: makeWrapper() });
    expect(container.firstChild).toBeTruthy();
  });
});

describe('DelegationListPage', () => {
  it('renders without crashing', () => {
    const { container } = render(<DelegationListPage />, { wrapper: makeWrapper() });
    expect(container.firstChild).toBeTruthy();
  });
});

describe('WorkflowInboxPage', () => {
  it('renders without crashing', () => {
    const { container } = render(<WorkflowInboxPage />, { wrapper: makeWrapper() });
    expect(container.firstChild).toBeTruthy();
  });
});

describe('MasterDataPage', () => {
  it('renders without crashing', () => {
    const { container } = render(<MasterDataPage />, { wrapper: makeWrapper() });
    expect(container.firstChild).toBeTruthy();
  });
});

describe('NotificationTemplatesAdminPage', () => {
  it('renders without crashing', () => {
    const { container } = render(<NotificationTemplatesAdminPage />, { wrapper: makeWrapper() });
    expect(container.firstChild).toBeTruthy();
  });
});

describe('OrgTreePage', () => {
  it('renders without crashing', () => {
    const { container } = render(<OrgTreePage />, { wrapper: makeWrapper() });
    expect(container.firstChild).toBeTruthy();
  });
});

describe('ProfilePage', () => {
  it('renders without crashing', () => {
    const { container } = render(<ProfilePage />, { wrapper: makeWrapper() });
    expect(container.firstChild).toBeTruthy();
  });
});

describe('ReportDefinitionsPage', () => {
  it('renders without crashing', () => {
    const { container } = render(<ReportDefinitionsPage />, { wrapper: makeWrapper() });
    expect(container.firstChild).toBeTruthy();
  });
});

describe('ReportExecutionsPage', () => {
  it('renders without crashing', () => {
    const { container } = render(<ReportExecutionsPage />, { wrapper: makeWrapper() });
    expect(container.firstChild).toBeTruthy();
  });
});

describe('RolesPage', () => {
  it('renders without crashing', () => {
    const { container } = render(<RolesPage />, { wrapper: makeWrapper() });
    expect(container.firstChild).toBeTruthy();
  });
});
