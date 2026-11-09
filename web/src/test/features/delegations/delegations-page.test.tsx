// TC-FE-DEL-001: Renders delegation list
// TC-FE-DEL-002: Create delegation form validation

import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { createElement, type ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

vi.mock('react-i18next', () => ({
  useTranslation: () => ({ t: (key: string) => key }),
}));

// Controllable delegation API — BE returns PagedResult<T>, see delegation-api.ts
const emptyPaged = {
  items: [] as DelegationDto[],
  totalCount: 0,
  meta: { page: 1, pageSize: 20, totalPages: 0, hasNextPage: false },
};
const mockUseDelegations = vi.fn(() => ({ data: emptyPaged, isLoading: false }));

// Controllable auth mocks — allow tests to override behavior
let mockHasRoleFn = vi.fn<(role: string) => boolean>(() => false);
let mockUserId = 'mock-user-id';

vi.mock('@/features/delegations/delegation-api', () => ({
  useDelegations: () => mockUseDelegations(),
  useCreateDelegation: () => ({ mutate: vi.fn(), mutateAsync: vi.fn(), isPending: false }),
  useRevokeDelegation: () => ({ mutate: vi.fn(), isPending: false }),
  useApproveDelegation: () => ({ mutate: vi.fn(), mutateAsync: vi.fn(), isPending: false }),
  // Re-implement helper inside the mock — page imports it directly from the module.
  deriveIsActive: (d: { isRevoked: boolean; validFrom: string; validTo: string }, now: Date = new Date()) => {
    if (d.isRevoked) return false;
    const ts = now.getTime();
    return new Date(d.validFrom).getTime() <= ts && ts <= new Date(d.validTo).getTime();
  },
}));

vi.mock('@/features/auth/use-permissions', () => ({
  usePermissions: () => ({ hasRole: mockHasRoleFn }),
}));

vi.mock('@/features/auth/me-permissions-api', () => ({
  useMyEffectivePermissions: () => ({ data: { userId: mockUserId } }),
}));

import { DelegationListPage } from '@/features/delegations/delegation-list-page';
import type { DelegationDto } from '@/features/delegations/delegation-api';

function makeWrapper() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return ({ children }: { children: ReactNode }) =>
    createElement(QueryClientProvider, { client: qc }, children);
}

// TC-FE-DEL-001: Renders delegation list
describe('DelegationListPage — TC-FE-DEL-001: renders delegation list', () => {
  it('renders without crashing', () => {
    const { container } = render(<DelegationListPage />, { wrapper: makeWrapper() });
    expect(container.firstChild).toBeTruthy();
  });

  it('renders page title', () => {
    render(<DelegationListPage />, { wrapper: makeWrapper() });
    expect(screen.getByText('delegations.title')).toBeTruthy();
  });

  it('renders delegation table', () => {
    const { container } = render(<DelegationListPage />, { wrapper: makeWrapper() });
    expect(container.querySelector('table')).toBeTruthy();
  });

  it('renders active-only toggle switch', () => {
    render(<DelegationListPage />, { wrapper: makeWrapper() });
    // Switch has checkedChildren text
    expect(screen.getByText('delegations.activeOnly')).toBeTruthy();
  });

  it('renders create delegation button', () => {
    render(<DelegationListPage />, { wrapper: makeWrapper() });
    expect(screen.getByText('common.add')).toBeTruthy();
  });

  it('renders table rows when delegations are provided', () => {
    // Wide validity window so deriveIsActive() always returns true regardless of test clock
    const farPast = new Date(Date.now() - 365 * 24 * 60 * 60 * 1000).toISOString();
    const farFuture = new Date(Date.now() + 365 * 24 * 60 * 60 * 1000).toISOString();
    mockUseDelegations.mockReturnValue({
      data: {
        items: [
          {
            id: 'del-1',
            delegatorId: 'user-1',
            delegatorName: 'Nguyễn Văn A',
            delegateId: 'user-2',
            delegateName: 'Trần Thị B',
            validFrom: farPast,
            validTo: farFuture,
            reason: 'Công tác',
            isRevoked: false,
            revokedAt: null,
            status: 'Active',
          },
        ],
        totalCount: 1,
        meta: { page: 1, pageSize: 20, totalPages: 1, hasNextPage: false },
      },
      isLoading: false,
    });
    render(<DelegationListPage />, { wrapper: makeWrapper() });
    expect(screen.getByText('Nguyễn Văn A')).toBeTruthy();
    expect(screen.getByText('Trần Thị B')).toBeTruthy();
  });
});

// TC-FE-DEL-002: Create delegation form validation
describe('DelegationListPage — TC-FE-DEL-002: create delegation form', () => {
  it('renders create modal title when modal is opened via create button click', () => {
    mockUseDelegations.mockReturnValue({ data: emptyPaged, isLoading: false });
    render(<DelegationListPage />, { wrapper: makeWrapper() });
    // The create button opens the modal
    fireEvent.click(screen.getByText('common.add'));
    // Modal title should be visible after click
    expect(screen.getByText('delegations.createTitle')).toBeTruthy();
  });

  it('renders delegator and delegate form fields in modal', () => {
    mockUseDelegations.mockReturnValue({ data: emptyPaged, isLoading: false });
    render(<DelegationListPage />, { wrapper: makeWrapper() });
    fireEvent.click(screen.getByText('common.add'));
    // Form labels rendered via t() keys — use getAllByText since label may appear in table header too
    expect(screen.getAllByText('delegations.col.delegator').length).toBeGreaterThan(0);
    expect(screen.getAllByText('delegations.col.delegate').length).toBeGreaterThan(0);
  });
});

// TC-FE-DEL-003: Approve action on PendingApproval delegations
describe('DelegationListPage — TC-FE-DEL-003: approve action', () => {
  it('renders status badge from record.status field', () => {
    const farPast = new Date(Date.now() - 365 * 24 * 60 * 60 * 1000).toISOString();
    const farFuture = new Date(Date.now() + 365 * 24 * 60 * 60 * 1000).toISOString();
    mockUseDelegations.mockReturnValue({
      data: {
        items: [
          {
            id: 'del-pending',
            delegatorId: 'user-1',
            delegatorName: 'Nguyễn Văn A',
            delegateId: 'user-2',
            delegateName: 'Trần Thị B',
            validFrom: farPast,
            validTo: farFuture,
            reason: null,
            isRevoked: false,
            revokedAt: null,
            status: 'PendingApproval',
          },
        ],
        totalCount: 1,
        meta: { page: 1, pageSize: 20, totalPages: 1, hasNextPage: false },
      },
      isLoading: false,
    });
    render(<DelegationListPage />, { wrapper: makeWrapper() });
    // Status badge should render with the i18n key for PendingApproval
    expect(screen.getByText('delegations.statusPending')).toBeTruthy();
  });

  it('shows approve button when PendingApproval AND current user is delegator', () => {
    const farPast = new Date(Date.now() - 365 * 24 * 60 * 60 * 1000).toISOString();
    const farFuture = new Date(Date.now() + 365 * 24 * 60 * 60 * 1000).toISOString();
    const delegatorId = 'user-1';
    mockUserId = delegatorId;
    mockHasRoleFn = vi.fn(() => false);
    mockUseDelegations.mockReturnValue({
      data: {
        items: [
          {
            id: 'del-pending',
            delegatorId,
            delegatorName: 'Nguyễn Văn A',
            delegateId: 'user-2',
            delegateName: 'Trần Thị B',
            validFrom: farPast,
            validTo: farFuture,
            reason: null,
            isRevoked: false,
            revokedAt: null,
            status: 'PendingApproval',
          },
        ],
        totalCount: 1,
        meta: { page: 1, pageSize: 20, totalPages: 1, hasNextPage: false },
      },
      isLoading: false,
    });
    render(<DelegationListPage />, { wrapper: makeWrapper() });
    expect(screen.getByText('delegations.approve')).toBeTruthy();
  });

  it('shows approve button when PendingApproval AND current user is SystemAdmin', () => {
    const farPast = new Date(Date.now() - 365 * 24 * 60 * 60 * 1000).toISOString();
    const farFuture = new Date(Date.now() + 365 * 24 * 60 * 60 * 1000).toISOString();
    mockUserId = 'user-admin';
    mockHasRoleFn = vi.fn((role: string) => role === 'SystemAdmin');
    mockUseDelegations.mockReturnValue({
      data: {
        items: [
          {
            id: 'del-pending',
            delegatorId: 'user-1',
            delegatorName: 'Nguyễn Văn A',
            delegateId: 'user-2',
            delegateName: 'Trần Thị B',
            validFrom: farPast,
            validTo: farFuture,
            reason: null,
            isRevoked: false,
            revokedAt: null,
            status: 'PendingApproval',
          },
        ],
        totalCount: 1,
        meta: { page: 1, pageSize: 20, totalPages: 1, hasNextPage: false },
      },
      isLoading: false,
    });
    render(<DelegationListPage />, { wrapper: makeWrapper() });
    expect(screen.getByText('delegations.approve')).toBeTruthy();
  });

  it('hides approve button when neither delegator nor SystemAdmin', () => {
    const farPast = new Date(Date.now() - 365 * 24 * 60 * 60 * 1000).toISOString();
    const farFuture = new Date(Date.now() + 365 * 24 * 60 * 60 * 1000).toISOString();
    mockUserId = 'user-3';
    mockHasRoleFn = vi.fn(() => false);
    mockUseDelegations.mockReturnValue({
      data: {
        items: [
          {
            id: 'del-pending',
            delegatorId: 'user-1',
            delegatorName: 'Nguyễn Văn A',
            delegateId: 'user-2',
            delegateName: 'Trần Thị B',
            validFrom: farPast,
            validTo: farFuture,
            reason: null,
            isRevoked: false,
            revokedAt: null,
            status: 'PendingApproval',
          },
        ],
        totalCount: 1,
        meta: { page: 1, pageSize: 20, totalPages: 1, hasNextPage: false },
      },
      isLoading: false,
    });
    render(<DelegationListPage />, { wrapper: makeWrapper() });
    // Query for approve button — should NOT be found
    const approveButtons = screen.queryAllByText('delegations.approve');
    expect(approveButtons.length).toBe(0);
  });

  it('hides approve button for Active delegations', () => {
    const farPast = new Date(Date.now() - 365 * 24 * 60 * 60 * 1000).toISOString();
    const farFuture = new Date(Date.now() + 365 * 24 * 60 * 60 * 1000).toISOString();
    mockUseDelegations.mockReturnValue({
      data: {
        items: [
          {
            id: 'del-active',
            delegatorId: 'user-1',
            delegatorName: 'Nguyễn Văn A',
            delegateId: 'user-2',
            delegateName: 'Trần Thị B',
            validFrom: farPast,
            validTo: farFuture,
            reason: null,
            isRevoked: false,
            revokedAt: null,
            status: 'Active',
          },
        ],
        totalCount: 1,
        meta: { page: 1, pageSize: 20, totalPages: 1, hasNextPage: false },
      },
      isLoading: false,
    });
    render(<DelegationListPage />, { wrapper: makeWrapper() });
    // Query for approve button — should NOT be found
    const approveButtons = screen.queryAllByText('delegations.approve');
    expect(approveButtons.length).toBe(0);
  });
});
