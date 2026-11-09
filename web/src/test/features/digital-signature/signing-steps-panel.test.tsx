/**
 * signing-steps-panel.test.tsx
 *
 * Covers:
 *   - Renders N rows ordered by sequenceNumber ascending
 *   - Empty state ("Chưa có bước ký") when steps array is empty
 *   - "Xem" button calls fetchSignedStepBlob with the right (scope, id, sequence)
 *   - "Tải về" button calls fetchSignedStepBlob with the right (scope, id, sequence)
 *   - Row actions are disabled when hasSignedPdf=false
 *   - signerName fallback rendered when signerName is null (Phase A invariant)
 *   - Error state shown when query fails
 */

import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { createElement, type ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

// ─── i18n stub ────────────────────────────────────────────────────────────────

vi.mock('react-i18next', () => ({
  useTranslation: () => ({
    // t(key) → key
    // t(key, fallbackString) → fallbackString
    // t(key, fallbackString, opts) → fallbackString with {{param}} replaced by opts values
    t: (key: string, fallbackOrOpts?: string | Record<string, unknown>, opts?: Record<string, unknown>) => {
      if (typeof fallbackOrOpts === 'string') {
        const interpolationOpts = opts ?? {};
        // Replace {{param}} placeholders with opts values
        return fallbackOrOpts.replace(/\{\{(\w+)\}\}/g, (_, k: string) =>
          String(interpolationOpts[k] ?? ''),
        );
      }
      return key;
    },
    i18n: { language: 'vi' },
  }),
}));

// ─── useSigningSteps mock ─────────────────────────────────────────────────────

const mockUseSigningSteps = vi.fn();

vi.mock(
  '@/features/digital-signature/signed-document/use-signing-steps',
  () => ({ useSigningSteps: (...args: unknown[]) => mockUseSigningSteps(...args) }),
);

// ─── fetchSignedStepBlob mock ─────────────────────────────────────────────────

const mockFetchSignedStepBlob = vi.fn();

vi.mock(
  '@/features/digital-signature/signed-document/signed-document-api',
  () => ({ fetchSignedStepBlob: (...args: unknown[]) => mockFetchSignedStepBlob(...args) }),
);

// ─── SignedPdfViewer lazy mock — avoids react-pdf pdfjs worker in jsdom ────────

vi.mock(
  '@/features/digital-signature/signed-document/signed-pdf-viewer',
  () => ({
    SignedPdfViewer: ({ open, title }: { open: boolean; title?: string }) =>
      open ? createElement('div', { 'data-testid': 'pdf-viewer' }, title ?? 'viewer') : null,
  }),
);

// ─── notification bridge mock ─────────────────────────────────────────────────

vi.mock('@/core/api/notification-bridge', () => ({
  appMessage: { error: vi.fn(), success: vi.fn() },
}));

// ─── extract-api-error mock ───────────────────────────────────────────────────

vi.mock('@/core/api/extract-api-error', () => ({
  extractApiErrorMessage: () => undefined,
}));

import { SigningStepsPanel } from '@/features/digital-signature/signed-document/signing-steps-panel';
import type { SigningStepRow } from '@/features/digital-signature/signed-document/signing-step-types';

// ─── Helpers ──────────────────────────────────────────────────────────────────

function makeWrapper() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return ({ children }: { children: ReactNode }) =>
    createElement(QueryClientProvider, { client: qc }, children);
}

function makeStep(overrides?: Partial<SigningStepRow>): SigningStepRow {
  const seq = overrides?.sequenceNumber ?? 1;
  return {
    // Default id derived from sequence so single-step tests get a stable rowKey; tests that render
    // multiple attempts at the same sequence must pass distinct signingRequestId values explicitly.
    signingRequestId: `req-${seq}`,
    sequenceNumber: 1,
    status: 'Completed',
    signerUserId: 'user-uuid-111',
    signerName: null,
    completedAt: '2026-06-18T10:00:00Z',
    hasSignedPdf: true,
    ...overrides,
  };
}

const PDF_BLOB = new Blob(['%PDF-1.4'], { type: 'application/pdf' });

// ─── Tests ────────────────────────────────────────────────────────────────────

describe('SigningStepsPanel — rendering', () => {
  beforeEach(() => vi.clearAllMocks());

  it('renders rows ordered by sequenceNumber ascending', () => {
    mockUseSigningSteps.mockReturnValue({
      data: [
        makeStep({ sequenceNumber: 3, signerName: 'Alice' }),
        makeStep({ sequenceNumber: 1, signerName: 'Bob' }),
        makeStep({ sequenceNumber: 2, signerName: 'Carol' }),
      ],
      isLoading: false,
      error: null,
    });

    const { getAllByRole } = render(
      <SigningStepsPanel scope="files" id="file-abc" />,
      { wrapper: makeWrapper() },
    );

    // Each row = one <tr> in the tbody; get all data rows
    const rows = getAllByRole('row');
    // rows[0] = header; rows[1..3] = data
    // The table renders sequenceNumber as first cell — check order
    const cells = rows.slice(1).map((r) => r.querySelector('td')?.textContent);
    expect(cells).toEqual(['1', '2', '3']);
  });

  it('shows the empty state when steps array is empty', () => {
    mockUseSigningSteps.mockReturnValue({ data: [], isLoading: false, error: null });

    render(<SigningStepsPanel scope="files" id="file-abc" />, { wrapper: makeWrapper() });

    expect(screen.getByText('Chưa có bước ký')).toBeTruthy();
  });

  it('shows error alert when query fails', () => {
    mockUseSigningSteps.mockReturnValue({
      data: [],
      isLoading: false,
      error: new Error('network'),
    });

    render(<SigningStepsPanel scope="files" id="file-abc" />, { wrapper: makeWrapper() });

    // Error message from i18n stub (key-as-fallback pattern)
    expect(screen.getByText('Không tải được lịch sử ký')).toBeTruthy();
  });

  it('renders signerName fallback "Người ký #N" when signerName is null', () => {
    mockUseSigningSteps.mockReturnValue({
      data: [makeStep({ sequenceNumber: 2, signerName: null })],
      isLoading: false,
      error: null,
    });

    render(<SigningStepsPanel scope="files" id="file-abc" />, { wrapper: makeWrapper() });

    // The i18n stub returns "Người ký #2" for the signerFallback key with seq=2
    expect(screen.getByText('Người ký #2')).toBeTruthy();
  });

  it('renders provided signerName when non-null (future capability)', () => {
    mockUseSigningSteps.mockReturnValue({
      data: [makeStep({ sequenceNumber: 1, signerName: 'Nguyễn Văn A' })],
      isLoading: false,
      error: null,
    });

    render(<SigningStepsPanel scope="files" id="file-abc" />, { wrapper: makeWrapper() });

    expect(screen.getByText('Nguyễn Văn A')).toBeTruthy();
  });
});

// ─── Row actions ──────────────────────────────────────────────────────────────

describe('SigningStepsPanel — Xem action', () => {
  beforeEach(() => vi.clearAllMocks());

  it('calls fetchSignedStepBlob with scope="files", id, and sequenceNumber on Xem click', async () => {
    mockUseSigningSteps.mockReturnValue({
      data: [makeStep({ sequenceNumber: 1, hasSignedPdf: true })],
      isLoading: false,
      error: null,
    });
    mockFetchSignedStepBlob.mockResolvedValue(PDF_BLOB);

    render(<SigningStepsPanel scope="files" id="file-xyz" />, { wrapper: makeWrapper() });

    const viewBtn = screen.getByRole('button', { name: /Xem/i });
    fireEvent.click(viewBtn);

    await waitFor(() => expect(mockFetchSignedStepBlob).toHaveBeenCalledOnce());
    expect(mockFetchSignedStepBlob).toHaveBeenCalledWith(
      'files',
      'file-xyz',
      expect.objectContaining({ sequenceNumber: 1, signingRequestId: 'req-1' }),
    );
  });

  it('calls fetchSignedStepBlob with scope="forms" for forms scope', async () => {
    mockUseSigningSteps.mockReturnValue({
      data: [makeStep({ sequenceNumber: 1, hasSignedPdf: true })],
      isLoading: false,
      error: null,
    });
    mockFetchSignedStepBlob.mockResolvedValue(PDF_BLOB);

    render(<SigningStepsPanel scope="forms" id="submission-pub-id" />, { wrapper: makeWrapper() });

    const viewBtn = screen.getByRole('button', { name: /Xem/i });
    fireEvent.click(viewBtn);

    await waitFor(() => expect(mockFetchSignedStepBlob).toHaveBeenCalledOnce());
    expect(mockFetchSignedStepBlob).toHaveBeenCalledWith(
      'forms',
      'submission-pub-id',
      expect.objectContaining({ sequenceNumber: 1 }),
    );
  });

  it('opens the PDF viewer after fetching the blob', async () => {
    mockUseSigningSteps.mockReturnValue({
      data: [makeStep({ sequenceNumber: 1, hasSignedPdf: true })],
      isLoading: false,
      error: null,
    });
    mockFetchSignedStepBlob.mockResolvedValue(PDF_BLOB);

    render(<SigningStepsPanel scope="files" id="file-xyz" />, { wrapper: makeWrapper() });

    fireEvent.click(screen.getByRole('button', { name: /Xem/i }));

    await waitFor(() =>
      expect(screen.getByTestId('pdf-viewer')).toBeTruthy(),
    );
  });
});

describe('SigningStepsPanel — Tải về action', () => {
  // Save the original createElement before any spy so we can call the real one
  const realCreateElement = document.createElement.bind(document);

  beforeEach(() => {
    vi.clearAllMocks();
    // Stub URL APIs that jsdom does not implement
    vi.stubGlobal('URL', {
      createObjectURL: vi.fn(() => 'blob:mock-url'),
      revokeObjectURL: vi.fn(),
    });
  });

  afterEach(() => {
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
  });

  it('calls fetchSignedStepBlob with correct args on Tải về click', async () => {
    mockUseSigningSteps.mockReturnValue({
      data: [makeStep({ sequenceNumber: 2, hasSignedPdf: true })],
      isLoading: false,
      error: null,
    });
    mockFetchSignedStepBlob.mockResolvedValue(PDF_BLOB);

    // Intercept anchor creation to stub .click() without recursion.
    // We call `realCreateElement` (captured before any spy) to avoid infinite recursion.
    const anchorClickSpy = vi.fn();
    vi.spyOn(document, 'createElement').mockImplementation((tag: string) => {
      const el = realCreateElement(tag);
      if (tag === 'a') {
        // Override click so jsdom's navigation attempt is a no-op
        Object.defineProperty(el, 'click', { value: anchorClickSpy, configurable: true });
      }
      return el;
    });

    render(<SigningStepsPanel scope="files" id="file-dl" />, { wrapper: makeWrapper() });

    const dlBtn = screen.getByRole('button', { name: /Tải về/i });
    fireEvent.click(dlBtn);

    await waitFor(() => expect(mockFetchSignedStepBlob).toHaveBeenCalledOnce());
    expect(mockFetchSignedStepBlob).toHaveBeenCalledWith(
      'files',
      'file-dl',
      expect.objectContaining({ sequenceNumber: 2, signingRequestId: 'req-2' }),
    );
  });
});

// ─── Independent re-signs of the same file (the multi-sign history bug) ──────────

describe('SigningStepsPanel — multiple signs at the same sequence', () => {
  beforeEach(() => vi.clearAllMocks());

  it('renders one row per Completed attempt even when they share the same sequenceNumber', () => {
    // A single-signer file signed twice → two requests, both sequence 1, distinct request ids/blobs.
    mockUseSigningSteps.mockReturnValue({
      data: [
        makeStep({ sequenceNumber: 1, signingRequestId: 'req-first', completedAt: '2026-06-18T06:29:00Z' }),
        makeStep({ sequenceNumber: 1, signingRequestId: 'req-second', completedAt: '2026-06-18T10:04:00Z' }),
      ],
      isLoading: false,
      error: null,
    });

    const { getAllByRole } = render(
      <SigningStepsPanel scope="files" id="file-multi" />,
      { wrapper: makeWrapper() },
    );

    // Header + 2 data rows — both attempts visible (no collapse-by-sequence).
    expect(getAllByRole('row')).toHaveLength(3);
  });

  it('downloads each attempt by its own signingRequestId, not the latest', async () => {
    mockUseSigningSteps.mockReturnValue({
      data: [
        makeStep({ sequenceNumber: 1, signingRequestId: 'req-first', completedAt: '2026-06-18T06:29:00Z' }),
        makeStep({ sequenceNumber: 1, signingRequestId: 'req-second', completedAt: '2026-06-18T10:04:00Z' }),
      ],
      isLoading: false,
      error: null,
    });
    mockFetchSignedStepBlob.mockResolvedValue(PDF_BLOB);

    render(<SigningStepsPanel scope="files" id="file-multi" />, { wrapper: makeWrapper() });

    // Click the FIRST attempt's "Xem" (rows sorted chronologically → first = req-first).
    const viewButtons = screen.getAllByRole('button', { name: /Xem/i });
    fireEvent.click(viewButtons[0]);

    await waitFor(() => expect(mockFetchSignedStepBlob).toHaveBeenCalledOnce());
    expect(mockFetchSignedStepBlob).toHaveBeenCalledWith(
      'files',
      'file-multi',
      expect.objectContaining({ signingRequestId: 'req-first' }),
    );
  });
});

describe('SigningStepsPanel — disabled actions', () => {
  beforeEach(() => vi.clearAllMocks());
  afterEach(() => vi.restoreAllMocks());

  it('disables Xem and Tải về when hasSignedPdf=false', () => {
    mockUseSigningSteps.mockReturnValue({
      data: [makeStep({ hasSignedPdf: false, status: 'Pending' })],
      isLoading: false,
      error: null,
    });

    render(<SigningStepsPanel scope="files" id="file-abc" />, { wrapper: makeWrapper() });

    const viewBtn = screen.getByRole('button', { name: /Xem/i });
    const dlBtn   = screen.getByRole('button', { name: /Tải về/i });

    expect(viewBtn).toBeDisabled();
    expect(dlBtn).toBeDisabled();
  });
});
