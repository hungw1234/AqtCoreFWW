import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { createElement, type ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { PendingWorkflowLinkListItemDto } from '@/features/forms/pending-workflow-links/pending-workflow-links-types';

vi.mock('react-i18next', () => ({
  useTranslation: () => ({ t: (_key: string, opts?: { defaultValue?: string }) => opts?.defaultValue ?? _key }),
}));

// Pagination hook drives the router — stub to fixed page/pageSize so no router context is needed.
vi.mock('@/core/hooks/use-server-pagination', () => ({
  useServerPagination: () => ({
    page: 1,
    pageSize: 20,
    setPage: vi.fn(),
    setPageSize: vi.fn(),
    antPagination: { current: 1, pageSize: 20, showSizeChanger: false, showTotal: () => '', onChange: vi.fn() },
  }),
}));

// Permission gate — default granted; individual tests override.
const mockHasPermission = vi.fn((_p: string) => true);
vi.mock('@/features/auth', () => ({
  usePermissions: () => ({ hasPermission: mockHasPermission }),
}));

const mockUsePendingLinks = vi.fn();
const mockRetryMutate = vi.fn();
const mockShelveMutate = vi.fn();
vi.mock('@/features/forms/pending-workflow-links/pending-workflow-links-api', () => ({
  usePendingWorkflowLinks: (...args: unknown[]) => mockUsePendingLinks(...args),
  useRetryWorkflowLinkNow: () => ({ mutate: mockRetryMutate, isPending: false, variables: undefined }),
  useShelveWorkflowLink: () => ({ mutate: mockShelveMutate, isPending: false, variables: undefined }),
}));

vi.mock('@/core/api/notification-bridge', () => ({
  appMessage: { success: vi.fn(), error: vi.fn() },
}));

import { PendingWorkflowLinksPage } from '@/features/forms/pending-workflow-links/pending-workflow-links-page';

function makeWrapper() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return ({ children }: { children: ReactNode }) =>
    createElement(QueryClientProvider, { client: qc }, children);
}

const row = (over: Partial<PendingWorkflowLinkListItemDto> = {}): PendingWorkflowLinkListItemDto => ({
  id: '11111111-1111-1111-1111-111111111111',
  formTemplateId: '22222222-2222-2222-2222-222222222222',
  templateName: 'Đơn xin nghỉ phép',
  approvalWorkflowDefinitionId: '33333333-3333-3333-3333-333333333333',
  attemptCount: 2,
  lastFailureCode: 'WF_NO_HANDLER',
  lastError: 'no handler pool',
  nextRetryAt: '2026-08-04T10:00:00Z',
  status: 'Pending',
  resolvedAt: null,
  resolvedBy: null,
  createdAt: '2026-08-04T08:00:00Z',
  ...over,
});

describe('PendingWorkflowLinksPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockHasPermission.mockReturnValue(true);
    mockUsePendingLinks.mockReturnValue({ data: { items: [row()], totalCount: 1 }, isFetching: false });
  });

  // FE-1: renders the list from the mocked hook.
  it('renders pending-link rows with the template name and status tag', () => {
    render(createElement(PendingWorkflowLinksPage), { wrapper: makeWrapper() });
    expect(screen.getByText('Đơn xin nghỉ phép')).toBeTruthy();
    expect(screen.getByText('Pending')).toBeTruthy();
  });

  // FE-2: Retry-now calls the mutation with the row id.
  it('calls the retry mutation with the row id when Retry-now is clicked', () => {
    render(createElement(PendingWorkflowLinksPage), { wrapper: makeWrapper() });
    fireEvent.click(screen.getByText('Thử lại ngay').closest('button')!);
    expect(mockRetryMutate).toHaveBeenCalledWith(row().id, expect.anything());
  });

  // FE-3: all-4-status render — Shelved shows its tag and HIDES the Shelve button (symmetry axis 3).
  it('renders a Shelved row with the Shelved tag and disables its Shelve action', () => {
    mockUsePendingLinks.mockReturnValue({ data: { items: [row({ status: 'Shelved' })], totalCount: 1 }, isFetching: false });
    render(createElement(PendingWorkflowLinksPage), { wrapper: makeWrapper() });
    expect(screen.getByText('Shelved')).toBeTruthy();
    // Shelve disabled for a Shelved row; Retry-now stays enabled (un-shelve path).
    const shelveBtn = screen.getByText('Xếp kho').closest('button')!;
    expect(shelveBtn.disabled).toBe(true);
    const retryBtn = screen.getByText('Thử lại ngay').closest('button')!;
    expect(retryBtn.disabled).toBe(false);
  });

  // FE-4: changing the status filter re-queries the hook with the new status arg.
  it('re-queries with the selected status when the filter changes', () => {
    render(createElement(PendingWorkflowLinksPage), { wrapper: makeWrapper() });
    // Initial render passes status=undefined as the first hook arg.
    expect(mockUsePendingLinks).toHaveBeenCalledWith(undefined, 1, 20);
    mockUsePendingLinks.mockClear();

    // Open the antd Select and pick "Succeeded" → state change re-renders → hook re-called with it.
    fireEvent.mouseDown(screen.getByText('Lọc theo trạng thái'));
    fireEvent.click(screen.getByText('Succeeded'));
    expect(mockUsePendingLinks).toHaveBeenCalledWith('Succeeded', 1, 20);
  });

  // FE-1 (gate): without the permission, action buttons are disabled.
  it('disables actions when the user lacks FORMS.WFLINK.MANAGE', () => {
    mockHasPermission.mockReturnValue(false);
    render(createElement(PendingWorkflowLinksPage), { wrapper: makeWrapper() });
    expect(screen.getByText('Thử lại ngay').closest('button')!.disabled).toBe(true);
    expect(screen.getByText('Xếp kho').closest('button')!.disabled).toBe(true);
  });
});
