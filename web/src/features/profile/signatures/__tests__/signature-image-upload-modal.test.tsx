// SignatureImageUploadModal — Phase 6 image slice.
// Covers: null-profile renders nothing; per-slot badges reflect the profile flags; a valid PNG pick
// uploads to the correct slot; a non-PNG/JPEG pick is rejected before any upload.

import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';

vi.mock('react-i18next', () => ({
  useTranslation: () => ({
    t: (key: string, opts?: { name?: string }) => (opts?.name ? `${key}:${opts.name}` : key),
  }),
}));

const mutateAsync = vi.fn().mockResolvedValue(undefined);
const fetchImageBlob = vi.fn().mockResolvedValue(new Blob(['x'], { type: 'image/png' }));
vi.mock('../signatures-api', () => ({
  useUploadSignatureImage: () => ({ mutateAsync, isPending: false }),
  fetchSignatureImageBlob: (...a: unknown[]) => fetchImageBlob(...a),
}));

// jsdom implements neither URL.createObjectURL nor revokeObjectURL — the preview effect needs both.
beforeEach(() => {
  URL.createObjectURL = vi.fn(() => 'blob:mock');
  URL.revokeObjectURL = vi.fn();
});

const errorMsg = vi.fn();
const successMsg = vi.fn();
const warningMsg = vi.fn();
vi.mock('@/core/api/notification-bridge', () => ({
  appMessage: {
    error: (...a: unknown[]) => errorMsg(...a),
    success: (...a: unknown[]) => successMsg(...a),
    warning: (...a: unknown[]) => warningMsg(...a),
  },
}));

// The draw path delegates to SignatureWidget (covered by its own suite); stub it so jsdom never touches canvas.
vi.mock('@/shared/components/signature-widget', () => ({
  SignatureWidget: () => null,
}));

import { SignatureImageUploadModal } from '../signature-image-upload-modal';
import type { SignatureProfileDto } from '../signature-types';

const profile: SignatureProfileDto = {
  publicId: 'p-1',
  displayName: 'Work',
  isDefault: false,
  hasCertLinked: false,
  hasFullImage: true,
  hasInitialImage: false,
  showSerial: true,
  showIssuer: true,
  createdAt: '2026-01-01T00:00:00Z',
  updatedAt: null,
};

beforeEach(() => {
  mutateAsync.mockClear();
  fetchImageBlob.mockClear();
  errorMsg.mockClear();
  successMsg.mockClear();
  warningMsg.mockClear();
});

describe('SignatureImageUploadModal', () => {
  it('renders nothing when no profile is targeted', () => {
    const { container } = render(<SignatureImageUploadModal open profile={null} onClose={vi.fn()} />);
    expect(container).toBeEmptyDOMElement();
  });

  it('badges each slot independently from the profile flags', () => {
    render(<SignatureImageUploadModal open profile={profile} onClose={vi.fn()} />);
    // Full slot is populated → "has"; Initial slot is empty → "none".
    expect(screen.getByText('profile.signatures.image.has')).toBeInTheDocument();
    expect(screen.getByText('profile.signatures.image.none')).toBeInTheDocument();
  });

  it('uploads a picked PNG to the full-signature slot', async () => {
    render(<SignatureImageUploadModal open profile={profile} onClose={vi.fn()} />);
    // Modal content mounts in a portal (outside the render container) → query the document.
    // The first file input belongs to the full-signature slot (rendered first).
    const input = document.querySelector('input[type="file"]') as HTMLInputElement;
    const file = new File(['x'], 'sig.png', { type: 'image/png' });
    fireEvent.change(input, { target: { files: [file] } });

    const saveButtons = await screen.findAllByText('profile.signatures.image.save');
    fireEvent.click(saveButtons[0].closest('button')!);

    await waitFor(() => expect(mutateAsync).toHaveBeenCalledTimes(1));
    expect(mutateAsync).toHaveBeenCalledWith(
      expect.objectContaining({ publicId: 'p-1', visualType: 'FullSignature', file }),
    );
  });

  it('fetches and previews the stored image only for a filled slot', async () => {
    render(<SignatureImageUploadModal open profile={profile} onClose={vi.fn()} />);
    // Full slot is filled → its preview is fetched once; the empty Initial slot never fetches.
    await waitFor(() => expect(fetchImageBlob).toHaveBeenCalledTimes(1));
    expect(fetchImageBlob).toHaveBeenCalledWith('p-1', 'FullSignature');
    expect(await screen.findByText('profile.signatures.image.current')).toBeInTheDocument();
  });

  it('rejects a non-PNG/JPEG pick before uploading', async () => {
    render(<SignatureImageUploadModal open profile={profile} onClose={vi.fn()} />);
    const input = document.querySelector('input[type="file"]') as HTMLInputElement;
    const svg = new File(['<svg/>'], 'x.svg', { type: 'image/svg+xml' });
    fireEvent.change(input, { target: { files: [svg] } });

    await waitFor(() => expect(errorMsg).toHaveBeenCalledWith('profile.signatures.image.invalidType'));
    expect(mutateAsync).not.toHaveBeenCalled();
  });
});
