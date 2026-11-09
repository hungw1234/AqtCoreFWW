/**
 * file-list-signed-badge-and-history.test.tsx
 *
 * Covers the Phase 05 additions to FileListPage:
 *   - "Đã ký" badge renders when isDigitallySigned=true
 *   - Badge absent when isDigitallySigned=false/undefined
 *   - "Lịch sử ký" icon button present for signed rows
 *   - "Lịch sử ký" button absent for unsigned rows
 *   - Clicking "Lịch sử ký" opens the SigningStepsPanel drawer with scope="files"
 */

import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { createElement, type ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

// ─── i18n stub ────────────────────────────────────────────────────────────────

vi.mock('react-i18next', () => ({
  useTranslation: () => ({
    t: (key: string, fallback?: string | Record<string, unknown>) =>
      typeof fallback === 'string' ? fallback : key,
    i18n: { language: 'vi' },
  }),
}));

// ─── file-api mock ─────────────────────────────────────────────────────────────

const mockUseFiles = vi.fn(() => ({ data: { items: [] as FileRecordDto[], totalCount: 0 }, isLoading: false }));

vi.mock('@/features/files/file-api', () => ({
  useFiles: () => mockUseFiles(),
  useUploadFile: () => ({ mutate: vi.fn(), isPending: false }),
  useDeleteFile: () => ({ mutate: vi.fn(), isPending: false }),
  useFileScanStatus: () => ({ data: undefined }),
  downloadFile: vi.fn(),
  fileQueryKeys: { list: (p: number) => ['files', 'list', p] },
}));

// ─── signing-provider mock ────────────────────────────────────────────────────

vi.mock('@/features/digital-signature/signature-api', () => ({
  useSigningProviderInfo: () => ({
    data: { isEnabled: true, providerKey: 'usb-vgca', capabilities: 'DocFlow,RequiresLocalAgent' },
    isLoading: false,
  }),
  useSigningRequestStatus: () => ({
    data: undefined, isSuccess: false, timedOut: false, retryPoll: vi.fn(),
  }),
  createSigningRequest: vi.fn(),
}));

// FileListPage gates its sign buttons on the user's enrollments; mock an eligible overview so the
// badge/history rows render as before (these tests don't exercise the sign buttons themselves).
vi.mock('@/features/profile/signatures/signatures-api', () => ({
  useSignatureOverview: () => ({
    data: {
      hasApprovedEnrollment: true,
      enrollments: [
        { kind: 'UsbTokenVgca', approvalStatus: 'Approved', isActive: true },
        { kind: 'RemoteViettelMysign', approvalStatus: 'Approved', isActive: true },
      ],
    },
  }),
}));

// ─── adapter registry mock ────────────────────────────────────────────────────

vi.mock('@/features/digital-signature/registry', () => ({
  resolveAdapter: vi.fn(),
  hasAdapter: vi.fn(() => false),
}));

// ─── vgca adapter side-effect imports (no-op in jsdom) ───────────────────────

vi.mock('@/features/digital-signature/providers/vgca-usb-adapter', () => ({}));
vi.mock('@/features/digital-signature/providers/remote-signing-adapter', () => ({}));

// ─── use-sign-uploaded-file mock ──────────────────────────────────────────────

vi.mock('@/features/files/use-sign-uploaded-file', () => ({
  useSignUploadedFile: () => ({
    isLoading: false, error: null, requestId: undefined,
    startSigning: vi.fn(), clearError: vi.fn(),
  }),
}));

// ─── SigningStepsPanel mock — track which scope+id was passed ─────────────────

const mockSigningStepsPanelProps: Array<{ scope: string; id: string }> = [];

vi.mock(
  '@/features/digital-signature/signed-document/signing-steps-panel',
  () => ({
    SigningStepsPanel: ({ scope, id }: { scope: string; id: string }) => {
      mockSigningStepsPanelProps.push({ scope, id });
      return createElement('div', { 'data-testid': 'signing-steps-panel' }, `${scope}:${id}`);
    },
  }),
);

import { FileListPage } from '@/features/files/file-list-page';
import type { FileRecordDto } from '@/features/files/file-types';

// ─── Helpers ──────────────────────────────────────────────────────────────────

function makeWrapper() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return ({ children }: { children: ReactNode }) =>
    createElement(QueryClientProvider, { client: qc }, children);
}

function makeRow(overrides?: Partial<FileRecordDto>): FileRecordDto {
  return {
    id: 'file-signed-1',
    originalFileName: 'signed.pdf',
    contentType: 'application/pdf',
    sizeBytes: 10240,
    uploadedBy: 'user-1',
    uploadedByName: 'Test User',
    uploadedAt: '2026-06-18T08:00:00Z',
    virusScanStatus: 'Clean',
    isDigitallySigned: false,
    ...overrides,
  };
}

// ─── Badge tests ──────────────────────────────────────────────────────────────

describe('FileListPage — isDigitallySigned badge', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockSigningStepsPanelProps.length = 0;
  });

  it('renders "Đã ký" badge tag for rows with isDigitallySigned=true', () => {
    mockUseFiles.mockReturnValue({
      data: { items: [makeRow({ isDigitallySigned: true })], totalCount: 1 },
      isLoading: false,
    });

    render(<FileListPage />, { wrapper: makeWrapper() });

    expect(screen.getByText('Đã ký')).toBeTruthy();
  });

  it('does NOT render the badge when isDigitallySigned=false', () => {
    mockUseFiles.mockReturnValue({
      data: { items: [makeRow({ isDigitallySigned: false })], totalCount: 1 },
      isLoading: false,
    });

    render(<FileListPage />, { wrapper: makeWrapper() });

    expect(screen.queryByText('Đã ký')).toBeNull();
  });

  it('does NOT render the badge when isDigitallySigned is undefined', () => {
    mockUseFiles.mockReturnValue({
      data: {
        items: [makeRow({ isDigitallySigned: undefined as unknown as boolean })],
        totalCount: 1,
      },
      isLoading: false,
    });

    render(<FileListPage />, { wrapper: makeWrapper() });

    expect(screen.queryByText('Đã ký')).toBeNull();
  });
});

// ─── "Lịch sử ký" action button tests ─────────────────────────────────────────

describe('FileListPage — Lịch sử ký action', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockSigningStepsPanelProps.length = 0;
  });

  it('renders the HistoryOutlined icon for signed rows', () => {
    mockUseFiles.mockReturnValue({
      data: { items: [makeRow({ isDigitallySigned: true })], totalCount: 1 },
      isLoading: false,
    });

    const { container } = render(<FileListPage />, { wrapper: makeWrapper() });

    // antd renders HistoryOutlined as <span class="anticon anticon-history">
    expect(container.querySelector('.anticon-history')).toBeTruthy();
  });

  it('does NOT render the history icon for unsigned rows', () => {
    mockUseFiles.mockReturnValue({
      data: { items: [makeRow({ isDigitallySigned: false })], totalCount: 1 },
      isLoading: false,
    });

    const { container } = render(<FileListPage />, { wrapper: makeWrapper() });

    expect(container.querySelector('.anticon-history')).toBeNull();
  });

  it('opens the drawer with SigningStepsPanel scope="files" and correct id on click', async () => {
    const signedRow = makeRow({ id: 'file-signed-abc', isDigitallySigned: true });
    mockUseFiles.mockReturnValue({
      data: { items: [signedRow], totalCount: 1 },
      isLoading: false,
    });

    const { container } = render(<FileListPage />, { wrapper: makeWrapper() });

    const historyBtn = container.querySelector('.anticon-history')?.closest('button');
    expect(historyBtn).toBeTruthy();
    fireEvent.click(historyBtn!);

    // The drawer renders SigningStepsPanel when open — panel is our mock
    await waitFor(() =>
      expect(screen.getByTestId('signing-steps-panel')).toBeTruthy(),
    );

    // Assert the panel received scope="files" and the correct file id
    expect(mockSigningStepsPanelProps.some(
      (p) => p.scope === 'files' && p.id === 'file-signed-abc',
    )).toBe(true);
  });
});
