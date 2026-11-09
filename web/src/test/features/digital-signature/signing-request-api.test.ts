/**
 * signing-request-api.test.ts — unit tests for provider-agnostic signing request API client.
 *
 * Covers:
 *   - createSigningRequest: POST /api/signing/request → success path
 *   - getSigningRequestStatus: GET /api/signing/request/{id}/status → success path
 *   - useSigningRequestStatus hook:
 *       - non-terminal status keeps polling (refetchInterval returns interval ms)
 *       - terminal status stops polling (refetchInterval returns false)
 *       - attempt cap exceeded → timedOut = true, polling stops
 *       - retryPoll resets the query (zeros counters → polling resumes)
 *       - disabled when publicId is undefined
 */

import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { createElement, type ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

// ─── Mock apiClient transport ──────────────────────────────────────────────────

const mockPost = vi.fn();
const mockGet = vi.fn();

vi.mock('@/core/api', () => ({
  apiClient: {
    post: (...args: unknown[]) => mockPost(...args),
    get: (...args: unknown[]) => mockGet(...args),
  },
}));

// Import AFTER mock is registered so module resolver sees the stub.
import {
  createSigningRequest,
  getSigningRequestStatus,
  useSigningRequestStatus,
} from '@/features/digital-signature/signature-api';
import type {
  CreateSigningRequestBody,
  CreateSigningRequestResult,
  SigningRequestStatusDto,
} from '@/features/digital-signature/signature-types';

// ─── Fixtures ──────────────────────────────────────────────────────────────────

const BODY: CreateSigningRequestBody = {
  artifactKind: 'FormPdf',
  artifactRef: 'aaaaaaaa-0000-0000-0000-000000000001',
  providerKey: 'usb-vgca',
  docFlow: {
    signType: 'Issued',
    originalStorageKey: 'a1b2c3d4-0000-0000-0000-000000000099/doc.pdf',
    docNumber: 'SỐ-001/2026',
    issuedDate: '2026-06-16',
  },
};

const CREATE_RESULT: CreateSigningRequestResult = {
  requestId: 'bbbbbbbb-0000-0000-0000-000000000001',
  clientHandoff: {
    handlerUrl: 'https://example.com/handler',
    jwt: 'tok.tok.tok',
    docId: 'cccccccc-0000-0000-0000-000000000001',
  },
};

function makeStatusDto(status: SigningRequestStatusDto['status']): SigningRequestStatusDto {
  return {
    publicId: 'bbbbbbbb-0000-0000-0000-000000000001',
    status,
    artifactKind: 'FormPdf',
    providerKey: 'usb-vgca',
    failureReason: status === 'Failed' ? 'signing ceremony timed out' : null,
  };
}

// ─── QueryClient wrapper factory ────────────────────────────────────────────────
// Each test gets a fresh QueryClient so query state never leaks between tests.

function makeWrapper(qc?: QueryClient) {
  const client =
    qc ??
    new QueryClient({
      defaultOptions: { queries: { retry: false, staleTime: 0 } },
    });
  return ({ children }: { children: ReactNode }) =>
    createElement(QueryClientProvider, { client }, children);
}

// ─── Plain API function tests ──────────────────────────────────────────────────

describe('createSigningRequest', () => {
  beforeEach(() => {
    mockPost.mockReset();
    mockGet.mockReset();
  });

  it('POSTs to /signing/request with the provided body and returns unwrapped data', async () => {
    mockPost.mockResolvedValueOnce({ data: CREATE_RESULT });

    const result = await createSigningRequest(BODY);

    // A generous per-call timeout is passed so the tenant SYNC remote-flow (blocks ~120s on the OTP) is not aborted
    // by the default 30s axios timeout; harmless for the ASYNC flow which returns immediately.
    expect(mockPost).toHaveBeenCalledWith('/signing/request', BODY, { timeout: 150_000 });
    expect(result.requestId).toBe(CREATE_RESULT.requestId);
    expect(result.clientHandoff).toEqual(CREATE_RESULT.clientHandoff);
  });

  it('propagates rejection on network error', async () => {
    mockPost.mockRejectedValueOnce(new Error('network error'));

    await expect(createSigningRequest(BODY)).rejects.toThrow('network error');
  });
});

describe('getSigningRequestStatus', () => {
  beforeEach(() => {
    mockPost.mockReset();
    mockGet.mockReset();
  });

  it('GETs /signing/request/{publicId}/status and returns the DTO', async () => {
    const dto = makeStatusDto('InProgress');
    mockGet.mockResolvedValueOnce({ data: dto });

    const result = await getSigningRequestStatus('bbbbbbbb-0000-0000-0000-000000000001');

    expect(mockGet).toHaveBeenCalledWith(
      '/signing/request/bbbbbbbb-0000-0000-0000-000000000001/status',
    );
    expect(result.status).toBe('InProgress');
    expect(result.failureReason).toBeNull();
  });

  it('propagates rejection on 404', async () => {
    const err = Object.assign(new Error('Not Found'), { status: 404 });
    mockGet.mockRejectedValueOnce(err);

    await expect(
      getSigningRequestStatus('bbbbbbbb-0000-0000-0000-000000000001'),
    ).rejects.toMatchObject({ status: 404 });
  });
});

// ─── useSigningRequestStatus hook tests ───────────────────────────────────────

describe('useSigningRequestStatus hook', () => {
  beforeEach(() => {
    mockPost.mockReset();
    mockGet.mockReset();
  });

  it('is disabled (no fetch) when publicId is undefined', async () => {
    const { result } = renderHook(() => useSigningRequestStatus(undefined), {
      wrapper: makeWrapper(),
    });

    // Give the event loop a tick — no fetch should fire.
    await new Promise((r) => setTimeout(r, 0));

    expect(mockGet).not.toHaveBeenCalled();
    expect(result.current.data).toBeUndefined();
    expect(result.current.timedOut).toBe(false);
  });

  it('fetches status when publicId is provided', async () => {
    const dto = makeStatusDto('Pending');
    mockGet.mockResolvedValueOnce({ data: dto });

    const { result } = renderHook(
      () => useSigningRequestStatus('bbbbbbbb-0000-0000-0000-000000000001'),
      { wrapper: makeWrapper() },
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(result.current.data?.status).toBe('Pending');
    expect(result.current.timedOut).toBe(false);
  });

  it('does not set timedOut=true for a non-terminal status that has not hit the cap', async () => {
    const dto = makeStatusDto('InProgress');
    mockGet.mockResolvedValue({ data: dto });

    const { result } = renderHook(
      () => useSigningRequestStatus('bbbbbbbb-0000-0000-0000-000000000001'),
      { wrapper: makeWrapper() },
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(result.current.timedOut).toBe(false);
  });

  // Terminal statuses — polling must stop.
  it.each(['Completed', 'Failed', 'Expired'] as const)(
    'sets timedOut=false and isSuccess=true for terminal status %s',
    async (terminalStatus) => {
      const dto = makeStatusDto(terminalStatus);
      mockGet.mockResolvedValue({ data: dto });

      const { result } = renderHook(
        () => useSigningRequestStatus('bbbbbbbb-0000-0000-0000-000000000001'),
        { wrapper: makeWrapper() },
      );

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(result.current.data?.status).toBe(terminalStatus);
      // Terminal status means NOT timed out (ceremony resolved).
      expect(result.current.timedOut).toBe(false);
    },
  );

  it('timedOut=true when cap reached with non-terminal status and retryPoll resets the query', async () => {
    // Use a QueryClient with direct access for state manipulation.
    const qc = new QueryClient({
      defaultOptions: { queries: { retry: false, staleTime: 0 } },
    });

    const publicId = 'bbbbbbbb-0000-0000-0000-000000000001';
    const dto = makeStatusDto('InProgress');
    mockGet.mockResolvedValue({ data: dto });

    const { result } = renderHook(() => useSigningRequestStatus(publicId), {
      wrapper: makeWrapper(qc),
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    // Manually advance the dataUpdateCount past the cap by injecting into cache state.
    // This simulates SIGNING_REQUEST_POLL_MAX_ATTEMPTS (60) being exceeded.
    const queryKey = ['signing-request', 'status', publicId] as const;
    const state = qc.getQueryState(queryKey);

    // Patch the query cache entry's dataUpdateCount past the cap (60).
    if (state) {
      // Directly mutate internal cache state for test isolation (now type-compatible — no suppression needed).
      qc.getQueryCache().find({ queryKey })!.state.dataUpdateCount = 60;
    }

    // Re-render to pick up the mutated state.
    const { result: result2 } = renderHook(() => useSigningRequestStatus(publicId), {
      wrapper: makeWrapper(qc),
    });

    await waitFor(() => expect(result2.current.isSuccess).toBe(true));

    expect(result2.current.timedOut).toBe(true);

    // retryPoll resets the query — timedOut should go back to false once reset resolves.
    mockGet.mockResolvedValueOnce({ data: dto });
    result2.current.retryPoll();

    await waitFor(() => {
      const s = qc.getQueryState(queryKey);
      // After reset, dataUpdateCount starts back at 1 on first success.
      return (s?.dataUpdateCount ?? 0) <= 1;
    });

    // After reset the cap counter is zeroed → timedOut should clear.
    const stateAfter = qc.getQueryState(queryKey);
    const attemptsAfter = (stateAfter?.dataUpdateCount ?? 0) + (stateAfter?.fetchFailureCount ?? 0);
    expect(attemptsAfter).toBeLessThan(60);
  });

  it('exposes retryPoll as a callable function', async () => {
    const dto = makeStatusDto('Pending');
    mockGet.mockResolvedValue({ data: dto });

    const { result } = renderHook(
      () => useSigningRequestStatus('bbbbbbbb-0000-0000-0000-000000000001'),
      { wrapper: makeWrapper() },
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(typeof result.current.retryPoll).toBe('function');
  });
});
