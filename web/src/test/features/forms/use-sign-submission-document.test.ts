/**
 * use-sign-submission-document.test.ts — wire-contract guard for the sign-document hook.
 *
 * Exercises the REAL hook (not a mock) to pin the hook→API request body. A prior bug shipped
 * because the button→hook contract was tested while the hook→API body silently dropped
 * `providerKey`, which the BE validator requires — every sign call 422'd in production while
 * unit tests (mocking at the command level) stayed green. This test asserts the exact body
 * the hook POSTs, so that class of FE↔BE drift fails here instead of at runtime.
 */
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderHook, act } from '@testing-library/react';
import { createElement, type ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

const mockSignSubmissionDocument = vi.fn();
const mockRunVgcaPlugin = vi.fn();

vi.mock('@/features/forms/sign-submission-document-api', () => ({
  signSubmissionDocument: (...args: unknown[]) => mockSignSubmissionDocument(...args),
}));

vi.mock('@/features/digital-signature/providers/vgca-docflow-methods', () => ({
  runVgcaPlugin: (...args: unknown[]) => mockRunVgcaPlugin(...args),
  signTypeToPluginFn: (signType: string) => `plugin_${signType}`,
}));

import { useSignSubmissionDocument } from '@/features/forms/use-sign-submission-document';

function wrapper({ children }: { children: ReactNode }) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return createElement(QueryClientProvider, { client }, children);
}

const baseParams = {
  publicId: '11111111-1111-1111-1111-111111111111',
  templateId: '22222222-2222-2222-2222-222222222222',
  providerKey: 'usb-vgca',
  signType: 'Issued' as const,
  docNumber: 'CV-2026/042',
  issuedDate: new Date('2026-06-17T00:00:00Z'),
};

describe('useSignSubmissionDocument — hook→API wire contract', () => {
  beforeEach(() => {
    mockSignSubmissionDocument.mockReset();
    mockRunVgcaPlugin.mockReset();
    mockSignSubmissionDocument.mockResolvedValue({
      requestId: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
      clientHandoff: { handlerUrl: 'wss://127.0.0.1:8987', jwt: 'jwt-token', docId: 'doc-1' },
    });
    mockRunVgcaPlugin.mockResolvedValue(undefined);
  });

  it('forwards providerKey (required by BE validator) in the POST body', async () => {
    const { result } = renderHook(() => useSignSubmissionDocument(), { wrapper });

    await act(async () => {
      await result.current.startSigning(baseParams);
    });

    expect(mockSignSubmissionDocument).toHaveBeenCalledTimes(1);
    expect(mockSignSubmissionDocument).toHaveBeenCalledWith(
      baseParams.publicId,
      expect.objectContaining({
        signType: 'Issued',
        docNumber: 'CV-2026/042',
        issuedDate: '2026-06-17', // sliced to YYYY-MM-DD
        providerKey: 'usb-vgca', // ← the field whose omission broke the happy path
      }),
    );
  });

  it('drives the VGCA plugin with the handoff from the POST (no second initiate)', async () => {
    const { result } = renderHook(() => useSignSubmissionDocument(), { wrapper });

    await act(async () => {
      await result.current.startSigning(baseParams);
    });

    expect(mockRunVgcaPlugin).toHaveBeenCalledTimes(1);
    expect(mockRunVgcaPlugin).toHaveBeenCalledWith(
      'plugin_Issued',
      expect.objectContaining({ handlerUrl: 'wss://127.0.0.1:8987', jwt: 'jwt-token', docId: 'doc-1' }),
      expect.objectContaining({ docNumber: 'CV-2026/042' }),
    );
    expect(result.current.requestId).toBe('aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa');
  });

  it('clears requestId when the plugin invocation fails so retry re-runs the POST', async () => {
    mockRunVgcaPlugin.mockRejectedValueOnce(new Error('agent timeout'));
    const { result } = renderHook(() => useSignSubmissionDocument(), { wrapper });

    await act(async () => {
      await result.current.startSigning(baseParams);
    });

    expect(result.current.requestId).toBeUndefined();
    expect(result.current.error).toBe('agent timeout');
  });
});
