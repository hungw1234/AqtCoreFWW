/**
 * sign-submission-document-flow.test.tsx
 *
 * Tests for the form-submission PDF signing integration:
 *
 *   1. SignSubmissionDocumentButton renders only for Approved (PDF-eligible) submissions;
 *      hidden for Pending and Rejected.
 *   2. Modal: provider null → unavailable alert.
 *   3. Modal: agent not-installed → install CTA shown; agent unreachable → retry button.
 *   4. Modal: metadata form present and submits to startSigning.
 *   5. Modal: poll Completed → success result + caveat restated.
 *   6. Modal: agent absent — install CTA shown (no sign button).
 *   7. Modal: poll timedOut → fallback alert + retry.
 *
 * Mirrors sign-uploaded-file-flow.test.tsx structure and mock patterns for consistency.
 */

import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
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

// ─── signing provider + status mocks ─────────────────────────────────────────

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

// ─── use-sign-submission-document mock ───────────────────────────────────────

const mockStartSigning = vi.fn();
const mockClearError = vi.fn();
const mockUseSignSubmissionDocument = vi.fn(() => ({
  isLoading: false,
  error: null,
  requestId: undefined as string | undefined,
  startSigning: mockStartSigning,
  clearError: mockClearError,
}));

vi.mock('@/features/forms/use-sign-submission-document', () => ({
  useSignSubmissionDocument: () => mockUseSignSubmissionDocument(),
}));

// ─── Imports after mocks ──────────────────────────────────────────────────────

import {
  SignSubmissionDocumentButton,
  isSubmissionPdfEligible,
} from '@/features/forms/sign-submission-document-button';
import type { SubmissionStatus } from '@/features/forms/form-types';
import type { SigningRequestStatusDto } from '@/features/digital-signature/signature-types';

// ─── Safe defaults after vi.clearAllMocks() ───────────────────────────────────
// clearAllMocks wipes mock implementations so we restore safe defaults file-wide.

afterEach(() => {
  mockUseSigningRequestStatus.mockReturnValue({
    data: undefined,
    isSuccess: false,
    timedOut: false,
    retryPoll: vi.fn(),
  });
});

// ─── Helpers ──────────────────────────────────────────────────────────────────

function makeWrapper() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return ({ children }: { children: ReactNode }) =>
    createElement(QueryClientProvider, { client: qc }, children);
}

function renderButton(status: SubmissionStatus, extras?: {
  existingRequestId?: string;
  submissionLabel?: string;
}) {
  return render(
    <SignSubmissionDocumentButton
      publicId="sub-guid-1"
      templateId="tpl-guid-1"
      status={status}
      existingRequestId={extras?.existingRequestId}
      submissionLabel={extras?.submissionLabel ?? 'Đơn xin nghỉ phép'}
    />,
    { wrapper: makeWrapper() },
  );
}

// ─── 1. PDF-eligible gate ─────────────────────────────────────────────────────

describe('isSubmissionPdfEligible helper', () => {
  it('returns true for Approved', () => {
    expect(isSubmissionPdfEligible('Approved')).toBe(true);
  });

  it('returns false for Pending', () => {
    expect(isSubmissionPdfEligible('Pending')).toBe(false);
  });

  it('returns false for Rejected', () => {
    expect(isSubmissionPdfEligible('Rejected')).toBe(false);
  });
});

describe('SignSubmissionDocumentButton — PDF-eligible gate', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockUseSigningProviderInfo.mockReturnValue({
      data: { isEnabled: true, providerKey: 'usb-vgca', capabilities: 'DocFlow, RequiresLocalAgent' },
      isLoading: false,
    });
    mockUseSignSubmissionDocument.mockReturnValue({
      isLoading: false,
      error: null,
      requestId: undefined,
      startSigning: mockStartSigning,
      clearError: mockClearError,
    });
  });

  it('renders the sign button for Approved submission', () => {
    const { container } = renderButton('Approved');
    expect(container.querySelector('.anticon-safety-certificate')).toBeTruthy();
  });

  it('renders nothing for Pending submission', () => {
    const { container } = renderButton('Pending');
    expect(container.firstChild).toBeNull();
  });

  it('renders nothing for Rejected submission', () => {
    const { container } = renderButton('Rejected');
    expect(container.firstChild).toBeNull();
  });
});

// ─── 2. Modal: provider unavailable ──────────────────────────────────────────

describe('SignSubmissionDocumentButton modal — provider unavailable', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockUseSignSubmissionDocument.mockReturnValue({
      isLoading: false,
      error: null,
      requestId: undefined,
      startSigning: mockStartSigning,
      clearError: mockClearError,
    });
  });

  it('shows unavailable alert when provider isEnabled=false', async () => {
    mockUseSigningProviderInfo.mockReturnValue({
      data: { isEnabled: false, providerKey: null, capabilities: '' },
      isLoading: false,
    });

    const { container } = renderButton('Approved');

    // Open modal
    const btn = container.querySelector('.anticon-safety-certificate')
      ?.closest('button') as HTMLButtonElement;
    fireEvent.click(btn);

    await waitFor(() =>
      expect(screen.getByText('Ký số chưa khả dụng')).toBeTruthy(),
    );
    expect(
      screen.getByText(
        'Tổ chức chưa cấu hình nhà cung cấp chữ ký số. Vui lòng liên hệ quản trị viên để bật ký số.',
      ),
    ).toBeTruthy();
  });
});

// ─── 3. Modal: agent probe states ────────────────────────────────────────────

describe('SignSubmissionDocumentButton modal — agent probe', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockUseSigningProviderInfo.mockReturnValue({
      data: { isEnabled: true, providerKey: 'usb-vgca', capabilities: 'DocFlow, RequiresLocalAgent' },
      isLoading: false,
    });
    mockUseSignSubmissionDocument.mockReturnValue({
      isLoading: false,
      error: null,
      requestId: undefined,
      startSigning: mockStartSigning,
      clearError: mockClearError,
    });
  });

  it('shows install CTA when agent is not-installed', async () => {
    mockProbeAgentStatus.mockResolvedValue('not-installed');

    const { container } = renderButton('Approved');
    const btn = container.querySelector('.anticon-safety-certificate')
      ?.closest('button') as HTMLButtonElement;
    fireEvent.click(btn);

    await waitFor(() =>
      expect(screen.getByText('Không kết nối được VGCASignService')).toBeTruthy(),
    );
    expect(screen.getByText(/Tải phần mềm VGCA/)).toBeTruthy();
    // No retry button for not-installed (install guide, not retry)
    expect(screen.queryByRole('button', { name: /Thử lại/ })).toBeNull();
  });

  it('shows retry button when agent is unreachable', async () => {
    mockProbeAgentStatus.mockResolvedValue('unreachable');

    const { container } = renderButton('Approved');
    const btn = container.querySelector('.anticon-safety-certificate')
      ?.closest('button') as HTMLButtonElement;
    fireEvent.click(btn);

    await waitFor(() =>
      expect(screen.getByText('Không kết nối được tới agent ký số')).toBeTruthy(),
    );
    expect(screen.getByRole('button', { name: /Thử lại/ })).toBeTruthy();
    expect(screen.queryByText(/Tải phần mềm VGCA/)).toBeNull();
  });
});

// ─── 4. Modal: metadata form + submit ────────────────────────────────────────

describe('SignSubmissionDocumentButton modal — metadata form', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockUseSigningProviderInfo.mockReturnValue({
      data: { isEnabled: true, providerKey: 'usb-vgca', capabilities: 'DocFlow, RequiresLocalAgent' },
      isLoading: false,
    });
    mockProbeAgentStatus.mockResolvedValue('available');
    mockUseSignSubmissionDocument.mockReturnValue({
      isLoading: false,
      error: null,
      requestId: undefined,
      startSigning: mockStartSigning,
      clearError: mockClearError,
    });
  });

  it('renders metadata form with caveat when agent is available', async () => {
    const { container } = renderButton('Approved', { submissionLabel: 'Đơn nghỉ phép 2026' });
    const btn = container.querySelector('.anticon-safety-certificate')
      ?.closest('button') as HTMLButtonElement;
    fireEvent.click(btn);

    await waitFor(() =>
      expect(screen.getByDisplayValue('Đơn nghỉ phép 2026')).toBeTruthy(),
    );
    expect(screen.getByText('Loại ký')).toBeTruthy();
    expect(screen.getByText('Ngày ban hành')).toBeTruthy();
    // Cert-not-verified caveat MUST be visible during signing
    expect(screen.getByText(/Chữ ký số chưa được xác minh người ký/)).toBeTruthy();
  });

  it('calls startSigning with correct params when OK button is clicked', async () => {
    const { container } = renderButton('Approved', { submissionLabel: 'Phiếu đề xuất' });
    const btn = container.querySelector('.anticon-safety-certificate')
      ?.closest('button') as HTMLButtonElement;
    fireEvent.click(btn);

    await waitFor(() => expect(screen.getByDisplayValue('Phiếu đề xuất')).toBeTruthy());

    // Click the OK / Ký số button in the modal
    const okButton = screen.getByRole('button', { name: 'Ký số' });
    fireEvent.click(okButton);

    await waitFor(() => expect(mockStartSigning).toHaveBeenCalledOnce());
    expect(mockStartSigning).toHaveBeenCalledWith(
      expect.objectContaining({
        publicId: 'sub-guid-1',
        templateId: 'tpl-guid-1',
        providerKey: 'usb-vgca',
        signType: 'Approved',
        docNumber: 'Phiếu đề xuất',
      }),
    );
  });
});

// ─── 5. Modal: poll Completed → success + caveat ─────────────────────────────

describe('SignSubmissionDocumentButton modal — poll Completed', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockUseSigningProviderInfo.mockReturnValue({
      data: { isEnabled: true, providerKey: 'usb-vgca', capabilities: 'DocFlow, RequiresLocalAgent' },
      isLoading: false,
    });
    mockProbeAgentStatus.mockResolvedValue('available');
  });

  it('shows success result with caveat when poll status is Completed', async () => {
    mockUseSignSubmissionDocument.mockReturnValue({
      isLoading: false,
      error: null,
      requestId: 'req-sub-123',
      startSigning: mockStartSigning,
      clearError: mockClearError,
    });
    mockUseSigningRequestStatus.mockReturnValue({
      data: {
        publicId: 'req-sub-123',
        status: 'Completed',
        artifactKind: 'FormPdf',
        providerKey: 'usb-vgca',
        failureReason: null,
      },
      isSuccess: true,
      timedOut: false,
      retryPoll: vi.fn(),
    });

    const { container } = renderButton('Approved');
    const btn = container.querySelector('.anticon-safety-certificate')
      ?.closest('button') as HTMLButtonElement;
    fireEvent.click(btn);

    await waitFor(() =>
      expect(screen.getByText('Ký số thành công')).toBeTruthy(),
    );
    expect(screen.getByText(/Tài liệu phiếu đã được ký số/)).toBeTruthy();
    // Cert-not-verified caveat MUST be restated on success
    const caveats = screen.getAllByText(/Chữ ký số chưa được xác minh người ký/);
    expect(caveats.length).toBeGreaterThanOrEqual(1);
  });
});

// ─── 6. Modal: agent absent — no sign button enabled ─────────────────────────

describe('SignSubmissionDocumentButton modal — agent absent blocks signing', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockUseSigningProviderInfo.mockReturnValue({
      data: { isEnabled: true, providerKey: 'usb-vgca', capabilities: 'DocFlow, RequiresLocalAgent' },
      isLoading: false,
    });
    mockUseSignSubmissionDocument.mockReturnValue({
      isLoading: false,
      error: null,
      requestId: undefined,
      startSigning: mockStartSigning,
      clearError: mockClearError,
    });
    // Must be set after clearAllMocks — the global declaration returns undefined after a clear
    mockUseSigningRequestStatus.mockReturnValue({
      data: undefined,
      isSuccess: false,
      timedOut: false,
      retryPoll: vi.fn(),
    });
    mockProbeAgentStatus.mockResolvedValue('not-installed');
  });

  it('disables the OK button when agent is not-installed', async () => {
    const { container } = renderButton('Approved');
    const btn = container.querySelector('.anticon-safety-certificate')
      ?.closest('button') as HTMLButtonElement;
    fireEvent.click(btn);

    // Wait for agent probe to complete and alert to appear
    await waitFor(() =>
      expect(screen.getByText('Không kết nối được VGCASignService')).toBeTruthy(),
    );

    // The modal OK "Ký số" button is present but disabled (agentReady=false → canSign=false)
    const okButton = screen.getByRole('button', { name: 'Ký số' });
    expect(okButton).toBeDisabled();
  });
});

// ─── 7. Modal: poll timedOut ──────────────────────────────────────────────────

describe('SignSubmissionDocumentButton modal — poll timedOut', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockUseSigningProviderInfo.mockReturnValue({
      data: { isEnabled: true, providerKey: 'usb-vgca', capabilities: 'DocFlow, RequiresLocalAgent' },
      isLoading: false,
    });
    mockProbeAgentStatus.mockResolvedValue('available');
  });

  it('shows timedOut alert with retry button', async () => {
    mockUseSignSubmissionDocument.mockReturnValue({
      isLoading: false,
      error: null,
      requestId: 'req-sub-456',
      startSigning: mockStartSigning,
      clearError: mockClearError,
    });
    const mockRetryPoll = vi.fn();
    mockUseSigningRequestStatus.mockReturnValue({
      data: {
        publicId: 'req-sub-456',
        status: 'Pending',
        artifactKind: 'FormPdf',
        providerKey: 'usb-vgca',
        failureReason: null,
      },
      isSuccess: true,
      timedOut: true,
      retryPoll: mockRetryPoll,
    });

    const { container } = renderButton('Approved');
    const btn = container.querySelector('.anticon-safety-certificate')
      ?.closest('button') as HTMLButtonElement;
    fireEvent.click(btn);

    await waitFor(() =>
      expect(screen.getByText('Hết thời gian chờ')).toBeTruthy(),
    );

    const retryBtn = screen.getByRole('button', { name: /Thử lại/ });
    fireEvent.click(retryBtn);
    expect(mockRetryPoll).toHaveBeenCalledOnce();
  });
});
