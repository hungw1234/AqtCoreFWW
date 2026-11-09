// P6 — ParallelBranchStatusPanel claim/resolve visibility.
//
// Facts:
//   FE-P6-1: pooled + unclaimed child (assigneeId null, taskId present) → Claim button.
//   FE-P6-2: clicking Claim calls useClaimTask with that taskId.
//   FE-P6-3: child assigned to current user → resolve buttons, NOT Claim.
//   FE-P6-4: child claimed by someone else → "awaiting", neither Claim nor resolve.

import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { createElement, type ReactNode } from 'react';

const h = vi.hoisted(() => ({
  branches: [] as unknown[],
  claimMutate: vi.fn(() => Promise.resolve({})),
}));

vi.mock('../workflow-api', () => ({
  useBranchStatuses: () => ({ data: h.branches, isLoading: false }),
  useResolveBranchChild: () => ({ mutateAsync: vi.fn(() => Promise.resolve({})), isPending: false }),
  useClaimTask: () => ({ mutateAsync: h.claimMutate, isPending: false }),
  BranchResolution: { Approved: 0, Rejected: 1, Escalated: 2, Timeout: 3 },
}));

vi.mock('@/core/api/notification-bridge', () => ({
  appMessage: { success: vi.fn(), error: vi.fn() },
}));

vi.mock('react-i18next', () => ({
  useTranslation: () => ({ t: (_key: string, def?: string) => def ?? _key }),
}));

import { ParallelBranchStatusPanel } from '../parallel-branch-status-panel';

const CURRENT_USER = 'user-1';

const wrapper = ({ children }: { children: ReactNode }) => {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return createElement(QueryClientProvider, { client }, children);
};

// Builds a single-branch payload with one child whose state the test controls.
function branchWithChild(child: Record<string, unknown>) {
  return [
    {
      id: 'bs-1',
      instanceId: 'inst-1',
      parallelBranchId: 'pb-1',
      status: 'InProgress',
      childStatuses: [{ id: 'c1', branchChildId: 'b1', ...child }],
    },
  ];
}

describe('ParallelBranchStatusPanel', () => {
  beforeEach(() => {
    h.branches = [];
    h.claimMutate.mockClear();
  });

  it('FE-P6-1: pooled unclaimed child with taskId renders a Claim button', () => {
    h.branches = branchWithChild({ resolutionType: undefined, assigneeId: undefined, taskId: 555 });

    render(<ParallelBranchStatusPanel instanceId="inst-1" currentUserId={CURRENT_USER} />, { wrapper });

    expect(screen.getByRole('button', { name: 'Nhận task' })).toBeInTheDocument();
    expect(screen.queryByText('Phê duyệt')).not.toBeInTheDocument();
  });

  it('FE-P6-2: clicking Claim invokes useClaimTask with the child task id', async () => {
    h.branches = branchWithChild({ resolutionType: undefined, assigneeId: undefined, taskId: 555 });

    render(<ParallelBranchStatusPanel instanceId="inst-1" currentUserId={CURRENT_USER} />, { wrapper });

    fireEvent.click(screen.getByRole('button', { name: 'Nhận task' }));

    await waitFor(() => expect(h.claimMutate).toHaveBeenCalledWith(555));
  });

  it('FE-P6-3: child assigned to current user shows resolve actions, not Claim', () => {
    h.branches = branchWithChild({ resolutionType: undefined, assigneeId: CURRENT_USER, taskId: 555 });

    render(<ParallelBranchStatusPanel instanceId="inst-1" currentUserId={CURRENT_USER} />, { wrapper });

    expect(screen.getByText('Phê duyệt')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Nhận task' })).not.toBeInTheDocument();
  });

  it('FE-P6-4: child claimed by another user shows awaiting, neither Claim nor resolve', () => {
    h.branches = branchWithChild({ resolutionType: undefined, assigneeId: 'user-2', taskId: 555 });

    render(<ParallelBranchStatusPanel instanceId="inst-1" currentUserId={CURRENT_USER} />, { wrapper });

    expect(screen.getByText('Đang chờ phê duyệt')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Nhận task' })).not.toBeInTheDocument();
    expect(screen.queryByText('Phê duyệt')).not.toBeInTheDocument();
  });

  it('FE-P6-5: pending child whose task is not yet staged (null taskId) shows awaiting, no Claim', () => {
    h.branches = branchWithChild({ resolutionType: undefined, assigneeId: undefined, taskId: null });

    render(<ParallelBranchStatusPanel instanceId="inst-1" currentUserId={CURRENT_USER} />, { wrapper });

    expect(screen.getByText('Đang chờ phê duyệt')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Nhận task' })).not.toBeInTheDocument();
  });
});
