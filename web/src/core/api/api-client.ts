import axios, { type AxiosError, type InternalAxiosRequestConfig } from 'axios';
import { onlineManager } from '@tanstack/react-query';
import i18next from 'i18next';
import { userManager } from '@/features/auth/auth-provider';
import { cleanupAndRedirect } from '@/features/auth/session-error-handler';
import { normalizeError, type NormalizedApiError } from './api-error';
import { getNotificationInstance } from './notification-bridge';

declare module 'axios' {
  // Opt-out flag for cosmetic/background requests (e.g. label-resolution lookups):
  // when true, the response interceptor swallows 403s instead of firing the global
  // forbidden toast or the password/MFA navigation interstitials.
  export interface AxiosRequestConfig {
    suppressAuthInterstitial?: boolean;
  }
}

// Base URL from env — empty string means Vite proxy handles routing to backend
const BASE_URL = import.meta.env.VITE_API_BASE_URL ?? '';

// Single in-flight silent renew promise — coalesces parallel 401s so we
// fire only one signinSilent() per burst (no thundering herd against OIDC iframe).
let renewInFlight: Promise<void> | null = null;

// ─── Backend circuit breaker (POLL-FU1, v2.133p17) ───────────────────────────
// Defense-in-depth third tier on top of query-config retry guard + SignalR CB.
// When ≥3 network failures (status=0, no response) occur within a 10s window,
// pause new requests for 30s and surface a single deduped toast. Auto-resets
// on the first successful response. In-flight requests are NOT cancelled —
// only NEW requests are short-circuited.
//
// CORS-LOOP-FIX: when breaker trips, also flip TanStack Query onlineManager
// offline so refetchInterval polls (notifications 30s, backup 10s, workflow
// 10s/30s) suspend during PAUSE_MS. Without this, polls cadenced ≥10s apart
// cannot accumulate 3 fails inside FAIL_WINDOW_MS (10s) → breaker never
// re-trips → user sees periodic CORS errors forever.
const NETWORK_FAIL_THRESHOLD = 3;
const FAIL_WINDOW_MS = 10_000;
const PAUSE_MS = 30_000;
let failTimestamps: number[] = [];
let pauseUntil = 0;
let restoreOnlineTimer: ReturnType<typeof setTimeout> | null = null;

/** Test-only: reset breaker state. Not for production code paths. */
export function __resetBackendBreakerForTests(): void {
  failTimestamps = [];
  pauseUntil = 0;
  if (restoreOnlineTimer) {
    clearTimeout(restoreOnlineTimer);
    restoreOnlineTimer = null;
  }
  onlineManager.setOnline(true);
}

/** Test-only: read breaker state for assertions. */
export function __getBackendBreakerState(): { failCount: number; pauseUntil: number } {
  return { failCount: failTimestamps.length, pauseUntil };
}

function backendUnavailableError(): NormalizedApiError {
  return {
    status: 0,
    code: 'BACKEND_UNAVAILABLE',
    message: i18next.isInitialized
      ? i18next.t('error.http.backendUnavailable', {
          defaultValue: 'Máy chủ tạm thời không phản hồi. Đang tạm dừng yêu cầu trong 30 giây.',
        })
      : 'Máy chủ tạm thời không phản hồi. Đang tạm dừng yêu cầu trong 30 giây.',
  };
}

function tripBackendBreaker(): void {
  pauseUntil = Date.now() + PAUSE_MS;
  failTimestamps = [];

  // Suspend TanStack Query polling/refetch for the pause window. Without this
  // gate, refetchInterval keeps emitting requests even while breaker is "open",
  // and slow-paced polls (≥10s apart) never accumulate enough fails to re-trip.
  onlineManager.setOnline(false);
  if (restoreOnlineTimer) clearTimeout(restoreOnlineTimer);
  restoreOnlineTimer = setTimeout(() => {
    restoreOnlineTimer = null;
    onlineManager.setOnline(true);
  }, PAUSE_MS);

  // antd dedupes by `key` — only one toast remains visible per trip burst
  getNotificationInstance()?.error({
    key: 'backend-unavailable',
    message: backendUnavailableError().message,
    duration: 5,
  });
  console.warn(
    `[apiClient] ${NETWORK_FAIL_THRESHOLD} network failures in ${FAIL_WINDOW_MS / 1000}s — circuit breaker open for ${PAUSE_MS / 1000}s`,
  );
}

export const apiClient = axios.create({
  baseURL: BASE_URL ? `${BASE_URL}/api/v1` : '/api/v1',
  headers: { 'Content-Type': 'application/json' },
  timeout: 30_000,
});

// ─── Request interceptors ────────────────────────────────────────────────────

// 1. Circuit breaker pre-check + attach auth token + correlation ID.
//    Breaker check is inlined at top so we skip auth/token work when BE is known-down.
apiClient.interceptors.request.use(async (config: InternalAxiosRequestConfig) => {
  if (Date.now() < pauseUntil) {
    return Promise.reject(backendUnavailableError());
  }

  // Attempt to get the current OIDC user — may be null when unauthenticated
  const user = await userManager.getUser().catch(() => null);
  if (user?.access_token && !user.expired) {
    config.headers.Authorization = `Bearer ${user.access_token}`;
  }

  // SystemAdmin cross-tenant: attach header if override is a valid GUID
  const tenantOverride = sessionStorage.getItem('admin-tenant-override');
  if (tenantOverride && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(tenantOverride)) {
    config.headers['X-Tenant-Id'] = tenantOverride;
  }

  // Unique ID per request for distributed tracing
  config.headers['X-Correlation-Id'] = crypto.randomUUID();

  return config;
});

// ─── Response interceptors ──────────────────────────────────────────────────

/** Read a Blob error body (from responseType:'blob' downloads) as JSON so typed BE error codes
 * survive normalization. Returns undefined for empty/non-JSON bodies → the caller falls back to
 * the generic status message. */
async function readBlobErrorBody(blob: Blob): Promise<unknown> {
  try {
    const text = await blob.text();
    return text ? JSON.parse(text) : undefined;
  } catch {
    return undefined;
  }
}

// 2. Unwrap ApiResponse<T> envelope — callers receive `.data` directly
apiClient.interceptors.response.use(
  (response) => {
    // Any successful response = backend reachable → reset breaker fail counter
    // and lift the polling suspension early so the UI recovers immediately.
    if (failTimestamps.length > 0) failTimestamps = [];
    if (pauseUntil !== 0) {
      pauseUntil = 0;
      if (restoreOnlineTimer) {
        clearTimeout(restoreOnlineTimer);
        restoreOnlineTimer = null;
      }
      if (!onlineManager.isOnline()) onlineManager.setOnline(true);
    }

    // Backend wraps successful payloads: { success, data, message, errors }
    const envelope = response.data as { success?: boolean; data?: unknown };
    if (envelope && typeof envelope === 'object' && 'success' in envelope) {
      response.data = envelope.data;
    }
    return response;
  },
  async (error: AxiosError) => {
    // Pass through any non-AxiosError rejection unchanged. This covers our own
    // pre-emptive BACKEND_UNAVAILABLE rejection (NormalizedApiError shape) plus
    // any future axios.Cancel / programmatic rejections. Without this, the
    // status=0 fallback below would overwrite the custom code/message.
    if (!error.isAxiosError) {
      return Promise.reject(error);
    }

    const status = error.response?.status ?? 0;

    // responseType:'blob' downloads (signed-PDF, file fetch) deliver a 4xx error body as a Blob,
    // not parsed JSON. normalizeError would see an object with all-undefined fields and fall back to
    // a generic message, dropping typed BE codes (e.g. SIGNING_PADES_PDF_NOT_READY). Read + JSON-parse
    // the blob so typed errors surface for every blob endpoint; non-JSON blobs degrade to the generic message.
    let body = error.response?.data;
    if (body instanceof Blob) {
      body = await readBlobErrorBody(body);
    }

    // Network failure (no response) — feed the breaker sliding window.
    // status===0 covers timeout, DNS, connection refused, CORS preflight fail.
    if (status === 0) {
      const now = Date.now();
      const cutoff = now - FAIL_WINDOW_MS;
      failTimestamps = failTimestamps.filter((t) => t >= cutoff);
      failTimestamps.push(now);
      if (failTimestamps.length >= NETWORK_FAIL_THRESHOLD) {
        tripBackendBreaker();
      }
    }

    const normalized = normalizeError(status, body);

    // 401 — try silent renew once before surfacing failure to user.
    // Replay request with fresh token if renew succeeds; trigger modal if it fails.
    if (status === 401) {
      const config = error.config as (InternalAxiosRequestConfig & { _retried?: boolean }) | undefined;
      if (config && !config._retried) {
        config._retried = true;
        try {
          renewInFlight ??= userManager.signinSilent().then(() => undefined);
          await renewInFlight;
          const fresh = await userManager.getUser();
          if (fresh?.access_token && !fresh.expired) {
            // Defensive double-write: request interceptor will also re-read user on replay,
            // but setting here guards against eventual-consistency lag in OIDC storage.
            config.headers.Authorization = `Bearer ${fresh.access_token}`;
            return apiClient(config); // replay original request with new token
          }
        } catch {
          // signinSilent rejected (invalid_grant ID2003 etc.) — handled below.
        } finally {
          renewInFlight = null;
        }
      }
      // We reach here in three scenarios, all "401 unrecoverable" — must clear
      // stale user so AuthProvider.onUserUnloaded → setUser(null) →
      // RouteGuard.signinRedirect fires immediately, regardless of toast UI state:
      //   (a) signinSilent rejected (catch above) — refresh token invalid
      //   (b) signinSilent resolved but no fresh access_token (else branch above)
      //   (c) replay with fresh token still returned 401 — JWKS rotation,
      //       audience/issuer drift, or BE policy mismatch (interceptor re-entry
      //       sees _retried=true and falls straight here)
      // cleanupAndRedirect() is idempotent; safe even when storage already empty.
      await cleanupAndRedirect();
    }

    // Background/cosmetic requests (e.g. NextHandlerCard label-resolution lookups) opt out
    // of the global 403 interstitials: a secondary lookup must never hijack navigation
    // (redirect to MFA / change-password) nor raise a forbidden toast. The caller degrades
    // gracefully — falling back to raw identifiers — on the rejected promise.
    if (status === 403 && error.config?.suppressAuthInterstitial === true) {
      return Promise.reject(normalized);
    }

    // 403 + X-Password-Expired — QĐ742: password rotation expired, redirect to profile change-pw
    if (status === 403 && error.response?.headers?.['x-password-expired'] === 'true') {
      window.location.href = '/profile?passwordExpired=true';
    // 403 + X-Must-Change-Password — QĐ742 1.3.a: first-login or admin-reset, redirect interstitial
    } else if (status === 403 && error.response?.headers?.['x-must-change-password'] === 'true') {
      window.location.href = '/change-password?reason=must-change';
    // 403 + X-Mfa-Required — QĐ742 1.6.b: missing/stale MFA session, redirect to TOTP enrolment / step-up
    } else if (status === 403 && error.response?.headers?.['x-mfa-required'] === 'true') {
      if (!window.location.pathname.startsWith('/account/mfa')) {
        window.location.href = '/account/mfa?reason=mfa-required';
      }
    } else if (status === 403) {
      getNotificationInstance()?.error({
        key: 'forbidden-403',
        message: i18next.t('error.http.forbidden', { defaultValue: 'Không có quyền truy cập' }),
        description: i18next.t('error.http.forbiddenDetail', { defaultValue: 'Bạn không có quyền thực hiện thao tác này.' }),
        duration: 5,
      });
    }

    // Attach normalized error to the thrown error for upstream consumption
    return Promise.reject(normalized);
  },
);

// Orval mutator — wraps apiClient for generated hooks
// See: https://orval.dev/reference/configuration/output#mutator
//
// Orval generates URLs from the OpenAPI spec which includes the `/api/v1` route prefix
// (e.g. `/api/v1/business-processes`). Our apiClient also has `baseURL: '/api/v1'`.
// Without stripping, axios joins → `/api/v1/api/v1/business-processes` → 404.
// (v2.137p53 incident: BP admin list was the first feature to actually consume an Orval-
// generated hook end-to-end; all other features bypass Orval with hand-written paths.)
// Hand-written hooks call `apiClient.get('/path')` directly and skip this mutator, so
// stripping here is safe.
export const apiClientMutator = <T>(config: import('axios').AxiosRequestConfig): Promise<T> => {
  if (config.url?.startsWith('/api/v1/')) {
    config.url = config.url.slice('/api/v1'.length);
  }
  return apiClient(config).then(({ data }) => data as T);
};

export default apiClient;
