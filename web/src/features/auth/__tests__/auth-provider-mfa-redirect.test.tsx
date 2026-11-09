// Phase-02 — token-driven MFA enrolment redirect (QĐ 742 1.6.b).
// Verifies AuthProvider's userLoaded handler reacts to `mfa_enrolment_required`
// claim emitted by AuthServer Phase-01 BE refactor (CreateIdentity).
//
// Decoupled from BE: synthesizes oidc-client-ts User-shaped objects; asserts
// behavior of the handler (replace target + loop guard + claim type tolerance).

import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render } from '@testing-library/react';
import type { User } from 'oidc-client-ts';

vi.mock('react-i18next', () => ({
  useTranslation: () => ({ t: (_k: string, fallback?: string) => fallback ?? _k }),
}));

const capturedHandlers = {
  onUserLoaded: null as ((u: User) => void) | null,
  onUserUnloaded: null as (() => void) | null,
  onSilentRenewError: null as ((err: Error) => void) | null,
};

vi.mock('oidc-client-ts', () => {
  class MockUserManager {
    getUser = vi.fn().mockResolvedValue(null);
    signinRedirect = vi.fn().mockResolvedValue(undefined);
    signoutRedirect = vi.fn().mockResolvedValue(undefined);
    signinSilent = vi.fn().mockResolvedValue(undefined);
    removeUser = vi.fn().mockResolvedValue(undefined);
    events = {
      addUserLoaded: vi.fn((cb: (u: User) => void) => { capturedHandlers.onUserLoaded = cb; }),
      addUserUnloaded: vi.fn((cb: () => void) => { capturedHandlers.onUserUnloaded = cb; }),
      addSilentRenewError: vi.fn((cb: (err: Error) => void) => { capturedHandlers.onSilentRenewError = cb; }),
      removeUserLoaded: vi.fn(),
      removeUserUnloaded: vi.fn(),
      removeSilentRenewError: vi.fn(),
    };
  }
  return { UserManager: MockUserManager, WebStorageStateStore: class {} };
});

vi.mock('@/features/auth/auth-config', () => ({ oidcConfig: {} }));
vi.mock('@/features/auth/session-config-api', () => ({
  fetchSessionConfig: vi.fn().mockResolvedValue({ idleTimeoutMinutes: 30, countdownSeconds: 60 }),
  clearSessionConfigCache: vi.fn(),
}));
vi.mock('@/features/auth/session-error-handler', () => ({
  triggerSessionError: vi.fn(),
  setSessionErrorHandler: vi.fn(),
}));
vi.mock('@/features/auth/use-idle-timeout', () => ({
  useIdleTimeout: () => ({ resetActivity: vi.fn() }),
}));
vi.mock('@/features/notifications/notification-signalr', () => ({
  stopSignalR: vi.fn().mockResolvedValue(undefined),
}));
vi.mock('@/core/api/notification-bridge', () => ({
  getNotificationInstance: () => ({
    error: vi.fn(), success: vi.fn(), info: vi.fn(),
    warning: vi.fn(), open: vi.fn(), destroy: vi.fn(),
  }),
}));

import { AuthProvider } from '@/features/auth/auth-provider';

const REDIRECT_TARGET = '/account/mfa?reason=mfa-enrolment-required';

function makeUser(profile: Record<string, unknown>): User {
  return {
    profile,
    expired: false,
    access_token: 'stub',
    id_token: 'stub',
  } as unknown as User;
}

describe('AuthProvider — TC-FE-AUTH-MFA-REDIRECT', () => {
  const originalLocation = window.location;
  let replaceMock: ReturnType<typeof vi.fn>;
  let pathnameStub = '/dashboard';

  beforeEach(() => {
    capturedHandlers.onUserLoaded = null;
    capturedHandlers.onUserUnloaded = null;
    capturedHandlers.onSilentRenewError = null;
    pathnameStub = '/dashboard';
    replaceMock = vi.fn();
    Object.defineProperty(window, 'location', {
      value: {
        ...originalLocation,
        get pathname() { return pathnameStub; },
        replace: replaceMock,
      },
      writable: true,
      configurable: true,
    });
    render(<AuthProvider><div /></AuthProvider>);
  });

  afterEach(() => {
    Object.defineProperty(window, 'location', {
      value: originalLocation,
      writable: true,
      configurable: true,
    });
    vi.clearAllMocks();
  });

  it('redirects when claim is JSON boolean true', () => {
    expect(capturedHandlers.onUserLoaded).not.toBeNull();
    capturedHandlers.onUserLoaded!(makeUser({ mfa_enrolment_required: true }));
    expect(replaceMock).toHaveBeenCalledWith(REDIRECT_TARGET);
  });

  it('redirects when claim is legacy string "true"', () => {
    capturedHandlers.onUserLoaded!(makeUser({ mfa_enrolment_required: 'true' }));
    expect(replaceMock).toHaveBeenCalledWith(REDIRECT_TARGET);
  });

  it('does NOT redirect when claim is absent', () => {
    capturedHandlers.onUserLoaded!(makeUser({ sub: 'user-1' }));
    expect(replaceMock).not.toHaveBeenCalled();
  });

  it('does NOT redirect when claim is boolean false', () => {
    capturedHandlers.onUserLoaded!(makeUser({ mfa_enrolment_required: false }));
    expect(replaceMock).not.toHaveBeenCalled();
  });

  it('does NOT redirect when claim is string "false"', () => {
    capturedHandlers.onUserLoaded!(makeUser({ mfa_enrolment_required: 'false' }));
    expect(replaceMock).not.toHaveBeenCalled();
  });

  it('does NOT redirect when already on /account/mfa (loop guard)', () => {
    pathnameStub = '/account/mfa';
    capturedHandlers.onUserLoaded!(makeUser({ mfa_enrolment_required: true }));
    expect(replaceMock).not.toHaveBeenCalled();
  });

  it('loop guard matches /account/mfa subpaths', () => {
    pathnameStub = '/account/mfa/setup';
    capturedHandlers.onUserLoaded!(makeUser({ mfa_enrolment_required: true }));
    expect(replaceMock).not.toHaveBeenCalled();
  });
});
