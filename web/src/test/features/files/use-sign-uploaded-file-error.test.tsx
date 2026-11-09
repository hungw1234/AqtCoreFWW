/**
 * use-sign-uploaded-file-error.test.tsx
 *
 * Pins the hook's error-surfacing contract: a backend rejection (e.g. the encrypted-PDF
 * ValidationError from stage-for-signing) must reach the modal as the backend's SPECIFIC reason,
 * not the generic "Ký số thất bại" fallback. Uses the real extractApiErrorMessage (pure util) so
 * the test exercises the actual normalized-error shape, not a mock mirror.
 */

import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderHook, act, waitFor } from '@testing-library/react';
import { createElement, type ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

const mockStageFileForSigning = vi.fn();

vi.mock('@/features/files/file-signing-api', () => ({
  stageFileForSigning: (...args: unknown[]) => mockStageFileForSigning(...args),
}));

// createSigningRequest / runVgcaPlugin are never reached in these tests (stage throws first),
// but must exist as module exports for the import graph.
vi.mock('@/features/digital-signature/signature-api', () => ({
  createSigningRequest: vi.fn(),
}));
vi.mock('@/features/digital-signature/providers/vgca-docflow-methods', () => ({
  runVgcaPlugin: vi.fn(),
  signTypeToPluginFn: (s: string) => `vgca_sign_${s.toLowerCase()}`,
}));

import { useSignUploadedFile } from '@/features/files/use-sign-uploaded-file';

function wrapper() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return ({ children }: { children: ReactNode }) =>
    createElement(QueryClientProvider, { client: qc }, children);
}

const PARAMS = {
  fileId: 'file-1',
  fileName: 'doc.pdf',
  providerKey: 'usb-vgca',
  signType: 'Issued' as const,
  docNumber: 'doc',
  issuedDate: new Date('2026-06-18'),
};

describe('useSignUploadedFile — error surfacing', () => {
  beforeEach(() => vi.clearAllMocks());

  it('surfaces the backend message from a NormalizedApiError (encrypted-PDF reject)', async () => {
    const beMessage =
      'Tệp PDF đã được mã hoá hoặc đặt mật khẩu bảo vệ nên không thể ký số. Vui lòng tải lên tệp PDF không mã hoá rồi thử lại.';
    // NormalizedApiError is a plain object (NOT an Error instance) — the prior `err.message` path
    // would have fallen straight to the generic label, which is the bug this guards.
    // GOV_SYS_002 = ErrorCodes.System.ValidationFailed, the code a BE ValidationError actually emits.
    mockStageFileForSigning.mockRejectedValue({ status: 422, code: 'GOV_SYS_002', message: beMessage });

    const { result } = renderHook(() => useSignUploadedFile(), { wrapper: wrapper() });
    await act(async () => { await result.current.startSigning(PARAMS); });

    await waitFor(() => expect(result.current.error).toBe(beMessage));
  });

  it('falls back to a thrown Error message (VGCA plugin / ceremony failure)', async () => {
    mockStageFileForSigning.mockRejectedValue(new Error('USB token not ready'));

    const { result } = renderHook(() => useSignUploadedFile(), { wrapper: wrapper() });
    await act(async () => { await result.current.startSigning(PARAMS); });

    await waitFor(() => expect(result.current.error).toBe('USB token not ready'));
  });

  it('uses the generic label only when neither an API message nor an Error message exists', async () => {
    // e.g. an antd form-validation rejection shape — extractApiErrorMessage returns undefined and it
    // is not an Error, so the generic label is the correct last resort.
    mockStageFileForSigning.mockRejectedValue({ errorFields: [], values: {} });

    const { result } = renderHook(() => useSignUploadedFile(), { wrapper: wrapper() });
    await act(async () => { await result.current.startSigning(PARAMS); });

    await waitFor(() => expect(result.current.error).toBe('Ký số thất bại'));
  });
});
