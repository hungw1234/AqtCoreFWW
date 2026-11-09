import { describe, it, expect } from 'vitest';
import {
  shouldInvalidateOnTenantSwitch,
  PRESERVED_KEY_PREFIXES,
} from '@/core/api/query-config';

// Pure-function unit tests for the tenant-switch predicate.
// The predicate decides which TanStack Query cache entries must refetch when
// SystemAdmin changes the X-Tenant-Id override.

describe('shouldInvalidateOnTenantSwitch — preserves auth/identity caches, invalidates everything else', () => {
  function q(...key: unknown[]) {
    return { queryKey: key } as { queryKey: readonly unknown[] };
  }

  it.each(PRESERVED_KEY_PREFIXES)('preserves "%s" prefix (no invalidate)', (root) => {
    expect(shouldInvalidateOnTenantSwitch(q(root))).toBe(false);
    expect(shouldInvalidateOnTenantSwitch(q(root, 'sub'))).toBe(false);
  });

  it('invalidates tenant-scoped feature queries', () => {
    expect(shouldInvalidateOnTenantSwitch(q('users', 'list'))).toBe(true);
    expect(shouldInvalidateOnTenantSwitch(q('workflow', 'definitions'))).toBe(true);
    expect(shouldInvalidateOnTenantSwitch(q('forms', 'templates'))).toBe(true);
    expect(shouldInvalidateOnTenantSwitch(q('sessions'))).toBe(true); // plural — admin sessions feature
    expect(shouldInvalidateOnTenantSwitch(q('notifications'))).toBe(true);
    expect(shouldInvalidateOnTenantSwitch(q('admin-dashboard', 'health'))).toBe(true);
  });

  it('invalidates non-string root keys (defensive default)', () => {
    expect(shouldInvalidateOnTenantSwitch(q(123))).toBe(true);
    expect(shouldInvalidateOnTenantSwitch(q({ scope: 'x' }))).toBe(true);
  });

  it('exact-match only — does not preserve substrings', () => {
    // 'session' is preserved but 'sessions' (plural admin feature) MUST invalidate.
    expect(shouldInvalidateOnTenantSwitch(q('sessions'))).toBe(true);
    // 'auth-config' is NOT 'auth' — would invalidate if anyone used such a key.
    expect(shouldInvalidateOnTenantSwitch(q('auth-config'))).toBe(true);
  });
});
