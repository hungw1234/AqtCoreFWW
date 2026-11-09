import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';

describe('oidcConfig defaults', () => {
  // We import oidcConfig after setting up env to verify defaults
  it('uses localhost:5002 as default OIDC authority when env var not set', async () => {
    const { oidcConfig } = await import('@/features/auth/auth-config');
    // Default authority points to AuthServer HTTP port (matches IssuerUri in appsettings)
    expect(oidcConfig.authority).toMatch(/localhost:5002/);
  });

  it('has the expected client_id', async () => {
    const { oidcConfig } = await import('@/features/auth/auth-config');
    expect(oidcConfig.client_id).toBe('aqtcorefw-spa-dev');
  });

  it('uses code response_type (PKCE)', async () => {
    const { oidcConfig } = await import('@/features/auth/auth-config');
    expect(oidcConfig.response_type).toBe('code');
  });

  it('includes offline_access in scope for refresh tokens', async () => {
    const { oidcConfig } = await import('@/features/auth/auth-config');
    expect(oidcConfig.scope).toContain('offline_access');
  });

  it('enables automaticSilentRenew', async () => {
    const { oidcConfig } = await import('@/features/auth/auth-config');
    expect(oidcConfig.automaticSilentRenew).toBe(true);
  });

  it('redirect_uri includes /callback', async () => {
    const { oidcConfig } = await import('@/features/auth/auth-config');
    expect(oidcConfig.redirect_uri).toContain('/callback');
  });

  it('silent_redirect_uri includes /silent-renew.html', async () => {
    const { oidcConfig } = await import('@/features/auth/auth-config');
    expect(oidcConfig.silent_redirect_uri).toContain('/silent-renew.html');
  });
});

describe('oidcConfig env var override', () => {
  const originalEnv = { ...import.meta.env };

  beforeEach(() => {
    // Reset module cache so re-import picks up new env
    vi.resetModules();
  });

  afterEach(() => {
    // Restore original env
    Object.assign(import.meta.env, originalEnv);
  });

  it('overrides authority when VITE_OIDC_AUTHORITY is set', async () => {
    import.meta.env.VITE_OIDC_AUTHORITY = 'https://auth.gov.example.com';
    const { oidcConfig } = await import('@/features/auth/auth-config');
    expect(oidcConfig.authority).toBe('https://auth.gov.example.com');
  });
});
