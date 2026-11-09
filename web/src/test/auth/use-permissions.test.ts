import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderHook } from '@testing-library/react';
import { type ReactNode, createElement } from 'react';
import { AuthContext } from '@/features/auth/auth-provider';
import type { AuthContextValue } from '@/features/auth/auth-provider';
import { usePermissions } from '@/features/auth/use-permissions';

// usePermissions reads from /api/v1/me/permissions via React Query (not JWT claims).
// Mock the hook to control what data the implementation sees per test.
vi.mock('@/features/auth/me-permissions-api', () => ({
  useMyEffectivePermissions: vi.fn(),
}));
import { useMyEffectivePermissions } from '@/features/auth/me-permissions-api';
const useMyEffectivePermissionsMock = useMyEffectivePermissions as unknown as ReturnType<typeof vi.fn>;

function makeUser(profile: Record<string, unknown>) {
  return {
    profile,
    access_token: 'tok',
    expired: false,
  } as unknown as import('oidc-client-ts').User;
}

function makeWrapper(ctxValue: AuthContextValue) {
  return ({ children }: { children: ReactNode }) =>
    createElement(AuthContext.Provider, { value: ctxValue }, children);
}

function makeCtx(user: import('oidc-client-ts').User | null): AuthContextValue {
  return {
    user,
    isAuthenticated: !!user,
    isLoading: false,
    login: async () => {},
    logout: async () => {},
  };
}

// Build the React Query result shape returned by useMyEffectivePermissions
function mockApi(roles: string[], permissionCodes: string[]) {
  useMyEffectivePermissionsMock.mockReturnValue({
    data: { userId: 'u-1', roles, permissionCodes },
    isLoading: false,
    isFetching: false,
  });
}

function mockApiEmpty() {
  useMyEffectivePermissionsMock.mockReturnValue({
    data: undefined,
    isLoading: false,
    isFetching: false,
  });
}

beforeEach(() => {
  useMyEffectivePermissionsMock.mockReset();
});

describe('usePermissions — permission parsing', () => {
  it('returns empty permissions when user is null', () => {
    mockApiEmpty();
    const { result } = renderHook(() => usePermissions(), {
      wrapper: makeWrapper(makeCtx(null)),
    });
    expect(result.current.permissions).toEqual([]);
    expect(result.current.hasPermission('any')).toBe(false);
  });

  it('exposes a single permission code from API', () => {
    mockApi([], ['users.read']);
    const { result } = renderHook(() => usePermissions(), {
      wrapper: makeWrapper(makeCtx(makeUser({}))),
    });
    expect(result.current.permissions).toEqual(['users.read']);
    expect(result.current.hasPermission('users.read')).toBe(true);
    expect(result.current.hasPermission('audit.read')).toBe(false);
  });

  it('exposes multiple permission codes from API', () => {
    mockApi([], ['users.read', 'audit.read', 'cases.write']);
    const { result } = renderHook(() => usePermissions(), {
      wrapper: makeWrapper(makeCtx(makeUser({}))),
    });
    expect(result.current.permissions).toHaveLength(3);
    expect(result.current.hasPermission('audit.read')).toBe(true);
    expect(result.current.hasPermission('admin.delete')).toBe(false);
  });

  it('returns empty permissions when API has no data', () => {
    mockApiEmpty();
    const { result } = renderHook(() => usePermissions(), {
      wrapper: makeWrapper(makeCtx(makeUser({ sub: 'user-123', email: 'test@gov.vn' }))),
    });
    expect(result.current.permissions).toEqual([]);
  });
});

describe('usePermissions — role parsing', () => {
  it('returns empty roles when API has no data and JWT has no role claim', () => {
    mockApiEmpty();
    const { result } = renderHook(() => usePermissions(), {
      wrapper: makeWrapper(makeCtx(makeUser({}))),
    });
    expect(result.current.roles).toEqual([]);
    expect(result.current.hasRole('Admin')).toBe(false);
  });

  it('falls back to JWT role claim while API not yet resolved', () => {
    mockApiEmpty();
    const user = makeUser({ role: 'Admin' });
    const { result } = renderHook(() => usePermissions(), {
      wrapper: makeWrapper(makeCtx(user)),
    });
    expect(result.current.roles).toEqual(['Admin']);
    expect(result.current.hasRole('Admin')).toBe(true);
  });

  it('parses an array of roles from JWT fallback', () => {
    mockApiEmpty();
    const user = makeUser({ role: ['Admin', 'Supervisor'] });
    const { result } = renderHook(() => usePermissions(), {
      wrapper: makeWrapper(makeCtx(user)),
    });
    expect(result.current.roles).toHaveLength(2);
    expect(result.current.hasRole('Supervisor')).toBe(true);
    expect(result.current.hasRole('Unknown')).toBe(false);
  });

  it('prefers API roles over JWT fallback when API resolves', () => {
    mockApi(['SystemAdmin', 'GovOfficer'], []);
    const user = makeUser({ role: 'Admin' });
    const { result } = renderHook(() => usePermissions(), {
      wrapper: makeWrapper(makeCtx(user)),
    });
    expect(result.current.roles).toEqual(['SystemAdmin', 'GovOfficer']);
    expect(result.current.hasRole('Admin')).toBe(false);
    expect(result.current.hasRole('SystemAdmin')).toBe(true);
  });
});

describe('usePermissions — loading state', () => {
  it('exposes isPermissionsLoading=true while API is fetching for authenticated user', () => {
    useMyEffectivePermissionsMock.mockReturnValue({
      data: undefined,
      isLoading: true,
      isFetching: true,
    });
    const { result } = renderHook(() => usePermissions(), {
      wrapper: makeWrapper(makeCtx(makeUser({}))),
    });
    expect(result.current.isPermissionsLoading).toBe(true);
  });

  it('isPermissionsLoading=false when not authenticated', () => {
    useMyEffectivePermissionsMock.mockReturnValue({
      data: undefined,
      isLoading: true,
      isFetching: true,
    });
    const { result } = renderHook(() => usePermissions(), {
      wrapper: makeWrapper(makeCtx(null)),
    });
    expect(result.current.isPermissionsLoading).toBe(false);
  });
});
