import { describe, it, expect, vi, beforeEach, afterEach, afterAll } from 'vitest';
import { AxiosError, type AxiosResponse } from 'axios';
import { onlineManager } from '@tanstack/react-query';

// v2.133p17 / POLL-FU1: Axios circuit breaker — sau ≥3 network failures
// (status=0) trong 10s window, pause new requests 30s + dedupe toast 1 lần.
// Auto-reset on successful response.

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

// Notification bridge mock — capture toast calls
const notificationError = vi.fn();
vi.mock('@/core/api/notification-bridge', () => ({
  getNotificationInstance: () => ({
    error: notificationError,
    success: vi.fn(),
    info: vi.fn(),
    warning: vi.fn(),
    open: vi.fn(),
    destroy: vi.fn(),
  }),
  setNotificationInstance: vi.fn(),
}));

import {
  apiClient,
  __resetBackendBreakerForTests,
  __getBackendBreakerState,
} from '@/core/api/api-client';

// Build an adapter that throws AxiosError with no response (network fail) for N calls,
// then optionally returns 200 OK for subsequent calls.
function networkFailAdapter(failCount: number, thenSuccess: boolean = false) {
  let i = 0;
  apiClient.defaults.adapter = vi.fn(async (config) => {
    const isNetworkFail = i++ < failCount;
    if (isNetworkFail) {
      // status=0 / no response = network failure (timeout, ECONNREFUSED, DNS)
      throw new AxiosError(
        'Network Error',
        AxiosError.ERR_NETWORK,
        config,
        {},
        undefined, // no response → status will be 0
      );
    }
    if (!thenSuccess) {
      throw new AxiosError('Network Error', AxiosError.ERR_NETWORK, config, {}, undefined);
    }
    return {
      status: 200,
      statusText: 'OK',
      data: { ok: true },
      headers: {},
      config,
      request: {},
    } as AxiosResponse;
  });
  return apiClient.defaults.adapter as ReturnType<typeof vi.fn>;
}

describe('apiClient — backend circuit breaker (POLL-FU1)', () => {
  const originalAdapter = apiClient.defaults.adapter;

  beforeEach(() => {
    __resetBackendBreakerForTests();
    notificationError.mockReset();
    vi.useRealTimers();
  });

  afterEach(() => {
    apiClient.defaults.adapter = originalAdapter;
    vi.useRealTimers();
  });

  it('T1: <3 network fails trong 10s — breaker stays closed', async () => {
    networkFailAdapter(2, false);

    await expect(apiClient.get('/probe')).rejects.toBeDefined();
    await expect(apiClient.get('/probe')).rejects.toBeDefined();

    const state = __getBackendBreakerState();
    expect(state.failCount).toBe(2);
    expect(state.pauseUntil).toBe(0);
  });

  it('T2: 3 network fails trong 10s — breaker trips, next request rejected with BACKEND_UNAVAILABLE', async () => {
    networkFailAdapter(3, false);

    await expect(apiClient.get('/probe')).rejects.toBeDefined();
    await expect(apiClient.get('/probe')).rejects.toBeDefined();
    await expect(apiClient.get('/probe')).rejects.toBeDefined();

    const state = __getBackendBreakerState();
    expect(state.pauseUntil).toBeGreaterThan(Date.now());
    expect(state.pauseUntil - Date.now()).toBeLessThanOrEqual(30_000);

    // Next request short-circuits at request-interceptor — adapter NOT called
    const adapterCallsBefore = (apiClient.defaults.adapter as ReturnType<typeof vi.fn>).mock.calls.length;
    await expect(apiClient.get('/anything')).rejects.toMatchObject({
      status: 0,
      code: 'BACKEND_UNAVAILABLE',
    });
    const adapterCallsAfter = (apiClient.defaults.adapter as ReturnType<typeof vi.fn>).mock.calls.length;
    expect(adapterCallsAfter).toBe(adapterCallsBefore);
  });

  it('T3: 3 fails → wait 30.1s → request proceeds normally', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    vi.setSystemTime(0);

    networkFailAdapter(3, true);

    // Trip the breaker
    await expect(apiClient.get('/probe')).rejects.toBeDefined();
    await expect(apiClient.get('/probe')).rejects.toBeDefined();
    await expect(apiClient.get('/probe')).rejects.toBeDefined();
    expect(__getBackendBreakerState().pauseUntil).toBeGreaterThan(0);

    // Jump past pause window (30s + 100ms)
    vi.setSystemTime(30_100);

    // Should proceed — adapter returns 200 next call
    const res = await apiClient.get('/probe');
    expect(res.data).toEqual({ ok: true });
  });

  it('T4: 2 fails → success → 2 fails — counter reset, no trip', async () => {
    let i = 0;
    apiClient.defaults.adapter = vi.fn(async (config) => {
      // call indices: 0,1 fail | 2 success | 3,4 fail
      if (i === 2) {
        i++;
        return {
          status: 200,
          statusText: 'OK',
          data: { ok: true },
          headers: {},
          config,
          request: {},
        } as AxiosResponse;
      }
      i++;
      throw new AxiosError('Network Error', AxiosError.ERR_NETWORK, config, {}, undefined);
    });

    await expect(apiClient.get('/probe')).rejects.toBeDefined();
    await expect(apiClient.get('/probe')).rejects.toBeDefined();
    expect(__getBackendBreakerState().failCount).toBe(2);

    // success → counter resets
    await apiClient.get('/probe');
    expect(__getBackendBreakerState().failCount).toBe(0);

    await expect(apiClient.get('/probe')).rejects.toBeDefined();
    await expect(apiClient.get('/probe')).rejects.toBeDefined();
    const state = __getBackendBreakerState();
    expect(state.failCount).toBe(2);
    expect(state.pauseUntil).toBe(0);
  });

  it('T5: 3 fails spread across 11s — sliding window prunes, no trip', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    vi.setSystemTime(0);

    networkFailAdapter(10, false);

    await expect(apiClient.get('/probe')).rejects.toBeDefined();
    expect(__getBackendBreakerState().failCount).toBe(1);

    vi.setSystemTime(6_000);
    await expect(apiClient.get('/probe')).rejects.toBeDefined();
    expect(__getBackendBreakerState().failCount).toBe(2);

    // 11s after first fail — first fail outside 10s window → pruned to {6s}, then push 11s → 2 entries
    vi.setSystemTime(11_000);
    await expect(apiClient.get('/probe')).rejects.toBeDefined();
    const state = __getBackendBreakerState();
    expect(state.failCount).toBe(2);
    expect(state.pauseUntil).toBe(0);
  });

  it('T7: breaker trip → onlineManager offline (suspends TanStack polling); reset → online', async () => {
    networkFailAdapter(3, false);

    expect(onlineManager.isOnline()).toBe(true);

    await expect(apiClient.get('/probe')).rejects.toBeDefined();
    await expect(apiClient.get('/probe')).rejects.toBeDefined();
    await expect(apiClient.get('/probe')).rejects.toBeDefined();

    expect(onlineManager.isOnline()).toBe(false);

    __resetBackendBreakerForTests();
    expect(onlineManager.isOnline()).toBe(true);
  });

  it('T6: breaker trip — toast called once with key=backend-unavailable', async () => {
    networkFailAdapter(3, false);

    await expect(apiClient.get('/probe')).rejects.toBeDefined();
    await expect(apiClient.get('/probe')).rejects.toBeDefined();
    await expect(apiClient.get('/probe')).rejects.toBeDefined();

    expect(notificationError).toHaveBeenCalledTimes(1);
    expect(notificationError).toHaveBeenCalledWith(
      expect.objectContaining({ key: 'backend-unavailable' }),
    );

    // Subsequent short-circuited requests do NOT fire additional toasts
    await expect(apiClient.get('/probe')).rejects.toBeDefined();
    expect(notificationError).toHaveBeenCalledTimes(1);
  });
});
