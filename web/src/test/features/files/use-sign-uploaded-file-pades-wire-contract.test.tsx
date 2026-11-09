/**
 * use-sign-uploaded-file-pades-wire-contract.test.tsx
 *
 * Wire-contract test: asserts useSignUploadedFilePades.startSigning forwards the `pades` parameters (with the
 * staged originalStorageKey) and the remote provider key into the createSigningRequest body — and does NOT send
 * `docFlow`. A missing/mis-shaped `pades` field would route to the wrong strategy or 400 at the BE; pinning the
 * body at the module boundary catches that before the wire.
 */

import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderHook, act, waitFor } from '@testing-library/react';
import { createElement, type ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

const mockStageFileForSigning = vi.fn();
const mockCreateSigningRequest = vi.fn();

vi.mock('@/features/files/file-signing-api', () => ({
  stageFileForSigning: (...args: unknown[]) => mockStageFileForSigning(...args),
}));

vi.mock('@/features/digital-signature/signature-api', () => ({
  createSigningRequest: (...args: unknown[]) => mockCreateSigningRequest(...args),
}));

import {
  useSignUploadedFilePades,
  REMOTE_MYSIGN_PROVIDER_KEY,
} from '@/features/files/use-sign-uploaded-file-pades';

function wrapper() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return ({ children }: { children: ReactNode }) =>
    createElement(QueryClientProvider, { client: qc }, children);
}

const FILE_ID = 'file-public-id-abc123';
const DOC_ID = 'doc-guid-xyz';
const STORAGE_KEY = 'tenant-1/doc-guid-xyz.pdf';

describe('useSignUploadedFilePades — pades wire contract', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockStageFileForSigning.mockResolvedValue({ docId: DOC_ID, originalStorageKey: STORAGE_KEY });
    mockCreateSigningRequest.mockResolvedValue({ requestId: 'req-222', clientHandoff: {} });
  });

  it('forwards pades.originalStorageKey + remote provider key, artifactRef = docId, and no docFlow', async () => {
    const { result } = renderHook(() => useSignUploadedFilePades(), { wrapper: wrapper() });

    await act(async () => {
      await result.current.startSigning({ fileId: FILE_ID, documentName: 'contract.pdf' });
    });

    await waitFor(() => expect(mockCreateSigningRequest).toHaveBeenCalledOnce());

    const [body] = mockCreateSigningRequest.mock.calls[0] as [Record<string, unknown>];
    expect(body.artifactKind).toBe('UploadedPdf');
    expect(body.artifactRef).toBe(DOC_ID);
    expect(body.providerKey).toBe(REMOTE_MYSIGN_PROVIDER_KEY);
    expect(body.sourceFileId).toBe(FILE_ID);

    // pades sub-body must carry the staged key; docFlow must be absent (wrong-strategy guard).
    const pades = body.pades as
      | { originalStorageKey: string; documentName?: string | null; signFlowMode?: string | null }
      | undefined;
    expect(pades).toBeDefined();
    expect(pades!.originalStorageKey).toBe(STORAGE_KEY);
    expect(pades!.documentName).toBe('contract.pdf');
    // No override passed → signFlowMode must be null (BE falls back to the tenant config).
    expect(pades!.signFlowMode).toBeNull();
    expect(body.docFlow).toBeUndefined();
  });

  it('forwards an explicit signFlowMode override into the pades sub-body', async () => {
    const { result } = renderHook(() => useSignUploadedFilePades(), { wrapper: wrapper() });

    await act(async () => {
      await result.current.startSigning({ fileId: FILE_ID, signFlowMode: 'Sync' });
    });

    await waitFor(() => expect(mockCreateSigningRequest).toHaveBeenCalledOnce());

    const [body] = mockCreateSigningRequest.mock.calls[0] as [Record<string, unknown>];
    const pades = body.pades as { signFlowMode?: string | null };
    expect(pades.signFlowMode).toBe('Sync');
  });

  it('exposes the created requestId for polling', async () => {
    const { result } = renderHook(() => useSignUploadedFilePades(), { wrapper: wrapper() });

    await act(async () => {
      await result.current.startSigning({ fileId: FILE_ID });
    });

    await waitFor(() => expect(result.current.requestId).toBe('req-222'));
    expect(result.current.error).toBeNull();
  });
});
