// SignUploadedFilePadesModal — Phase 6 Slice D.
// Covers: approved-enrollment gate disables signing; happy path forwards the placement JSON
// (profile + visualType + PDF-point rect) into startSigning; no-rectangle path signs invisibly (placement null).

import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import type { SignatureOverviewDto } from '@/features/profile/signatures/signature-types';
import { buildPlacementJson } from '@/features/digital-signature/placement/placement-geometry';

vi.mock('react-i18next', () => ({
  useTranslation: () => ({ t: (key: string, fallback?: string) => fallback ?? key }),
}));

// Heavy react-pdf children — replaced so the modal renders without pdfjs.
vi.mock('@/features/digital-signature/placement/signature-placement-overlay', () => ({
  SignaturePlacementOverlay: ({ onChange }: { onChange: (s: unknown) => void }) => (
    <button
      type="button"
      data-testid="emit-placement"
      onClick={() => onChange({ page: 2, rect: { x: 100, y: 692, w: 200, h: 50 } })}
    >
      emit
    </button>
  ),
}));
vi.mock('@/features/digital-signature/signed-document/signed-pdf-viewer', () => ({
  SignedPdfViewer: () => null,
}));

const startSigning = vi.fn();
// Stable identity — the hook returns a useCallback in production, so the modal's open-reset effect must
// not re-fire every render (a fresh vi.fn() here would, wiping the drawn placement before the OK click).
const resetMock = vi.fn();
const useSignatureOverviewMock = vi.fn();

vi.mock('@/features/profile/signatures/signatures-api', () => ({
  useSignatureOverview: () => useSignatureOverviewMock(),
}));
vi.mock('../file-api', () => ({
  useFileBlob: () => ({ data: undefined }),
}));
vi.mock('../use-sign-uploaded-file-pades', () => ({
  useSignUploadedFilePades: () => ({
    isLoading: false,
    error: null,
    requestId: undefined,
    startSigning,
    reset: resetMock,
  }),
}));
vi.mock('@/features/digital-signature/signature-api', () => ({
  useSigningRequestStatus: () => ({ data: undefined, timedOut: false, retryPoll: vi.fn() }),
  downloadSignedPdf: vi.fn(),
}));

import { SignUploadedFilePadesModal } from '../sign-uploaded-file-pades-modal';

function makeOverview(overrides: Partial<SignatureOverviewDto>): SignatureOverviewDto {
  return {
    nationalIdEstablished: true,
    hasApprovedEnrollment: true,
    profiles: [
      { publicId: 'P1', displayName: 'Mặc định', isDefault: true, hasCertLinked: true, hasFullImage: true, hasInitialImage: false, showSerial: true, showIssuer: true, createdAt: '2026-06-30T00:00:00Z', updatedAt: null },
    ],
    enrollments: [],
    ...overrides,
  };
}

function renderModal() {
  return render(
    <SignUploadedFilePadesModal open fileId="F1" originalFileName="doc.pdf" onClose={() => {}} />,
  );
}

describe('SignUploadedFilePadesModal', () => {
  beforeEach(() => {
    startSigning.mockReset();
    useSignatureOverviewMock.mockReset();
  });

  it('blocks signing until an enrollment is approved', () => {
    useSignatureOverviewMock.mockReturnValue({ data: makeOverview({ hasApprovedEnrollment: false }), isLoading: false });
    renderModal();
    expect(screen.getByText('Chưa có chứng thư số được duyệt')).toBeInTheDocument();
    const okBtn = screen.getByRole('button', { name: 'Ký số' });
    expect(okBtn).toBeDisabled();
  });

  it('forwards the placement JSON (profile + visualType + rect) into startSigning', async () => {
    useSignatureOverviewMock.mockReturnValue({ data: makeOverview({}), isLoading: false });
    renderModal();

    fireEvent.click(screen.getByTestId('emit-placement'));
    fireEvent.click(screen.getByRole('button', { name: 'Ký số' }));

    await waitFor(() => expect(startSigning).toHaveBeenCalledTimes(1));
    expect(startSigning).toHaveBeenCalledWith({
      fileId: 'F1',
      documentName: 'doc.pdf',
      reason: '',
      placement: buildPlacementJson({
        page: 2, x: 100, y: 692, w: 200, h: 50, profilePublicId: 'P1', visualType: 'full',
      }),
    });
  });

  it('signs invisibly (placement null) when no rectangle is drawn', async () => {
    useSignatureOverviewMock.mockReturnValue({ data: makeOverview({}), isLoading: false });
    renderModal();

    fireEvent.click(screen.getByRole('button', { name: 'Ký số' }));

    await waitFor(() => expect(startSigning).toHaveBeenCalledTimes(1));
    expect(startSigning).toHaveBeenCalledWith({ fileId: 'F1', documentName: 'doc.pdf', reason: '', placement: null });
  });
});
