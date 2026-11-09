/**
 * sign-uploaded-file-flow.test.tsx
 *
 * Tests for the uploaded-PDF signing integration on the Files page:
 *
 *   1. file-list-page: "Ký số" button renders only for application/pdf rows,
 *      absent for other content types.
 *   2. sign-uploaded-file-modal: agent 'not-installed' → install CTA shown.
 *   3. sign-uploaded-file-modal: happy path — stage → createSigningRequest →
 *      runVgcaPlugin → poll Completed → success result.
 */

import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { createElement, type ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

// ─── i18n stub ────────────────────────────────────────────────────────────────

vi.mock('react-i18next', () => ({
  useTranslation: () => ({
    t: (key: string, fallback?: string | Record<string, unknown>) => {
      if (typeof fallback === 'string') return fallback;
      return key;
    },
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

// ─── signing provider mock ────────────────────────────────────────────────────

const mockUseSigningProviderInfo = vi.fn();
const mockUseSigningRequestStatus = vi.fn(() => ({
  data: undefined as SigningRequestStatusDto | undefined,
  isSuccess: false,
  timedOut: false,
  retryPoll: vi.fn(),
}));

vi.mock('@/features/digital-signature/signature-api', () => ({
  useSigningProviderInfo: () => mockUseSigningProviderInfo(),
  useSigningRequestStatus: () => mockUseSigningRequestStatus(),
  createSigningRequest: vi.fn(),
}));

// FileListPage now gates its sign buttons on the user's own enrollments (not the tenant provider).
// Mock an eligible overview so the render-presence assertions below see the buttons.
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

const mockProbeAgentStatus = vi.fn();
const mockResolveAdapter = vi.fn((..._args: unknown[]) => ({ probeAgentStatus: mockProbeAgentStatus }));
const mockHasAdapter = vi.fn((..._args: unknown[]) => true);

vi.mock('@/features/digital-signature/registry', () => ({
  resolveAdapter: (...args: unknown[]) => mockResolveAdapter(...args),
  hasAdapter: (...args: unknown[]) => mockHasAdapter(...args),
}));

// ─── vgca adapter side-effect imports (no-op in jsdom) ───────────────────────

vi.mock('@/features/digital-signature/providers/vgca-usb-adapter', () => ({}));
vi.mock('@/features/digital-signature/providers/remote-signing-adapter', () => ({}));

// ─── use-sign-uploaded-file mock ──────────────────────────────────────────────

const mockStartSigning = vi.fn();
const mockClearError = vi.fn();
const mockUseSignUploadedFile = vi.fn(() => ({
  isLoading: false,
  error: null as string | null,
  requestId: undefined as string | undefined,
  startSigning: mockStartSigning,
  clearError: mockClearError,
}));

vi.mock('@/features/files/use-sign-uploaded-file', () => ({
  useSignUploadedFile: () => mockUseSignUploadedFile(),
}));

// ─── upload-sign feature-flag mock ─────────────────────────────────────────────
// FileListPage gates the uploaded-PDF sign buttons on feature:signing.upload-arbitrary-pdf. Default the
// mock to ENABLED so the existing render-presence assertions still see the buttons; a dedicated test
// flips it off to assert the entries are hidden.
const mockUseUploadSignEnabled = vi.fn(() => true);

vi.mock('@/features/files/file-signing-api', () => ({
  useUploadSignEnabled: () => mockUseUploadSignEnabled(),
}));

// ─── Imports after mocks ───────────────────────────────────────────────────────

import { FileListPage } from '@/features/files/file-list-page';
import { SignUploadedFileModal } from '@/features/files/sign-uploaded-file-modal';
import type { FileRecordDto } from '@/features/files/file-types';
import type { SigningRequestStatusDto } from '@/features/digital-signature/signature-types';

// ─── Helpers ──────────────────────────────────────────────────────────────────

function makeWrapper() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return ({ children }: { children: ReactNode }) =>
    createElement(QueryClientProvider, { client: qc }, children);
}

function makePdfRow(overrides?: Partial<FileRecordDto>): FileRecordDto {
  return {
    id: 'file-pdf-1',
    originalFileName: 'contract.pdf',
    contentType: 'application/pdf',
    sizeBytes: 204800,
    uploadedBy: 'user-1',
    uploadedByName: 'Test User',
    uploadedAt: '2026-06-17T08:00:00Z',
    virusScanStatus: 'Clean',
    isDigitallySigned: false,
    ...overrides,
  };
}

// ─── 1. file-list-page: PDF gate ─────────────────────────────────────────────
//
// antd Tooltip renders its title into a portal (not as an HTML title attr on the
// wrapped element), so we cannot use getByTitle/queryByTitle for sign buttons.
// Instead we query by the antd icon class anticon-safety-certificate which antd
// always applies to SafetyCertificateOutlined — this is stable across antd v5.

describe('FileListPage — Ký số button visibility', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    // clearAllMocks wipes call history but NOT a prior mockReturnValue override — restore the enabled
    // default so the flag-OFF test cannot leak `false` into the tests that run after it.
    mockUseUploadSignEnabled.mockReturnValue(true);
  });

  it('renders the SafetyCertificate icon button for application/pdf rows', () => {
    mockUseFiles.mockReturnValue({
      data: { items: [makePdfRow()], totalCount: 1 },
      isLoading: false,
    });

    const { container } = render(<FileListPage />, { wrapper: makeWrapper() });

    // antd renders SafetyCertificateOutlined as <span class="anticon anticon-safety-certificate">
    expect(container.querySelector('.anticon-safety-certificate')).toBeTruthy();
  });

  it('does NOT render the sign icon for non-PDF rows (image/png)', () => {
    mockUseFiles.mockReturnValue({
      data: {
        items: [
          makePdfRow({ id: 'img-1', originalFileName: 'photo.png', contentType: 'image/png' }),
        ],
        totalCount: 1,
      },
      isLoading: false,
    });

    const { container } = render(<FileListPage />, { wrapper: makeWrapper() });

    expect(container.querySelector('.anticon-safety-certificate')).toBeNull();
  });

  it('does NOT render the sign icon for a PDF whose scan has not passed (Pending)', () => {
    // Staging requires an Available file (BE 404s otherwise); the button is gated on a Clean
    // scan so a still-scanning PDF does not present a dead click.
    mockUseFiles.mockReturnValue({
      data: {
        items: [makePdfRow({ id: 'pdf-pending', virusScanStatus: 'Pending' })],
        totalCount: 1,
      },
      isLoading: false,
    });

    const { container } = render(<FileListPage />, { wrapper: makeWrapper() });

    expect(container.querySelector('.anticon-safety-certificate')).toBeNull();
  });

  it('hides both sign icons when the upload-arbitrary-pdf flag is OFF (even for a Clean PDF + eligible signer)', () => {
    // Deployment-wide gate: the untrusted-PDF crypto-verify chain is deferred, so the upload-sign entries
    // are hidden entirely — not shown-but-disabled — regardless of enrollment or scan status.
    mockUseUploadSignEnabled.mockReturnValue(false);
    mockUseFiles.mockReturnValue({
      data: { items: [makePdfRow()], totalCount: 1 },
      isLoading: false,
    });

    const { container } = render(<FileListPage />, { wrapper: makeWrapper() });

    // USB DocFlow (safety-certificate) and remote-PAdES (highlight) sign icons both absent.
    expect(container.querySelector('.anticon-safety-certificate')).toBeNull();
    expect(container.querySelector('.anticon-highlight')).toBeNull();
  });

  it('does NOT render the sign icon for docx rows', () => {
    mockUseFiles.mockReturnValue({
      data: {
        items: [
          makePdfRow({
            id: 'docx-1',
            originalFileName: 'report.docx',
            contentType: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
          }),
        ],
        totalCount: 1,
      },
      isLoading: false,
    });

    const { container } = render(<FileListPage />, { wrapper: makeWrapper() });

    expect(container.querySelector('.anticon-safety-certificate')).toBeNull();
  });

  it('renders exactly one sign icon for a mixed PDF+PNG list', () => {
    mockUseFiles.mockReturnValue({
      data: {
        items: [
          makePdfRow({ id: 'pdf-row', originalFileName: 'a.pdf', contentType: 'application/pdf' }),
          makePdfRow({ id: 'png-row', originalFileName: 'b.png', contentType: 'image/png' }),
        ],
        totalCount: 2,
      },
      isLoading: false,
    });

    const { container } = render(<FileListPage />, { wrapper: makeWrapper() });

    // Exactly one SafetyCertificate icon (for the PDF row only)
    expect(container.querySelectorAll('.anticon-safety-certificate')).toHaveLength(1);
  });
});

// ─── 2. Modal: agent not-installed → install CTA ──────────────────────────────

describe('SignUploadedFileModal — agent not installed', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockUseSignUploadedFile.mockReturnValue({
      isLoading: false,
      error: null,
      requestId: undefined,
      startSigning: mockStartSigning,
      clearError: mockClearError,
    });
  });

  it('shows install CTA when agent is not-installed', async () => {
    mockProbeAgentStatus.mockResolvedValue('not-installed');

    render(
      <SignUploadedFileModal
        open
        fileId="file-pdf-1"
        originalFileName="contract.pdf"
        onClose={vi.fn()}
      />,
      { wrapper: makeWrapper() },
    );

    await waitFor(() =>
      expect(screen.getByText('Không kết nối được VGCASignService')).toBeTruthy(),
    );
    expect(screen.getByText(/Tải phần mềm VGCA/)).toBeTruthy();
    // Retry button absent — wrong message for "not-installed"
    expect(screen.queryByRole('button', { name: /Thử lại/ })).toBeNull();
  });

  it('shows retry when agent is unreachable (not the install guide)', async () => {
    mockProbeAgentStatus.mockResolvedValue('unreachable');

    render(
      <SignUploadedFileModal
        open
        fileId="file-pdf-1"
        originalFileName="contract.pdf"
        onClose={vi.fn()}
      />,
      { wrapper: makeWrapper() },
    );

    await waitFor(() =>
      expect(screen.getByText('Không kết nối được tới agent ký số')).toBeTruthy(),
    );
    expect(screen.getByRole('button', { name: /Thử lại/ })).toBeTruthy();
    expect(screen.queryByText(/Tải phần mềm VGCA/)).toBeNull();
  });
});

// ─── 3. Modal: happy path — stage → create → plugin → Completed ───────────────

describe('SignUploadedFileModal — happy path', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockProbeAgentStatus.mockResolvedValue('available');
  });

  it('renders the metadata form when agent is available', async () => {
    mockUseSignUploadedFile.mockReturnValue({
      isLoading: false,
      error: null,
      requestId: undefined,
      startSigning: mockStartSigning,
      clearError: mockClearError,
    });

    render(
      <SignUploadedFileModal
        open
        fileId="file-pdf-1"
        originalFileName="contract.pdf"
        onClose={vi.fn()}
      />,
      { wrapper: makeWrapper() },
    );

    await waitFor(() =>
      // Doc number input pre-filled with filename sans extension
      expect(screen.getByDisplayValue('contract')).toBeTruthy(),
    );
    // Sign type selector present
    expect(screen.getByText('Loại ký')).toBeTruthy();
    // Issued date picker present
    expect(screen.getByText('Ngày ban hành')).toBeTruthy();
    // Caveat alert present
    expect(screen.getByText(/Chữ ký số chưa được xác minh người ký/)).toBeTruthy();
  });

  it('calls startSigning when OK button is clicked', async () => {
    mockUseSignUploadedFile.mockReturnValue({
      isLoading: false,
      error: null,
      requestId: undefined,
      startSigning: mockStartSigning,
      clearError: mockClearError,
    });

    render(
      <SignUploadedFileModal
        open
        fileId="file-pdf-1"
        originalFileName="contract.pdf"
        onClose={vi.fn()}
      />,
      { wrapper: makeWrapper() },
    );

    await waitFor(() => expect(screen.getByDisplayValue('contract')).toBeTruthy());

    const okButton = screen.getByRole('button', { name: 'Ký số' });
    fireEvent.click(okButton);

    await waitFor(() => expect(mockStartSigning).toHaveBeenCalledOnce());
    expect(mockStartSigning).toHaveBeenCalledWith(
      expect.objectContaining({
        fileId: 'file-pdf-1',
        providerKey: 'usb-vgca',
        signType: 'Issued',
        docNumber: 'contract',
      }),
    );
  });

  it('shows success result when poll status is Completed', async () => {
    mockUseSignUploadedFile.mockReturnValue({
      isLoading: false,
      error: null,
      requestId: 'req-123',
      startSigning: mockStartSigning,
      clearError: mockClearError,
    });
    mockUseSigningRequestStatus.mockReturnValue({
      data: { publicId: 'req-123', status: 'Completed', artifactKind: 'UploadedPdf', providerKey: 'usb-vgca', failureReason: null },
      isSuccess: true,
      timedOut: false,
      retryPoll: vi.fn(),
    });

    render(
      <SignUploadedFileModal
        open
        fileId="file-pdf-1"
        originalFileName="contract.pdf"
        onClose={vi.fn()}
      />,
      { wrapper: makeWrapper() },
    );

    await waitFor(() =>
      expect(screen.getByText('Ký số thành công')).toBeTruthy(),
    );
    expect(screen.getByText(/Tệp PDF đã được ký số/)).toBeTruthy();
  });

  it('shows error alert when hook reports an error', async () => {
    mockUseSignUploadedFile.mockReturnValue({
      isLoading: false,
      error: 'vgca_sign_issued not available — plugin not loaded',
      requestId: undefined,
      startSigning: mockStartSigning,
      clearError: mockClearError,
    });
    // Precondition: no completed record. Stated explicitly because clearAllMocks() does NOT reset a
    // mockReturnValue set by a prior test — the error alert is only suppressed once the record completes.
    mockUseSigningRequestStatus.mockReturnValue({
      data: undefined,
      isSuccess: false,
      timedOut: false,
      retryPoll: vi.fn(),
    });

    render(
      <SignUploadedFileModal
        open
        fileId="file-pdf-1"
        originalFileName="contract.pdf"
        onClose={vi.fn()}
      />,
      { wrapper: makeWrapper() },
    );

    await waitFor(() =>
      expect(screen.getByText('Ký số thất bại')).toBeTruthy(),
    );
    expect(
      screen.getByText('vgca_sign_issued not available — plugin not loaded'),
    ).toBeTruthy();
  });

  it('suppresses the hook error once the record reports Completed (durable record wins)', async () => {
    // Reproduces the contradictory state observed in the field: the VGCA plugin callback timed out
    // (hook error set) but the durable signing-request record already completed server-side. The
    // success result must render and the spurious failure alert must NOT — the status poll is the
    // source of truth, and a missing best-effort callback does not mean the signing failed.
    mockUseSignUploadedFile.mockReturnValue({
      isLoading: false,
      error:
        'vgca_sign_issued timed out after 120s — check that VGCASignService is running and the user completed the PIN dialog',
      requestId: 'req-789',
      startSigning: mockStartSigning,
      clearError: mockClearError,
    });
    mockUseSigningRequestStatus.mockReturnValue({
      data: { publicId: 'req-789', status: 'Completed', artifactKind: 'UploadedPdf', providerKey: 'usb-vgca', failureReason: null },
      isSuccess: true,
      timedOut: false,
      retryPoll: vi.fn(),
    });

    render(
      <SignUploadedFileModal
        open
        fileId="file-pdf-1"
        originalFileName="contract.pdf"
        onClose={vi.fn()}
      />,
      { wrapper: makeWrapper() },
    );

    await waitFor(() => expect(screen.getByText('Ký số thành công')).toBeTruthy());
    // The timeout error string must NOT appear — suppressed because the record completed.
    expect(screen.queryByText(/timed out after 120s/)).toBeNull();
    // No failure header from the error branch either.
    expect(screen.queryByText('Ký số thất bại')).toBeNull();
  });

  it('shows timedOut fallback when poll cap exceeded', async () => {
    mockUseSignUploadedFile.mockReturnValue({
      isLoading: false,
      error: null,
      requestId: 'req-456',
      startSigning: mockStartSigning,
      clearError: mockClearError,
    });
    const mockRetryPoll = vi.fn();
    mockUseSigningRequestStatus.mockReturnValue({
      data: { publicId: 'req-456', status: 'Pending', artifactKind: 'UploadedPdf', providerKey: 'usb-vgca', failureReason: null },
      isSuccess: true,
      timedOut: true,
      retryPoll: mockRetryPoll,
    });

    render(
      <SignUploadedFileModal
        open
        fileId="file-pdf-1"
        originalFileName="contract.pdf"
        onClose={vi.fn()}
      />,
      { wrapper: makeWrapper() },
    );

    await waitFor(() =>
      expect(screen.getByText('Hết thời gian chờ')).toBeTruthy(),
    );
    // Retry poll button
    const retryBtn = screen.getByRole('button', { name: /Thử lại/ });
    fireEvent.click(retryBtn);
    expect(mockRetryPoll).toHaveBeenCalledOnce();
  });
});
