// auth-failure-controller.ts
// Pure, dependency-injected state machine for "silent renew failed → countdown → redirect".
// Extracted from auth-provider.tsx so countdown / redirect / guard logic can be unit-tested
// without mounting the full React tree + OIDC UserManager.
//
// Owner: AuthProvider — instantiates once per app lifetime via useMemo, calls dispose() on unmount.

export const AUTH_FAILURE_TOAST_KEY = 'auth-failure-countdown';
export const DEFAULT_COUNTDOWN_SECONDS = 10;

export interface AuthFailureDeps {
  /** Render or update the persistent toast. Implementation chooses the UI (antd notification key). */
  renderToast(remainingSeconds: number, onClickRedirectNow: () => void): void;
  /** Dismiss the countdown toast before navigating away. */
  destroyToast(): void;
  /** Disconnect any open sockets (SignalR). */
  stopSignalR(): Promise<void>;
  /** Redirect user to the OIDC login page. */
  signinRedirect(): Promise<void>;
  /** Override the default 10s countdown (primarily for tests). */
  countdownSeconds?: number;
}

export interface AuthFailureController {
  /** Start the countdown + toast. No-op if already running or already redirecting. */
  trigger(): void;
  /** Jump straight to redirect (user clicked "Đăng nhập ngay" button). Idempotent. */
  redirectNow(): void;
  /** Clear any pending timer. Call on AuthProvider unmount. */
  dispose(): void;
}

export function createAuthFailureController(deps: AuthFailureDeps): AuthFailureController {
  const total = deps.countdownSeconds ?? DEFAULT_COUNTDOWN_SECONDS;
  let redirecting = false;
  let interval: ReturnType<typeof setInterval> | null = null;

  function clearTimer() {
    if (interval !== null) {
      clearInterval(interval);
      interval = null;
    }
  }

  function redirectNow() {
    if (redirecting) return;
    redirecting = true;
    clearTimer();
    deps.destroyToast();
    // Fire-and-forget: stopSignalR failure should not block redirect.
    deps.stopSignalR().catch(() => {}).finally(() => {
      deps.signinRedirect().catch((e) =>
        // eslint-disable-next-line no-console
        console.error('[Auth] signinRedirect failed:', e));
    });
  }

  function trigger() {
    if (redirecting || interval !== null) return;

    let remaining = total;
    deps.renderToast(remaining, redirectNow);

    interval = setInterval(() => {
      remaining -= 1;
      if (remaining <= 0) {
        redirectNow();
        return;
      }
      deps.renderToast(remaining, redirectNow);
    }, 1000);
  }

  function dispose() {
    clearTimer();
  }

  return { trigger, redirectNow, dispose };
}
