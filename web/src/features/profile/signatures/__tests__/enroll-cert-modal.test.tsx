// EnrollCertModal — Phase 4 provider-first re-order.
// New flow: modal always opens at 'pick' (provider selection).
//   - USB: goes straight to the token ceremony, CCCD step is NEVER shown.
//   - Remote + !nationalIdEstablished: clicking Remote shows the CCCD step, then proceeds to Remote enrollment.
//   - Remote + nationalIdEstablished: goes straight to Remote enrollment.
// Preserves: prevOpenRef guard (no mid-OTP reset race), USB ceremony, Remote OTP-wait ceremony.

import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';

vi.mock('react-i18next', () => ({
  useTranslation: () => ({ t: (key: string, fallback?: string) => fallback ?? key }),
}));
vi.mock('@/core/api/extract-api-error', () => ({ extractApiErrorMessage: () => null }));
// Side-effect adapter registration import → no-op in tests.
vi.mock('@/features/digital-signature/providers/vgca-usb-adapter', () => ({}));

const setCccdMutate = vi.fn().mockResolvedValue(undefined);
const issueMutate = vi.fn();
const enrollMutate = vi.fn().mockResolvedValue('enroll-id');
vi.mock('../signatures-api', () => ({
  useSetSigningNationalId: () => ({ mutateAsync: setCccdMutate, isPending: false }),
  useIssueEnrollmentChallenge: () => ({ mutateAsync: issueMutate }),
  useEnrollSignatureCert: () => ({ mutateAsync: enrollMutate }),
}));

const signMock = vi.fn();
const probeMock = vi.fn();
vi.mock('@/features/digital-signature/registry', () => ({
  resolveAdapter: () => ({ probeAgentStatus: probeMock, sign: signMock }),
}));

import { EnrollCertModal } from '../enroll-cert-modal';

beforeEach(() => {
  setCccdMutate.mockClear();
  enrollMutate.mockClear();
  issueMutate.mockReset().mockResolvedValue({ nonceId: 'N1', challengeBase64: btoa('nonce'), expiresAt: '' });
  signMock.mockReset().mockResolvedValue({ signatureBase64: 'SIG64', signerCertBase64: 'CERT64' });
  probeMock.mockReset().mockResolvedValue('available');
});

describe('EnrollCertModal', () => {
  it('opens at the provider-pick step regardless of nationalIdEstablished', () => {
    render(<EnrollCertModal open nationalIdEstablished={false} onClose={() => {}} />);
    // Provider buttons are visible immediately — CCCD input is NOT shown.
    expect(screen.getByRole('button', { name: /Ký số từ xa/ })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /USB Token/ })).toBeInTheDocument();
    expect(screen.queryByPlaceholderText('079201001234')).not.toBeInTheDocument();
  });

  it('USB path completes WITHOUT a CCCD entry step even when nationalIdEstablished is false', async () => {
    render(<EnrollCertModal open nationalIdEstablished={false} onClose={() => {}} />);

    // Provider pick is shown — CCCD input absent.
    expect(screen.queryByPlaceholderText('079201001234')).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: /USB Token/ }));

    await waitFor(() => expect(enrollMutate).toHaveBeenCalledTimes(1));
    // USB ceremony runs: probe → sign → enroll; CCCD never set.
    expect(probeMock).toHaveBeenCalled();
    expect(signMock).toHaveBeenCalledWith(expect.any(Uint8Array), { encode: 'RSA' });
    expect(enrollMutate).toHaveBeenCalledWith({
      kind: 'UsbTokenVgca',
      nonceId: 'N1',
      signedNonceBase64: 'SIG64',
      certBase64: 'CERT64',
    });
    expect(setCccdMutate).not.toHaveBeenCalled();
  });

  it('Remote path shows CCCD step when nationalIdEstablished is false, then proceeds to Remote enrollment', async () => {
    render(<EnrollCertModal open nationalIdEstablished={false} onClose={() => {}} />);

    // Pick step — no CCCD yet.
    expect(screen.queryByPlaceholderText('079201001234')).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: /Ký số từ xa/ }));

    // CCCD step appears (Mysign requires it).
    const input = await screen.findByPlaceholderText('079201001234');
    fireEvent.change(input, { target: { value: '079201001234' } });
    fireEvent.click(screen.getByRole('button', { name: 'Tiếp tục' }));

    await waitFor(() => expect(setCccdMutate).toHaveBeenCalledWith({ nationalId: '079201001234' }));
    // After CCCD, Remote enrollment proceeds automatically (no second provider click needed).
    await waitFor(() => expect(enrollMutate).toHaveBeenCalledWith({ kind: 'RemoteViettelMysign', nonceId: 'N1' }));
    expect(signMock).not.toHaveBeenCalled();
  });

  it('Remote enroll goes straight to sign when nationalIdEstablished is true', async () => {
    render(<EnrollCertModal open nationalIdEstablished onClose={() => {}} />);

    fireEvent.click(screen.getByRole('button', { name: /Ký số từ xa/ }));

    await waitFor(() => expect(enrollMutate).toHaveBeenCalledTimes(1));
    expect(enrollMutate).toHaveBeenCalledWith({ kind: 'RemoteViettelMysign', nonceId: 'N1' });
    expect(setCccdMutate).not.toHaveBeenCalled();
    expect(signMock).not.toHaveBeenCalled();
  });

  it('USB enroll runs the token ceremony and submits cert + signed nonce', async () => {
    render(<EnrollCertModal open nationalIdEstablished onClose={() => {}} />);

    fireEvent.click(screen.getByRole('button', { name: /USB Token/ }));

    await waitFor(() => expect(enrollMutate).toHaveBeenCalledTimes(1));
    expect(probeMock).toHaveBeenCalled();
    // The RAW nonce bytes are signed (adapter hashes internally) — not the base64 string.
    expect(signMock).toHaveBeenCalledWith(expect.any(Uint8Array), { encode: 'RSA' });
    expect(enrollMutate).toHaveBeenCalledWith({
      kind: 'UsbTokenVgca',
      nonceId: 'N1',
      signedNonceBase64: 'SIG64',
      certBase64: 'CERT64',
    });
  });

  it('USB enroll aborts (no submit) when the agent is unavailable', async () => {
    probeMock.mockResolvedValue('not-installed');
    render(<EnrollCertModal open nationalIdEstablished onClose={() => {}} />);

    fireEvent.click(screen.getByRole('button', { name: /USB Token/ }));

    await waitFor(() => expect(probeMock).toHaveBeenCalled());
    expect(signMock).not.toHaveBeenCalled();
    expect(enrollMutate).not.toHaveBeenCalled();
  });
});
