/**
 * sign-submission-document-button-history.test.tsx
 *
 * Covers the Phase 06 addition to SignSubmissionDocumentButton:
 *   - "Lịch sử ký" button appears alongside the disabled "Đã ký" badge
 *     when alreadySigned (isCompleted=true, existingRequestId set, no new requestId)
 *   - Clicking the history button opens a Drawer containing SigningStepsPanel
 *     with scope="forms" and the correct submission publicId
 *   - History button absent when submission is NOT already signed
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

// ─── signing provider / status mocks ─────────────────────────────────────────

const mockUseSigningRequestStatus = vi.fn();

vi.mock('@/features/digital-signature/signature-api', () => ({
  useSigningProviderInfo: () => ({
    data: { isEnabled: true, providerKey: 'usb-vgca', capabilities: 'DocFlow,RequiresLocalAgent' },
    isLoading: false,
  }),
  useSigningRequestStatus: () => mockUseSigningRequestStatus(),
  createSigningRequest: vi.fn(),
}));

// ─── adapter registry mock ────────────────────────────────────────────────────

vi.mock('@/features/digital-signature/registry', () => ({
  resolveAdapter: vi.fn(),
  hasAdapter: vi.fn(() => false),
}));

// ─── vgca adapter side-effect imports (no-op in jsdom) ───────────────────────

vi.mock('@/features/digital-signature/providers/vgca-usb-adapter', () => ({}));
vi.mock('@/features/digital-signature/providers/remote-signing-adapter', () => ({}));

// ─── use-sign-submission-document mock ───────────────────────────────────────

vi.mock('@/features/forms/use-sign-submission-document', () => ({
  useSignSubmissionDocument: () => ({
    isLoading: false, error: null, requestId: undefined,
    startSigning: vi.fn(), clearError: vi.fn(),
  }),
}));

// ─── AgentStatusAlerts mock ───────────────────────────────────────────────────

vi.mock('@/features/files/sign-uploaded-file-agent-alerts', () => ({
  AgentStatusAlerts: () => null,
}));

// ─── SigningStepsPanel mock — capture props ───────────────────────────────────

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
const EXISTING_REQ_ID = 'req-existing-001';

// ─── Tests ────────────────────────────────────────────────────────────────────

describe('SignSubmissionDocumentButton — signing history surface (alreadySigned state)', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    capturedPanelProps.length = 0;

    // Simulate alreadySigned: poll returns Completed for the existing request
    mockUseSigningRequestStatus.mockReturnValue({
      data: {
        publicId: EXISTING_REQ_ID,
        status: 'Completed',
        artifactKind: 'FormPdf',
        providerKey: 'usb-vgca',
        failureReason: null,
      },
      isSuccess: true,
      timedOut: false,
      retryPoll: vi.fn(),
    });
  });

  it('shows the history button alongside the disabled "Đã ký" badge when already signed', () => {
    render(
      <SignSubmissionDocumentButton
        publicId={SUBMISSION_ID}
        templateId="template-1"
        status="Approved"
        existingRequestId={EXISTING_REQ_ID}
      />,
      { wrapper: makeWrapper() },
    );

    // The disabled "Đã ký" badge
    expect(screen.getByText('Đã ký')).toBeTruthy();

    // The HistoryOutlined icon button should now appear next to the badge
    const { container } = render(
      <SignSubmissionDocumentButton
        publicId={SUBMISSION_ID}
        templateId="template-1"
        status="Approved"
        existingRequestId={EXISTING_REQ_ID}
      />,
      { wrapper: makeWrapper() },
    );
    expect(container.querySelector('.anticon-history')).toBeTruthy();
  });

  it('opens a drawer with SigningStepsPanel scope="forms" and the submission publicId', async () => {
    const { container } = render(
      <SignSubmissionDocumentButton
        publicId={SUBMISSION_ID}
        templateId="template-1"
        status="Approved"
        existingRequestId={EXISTING_REQ_ID}
      />,
      { wrapper: makeWrapper() },
    );

    const historyBtn = container.querySelector('.anticon-history')?.closest('button');
    expect(historyBtn).toBeTruthy();
    fireEvent.click(historyBtn!);

    await waitFor(() =>
      expect(screen.getByTestId('signing-steps-panel')).toBeTruthy(),
    );

    // The shared panel must receive scope="forms" + the submission's publicId —
    // NOT a file id, NOT a template id.
    expect(
      capturedPanelProps.some(
        (p) => p.scope === 'forms' && p.id === SUBMISSION_ID,
      ),
    ).toBe(true);
  });
});

describe('SignSubmissionDocumentButton — history button absent when not signed', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    capturedPanelProps.length = 0;

    // Not yet signed: no existing request, status Pending from poll
    mockUseSigningRequestStatus.mockReturnValue({
      data: undefined,
      isSuccess: false,
      timedOut: false,
      retryPoll: vi.fn(),
    });
  });

  it('does NOT render the history icon when existingRequestId is absent', () => {
    const { container } = render(
      <SignSubmissionDocumentButton
        publicId={SUBMISSION_ID}
        templateId="template-1"
        status="Approved"
        // No existingRequestId → alreadySigned = false → shows sign button, not history
      />,
      { wrapper: makeWrapper() },
    );

    expect(container.querySelector('.anticon-history')).toBeNull();
  });

  it('returns null for non-Approved status (PDF-ineligible gate)', () => {
    const { container } = render(
      <SignSubmissionDocumentButton
        publicId={SUBMISSION_ID}
        templateId="template-1"
        status="Pending"
      />,
      { wrapper: makeWrapper() },
    );

    // Component renders nothing for non-Approved statuses
    expect(container.firstChild).toBeNull();
  });
});
