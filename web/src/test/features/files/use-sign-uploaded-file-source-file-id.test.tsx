/**
 * use-sign-uploaded-file-source-file-id.test.tsx
 *
 * Wire-contract test: asserts that useSignUploadedFile.startSigning forwards
 * `sourceFileId: params.fileId` in the createSigningRequest POST body when
 * artifactKind === 'UploadedPdf'.
 *
 * This field links the signing request back to the Files FileRecord so
 * GET /files/{id}/signing-steps returns rows after the ceremony completes.
 * Without it the steps list is always empty — the test pins the boundary so
 * any accidental omission is caught before reaching the BE.
 *
 * Pin strategy: mock createSigningRequest at the module boundary (the real
 * serialization layer), not at the hook level, so the test exercises the
 * actual call-site code that assembles the request body.
 */

import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderHook, act, waitFor } from '@testing-library/react';
import { createElement, type ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

// ─── Module mocks ──────────────────────────────────────────────────────────────

const mockStageFileForSigning = vi.fn();
const mockCreateSigningRequest = vi.fn();
const mockRunVgcaPlugin = vi.fn();

vi.mock('@/features/files/file-signing-api', () => ({
  stageFileForSigning: (...args: unknown[]) => mockStageFileForSigning(...args),
}));

vi.mock('@/features/digital-signature/signature-api', () => ({
  createSigningRequest: (...args: unknown[]) => mockCreateSigningRequest(...args),
}));

vi.mock('@/features/digital-signature/providers/vgca-docflow-methods', () => ({
  runVgcaPlugin: (...args: unknown[]) => mockRunVgcaPlugin(...args),
  signTypeToPluginFn: (s: string) => `vgca_sign_${s.toLowerCase()}`,
}));

import { useSignUploadedFile } from '@/features/files/use-sign-uploaded-file';

// ─── Helpers ──────────────────────────────────────────────────────────────────

function wrapper() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return ({ children }: { children: ReactNode }) =>
    createElement(QueryClientProvider, { client: qc }, children);
}

const FILE_ID = 'file-public-id-abc123';
const DOC_ID  = 'doc-guid-xyz';
const STORAGE_KEY = 'tenant-1/signing-original/doc-guid-xyz.pdf';

const STAGE_RESULT = { docId: DOC_ID, originalStorageKey: STORAGE_KEY };
const CREATE_RESULT = {
  requestId: 'req-111',
  clientHandoff: {
    handlerUrl: 'http://localhost:5001/api/v1/signing/docflow/handler',
    jwt: 'test-jwt',
    docId: DOC_ID,
  },
};

const PARAMS = {
  fileId: FILE_ID,
  fileName: 'contract.pdf',
  providerKey: 'usb-vgca',
  signType: 'Issued' as const,
  docNumber: 'CV-2026-001',
  issuedDate: new Date('2026-06-18'),
};

// ─── Tests ────────────────────────────────────────────────────────────────────

describe('useSignUploadedFile — sourceFileId wire contract', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockStageFileForSigning.mockResolvedValue(STAGE_RESULT);
    mockCreateSigningRequest.mockResolvedValue(CREATE_RESULT);
    mockRunVgcaPlugin.mockResolvedValue({ status: 'Completed', storageKey: null });
  });

  it('includes sourceFileId === fileId in the createSigningRequest body for UploadedPdf', async () => {
    const { result } = renderHook(() => useSignUploadedFile(), { wrapper: wrapper() });

    await act(async () => {
      await result.current.startSigning(PARAMS);
    });

    await waitFor(() => expect(mockCreateSigningRequest).toHaveBeenCalledOnce());

    // The body passed to createSigningRequest must contain sourceFileId = fileId.
    // This is the field that links the signing request to the FileRecord so
    // GET /files/{id}/signing-steps returns rows after the ceremony.
    const [body] = mockCreateSigningRequest.mock.calls[0] as [Record<string, unknown>];
    expect(body.artifactKind).toBe('UploadedPdf');
    expect(body.sourceFileId).toBe(FILE_ID);
  });

  it('passes artifactRef as the staged docId (not the fileId)', async () => {
    const { result } = renderHook(() => useSignUploadedFile(), { wrapper: wrapper() });

    await act(async () => {
      await result.current.startSigning(PARAMS);
    });

    await waitFor(() => expect(mockCreateSigningRequest).toHaveBeenCalledOnce());

    const [body] = mockCreateSigningRequest.mock.calls[0] as [Record<string, unknown>];
    // artifactRef = docId from the staging response (not the fileId public id)
    expect(body.artifactRef).toBe(DOC_ID);
    // sourceFileId is the FILES-module public id so steps can be queried by file
    expect(body.sourceFileId).toBe(FILE_ID);
    expect(body.sourceFileId).not.toBe(DOC_ID);
  });

  it('still calls runVgcaPlugin after createSigningRequest resolves', async () => {
    const { result } = renderHook(() => useSignUploadedFile(), { wrapper: wrapper() });

    await act(async () => {
      await result.current.startSigning(PARAMS);
    });

    await waitFor(() => expect(result.current.requestId).toBe('req-111'));
    expect(mockRunVgcaPlugin).toHaveBeenCalledOnce();
  });
});
