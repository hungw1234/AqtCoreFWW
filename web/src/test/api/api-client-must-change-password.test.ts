// TC-FE-API-MCP-001: 403 + X-Must-Change-Password: true → navigate /change-password?reason=must-change

import { describe, it, expect, vi, beforeEach, afterAll, afterEach } from 'vitest';
import { AxiosError, type AxiosResponse } from 'axios';

// Stub env before api-client is evaluated
vi.hoisted(() => {
  vi.stubEnv('VITE_API_BASE_URL', '');
});

afterAll(() => {
  vi.unstubAllEnvs();
});

// Mock oidc UserManager — request interceptor calls getUser
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

// ─── Helpers ─────────────────────────────────────────────────────────────────

/** Build an adapter that returns a single 403 response with custom headers. */
function make403Adapter(headers: Record<string, string>) {
  const originalAdapter = apiClient.defaults.adapter;
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
  return originalAdapter;
}

// ─── TC-FE-API-MCP-001 ───────────────────────────────────────────────────────

describe('apiClient — TC-FE-API-MCP-001: 403 + X-Must-Change-Password header', () => {
  const originalAdapter = apiClient.defaults.adapter;
  // Capture window.location.href assignments
  let hrefAssigned: string | null = null;

  beforeEach(() => {
    hrefAssigned = null;
    // jsdom does not allow direct assignment to window.location.href in strict mode —
    // use Object.defineProperty to intercept the assignment.
    Object.defineProperty(window, 'location', {
      value: {
        ...window.location,
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

  it('sets window.location.href to /change-password?reason=must-change on 403 + X-Must-Change-Password: true', async () => {
    make403Adapter({ 'x-must-change-password': 'true' });

    // The error interceptor runs synchronously on the rejected promise.
    // We don't await a successful response — just wait for the interceptor to fire.
    await expect(apiClient.get('/probe')).rejects.toBeDefined();

    expect(hrefAssigned).toBe('/change-password?reason=must-change');
  });

  it('does NOT redirect to change-password when X-Must-Change-Password header is absent', async () => {
    make403Adapter({}); // plain 403, no header

    await expect(apiClient.get('/probe')).rejects.toBeDefined();

    expect(hrefAssigned).not.toBe('/change-password?reason=must-change');
  });

  it('does NOT redirect to change-password when X-Must-Change-Password is false', async () => {
    make403Adapter({ 'x-must-change-password': 'false' });

    await expect(apiClient.get('/probe')).rejects.toBeDefined();

    expect(hrefAssigned).not.toBe('/change-password?reason=must-change');
  });

  it('still handles X-Password-Expired independently (no regression)', async () => {
    make403Adapter({ 'x-password-expired': 'true' });

    await expect(apiClient.get('/probe')).rejects.toBeDefined();

    expect(hrefAssigned).toBe('/profile?passwordExpired=true');
  });
});
