import { describe, it, expect, vi, beforeEach, afterEach, type Mock } from 'vitest';
import {
  createAuthFailureController,
  type AuthFailureDeps,
} from '@/features/auth/auth-failure-controller';

/**
 * Unit tests for createAuthFailureController — the pure state machine behind
 * "silent renew failed → 10s countdown toast → auto-redirect".
 *
 * Not testing the React wiring (antd notification, i18n) — those are mocked via deps.
 */

// Mock each dep with its real AuthFailureDeps signature so the controller accepts the object
// (Vitest 4 narrowed vi.fn()/Mock<T>), while preserving the mock assertion API. Indexed off the
// source type so the signatures can't drift.
interface Deps {
  renderToast: Mock<AuthFailureDeps['renderToast']>;
  destroyToast: Mock<AuthFailureDeps['destroyToast']>;
  stopSignalR: Mock<AuthFailureDeps['stopSignalR']>;
  signinRedirect: Mock<AuthFailureDeps['signinRedirect']>;
}

function makeDeps(): Deps {
  return {
    renderToast: vi.fn(),
    destroyToast: vi.fn(),
    stopSignalR: vi.fn<() => Promise<void>>().mockResolvedValue(undefined),
    signinRedirect: vi.fn<() => Promise<void>>().mockResolvedValue(undefined),
  };
}

describe('createAuthFailureController', () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => { vi.useRealTimers(); vi.clearAllMocks(); });

  it('renders the initial toast immediately with full countdown', () => {
    const deps = makeDeps();
    const c = createAuthFailureController(deps);

    c.trigger();

    expect(deps.renderToast).toHaveBeenCalledTimes(1);
    expect(deps.renderToast).toHaveBeenLastCalledWith(10, expect.any(Function));
  });

  it('re-renders the toast each second of the countdown', async () => {
    const deps = makeDeps();
    const c = createAuthFailureController(deps);

    c.trigger();
    await vi.advanceTimersByTimeAsync(1000);
    expect(deps.renderToast).toHaveBeenLastCalledWith(9, expect.any(Function));

    await vi.advanceTimersByTimeAsync(1000);
    expect(deps.renderToast).toHaveBeenLastCalledWith(8, expect.any(Function));

    await vi.advanceTimersByTimeAsync(5000);
    expect(deps.renderToast).toHaveBeenLastCalledWith(3, expect.any(Function));
  });

  it('redirects when the countdown reaches zero', async () => {
    const deps = makeDeps();
    const c = createAuthFailureController(deps);

    c.trigger();
    expect(deps.signinRedirect).not.toHaveBeenCalled();

    await vi.advanceTimersByTimeAsync(10_000);
    // stopSignalR then signinRedirect in finally
    await vi.runAllTimersAsync();

    expect(deps.stopSignalR).toHaveBeenCalledTimes(1);
    expect(deps.signinRedirect).toHaveBeenCalledTimes(1);
    expect(deps.destroyToast).toHaveBeenCalledTimes(1);
  });

  it('redirectNow() aborts the countdown and redirects immediately', async () => {
    const deps = makeDeps();
    const c = createAuthFailureController(deps);

    c.trigger();
    await vi.advanceTimersByTimeAsync(2000); // partway through

    c.redirectNow();
    await vi.runAllTimersAsync();

    expect(deps.signinRedirect).toHaveBeenCalledTimes(1);
    // Ticks after redirectNow should not keep firing
    const ticksBefore = deps.renderToast.mock.calls.length;
    await vi.advanceTimersByTimeAsync(5000);
    expect(deps.renderToast.mock.calls.length).toBe(ticksBefore);
  });

  it('trigger() is idempotent while a countdown is in flight', async () => {
    const deps = makeDeps();
    const c = createAuthFailureController(deps);

    c.trigger();
    c.trigger();
    c.trigger();
    await vi.advanceTimersByTimeAsync(1000);

    // Initial render (×1 from each trigger attempt), but only the first actually runs
    // — subsequent triggers early-return so only ONE initial render call
    expect(deps.renderToast).toHaveBeenCalledTimes(2); // 1 initial + 1 tick after 1s
  });

  it('redirectNow() is idempotent (multiple button clicks do not double-redirect)', async () => {
    const deps = makeDeps();
    const c = createAuthFailureController(deps);

    c.trigger();
    c.redirectNow();
    c.redirectNow();
    c.redirectNow();
    await vi.runAllTimersAsync();

    expect(deps.signinRedirect).toHaveBeenCalledTimes(1);
  });

  it('still redirects even if stopSignalR rejects', async () => {
    const deps = makeDeps();
    deps.stopSignalR.mockRejectedValueOnce(new Error('hub disconnect failed'));
    const c = createAuthFailureController(deps);

    c.redirectNow();
    await vi.runAllTimersAsync();

    expect(deps.signinRedirect).toHaveBeenCalledTimes(1);
  });

  it('respects a custom countdownSeconds override', async () => {
    const deps = makeDeps();
    const c = createAuthFailureController({ ...deps, countdownSeconds: 3 });

    c.trigger();
    expect(deps.renderToast).toHaveBeenLastCalledWith(3, expect.any(Function));

    await vi.advanceTimersByTimeAsync(3000);
    await vi.runAllTimersAsync();

    expect(deps.signinRedirect).toHaveBeenCalledTimes(1);
  });

  it('dispose() clears a pending countdown so later ticks do not fire', async () => {
    const deps = makeDeps();
    const c = createAuthFailureController(deps);

    c.trigger();
    await vi.advanceTimersByTimeAsync(2000);
    c.dispose();

    const callsBeforeDispose = deps.renderToast.mock.calls.length;
    await vi.advanceTimersByTimeAsync(10_000);

    expect(deps.renderToast.mock.calls.length).toBe(callsBeforeDispose);
    expect(deps.signinRedirect).not.toHaveBeenCalled();
  });

  it('clicking the toast button forwards to redirectNow (onClickRedirectNow callback)', async () => {
    const deps = makeDeps();
    const c = createAuthFailureController(deps);

    c.trigger();
    const [, onClick] = deps.renderToast.mock.calls[0];
    onClick(); // simulate user clicking "Đăng nhập ngay"
    await vi.runAllTimersAsync();

    expect(deps.signinRedirect).toHaveBeenCalledTimes(1);
  });
});
