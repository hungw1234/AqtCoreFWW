// Verifies the global QueryCache.onError toast policy:
//   - 401 is owned by the auth flow → no generic toast.
//   - queries flagged meta.suppressErrorToast (cosmetic/background lookups, e.g.
//     NextHandlerCard label resolution) → no generic toast even on 403.
//   - everything else → the generic "load failed" toast fires once.

import { describe, it, expect, vi, beforeEach } from 'vitest';
import type { Query } from '@tanstack/react-query';
import type { NormalizedApiError } from '@/core/api/api-error';

const errorSpy = vi.fn();
vi.mock('@/core/api/notification-bridge', () => ({
  getNotificationInstance: () => ({
    error: errorSpy,
    success: vi.fn(),
    info: vi.fn(),
    warning: vi.fn(),
    open: vi.fn(),
    destroy: vi.fn(),
  }),
  setNotificationInstance: vi.fn(),
}));

import { queryClient } from '@/core/api/query-config';

// The onError handler under test lives on the singleton's QueryCache config. TanStack types the
// error param as the default `Error`, but at runtime the apiClient interceptor rejects with a
// NormalizedApiError — retype to the real runtime shape so the policy assertions read its fields.
const onError = queryClient.getQueryCache().config.onError! as unknown as (
  error: NormalizedApiError,
  query: Query,
) => void;

function err(status: number): NormalizedApiError {
  return { status, message: `HTTP ${status}`, code: 'X', detail: undefined } as NormalizedApiError;
}

function queryWithMeta(meta?: Record<string, unknown>): Query {
  return { meta } as unknown as Query;
}

describe('QueryCache.onError — generic load-failed toast policy', () => {
  beforeEach(() => errorSpy.mockClear());

  it('fires the toast for an ordinary 403 query (no suppress meta)', () => {
    onError(err(403), queryWithMeta(undefined));
    expect(errorSpy).toHaveBeenCalledTimes(1);
  });

  it('does NOT fire for a 403 query flagged meta.suppressErrorToast', () => {
    onError(err(403), queryWithMeta({ suppressErrorToast: true }));
    expect(errorSpy).not.toHaveBeenCalled();
  });

  it('does NOT fire for 401 (owned by the auth flow)', () => {
    onError(err(401), queryWithMeta(undefined));
    expect(errorSpy).not.toHaveBeenCalled();
  });

  it('fires for a 500 query without suppress meta', () => {
    onError(err(500), queryWithMeta({}));
    expect(errorSpy).toHaveBeenCalledTimes(1);
  });
});
