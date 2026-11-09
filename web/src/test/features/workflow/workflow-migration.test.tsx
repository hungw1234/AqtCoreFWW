// TC-MIG-001: Wizard shows unmapped states on failed validation
// TC-MIG-002: Wizard locked (hash badge) after creation
// TC-MIG-003: Approve button hidden when currentUser === createdBy
// TC-MIG-004: Cancel button visible only in allowed statuses
// TC-MIG-005: Items table filter chip switches between All/Failed/Skipped

import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { createElement, type ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { AuthContext } from '@/features/auth/auth-provider';
import type { AuthContextValue } from '@/features/auth/auth-provider';

// ─── Global mocks ──────────────────────────────────────────────────────────────

vi.mock('react-i18next', () => ({
  useTranslation: () => ({ t: (key: string) => key }),
}));

vi.mock('@tanstack/react-router', () => ({
  useNavigate: () => vi.fn(),
  useParams: () => ({}),
  Link: ({ children }: { children: ReactNode }) => children,
}));

// usePermissions sources permission codes from GET /api/v1/me/permissions via
// useMyEffectivePermissions — NOT from the JWT profile claim. Mock that API hook
// so the REAL usePermissions().hasPermission logic runs against per-test codes.
const mockEffectivePermissions = vi.fn();
vi.mock('@/features/auth/me-permissions-api', () => ({
  useMyEffectivePermissions: () => mockEffectivePermissions(),
}));

// ─── API mocks ─────────────────────────────────────────────────────────────────

const mockUseWorkflowDefinition = vi.fn();
const mockUseValidateMapping = vi.fn();
const mockUseCreateMigration = vi.fn();

vi.mock('@/features/workflow/workflow-api', () => ({
  useWorkflowDefinition: (id: string) => mockUseWorkflowDefinition(id),
}));

vi.mock('@/features/workflow/workflow-migration-api', () => ({
  useValidateMapping: () => mockUseValidateMapping(),
  useCreateMigration: () => mockUseCreateMigration(),
  useMigration: (id: string) => mockUseMigration(id),
  useMigrationItems: (id: string, page: number, status?: string) =>
    mockUseMigrationItems(id, page, status),
  useApproveMigration: () => mockUseApproveMigration(),
  useExecuteMigration: () => mockUseExecuteMigration(),
  useCancelMigration: () => mockUseCancelMigration(),
}));

const mockUseMigration = vi.fn();
const mockUseMigrationItems = vi.fn();
const mockUseApproveMigration = vi.fn();
const mockUseExecuteMigration = vi.fn();
const mockUseCancelMigration = vi.fn();

// ─── Test helpers ──────────────────────────────────────────────────────────────

function makeQc() {
  return new QueryClient({ defaultOptions: { queries: { retry: false } } });
}

function makeAuthCtx(userId = 'user-a'): AuthContextValue {
  return {
    user: { profile: { sub: userId }, access_token: 'tok', expired: false } as never,
    isAuthenticated: true,
    isLoading: false,
    login: async () => {},
    logout: async () => {},
  };
}

function makeWrapper(permissions: string[] = [], userId = 'user-a') {
  const qc = makeQc();
  const auth = makeAuthCtx(userId);
  // Drive the effective-permissions API hook that usePermissions() reads from.
  // currentUserId still comes from profile.sub (auth context) for the self-approval gate.
  mockEffectivePermissions.mockReturnValue({
    data: { userId, roles: [], permissionCodes: permissions },
    isLoading: false,
    isFetching: false,
  });
  return ({ children }: { children: ReactNode }) =>
    createElement(
      QueryClientProvider,
      { client: qc },
      createElement(AuthContext.Provider, { value: auth }, children),
    );
}

const srcDef = {
  id: 'src-def', version: 1, definitionKey: 'wf', isActive: true, isLatest: false,
  states: [{ id: 'st-a', name: 'StateA', isInitial: true, isFinal: false, color: '#fff', sortOrder: 0, displayNameVi: '', displayNameEn: '' }],
  transitions: [],
};
const tgtDef = {
  id: 'tgt-def', version: 2, definitionKey: 'wf', isActive: true, isLatest: true,
  states: [{ id: 'st-b', name: 'StateB', isInitial: true, isFinal: false, color: '#fff', sortOrder: 0, displayNameVi: '', displayNameEn: '' }],
  transitions: [],
};

// ─── TC-MIG-001 + TC-MIG-002: Wizard ─────────────────────────────────────────

import { WorkflowStateMappingWizard } from '@/features/workflow/migration/workflow-state-mapping-wizard';

describe('WorkflowStateMappingWizard — TC-MIG-001: validate shows unmapped states', () => {
  beforeEach(() => {
    mockUseWorkflowDefinition.mockImplementation((id: string) => ({
      data: id === 'src-def' ? srcDef : tgtDef,
      isLoading: false,
    }));
    mockUseValidateMapping.mockReturnValue({
      mutateAsync: vi.fn().mockResolvedValue({
        isValid: false,
        errors: ['State mapping incomplete'],
        unmappedStates: [{ stateId: 'st-a', stateName: 'StateA', activeInstanceCount: 5 }],
      }),
      isPending: false,
    });
    mockUseCreateMigration.mockReturnValue({
      mutateAsync: vi.fn(),
      isPending: false,
    });
  });

  it('renders validate button', () => {
    render(
      <WorkflowStateMappingWizard
        sourceDefinitionId="src-def"
        targetDefinitionId="tgt-def"
        definitionKey="wf"
        onCreated={vi.fn()}
      />,
      { wrapper: makeWrapper() },
    );
    expect(screen.getByText('workflow.migration.action.validate')).toBeTruthy();
  });

  it('shows error alert after failed validation', async () => {
    render(
      <WorkflowStateMappingWizard
        sourceDefinitionId="src-def"
        targetDefinitionId="tgt-def"
        definitionKey="wf"
        onCreated={vi.fn()}
      />,
      { wrapper: makeWrapper() },
    );
    fireEvent.click(screen.getByText('workflow.migration.action.validate'));
    await waitFor(() => {
      expect(screen.getByText('State mapping incomplete')).toBeTruthy();
    });
  });

  it('TC-MIG-002: Create button disabled until validation passes', async () => {
    render(
      <WorkflowStateMappingWizard
        sourceDefinitionId="src-def"
        targetDefinitionId="tgt-def"
        definitionKey="wf"
        onCreated={vi.fn()}
      />,
      { wrapper: makeWrapper() },
    );
    const createBtn = screen.getByText('workflow.migration.action.create').closest('button');
    expect(createBtn?.disabled).toBe(true);
  });
});

// ─── TC-MIG-003 + TC-MIG-004: StatusPanel ────────────────────────────────────

import { WorkflowMigrationStatusPanel } from '@/features/workflow/migration/workflow-migration-status-panel';

const baseMigration = {
  id: 'mig-1',
  sourceDefinitionId: 'src-def',
  targetDefinitionId: 'tgt-def',
  mappingHash: 'abcdef1234567890',
  totalInstances: 10,
  migratedCount: 5,
  failedCount: 2,
  skippedCount: 1,
  createdBy: 'user-creator',
  status: 'PendingApproval' as const,
  approvedBy: null,
  approvedAt: null,
  startedAt: null,
  completedAt: null,
  hangfireJobId: null,
  hangfireJobState: null,
};

const baseItemsPage = { items: [], totalCount: 0, page: 1, pageSize: 50 };

describe('WorkflowMigrationStatusPanel — TC-MIG-003: Approve hidden for creator', () => {
  beforeEach(() => {
    mockUseMigration.mockReturnValue({ data: baseMigration, isLoading: false });
    mockUseMigrationItems.mockReturnValue({ data: baseItemsPage, isLoading: false });
    mockUseApproveMigration.mockReturnValue({ mutateAsync: vi.fn(), isPending: false });
    mockUseExecuteMigration.mockReturnValue({ mutateAsync: vi.fn(), isPending: false });
    mockUseCancelMigration.mockReturnValue({ mutateAsync: vi.fn(), isPending: false });
  });

  it('hides Approve button when currentUser is the creator', () => {
    render(
      <WorkflowMigrationStatusPanel migrationId="mig-1" definitionKey="wf" />,
      // currentUser = 'user-creator' = same as createdBy
      { wrapper: makeWrapper(['WORKFLOW.MIGRATION.APPROVE'], 'user-creator') },
    );
    expect(screen.queryByText('workflow.migration.action.approve')).toBeNull();
  });

  it('shows Approve button when currentUser is different from creator', () => {
    render(
      <WorkflowMigrationStatusPanel migrationId="mig-1" definitionKey="wf" />,
      { wrapper: makeWrapper(['WORKFLOW.MIGRATION.APPROVE'], 'user-other') },
    );
    expect(screen.getByText('workflow.migration.action.approve')).toBeTruthy();
  });
});

describe('WorkflowMigrationStatusPanel — TC-MIG-004: Cancel button per status', () => {
  beforeEach(() => {
    mockUseMigrationItems.mockReturnValue({ data: baseItemsPage, isLoading: false });
    mockUseApproveMigration.mockReturnValue({ mutateAsync: vi.fn(), isPending: false });
    mockUseExecuteMigration.mockReturnValue({ mutateAsync: vi.fn(), isPending: false });
    mockUseCancelMigration.mockReturnValue({ mutateAsync: vi.fn(), isPending: false });
  });

  const cancelPerms = ['WORKFLOW.MIGRATION.CANCEL'];

  it.each(['PendingApproval', 'Approved', 'InProgress'] as const)(
    'shows Cancel button for status=%s',
    (status) => {
      mockUseMigration.mockReturnValue({
        data: { ...baseMigration, status },
        isLoading: false,
      });
      render(
        <WorkflowMigrationStatusPanel migrationId="mig-1" definitionKey="wf" />,
        { wrapper: makeWrapper(cancelPerms, 'user-other') },
      );
      expect(screen.getByText('workflow.migration.action.cancel')).toBeTruthy();
    },
  );

  it.each(['Completed', 'Failed', 'Cancelled'] as const)(
    'hides Cancel button for terminal status=%s',
    (status) => {
      mockUseMigration.mockReturnValue({
        data: { ...baseMigration, status },
        isLoading: false,
      });
      render(
        <WorkflowMigrationStatusPanel migrationId="mig-1" definitionKey="wf" />,
        { wrapper: makeWrapper(cancelPerms, 'user-other') },
      );
      expect(screen.queryByText('workflow.migration.action.cancel')).toBeNull();
    },
  );
});

// ─── TC-MIG-005: Items filter chip ────────────────────────────────────────────

describe('WorkflowMigrationStatusPanel — TC-MIG-005: filter chip changes query', () => {
  beforeEach(() => {
    mockUseMigration.mockReturnValue({ data: baseMigration, isLoading: false });
    mockUseMigrationItems.mockReturnValue({ data: baseItemsPage, isLoading: false });
    mockUseApproveMigration.mockReturnValue({ mutateAsync: vi.fn(), isPending: false });
    mockUseExecuteMigration.mockReturnValue({ mutateAsync: vi.fn(), isPending: false });
    mockUseCancelMigration.mockReturnValue({ mutateAsync: vi.fn(), isPending: false });
  });

  it('renders all three filter segments', () => {
    render(
      <WorkflowMigrationStatusPanel migrationId="mig-1" definitionKey="wf" />,
      { wrapper: makeWrapper([], 'user-other') },
    );
    expect(screen.getByText('common.total')).toBeTruthy();
    expect(screen.getByText('workflow.migration.itemStatus.failed')).toBeTruthy();
    expect(screen.getByText('workflow.migration.itemStatus.skipped')).toBeTruthy();
  });

  it('clicking Failed segment calls useMigrationItems with Failed filter', () => {
    render(
      <WorkflowMigrationStatusPanel migrationId="mig-1" definitionKey="wf" />,
      { wrapper: makeWrapper([], 'user-other') },
    );
    fireEvent.click(screen.getByText('workflow.migration.itemStatus.failed'));
    // useMigrationItems should have been called with 'Failed' at some point
    const calls = mockUseMigrationItems.mock.calls;
    const failedCall = calls.find((c) => c[2] === 'Failed');
    expect(failedCall).toBeTruthy();
  });
});
