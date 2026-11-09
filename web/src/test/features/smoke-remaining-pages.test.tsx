// Smoke tests for remaining feature pages not covered by dedicated test files
// TC-FE-SESS-001: Sessions page renders
// TC-FE-BACK-001: Backup page renders
// TC-FE-ABAC-001: ABAC rules page renders
// TC-FE-AR-001: Access reviews page renders
// TC-FE-AI-001: AI search page renders
// TC-FE-PROF-001: Profile page renders
// TC-FE-ROLE-001: Roles page renders

import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { createElement, type ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

// ──────────────────────────────────────────────────────────────────────────────
// Global mocks
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
    user: {
      profile: {
        tenant_id: 'test-tenant',
        sub: 'test-user',
        name: 'Test Admin',
        email: 'admin@example.com',
        role: ['SystemAdmin'],
      },
    },
    isAuthenticated: true,
    login: vi.fn(),
    logout: vi.fn(),
  }),
}));

vi.mock('@/features/auth', () => ({
  useAuth: () => ({
    user: {
      profile: {
        tenant_id: 'test-tenant',
        sub: 'test-user',
        name: 'Test Admin',
        email: 'admin@example.com',
        role: ['SystemAdmin'],
      },
    },
    isAuthenticated: true,
    login: vi.fn(),
    logout: vi.fn(),
  }),
}));

vi.mock('@/core/hooks/use-server-pagination', () => ({
  useServerPagination: () => ({
    antPagination: { current: 1, pageSize: 20 },
    toQueryParams: () => ({ pageNumber: 1, pageSize: 20 }),
  }),
}));

// ──────────────────────────────────────────────────────────────────────────────
// Feature-specific API mocks
// ──────────────────────────────────────────────────────────────────────────────

// TC-FE-SESS-001: Sessions
vi.mock('@/features/sessions/session-api', () => ({
  useActiveSessions: () => ({ data: { items: [], totalCount: 0 }, isFetching: false }),
  useRevokeSession: () => ({ mutate: vi.fn(), isPending: false }),
  useRevokeUserSessions: () => ({ mutate: vi.fn(), isPending: false }),
}));

// TC-FE-BACK-001: Backup
vi.mock('@/features/backup/backup-api', () => ({
  useBackupRecords: () => ({
    data: { items: [], totalCount: 0 },
    isFetching: false,
    refetch: vi.fn(),
  }),
  useTriggerBackup: () => ({ mutateAsync: vi.fn(), isPending: false }),
  useTriggerRestoreDrill: () => ({ mutateAsync: vi.fn(), isPending: false }),
}));

// TC-FE-ABAC-001: ABAC rules
vi.mock('@/features/abac-rules/abac-rules-api', () => ({
  useAbacRules: () => ({ data: [], isLoading: false }),
  useCreateAbacRule: () => ({ mutateAsync: vi.fn(), isPending: false }),
  useUpdateAbacRule: () => ({ mutateAsync: vi.fn(), isPending: false }),
  useDeleteAbacRule: () => ({ mutate: vi.fn(), isPending: false }),
}));

// TC-FE-AR-001: Access reviews
vi.mock('@/features/access-reviews/access-review-api', () => ({
  usePendingAccessReviews: () => ({ data: { items: [], totalCount: 0 }, isLoading: false }),
  useApproveAccessReview: () => ({ mutate: vi.fn(), isPending: false }),
  useRejectAccessReview: () => ({ mutate: vi.fn(), isPending: false }),
}));

// TC-FE-AI-001: AI search
vi.mock('@/features/ai/ai-api', () => ({
  useNlqQuery: () => ({ mutate: vi.fn(), isPending: false }),
}));

// TC-FE-PROF-001: Profile
vi.mock('@/features/profile/profile-api', () => ({
  useChangePassword: () => ({ mutateAsync: vi.fn(), isPending: false }),
}));

// TC-FE-ROLE-001: Roles — stub all hooks from roles-api (modal + drawer + page)
vi.mock('@/features/roles/roles-api', () => ({
  useRoles: () => ({
    data: [
      { id: 'r1', name: 'SystemAdmin', code: 'system-admin', roleType: 1, isActive: true },
      { id: 'r2', name: 'GovOfficer',  code: 'gov-officer',  roleType: 2, isActive: true },
    ],
    isLoading: false,
  }),
  useDeleteRole:          () => ({ mutateAsync: vi.fn(), isPending: false }),
  useActivateRole:        () => ({ mutateAsync: vi.fn(), isPending: false }),
  useCreateRole:          () => ({ mutateAsync: vi.fn(), isPending: false }),
  useUpdateRole:          () => ({ mutateAsync: vi.fn(), isPending: false }),
  usePermissions:         () => ({ data: [], isLoading: false }),
  useRolePermissions:     () => ({ data: [], isLoading: false }),
  useSyncRolePermissions: () => ({ mutateAsync: vi.fn(), isPending: false }),
}));

// ──────────────────────────────────────────────────────────────────────────────
// Imports — after mocks
// ──────────────────────────────────────────────────────────────────────────────

import { SessionAdminPage } from '@/features/sessions/session-admin-page';
import { BackupAdminPage } from '@/features/backup/backup-admin-page';
import { AbacRulesPage } from '@/features/abac-rules/abac-rules-page';
import { AccessReviewPage } from '@/features/access-reviews/access-review-page';
import { AiSearchPage } from '@/features/ai/ai-search-page';
import { ProfilePage } from '@/features/profile/profile-page';
import { RolesPage } from '@/features/roles/roles-page';

// ──────────────────────────────────────────────────────────────────────────────
// Utility
// ──────────────────────────────────────────────────────────────────────────────

function makeWrapper() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return ({ children }: { children: ReactNode }) =>
    createElement(QueryClientProvider, { client: qc }, children);
}

// ──────────────────────────────────────────────────────────────────────────────
// Tests
// ──────────────────────────────────────────────────────────────────────────────

describe('SessionAdminPage — TC-FE-SESS-001', () => {
  it('renders without crashing', () => {
    const { container } = render(<SessionAdminPage />, { wrapper: makeWrapper() });
    expect(container.firstChild).toBeTruthy();
  });

  it('renders page title', () => {
    render(<SessionAdminPage />, { wrapper: makeWrapper() });
    expect(screen.getByText('sessions.title')).toBeTruthy();
  });

  it('renders sessions table', () => {
    const { container } = render(<SessionAdminPage />, { wrapper: makeWrapper() });
    expect(container.querySelector('table')).toBeTruthy();
  });

  it('renders user filter input', () => {
    const { container } = render(<SessionAdminPage />, { wrapper: makeWrapper() });
    expect(container.querySelector('input')).toBeTruthy();
  });
});

describe('BackupAdminPage — TC-FE-BACK-001', () => {
  it('renders without crashing', () => {
    const { container } = render(<BackupAdminPage />, { wrapper: makeWrapper() });
    expect(container.firstChild).toBeTruthy();
  });

  it('renders page title', () => {
    render(<BackupAdminPage />, { wrapper: makeWrapper() });
    expect(screen.getByText('backup.title')).toBeTruthy();
  });

  it('renders trigger backup button', () => {
    render(<BackupAdminPage />, { wrapper: makeWrapper() });
    expect(screen.getByText('backup.triggerBackup')).toBeTruthy();
  });

  it('renders trigger drill button', () => {
    render(<BackupAdminPage />, { wrapper: makeWrapper() });
    expect(screen.getByText('backup.triggerDrill')).toBeTruthy();
  });

  it('renders backup history table', () => {
    const { container } = render(<BackupAdminPage />, { wrapper: makeWrapper() });
    expect(container.querySelector('table')).toBeTruthy();
  });
});

describe('AbacRulesPage — TC-FE-ABAC-001', () => {
  it('renders without crashing', () => {
    const { container } = render(<AbacRulesPage />, { wrapper: makeWrapper() });
    expect(container.firstChild).toBeTruthy();
  });

  it('renders page title', () => {
    render(<AbacRulesPage />, { wrapper: makeWrapper() });
    expect(screen.getByText('abacRules.title')).toBeTruthy();
  });

  it('renders add rule button', () => {
    render(<AbacRulesPage />, { wrapper: makeWrapper() });
    expect(screen.getByText('common.add')).toBeTruthy();
  });

  it('renders rules table', () => {
    const { container } = render(<AbacRulesPage />, { wrapper: makeWrapper() });
    expect(container.querySelector('table')).toBeTruthy();
  });
});

describe('AccessReviewPage — TC-FE-AR-001', () => {
  it('renders without crashing', () => {
    const { container } = render(<AccessReviewPage />, { wrapper: makeWrapper() });
    expect(container.firstChild).toBeTruthy();
  });

  it('renders page title', () => {
    render(<AccessReviewPage />, { wrapper: makeWrapper() });
    expect(screen.getByText('accessReviews.title')).toBeTruthy();
  });

  it('renders reviews table', () => {
    const { container } = render(<AccessReviewPage />, { wrapper: makeWrapper() });
    expect(container.querySelector('table')).toBeTruthy();
  });
});

describe('AiSearchPage — TC-FE-AI-001', () => {
  it('renders without crashing', () => {
    const { container } = render(<AiSearchPage />, { wrapper: makeWrapper() });
    expect(container.firstChild).toBeTruthy();
  });

  it('renders page title', () => {
    render(<AiSearchPage />, { wrapper: makeWrapper() });
    expect(screen.getByText('page.ai.title')).toBeTruthy();
  });

  it('renders NLQ search input', () => {
    const { container } = render(<AiSearchPage />, { wrapper: makeWrapper() });
    expect(container.querySelector('input')).toBeTruthy();
  });

  it('renders send button', () => {
    render(<AiSearchPage />, { wrapper: makeWrapper() });
    expect(screen.getByText('page.ai.btnSend')).toBeTruthy();
  });
});

describe('ProfilePage — TC-FE-PROF-001', () => {
  it('renders without crashing', () => {
    const { container } = render(<ProfilePage />, { wrapper: makeWrapper() });
    expect(container.firstChild).toBeTruthy();
  });

  it('renders profile title', () => {
    render(<ProfilePage />, { wrapper: makeWrapper() });
    expect(screen.getByText('nav.profile')).toBeTruthy();
  });

  it('renders user name from auth profile', () => {
    render(<ProfilePage />, { wrapper: makeWrapper() });
    expect(screen.getByText('Test Admin')).toBeTruthy();
  });

  it('renders user email from auth profile', () => {
    render(<ProfilePage />, { wrapper: makeWrapper() });
    expect(screen.getByText('admin@example.com')).toBeTruthy();
  });

  it('renders change password button', () => {
    render(<ProfilePage />, { wrapper: makeWrapper() });
    expect(screen.getByText('profile.changePassword')).toBeTruthy();
  });
});

describe('RolesPage — TC-FE-ROLE-001', () => {
  it('renders without crashing', () => {
    const { container } = render(<RolesPage />, { wrapper: makeWrapper() });
    expect(container.firstChild).toBeTruthy();
  });

  it('renders roles page title', () => {
    render(<RolesPage />, { wrapper: makeWrapper() });
    expect(screen.getByText('roles.title')).toBeTruthy();
  });

  it('renders SystemAdmin role', () => {
    render(<RolesPage />, { wrapper: makeWrapper() });
    expect(screen.getByText('SystemAdmin')).toBeTruthy();
  });

  it('renders GovOfficer role', () => {
    render(<RolesPage />, { wrapper: makeWrapper() });
    // RolesPage renders GovOfficer as a role tag AND as a policy tag inside other rows
    expect(screen.getAllByText('GovOfficer').length).toBeGreaterThan(0);
  });

  it('renders roles table', () => {
    const { container } = render(<RolesPage />, { wrapper: makeWrapper() });
    expect(container.querySelector('table')).toBeTruthy();
  });
});
