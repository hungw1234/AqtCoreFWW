import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, waitFor, fireEvent } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { AdminTenantSelector } from '@/shared/components/admin-tenant-selector';
import { shouldInvalidateOnTenantSwitch } from '@/core/api';

// Mock usePermissions to control the SystemAdmin gate. The component gates on
// hasPermission('SYSTEM.CONFIG.MANAGE') (useIsSystemAdmin), NOT hasRole — so the
// stub must grant that permission or the component renders null and finds nothing.
vi.mock('@/features/auth/use-permissions', () => ({
  usePermissions: () => ({
    permissions: ['SYSTEM.CONFIG.MANAGE'],
    roles: ['SystemAdmin'],
    hasPermission: (p: string) => p === 'SYSTEM.CONFIG.MANAGE',
    hasRole: (r: string) => r === 'SystemAdmin',
  }),
}));

// Mock apiClient.get so fetchTenants resolves synchronously without network.
vi.mock('@/core/api', async () => {
  const actual = await vi.importActual<typeof import('@/core/api')>('@/core/api');
  return {
    ...actual,
    apiClient: {
      get: vi.fn().mockResolvedValue({
        data: [
          { tenantId: '11111111-1111-1111-1111-111111111111', tenantName: 'Tenant A', userCount: 3 },
          { tenantId: '22222222-2222-2222-2222-222222222222', tenantName: 'Tenant B', userCount: 5 },
        ],
      }),
    },
  };
});

function makeClient() {
  return new QueryClient({ defaultOptions: { queries: { retry: false } } });
}

function renderWithClient(client: QueryClient) {
  return render(
    <QueryClientProvider client={client}>
      <AdminTenantSelector />
    </QueryClientProvider>,
  );
}

describe('AdminTenantSelector — tenant switch reloads tenant-scoped caches', () => {
  beforeEach(() => {
    sessionStorage.clear();
  });
  afterEach(() => {
    vi.clearAllMocks();
  });

  it('handleClear cancels in-flight queries BEFORE invalidating (race-fix order)', async () => {
    sessionStorage.setItem('admin-tenant-override', 'X');
    const client = makeClient();
    const callOrder: string[] = [];
    const cancelSpy = vi.spyOn(client, 'cancelQueries').mockImplementation(async () => {
      callOrder.push('cancel');
    });
    const invalidateSpy = vi.spyOn(client, 'invalidateQueries').mockImplementation(async () => {
      callOrder.push('invalidate');
    });

    renderWithClient(client);
    // The active-tenant tag renders with a closable "X" — fire its close handler.
    const closeIcon = await screen.findByLabelText(/close/i);
    fireEvent.click(closeIcon);

    await waitFor(() => {
      expect(cancelSpy).toHaveBeenCalled();
      expect(invalidateSpy).toHaveBeenCalled();
    });

    expect(callOrder).toEqual(['cancel', 'invalidate']);
    expect(sessionStorage.getItem('admin-tenant-override')).toBeNull();

    // Predicate must be present + invalidate must use refetchType:'active'.
    const cancelArg = cancelSpy.mock.calls[0]?.[0] as { predicate?: unknown } | undefined;
    const invalidateArg = invalidateSpy.mock.calls[0]?.[0] as
      | { predicate?: unknown; refetchType?: string }
      | undefined;
    expect(typeof cancelArg?.predicate).toBe('function');
    expect(typeof invalidateArg?.predicate).toBe('function');
    expect(invalidateArg?.refetchType).toBe('active');
  });

  it('predicate preserves global caches but invalidates tenant-scoped + feature caches', () => {
    // Sanity check: same predicate the component uses.
    // Global (cross-tenant) caches survive tenant switch:
    expect(shouldInvalidateOnTenantSwitch({ queryKey: ['auth'] })).toBe(false);
    expect(shouldInvalidateOnTenantSwitch({ queryKey: ['i18n'] })).toBe(false);
    // Tenant-scoped caches must invalidate (RBAC-5 v2.133p8: `me` and `menus` roots
    // are intentionally NOT preserved — effective perms differ per tenant override).
    expect(shouldInvalidateOnTenantSwitch({ queryKey: ['me', 'permissions'] })).toBe(true);
    expect(shouldInvalidateOnTenantSwitch({ queryKey: ['menus', 'tree'] })).toBe(true);
    // Feature caches always invalidate.
    expect(shouldInvalidateOnTenantSwitch({ queryKey: ['users', 'list'] })).toBe(true);
    expect(shouldInvalidateOnTenantSwitch({ queryKey: ['workflow', 'tasks'] })).toBe(true);
  });
});
