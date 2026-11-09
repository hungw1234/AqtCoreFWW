// Plan 260523-1241 Phase 06b — `useWorkflowPermCodes` returns the post-interceptor
// unwrapped string[] from the orval hook.
//
// Important runtime contract (precedent: business-process-admin-api.ts:96):
//   The api-client axios interceptor at `core/api/api-client.ts:138-142` strips the
//   envelope BEFORE returning to orval. So `useGetApiV1SecurityPermCodes` resolves
//   with the inner `string[]` at runtime, NOT the envelope shape the orval-generated
//   TYPE claims. Tests mock the POST-INTERCEPTOR shape — anything else gives a
//   green test against a phantom envelope that never exists at runtime.

import { describe, it, expect, vi } from 'vitest';
import { renderHook } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';
import { createElement } from 'react';

vi.mock('@/api/generated/security/security', () => ({
  useGetApiV1SecurityPermCodes: vi.fn(),
}));

import { useGetApiV1SecurityPermCodes } from '@/api/generated/security/security';
import { useWorkflowPermCodes } from '../use-perm-codes';

const wrapper = ({ children }: { children: ReactNode }) => {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return createElement(QueryClientProvider, { client }, children);
};

describe('useWorkflowPermCodes', () => {
  it('returns the runtime string[] directly (post-interceptor unwrap)', () => {
    vi.mocked(useGetApiV1SecurityPermCodes).mockReturnValue({
      data: ['WORKFLOW.TASK.PROCESS', 'WORKFLOW.DEF.MANAGE'],
      isLoading: false,
      isError: false,
    } as unknown as ReturnType<typeof useGetApiV1SecurityPermCodes>);

    const { result } = renderHook(() => useWorkflowPermCodes(), { wrapper });

    expect(result.current.codes).toEqual(['WORKFLOW.TASK.PROCESS', 'WORKFLOW.DEF.MANAGE']);
    expect(result.current.isLoading).toBe(false);
  });

  it('returns empty array when query.data is undefined (loading / not-yet-fetched)', () => {
    vi.mocked(useGetApiV1SecurityPermCodes).mockReturnValue({
      data: undefined,
      isLoading: true,
      isError: false,
    } as unknown as ReturnType<typeof useGetApiV1SecurityPermCodes>);

    const { result } = renderHook(() => useWorkflowPermCodes(), { wrapper });

    expect(result.current.codes).toEqual([]);
    expect(result.current.isLoading).toBe(true);
  });

  it('returns empty array on error state (UI can render the ByPermission Select empty)', () => {
    vi.mocked(useGetApiV1SecurityPermCodes).mockReturnValue({
      data: undefined,
      isLoading: false,
      isError: true,
    } as unknown as ReturnType<typeof useGetApiV1SecurityPermCodes>);

    const { result } = renderHook(() => useWorkflowPermCodes(), { wrapper });

    expect(result.current.codes).toEqual([]);
    expect(result.current.isError).toBe(true);
  });

  // Regression guard for the runtime-vs-type gap. If someone naively types the
  // orval call as the envelope shape and accesses `query.data?.data`, this test
  // STILL passes IF the mock returns the envelope — i.e., a false-green. So the
  // canonical mock here mirrors what the axios interceptor actually emits.
  it('matches the post-interceptor envelope-stripped runtime contract', () => {
    const runtimeShape = ['WORKFLOW.A', 'WORKFLOW.B'];
    vi.mocked(useGetApiV1SecurityPermCodes).mockReturnValue({
      data: runtimeShape,
      isLoading: false,
      isError: false,
    } as unknown as ReturnType<typeof useGetApiV1SecurityPermCodes>);

    const { result } = renderHook(() => useWorkflowPermCodes(), { wrapper });

    expect(result.current.codes).toBe(runtimeShape);
  });
});
