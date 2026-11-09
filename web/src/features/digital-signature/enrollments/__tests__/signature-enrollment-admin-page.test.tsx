// SignatureEnrollmentAdminPage — Phase 6 Slice C + Phase 4 additions.
// Covers: pending rows + scope-note render; approve via Popconfirm; reject opens reason modal and
// submits the trimmed reason. Tenant-scope banner is asserted (H4 — JWT-tenant, not override).
// Phase 4: SubjectType tag (Personal/Organization) + displayName are rendered for each pending row.

import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';

vi.mock('react-i18next', () => ({
  useTranslation: () => ({ t: (key: string) => key }),
}));

// useServerPagination reads/writes the router URL search params.
vi.mock('@tanstack/react-router', () => ({
  useSearch: () => ({}),
  useNavigate: () => vi.fn(),
}));

vi.mock('@/core/api/notification-bridge', () => ({
  appMessage: { success: vi.fn(), error: vi.fn() },
}));

const approveMutate = vi.fn().mockResolvedValue(undefined);
const rejectMutate = vi.fn().mockResolvedValue(undefined);
const mockPending = vi.fn();

vi.mock('../signature-enrollment-admin-api', () => ({
  usePendingEnrollments: () => mockPending(),
  useApproveEnrollment: () => ({ mutateAsync: approveMutate, isPending: false }),
  useRejectEnrollment: () => ({ mutateAsync: rejectMutate, isPending: false }),
}));

import { SignatureEnrollmentAdminPage } from '../signature-enrollment-admin-page';

const PENDING_PAGE = {
  items: [
    {
      publicId: 'enr-1',
      userId: 'user-1',
      kind: 'RemoteViettelMysign' as const,
      serialNumber: 'SER-001',
      issuer: 'CN=Test CA',
      enrolledAt: '2026-06-30T03:00:00Z',
      subjectType: 'Organization' as const,
      displayName: 'Công ty TNHH ABC',
    },
  ],
  totalCount: 1,
  meta: { page: 1, pageSize: 20, totalPages: 1, nextCursor: null, prevCursor: null, hasNextPage: false },
};

describe('SignatureEnrollmentAdminPage', () => {
  beforeEach(() => {
    approveMutate.mockClear();
    rejectMutate.mockClear();
    mockPending.mockReturnValue({ data: PENDING_PAGE, isFetching: false });
  });

  it('renders the pending queue with the JWT-tenant scope note', () => {
    render(<SignatureEnrollmentAdminPage />);
    expect(screen.getByText('signatureEnrollments.scopeNote')).toBeInTheDocument();
    expect(screen.getByText('SER-001')).toBeInTheDocument();
    expect(screen.getByText('signatureEnrollments.kind.remote')).toBeInTheDocument();
  });

  it('renders SubjectType tag and displayName for each pending enrollment', () => {
    render(<SignatureEnrollmentAdminPage />);
    // Organization tag via i18n key.
    expect(screen.getByText('signatureEnrollments.subjectType.organization')).toBeInTheDocument();
    // Display name so the admin knows the org to approve.
    expect(screen.getByText('Công ty TNHH ABC')).toBeInTheDocument();
  });

  it('renders Personal tag when subjectType is Personal', () => {
    mockPending.mockReturnValue({
      data: {
        ...PENDING_PAGE,
        items: [{ ...PENDING_PAGE.items[0], subjectType: 'Personal' as const, displayName: 'Nguyễn Văn A' }],
      },
      isFetching: false,
    });
    render(<SignatureEnrollmentAdminPage />);
    expect(screen.getByText('signatureEnrollments.subjectType.personal')).toBeInTheDocument();
    expect(screen.getByText('Nguyễn Văn A')).toBeInTheDocument();
  });

  it('approves an enrollment via the confirm popover', async () => {
    render(<SignatureEnrollmentAdminPage />);
    fireEvent.click(screen.getByText('signatureEnrollments.approve'));
    // Popconfirm OK button uses common.confirm.
    const confirmBtn = await screen.findByText('common.confirm');
    fireEvent.click(confirmBtn);
    await waitFor(() => expect(approveMutate).toHaveBeenCalledWith('enr-1'));
  });

  it('rejects an enrollment with a trimmed reason via the modal', async () => {
    render(<SignatureEnrollmentAdminPage />);
    fireEvent.click(screen.getByText('signatureEnrollments.reject'));

    const reason = await screen.findByPlaceholderText('signatureEnrollments.rejectReasonPlaceholder');
    fireEvent.change(reason, { target: { value: '  cert expired  ' } });
    fireEvent.click(screen.getByText('signatureEnrollments.rejectConfirm'));

    await waitFor(() =>
      expect(rejectMutate).toHaveBeenCalledWith({ publicId: 'enr-1', reason: 'cert expired' }),
    );
  });

  it('shows the empty state when the queue is empty', () => {
    mockPending.mockReturnValue({ data: { ...PENDING_PAGE, items: [], totalCount: 0 }, isFetching: false });
    render(<SignatureEnrollmentAdminPage />);
    expect(screen.getByText('signatureEnrollments.empty')).toBeInTheDocument();
  });
});
