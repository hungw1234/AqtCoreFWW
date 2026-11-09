/**
 * sign-submission-history-action.test.tsx
 *
 * Covers the Phase 06 addition to SignSubmissionDocumentButton:
 *   - "Lịch sử ký" button renders when alreadySigned (isCompleted + existingRequestId)
 *   - "Lịch sử ký" button absent when not yet signed
 *   - Clicking opens SigningStepsPanel with scope="forms" + correct publicId
 *   - Panel is hidden when not signed
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

// ─── signature-api mocks ──────────────────────────────────────────────────────

const mockUseSigningProviderInfo = vi.fn();
const mockUseSigningRequestStatus = vi.fn();

vi.mock('@/features/digital-signature/signature-api', () => ({
  useSigningProviderInfo: () => mockUseSigningProviderInfo(),
  useSigningRequestStatus: () => mockUseSigningRequestStatus(),
  createSigningRequest: vi.fn(),
}));

// ─── adapter registry mock ────────────────────────────────────────────────────

vi.mock('@/features/digital-signature/registry', () => ({
  resolveAdapter: vi.fn(),
  hasAdapter: vi.fn(() => false),
}));

// ─── adapter side-effect imports (no-op in jsdom) ────────────────────────────

vi.mock('@/features/digital-signature/providers/vgca-usb-adapter', () => ({}));
vi.mock('@/features/digital-signature/providers/remote-signing-adapter', () => ({}));

// ─── use-sign-submission-document mock ───────────────────────────────────────

vi.mock('@/features/forms/use-sign-submission-document', () => ({
  useSignSubmissionDocument: () => ({
    isLoading: false, error: null, requestId: undefined,
    startSigning: vi.fn(), clearError: vi.fn(),
  }),
}));

// ─── SigningStepsPanel mock — captures props ──────────────────────────────────

const capturedPanelProps: Array<{ scope: string; id: string }> = [];

vi.mock(
  '@/features/digital-signature/signed-document/signing-steps-panel',
  () => ({
    SigningStepsPanel: ({ scope, id }: { scope: string; id: string }) => {
      capturedPanelProps.push({ scope, id });
      return createElement(
        'div',
        { 'data-testid': 'signing-steps-panel' },
        `${scope}:${id}`,
      );
    },
  }),
);

import { SignSubmissionDocumentButton } from '@/features/forms/sign-submission-document-button';

// ─── Helpers ──────────────────────────────────────────────────────────────────

function makeWrapper() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return ({ children }: { children: ReactNode }) =>
    createElement(QueryClientProvider, { client: qc }, children);
}

const SUBMISSION_ID = 'sub-pub-id-abc123';
const TEMPLATE_ID   = 'tpl-pub-id-xyz789';
const EXISTING_REQ  = 'req-existing-111';

// Shared "already signed" status mock
function setupAlreadySigned() {
  mockUseSigningProviderInfo.mockReturnValue({
    data: { isEnabled: true, providerKey: 'usb-vgca', capabilities: 'DocFlow,RequiresLocalAgent' },
    isLoading: false,
  });
  mockUseSigningRequestStatus.mockReturnValue({
    data: {
      publicId: EXISTING_REQ,
      status: 'Completed',
      artifactKind: 'FormPdf',
      providerKey: 'usb-vgca',
      failureReason: null,
    },
    isSuccess: true,
    timedOut: false,
    retryPoll: vi.fn(),
  });
}

// ─── Tests ────────────────────────────────────────────────────────────────────

describe('SignSubmissionDocumentButton — history action when already signed', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    capturedPanelProps.length = 0;
  });

  it('renders the history icon button when alreadySigned', () => {
    setupAlreadySigned();

    const { container } = render(
      <SignSubmissionDocumentButton
        publicId={SUBMISSION_ID}
        templateId={TEMPLATE_ID}
        status="Approved"
        existingRequestId={EXISTING_REQ}
      />,
      { wrapper: makeWrapper() },
    );

    // antd renders HistoryOutlined as <span class="anticon anticon-history">
    expect(container.querySelector('.anticon-history')).toBeTruthy();
  });

  it('does NOT render the history button when not signed', () => {
    mockUseSigningProviderInfo.mockReturnValue({
      data: { isEnabled: true, providerKey: 'usb-vgca', capabilities: 'DocFlow,RequiresLocalAgent' },
      isLoading: false,
    });
    // No completed request — alreadySigned condition is false
    mockUseSigningRequestStatus.mockReturnValue({
      data: undefined,
      isSuccess: false,
      timedOut: false,
      retryPoll: vi.fn(),
    });

    const { container } = render(
      <SignSubmissionDocumentButton
        publicId={SUBMISSION_ID}
        templateId={TEMPLATE_ID}
        status="Approved"
        existingRequestId={null}
      />,
      { wrapper: makeWrapper() },
    );

    expect(container.querySelector('.anticon-history')).toBeNull();
  });

  it('opens SigningStepsPanel with scope="forms" and publicId on history click', async () => {
    setupAlreadySigned();

    const { container } = render(
      <SignSubmissionDocumentButton
        publicId={SUBMISSION_ID}
        templateId={TEMPLATE_ID}
        status="Approved"
        existingRequestId={EXISTING_REQ}
      />,
      { wrapper: makeWrapper() },
    );

    const historyBtn = container.querySelector('.anticon-history')?.closest('button');
    expect(historyBtn).toBeTruthy();
    fireEvent.click(historyBtn!);

    await waitFor(() =>
      expect(screen.getByTestId('signing-steps-panel')).toBeTruthy(),
    );

    // The panel MUST receive scope="forms" (not "files") and the submission publicId
    expect(
      capturedPanelProps.some(
        (p) => p.scope === 'forms' && p.id === SUBMISSION_ID,
      ),
    ).toBe(true);
  });

  it('renders "Đã ký" disabled badge alongside the history button', () => {
    setupAlreadySigned();

    render(
      <SignSubmissionDocumentButton
        publicId={SUBMISSION_ID}
        templateId={TEMPLATE_ID}
        status="Approved"
        existingRequestId={EXISTING_REQ}
      />,
      { wrapper: makeWrapper() },
    );

    // The "Đã ký" badge button is present and disabled
    const signedBtn = screen.getByRole('button', { name: /Đã ký/ });
    expect(signedBtn).toBeDisabled();
  });

  it('returns null for non-PDF-eligible statuses (Pending)', () => {
    setupAlreadySigned();

    const { container } = render(
      <SignSubmissionDocumentButton
        publicId={SUBMISSION_ID}
        templateId={TEMPLATE_ID}
        status="Pending"
        existingRequestId={EXISTING_REQ}
      />,
      { wrapper: makeWrapper() },
    );

    // isSubmissionPdfEligible('Pending') = false → component renders null
    expect(container.firstChild).toBeNull();
  });
});
