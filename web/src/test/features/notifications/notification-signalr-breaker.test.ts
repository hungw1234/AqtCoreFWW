import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';

// v2.133p16: SignalR circuit breaker — sau 5 lần startSignalR() fail liên tiếp,
// pause 5 phút để chặn negotiate spam khi BE down.

// ─── Mock @microsoft/signalr trước khi import notification-signalr ───────────

const startMock = vi.fn();
const stopMock = vi.fn();
const onMock = vi.fn();
const onreconnectingMock = vi.fn();
const onreconnectedMock = vi.fn();
const oncloseMock = vi.fn();

const fakeConnection = {
  start: startMock,
  stop: stopMock,
  on: onMock,
  onreconnecting: onreconnectingMock,
  onreconnected: onreconnectedMock,
  onclose: oncloseMock,
  state: 'Disconnected',
};

vi.mock('@microsoft/signalr', () => {
  class HubConnectionBuilder {
    withUrl() { return this; }
    withAutomaticReconnect() { return this; }
    configureLogging() { return this; }
    build() { return fakeConnection; }
  }
  return {
    HubConnectionBuilder,
    HubConnectionState: { Disconnected: 'Disconnected' },
    LogLevel: { Warning: 3 },
  };
});

// ─── Mock auth-provider userManager ──────────────────────────────────────────

const getUserMock = vi.fn();
vi.mock('@/features/auth/auth-provider', () => ({
  userManager: {
    getUser: () => getUserMock(),
  },
}));

// ─── Imports SAU mocks (vitest hoists vi.mock above import per docs) ────────

import {
  startSignalR,
  stopSignalR,
  __resetSignalRBreakerForTests,
  __getSignalRBreakerState,
} from '@/features/notifications/notification-signalr';

// Helper: authenticated user with valid token
function authenticatedUser() {
  return { access_token: 'tok', expired: false };
}

describe('SignalR circuit breaker', () => {
  beforeEach(() => {
    __resetSignalRBreakerForTests();
    vi.useFakeTimers();
    startMock.mockReset();
    stopMock.mockReset();
    getUserMock.mockReset();
    fakeConnection.state = 'Disconnected';
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('skip start khi không có user session (logout / token expired)', async () => {
    getUserMock.mockResolvedValue(null);
    await startSignalR();
    expect(startMock).not.toHaveBeenCalled();
  });

  it('skip start khi token expired', async () => {
    getUserMock.mockResolvedValue({ access_token: 'tok', expired: true });
    await startSignalR();
    expect(startMock).not.toHaveBeenCalled();
  });

  it('reset failure counter khi start thành công', async () => {
    getUserMock.mockResolvedValue(authenticatedUser());
    startMock.mockResolvedValueOnce(undefined);
    await startSignalR();
    expect(__getSignalRBreakerState().consecutiveFailures).toBe(0);
  });

  it('mở breaker sau 5 lần start fail liên tiếp — pause 5 phút', async () => {
    getUserMock.mockResolvedValue(authenticatedUser());
    startMock.mockRejectedValue(new Error('connection refused'));

    // 5 lần startSignalR (mỗi lần MAX_RETRIES=3 attempts ở trong)
    for (let i = 0; i < 5; i++) {
      const p = startSignalR();
      // advance fake timer to flush internal retry sleeps (2s, 4s)
      await vi.advanceTimersByTimeAsync(10_000);
      await p;
    }

    const state = __getSignalRBreakerState();
    expect(state.pauseUntil).toBeGreaterThan(Date.now());
    expect(state.pauseUntil - Date.now()).toBeLessThanOrEqual(5 * 60_000);
  });

  it('breaker open → startSignalR no-op (không gọi connection.start)', async () => {
    getUserMock.mockResolvedValue(authenticatedUser());
    startMock.mockRejectedValue(new Error('refused'));

    // Trip the breaker
    for (let i = 0; i < 5; i++) {
      const p = startSignalR();
      await vi.advanceTimersByTimeAsync(10_000);
      await p;
    }
    startMock.mockClear();

    // Breaker open — next call should NOT invoke start
    await startSignalR();
    expect(startMock).not.toHaveBeenCalled();
  });

  it('stopSignalR reset breaker state', async () => {
    getUserMock.mockResolvedValue(authenticatedUser());
    startMock.mockRejectedValue(new Error('refused'));

    for (let i = 0; i < 5; i++) {
      const p = startSignalR();
      await vi.advanceTimersByTimeAsync(10_000);
      await p;
    }
    expect(__getSignalRBreakerState().pauseUntil).toBeGreaterThan(Date.now());

    await stopSignalR();
    const state = __getSignalRBreakerState();
    expect(state.pauseUntil).toBe(0);
    expect(state.consecutiveFailures).toBe(0);
  });
});
