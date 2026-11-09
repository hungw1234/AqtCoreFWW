import { describe, it, expect, vi, afterAll, beforeEach } from 'vitest';
import { AxiosError, type AxiosResponse } from 'axios';
import { apiClient } from '@/core/api/api-client';
import { userManager } from '@/features/auth/auth-provider';
import { triggerSessionError } from '@/features/auth/session-error-handler';

// Stub VITE_API_BASE_URL to empty string BEFORE api-client.ts is evaluated at import
// time so that BASE_URL = '' and the computed baseURL = '/api/v1' (not the .env.local value).
vi.hoisted(() => {
  vi.stubEnv('VITE_API_BASE_URL', '');
});

afterAll(() => {
  vi.unstubAllEnvs();
});

// Shared mock refs — vi.hoisted ensures they exist before vi.mock factories run,
// letting the session-error-handler mock dispatch to the same userManager.removeUser
// fn that the oidc-client-ts mock attaches to its MockUserManager instance.
const sharedMocks = vi.hoisted(() => ({
  removeUser: vi.fn().mockResolvedValue(undefined),
  trigger: vi.fn(),
}));

// Mock oidc-client-ts UserManager before importing api-client
vi.mock('oidc-client-ts', () => {
  class MockUserManager {
    getUser = vi.fn().mockResolvedValue(null);
    signinRedirect = vi.fn().mockResolvedValue(undefined);
    signoutRedirect = vi.fn().mockResolvedValue(undefined);
    signinSilent = vi.fn().mockResolvedValue(undefined);
    removeUser = sharedMocks.removeUser;
    events = {
      addUserLoaded: vi.fn(),
      addUserUnloaded: vi.fn(),
      addSilentRenewError: vi.fn(),
      removeUserLoaded: vi.fn(),
      removeUserUnloaded: vi.fn(),
      removeSilentRenewError: vi.fn(),
    };
  }
  return {
    UserManager: MockUserManager,
    WebStorageStateStore: class {},
  };
});

// session-error-handler mock dispatches cleanupAndRedirect → same shared removeUser
// + trigger fns. Existing assertions on userManager.removeUser / triggerSessionError
// thus see both effects, matching real-helper semantics.
vi.mock('@/features/auth/session-error-handler', () => ({
  triggerSessionError: sharedMocks.trigger,
  setSessionErrorHandler: vi.fn(),
  cleanupAndRedirect: async () => {
    await sharedMocks.removeUser().catch(() => {});
    sharedMocks.trigger();
  },
}));

// We test the interceptor behaviour by exercising the apiClient directly.
// axios interceptors run in the request/response pipeline — we verify
// the normalized error shape and envelope unwrapping via mock adapter.

describe('apiClient — base config', () => {
  it('is created with baseURL /api/v1 when VITE_API_BASE_URL is unset', () => {
    expect(apiClient.defaults.baseURL).toBe('/api/v1');
  });

  it('has Content-Type: application/json header', () => {
    expect(apiClient.defaults.headers['Content-Type']).toBe('application/json');
  });

  it('has timeout of 30 000ms', () => {
    expect(apiClient.defaults.timeout).toBe(30_000);
  });
});

describe('apiClient — interceptors registration', () => {
  it('has at least one request interceptor attached', () => {
    // Axios stores interceptors internally; verify the manager is non-empty
    // @ts-expect-error accessing internal axios structure for test assertion
    expect(apiClient.interceptors.request.handlers.length).toBeGreaterThan(0);
  });

  it('has at least one response interceptor attached', () => {
    // @ts-expect-error accessing internal axios structure for test assertion
    expect(apiClient.interceptors.response.handlers.length).toBeGreaterThan(0);
  });
});

describe('apiClient — envelope unwrapping (response interceptor)', () => {
  it('unwraps { success, data } envelope and returns inner data', () => {
    // Access the response success interceptor directly
    // @ts-expect-error accessing internal handlers
    const successHandler = apiClient.interceptors.response.handlers[0].fulfilled;
    // Cast to AxiosResponse — we're testing interceptor internals directly; fields beyond 'data' are not exercised here
    const fakeResponse = {
      data: { success: true, data: { id: '123', name: 'Test' } },
    } as AxiosResponse;
    const result = successHandler(fakeResponse) as AxiosResponse;
    expect(result.data).toEqual({ id: '123', name: 'Test' });
  });

  it('passes through response when no envelope detected', () => {
    // @ts-expect-error accessing internal handlers
    const successHandler = apiClient.interceptors.response.handlers[0].fulfilled;
    // Cast to AxiosResponse — testing interceptor pass-through with minimal fixture
    const fakeResponse = { data: [1, 2, 3] } as AxiosResponse;
    const result = successHandler(fakeResponse) as AxiosResponse;
    expect(result.data).toEqual([1, 2, 3]);
  });
});

describe('normalizeError integration via api-client error path', () => {
  it('rejects with normalized AppError shape on HTTP error', async () => {
    // @ts-expect-error accessing internal handlers
    const errorHandler = apiClient.interceptors.response.handlers[0].rejected!;

    const axiosError = {
      isAxiosError: true,
      response: {
        status: 403,
        data: { detail_vi: 'Bạn không có quyền thực hiện thao tác này.' },
      },
    };

    await expect(errorHandler(axiosError)).rejects.toMatchObject({
      status: 403,
      message: 'Bạn không có quyền thực hiện thao tác này.',
    });
  });

  it('rejects with 500 normalized error on empty body', async () => {
    // @ts-expect-error accessing internal handlers
    const errorHandler = apiClient.interceptors.response.handlers[0].rejected!;

    const axiosError = {
      isAxiosError: true,
      response: { status: 500, data: null },
    };

    await expect(errorHandler(axiosError)).rejects.toMatchObject({
      status: 500,
    });
  });

  // jsdom's Blob lacks .text() (present in all real browsers + the axios runtime). Supply it so the
  // interceptor's blob-read path is exercised; instanceof Blob still holds for the branch guard.
  function browserLikeBlob(text: string, type: string): Blob {
    const b = new Blob([text], { type });
    (b as Blob & { text: () => Promise<string> }).text = () => Promise.resolve(text);
    return b;
  }

  it('reads a Blob error body (responseType:blob downloads) so typed BE codes survive', async () => {
    // @ts-expect-error accessing internal handlers
    const errorHandler = apiClient.interceptors.response.handlers[0].rejected!;

    // responseType:'blob' delivers a 4xx body as a Blob. Without the Blob→JSON read,
    // normalizeError sees an opaque object → code defaults to String(status) ('400').
    const blobBody = browserLikeBlob(
      JSON.stringify({ code: 'SIGNING_PADES_PDF_NOT_READY', message: 'PDF chưa sẵn sàng.' }),
      'application/json',
    );
    const axiosError = {
      isAxiosError: true,
      response: { status: 400, data: blobBody },
    };

    await expect(errorHandler(axiosError)).rejects.toMatchObject({
      status: 400,
      code: 'SIGNING_PADES_PDF_NOT_READY',
      message: 'PDF chưa sẵn sàng.',
    });
  });

  it('falls back to the generic message when a Blob error body is not JSON', async () => {
    // @ts-expect-error accessing internal handlers
    const errorHandler = apiClient.interceptors.response.handlers[0].rejected!;

    const pdfBlob = browserLikeBlob('%PDF-1.7 not-error-json', 'application/pdf');
    const axiosError = {
      isAxiosError: true,
      response: { status: 400, data: pdfBlob },
    };

    await expect(errorHandler(axiosError)).rejects.toMatchObject({
      status: 400,
      code: '400',
    });
  });
});

// ── 401 silent-renew retry behaviour (Phase B) ──────────────────────────────
// Strategy: replace apiClient.defaults.adapter with a vi.fn that returns
// queued responses. axios will call adapter → if status !== 2xx, axios throws
// AxiosError → error interceptor fires → exercises 401 retry branch.
describe('apiClient — 401 silent-renew retry', () => {
  // Hold reference to mocked methods (typed via vi.mocked for IDE support)
  const mockSigninSilent = userManager.signinSilent as ReturnType<typeof vi.fn>;
  const mockGetUser = userManager.getUser as ReturnType<typeof vi.fn>;
  const mockTrigger = triggerSessionError as ReturnType<typeof vi.fn>;
  const originalAdapter = apiClient.defaults.adapter;

  beforeEach(() => {
    mockSigninSilent.mockReset();
    mockGetUser.mockReset();
    mockTrigger.mockReset();
    // Safe default: getUser must always return a Promise so request interceptor's
    // `.catch(() => null)` chain doesn't throw on undefined. Tests override below.
    mockGetUser.mockResolvedValue(null);
    mockSigninSilent.mockResolvedValue(undefined);
  });

  afterAll(() => {
    apiClient.defaults.adapter = originalAdapter;
  });

  // Build a minimal adapter that returns queued responses based on call count.
  // Custom adapters DON'T auto-invoke axios's settle(), so we must throw AxiosError
  // ourselves for non-2xx statuses to properly trigger the error interceptor.
  function queueAdapter(responses: { status: number; data?: unknown }[]) {
    let i = 0;
    apiClient.defaults.adapter = vi.fn(async (config) => {
      const r = responses[i++] ?? responses[responses.length - 1];
      const response = {
        status: r.status,
        statusText: r.status === 200 ? 'OK' : 'Error',
        data: r.data ?? {},
        headers: {},
        config,
        request: {},
      } as AxiosResponse;
      if (r.status >= 400) {
        throw new AxiosError(
          `Request failed with status code ${r.status}`,
          AxiosError.ERR_BAD_REQUEST,
          config,
          {},
          response,
        );
      }
      return response;
    });
    return apiClient.defaults.adapter as ReturnType<typeof vi.fn>;
  }

  it('T1: 401 → silent renew OK → request replayed with fresh token, no error surfaced', async () => {
    const adapter = queueAdapter([
      { status: 401 },
      { status: 200, data: { ok: true } },
    ]);
    // Persistent: request interceptor + error interceptor both call getUser
    mockGetUser.mockResolvedValue({ access_token: 'NEW_TOKEN', expired: false });

    const res = await apiClient.get('/probe');

    expect(res.data).toEqual({ ok: true });
    expect(mockSigninSilent).toHaveBeenCalledTimes(1);
    expect(mockTrigger).not.toHaveBeenCalled();
    // Replay must use the fresh token
    const replayCall = adapter.mock.calls[1][0] as { headers?: Record<string, string> };
    expect(replayCall.headers?.Authorization).toBe('Bearer NEW_TOKEN');
  });

  it('T2: 401 → silent renew FAIL → triggerSessionError called, error rejects', async () => {
    queueAdapter([{ status: 401 }]);
    mockSigninSilent.mockReset();
    mockSigninSilent.mockRejectedValue(new Error('renew failed'));

    await expect(apiClient.get('/probe')).rejects.toMatchObject({ status: 401 });
    expect(mockSigninSilent).toHaveBeenCalledTimes(1);
    expect(mockTrigger).toHaveBeenCalledTimes(1);
  });

  it('T2b: silent renew FAIL (invalid_grant) → removeUser called so RouteGuard can redirect', async () => {
    // Regression: countdown toast UI is fragile (notification-bridge race / strict-mode handler nulling).
    // Removing the stale user from storage flips React state via userUnloaded → RouteGuard.signinRedirect,
    // which is the reliable redirect path independent of UI lifecycle.
    queueAdapter([{ status: 401 }]);
    mockSigninSilent.mockReset();
    mockSigninSilent.mockRejectedValue(new Error('invalid_grant'));
    const mockRemoveUser = userManager.removeUser as ReturnType<typeof vi.fn>;
    mockRemoveUser.mockClear();

    await expect(apiClient.get('/probe')).rejects.toMatchObject({ status: 401 });
    expect(mockRemoveUser).toHaveBeenCalledTimes(1);
  });

  it('T3: replay also returns 401 → no infinite loop, signinSilent called once, trigger fires', async () => {
    const adapter = queueAdapter([{ status: 401 }, { status: 401 }]);
    mockGetUser.mockResolvedValue({ access_token: 'TOKEN_2', expired: false });

    await expect(apiClient.get('/probe')).rejects.toMatchObject({ status: 401 });
    // Adapter hit twice (initial + replay), but signinSilent only fires on first 401
    expect(adapter).toHaveBeenCalledTimes(2);
    expect(mockSigninSilent).toHaveBeenCalledTimes(1);
    expect(mockTrigger).toHaveBeenCalledTimes(1);
  });

  it('T3b: silent renew SUCCESS but replay still 401 → removeUser called so RouteGuard can redirect', async () => {
    // Regression (v2.133p23 follow-up): when AuthServer rotates signing keys (BE restart in dev) or
    // audience/issuer drifts post-migration, signinSilent succeeds but the new token still fails BE
    // validation. Without removeUser, isAuthenticated stays true → RouteGuard never redirects, and the
    // user is stuck on a page making looping 401s with only the 10s countdown toast as fallback.
    queueAdapter([{ status: 401 }, { status: 401 }]);
    mockGetUser.mockResolvedValue({ access_token: 'STALE_TOKEN', expired: false });
    const mockRemoveUser = userManager.removeUser as ReturnType<typeof vi.fn>;
    mockRemoveUser.mockClear();

    await expect(apiClient.get('/probe')).rejects.toMatchObject({ status: 401 });
    expect(mockRemoveUser).toHaveBeenCalledTimes(1);
  });

  it('T4: parallel 401s coalesce to a single signinSilent call', async () => {
    let callCount = 0;
    apiClient.defaults.adapter = vi.fn(async (config) => {
      callCount++;
      // First two requests get 401 (initial calls), subsequent (replays) get 200
      const status = callCount <= 2 ? 401 : 200;
      const response = {
        status,
        statusText: status === 200 ? 'OK' : 'Error',
        data: status === 200 ? { ok: true } : {},
        headers: {},
        config,
        request: {},
      } as AxiosResponse;
      if (status >= 400) {
        throw new AxiosError(
          `Request failed with status code ${status}`,
          AxiosError.ERR_BAD_REQUEST,
          config,
          {},
          response,
        );
      }
      return response;
    });
    // Slow signinSilent so both 401s arrive while it's in-flight
    mockSigninSilent.mockReset();
    mockSigninSilent.mockImplementation(
      () => new Promise((resolve) => setTimeout(() => resolve(undefined), 20))
    );
    mockGetUser.mockResolvedValue({ access_token: 'SHARED_TOKEN', expired: false });

    const [r1, r2] = await Promise.all([apiClient.get('/a'), apiClient.get('/b')]);

    expect(r1.data).toEqual({ ok: true });
    expect(r2.data).toEqual({ ok: true });
    expect(mockSigninSilent).toHaveBeenCalledTimes(1); // coalesced
    expect(mockTrigger).not.toHaveBeenCalled();
  });
});
