// TC-FE-API-MFA-001: 403 + X-Mfa-Required: true → navigate /account/mfa?reason=mfa-required
//
// Pairs with BE MfaForbidResultHandler that tags 403 responses produced by a
// failing [RequiresMfa] policy. Verifies FE redirect behavior and isolation
// from existing X-Must-Change-Password / X-Password-Expired branches.

import { describe, it, expect, vi, beforeEach, afterAll, afterEach } from 'vitest';
import { AxiosError, type AxiosResponse } from 'axios';

vi.hoisted(() => {
  vi.stubEnv('VITE_API_BASE_URL', '');
});

afterAll(() => {
  vi.unstubAllEnvs();
});

vi.mock('oidc-client-ts', () => {
  class MockUserManager {
    getUser = vi.fn().mockResolvedValue(null);
    signinRedirect = vi.fn().mockResolvedValue(undefined);
    signoutRedirect = vi.fn().mockResolvedValue(undefined);
    signinSilent = vi.fn().mockResolvedValue(undefined);
    removeUser = vi.fn().mockResolvedValue(undefined);
    events = {
      addUserLoaded: vi.fn(),
      addUserUnloaded: vi.fn(),
      addSilentRenewError: vi.fn(),
      removeUserLoaded: vi.fn(),
      removeUserUnloaded: vi.fn(),
      removeSilentRenewError: vi.fn(),
    };
  }
  return { UserManager: MockUserManager, WebStorageStateStore: class {} };
});

vi.mock('@/features/auth/session-error-handler', () => ({
  triggerSessionError: vi.fn(),
  setSessionErrorHandler: vi.fn(),
}));

vi.mock('@/core/api/notification-bridge', () => ({
  getNotificationInstance: () => ({
    error: vi.fn(),
    success: vi.fn(),
    info: vi.fn(),
    warning: vi.fn(),
    open: vi.fn(),
    destroy: vi.fn(),
  }),
  setNotificationInstance: vi.fn(),
}));

import { apiClient } from '@/core/api/api-client';

function make403Adapter(headers: Record<string, string>) {
  apiClient.defaults.adapter = vi.fn(async (config) => {
    const response = {
      status: 403,
      statusText: 'Forbidden',
      data: { detail: 'Forbidden' },
      headers,
      config,
      request: {},
    } as AxiosResponse;
    throw new AxiosError(
      'Request failed with status code 403',
      AxiosError.ERR_BAD_REQUEST,
      config,
      {},
      response,
    );
  });
}

describe('apiClient — TC-FE-API-MFA-001: 403 + X-Mfa-Required header', () => {
  const originalAdapter = apiClient.defaults.adapter;
  let hrefAssigned: string | null = null;
  let pathnameStub = '/admin/users';

  beforeEach(() => {
    hrefAssigned = null;
    pathnameStub = '/admin/users';
    Object.defineProperty(window, 'location', {
      value: {
        ...window.location,
        get pathname() { return pathnameStub; },
        get href() { return hrefAssigned ?? '/'; },
        set href(val: string) { hrefAssigned = val; },
      },
      writable: true,
      configurable: true,
    });
  });

  afterEach(() => {
    apiClient.defaults.adapter = originalAdapter;
  });

  it('redirects to /account/mfa?reason=mfa-required on 403 + X-Mfa-Required: true', async () => {
    make403Adapter({ 'x-mfa-required': 'true' });

    await expect(apiClient.get('/admin/users')).rejects.toBeDefined();

    expect(hrefAssigned).toBe('/account/mfa?reason=mfa-required');
  });

  it('does NOT redirect when X-Mfa-Required header is absent', async () => {
    make403Adapter({});

    await expect(apiClient.get('/admin/users')).rejects.toBeDefined();

    expect(hrefAssigned).not.toBe('/account/mfa?reason=mfa-required');
  });

  it('does NOT redirect when X-Mfa-Required is false', async () => {
    make403Adapter({ 'x-mfa-required': 'false' });

    await expect(apiClient.get('/admin/users')).rejects.toBeDefined();

    expect(hrefAssigned).not.toBe('/account/mfa?reason=mfa-required');
  });

  // Prevents redirect loop: a 403 returned WHILE the user is already on the
  // enrolment page (e.g. a stale background poll) must not bounce them back.
  it('does NOT redirect when already on /account/mfa', async () => {
    pathnameStub = '/account/mfa';
    make403Adapter({ 'x-mfa-required': 'true' });

    await expect(apiClient.get('/account/mfa/state')).rejects.toBeDefined();

    expect(hrefAssigned).toBeNull();
  });

  it('X-Password-Expired takes precedence over X-Mfa-Required', async () => {
    make403Adapter({ 'x-password-expired': 'true', 'x-mfa-required': 'true' });

    await expect(apiClient.get('/admin/users')).rejects.toBeDefined();

    expect(hrefAssigned).toBe('/profile?passwordExpired=true');
  });

  it('X-Must-Change-Password takes precedence over X-Mfa-Required', async () => {
    make403Adapter({ 'x-must-change-password': 'true', 'x-mfa-required': 'true' });

    await expect(apiClient.get('/admin/users')).rejects.toBeDefined();

    expect(hrefAssigned).toBe('/change-password?reason=must-change');
  });

  // Background/cosmetic lookups (e.g. NextHandlerCard label resolution) opt out of the
  // interstitials: even a genuine X-Mfa-Required 403 must not hijack navigation — the
  // caller degrades to raw identifiers on the rejected promise.
  it('does NOT redirect on X-Mfa-Required when suppressAuthInterstitial is set', async () => {
    make403Adapter({ 'x-mfa-required': 'true' });

    await expect(
      apiClient.get('/admin/org/units', { suppressAuthInterstitial: true }),
    ).rejects.toBeDefined();

    expect(hrefAssigned).toBeNull();
  });

  it('still redirects when suppressAuthInterstitial is NOT set (control)', async () => {
    make403Adapter({ 'x-mfa-required': 'true' });

    await expect(apiClient.get('/admin/org/units')).rejects.toBeDefined();

    expect(hrefAssigned).toBe('/account/mfa?reason=mfa-required');
  });
});
