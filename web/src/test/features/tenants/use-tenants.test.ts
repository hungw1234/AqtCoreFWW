import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { type ReactNode, createElement } from 'react';
import {
  useTenants,
  useCreateTenant,
  useUpdateTenant,
  useArchiveTenant,
  tenantQueryKeys,
} from '@/features/tenants/tenant-api';

// Mock apiClient — captured per-call for assertion of HTTP method/URL/body.
const mockGet = vi.fn();
const mockPost = vi.fn();
const mockPut = vi.fn();
const mockDelete = vi.fn();

vi.mock('@/core/api', async () => {
  const actual = await vi.importActual<typeof import('@/core/api')>('@/core/api');
  return {
    ...actual,
    apiClient: {
      get: (...args: unknown[]) => mockGet(...args),
      post: (...args: unknown[]) => mockPost(...args),
      put: (...args: unknown[]) => mockPut(...args),
      delete: (...args: unknown[]) => mockDelete(...args),
    },
  };
});

function makeWrapper() {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  const wrapper = ({ children }: { children: ReactNode }) =>
    createElement(QueryClientProvider, { client }, children);
  return { client, wrapper };
}

describe('tenant-api hooks', () => {
  beforeEach(() => {
    mockGet.mockReset();
    mockPost.mockReset();
    mockPut.mockReset();
    mockDelete.mockReset();
  });

  afterEach(() => {
    vi.clearAllMocks();
  });

  it('tenantQueryKeys.all root is "tenants" — invalidation predicate excludes auth/permissions', () => {
    expect(tenantQueryKeys.all[0]).toBe('tenants');
  });

  it('useTenants calls GET /admin/tenants with params', async () => {
    mockGet.mockResolvedValue({
      data: { items: [], totalCount: 0, meta: { page: 1, pageSize: 20, totalPages: 0, nextCursor: null, prevCursor: null, hasNextPage: false } },
    });
    const { wrapper } = makeWrapper();

    const { result } = renderHook(
      () => useTenants({ search: 'abc', status: 'Active', page: 2, pageSize: 50 }),
      { wrapper },
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(mockGet).toHaveBeenCalledWith('/admin/tenants', {
      params: { search: 'abc', status: 'Active', page: 2, pageSize: 50 },
    });
  });

  it('useCreateTenant POSTs to /admin/tenants and invalidates ["tenants"]', async () => {
    mockPost.mockResolvedValue({ data: { id: '00000000-0000-0000-0000-000000000001' } });
    const { client, wrapper } = makeWrapper();
    const invalidateSpy = vi.spyOn(client, 'invalidateQueries');

    const { result } = renderHook(() => useCreateTenant(), { wrapper });

    await result.current.mutateAsync({
      name: 'Test',
      code: 'TST-001',
      contactEmail: 'a@b.vn',
    });

    expect(mockPost).toHaveBeenCalledWith('/admin/tenants', {
      name: 'Test',
      code: 'TST-001',
      contactEmail: 'a@b.vn',
    });
    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: tenantQueryKeys.all });
  });

  it('useUpdateTenant PUTs to /admin/tenants/{id} (no Code field — immutable)', async () => {
    mockPut.mockResolvedValue({ data: undefined });
    const { wrapper } = makeWrapper();

    const { result } = renderHook(() => useUpdateTenant('abc-123'), { wrapper });
    await result.current.mutateAsync({ name: 'New', contactEmail: null });

    expect(mockPut).toHaveBeenCalledWith('/admin/tenants/abc-123', {
      name: 'New',
      contactEmail: null,
    });
  });

  it('useArchiveTenant DELETEs to /admin/tenants/{id} (soft-delete)', async () => {
    mockDelete.mockResolvedValue({ data: undefined });
    const { client, wrapper } = makeWrapper();
    const invalidateSpy = vi.spyOn(client, 'invalidateQueries');

    const { result } = renderHook(() => useArchiveTenant(), { wrapper });
    await result.current.mutateAsync('xyz-789');

    expect(mockDelete).toHaveBeenCalledWith('/admin/tenants/xyz-789');
    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: tenantQueryKeys.all });
  });
});
