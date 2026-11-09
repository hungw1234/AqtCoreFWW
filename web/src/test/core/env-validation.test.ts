import { describe, it, expect, vi, beforeEach } from 'vitest';

// env-validation uses import.meta.env — stub it via vi.stubEnv per vitest docs
describe('validateEnv — required var enforcement', () => {
  beforeEach(() => {
    vi.unstubAllEnvs();
  });

  it('does not throw when all vars have defaults (none are required)', async () => {
    // Per source: all ENV_VARS have required:false — so validateEnv never throws
    const { validateEnv } = await import('@/core/env-validation');
    expect(() => validateEnv()).not.toThrow();
  });

  it('does not throw when VITE_API_BASE_URL is absent (optional)', async () => {
    vi.stubEnv('VITE_API_BASE_URL', '');
    const { validateEnv } = await import('@/core/env-validation');
    expect(() => validateEnv()).not.toThrow();
  });

  it('does not throw when VITE_OIDC_AUTHORITY is set', async () => {
    vi.stubEnv('VITE_OIDC_AUTHORITY', 'http://auth.example.com');
    const { validateEnv } = await import('@/core/env-validation');
    expect(() => validateEnv()).not.toThrow();
  });

  it('exports validateEnv as a function', async () => {
    const mod = await import('@/core/env-validation');
    expect(typeof mod.validateEnv).toBe('function');
  });
});
