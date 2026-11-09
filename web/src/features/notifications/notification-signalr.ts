// SignalR connection singleton for /hubs/notifications
// Handles connection lifecycle, auto-reconnect, circuit breaker, and notification callbacks.
//
// v2.133p16 hardening:
// - Circuit breaker: pause 5 minutes after FAIL_THRESHOLD consecutive failed startSignalR() calls.
//   Without this, a downed BE causes infinite negotiate spam (each consumer mount → 3 attempts).
// - Skip start when no authenticated user — avoid empty-token negotiate noise on logout / token expiry.

import {
  HubConnectionBuilder,
  HubConnection,
  LogLevel,
  HubConnectionState,
} from '@microsoft/signalr';
import { userManager } from '@/features/auth/auth-provider';
import { cleanupAndRedirect } from '@/features/auth/session-error-handler';

export type NotificationCallback = (notification: {
  id: string;
  title: string;
  body: string;
  deepLink?: string;
}) => void;

let connection: HubConnection | null = null;
const callbacks: Set<NotificationCallback> = new Set();

// ─── Circuit breaker state (module-scoped) ───────────────────────────────────

const FAIL_THRESHOLD = 5;
const PAUSE_MS = 5 * 60_000; // 5 minutes
let consecutiveFailures = 0;
let pauseUntil = 0;

/** Test-only: reset breaker state. Not for production code paths. */
export function __resetSignalRBreakerForTests(): void {
  consecutiveFailures = 0;
  pauseUntil = 0;
}

/** Test-only: read breaker state for assertions. */
export function __getSignalRBreakerState() {
  return { consecutiveFailures, pauseUntil };
}

/** Get or create the SignalR hub connection (singleton) */
function getConnection(): HubConnection {
  if (connection) return connection;

  connection = new HubConnectionBuilder()
    .withUrl('/hubs/notifications', {
      // Auth token passed as query param for WebSocket compatibility.
      // Mid-session expiry path: attempt silent renew; on failure drop user so
      // AuthProvider.onUserUnloaded → RouteGuard.signinRedirect (don't return
      // an expired token — hub would 401 → reconnect storm → CB open).
      accessTokenFactory: async () => {
        let user = await userManager.getUser().catch(() => null);
        if (user?.expired) {
          try {
            user = await userManager.signinSilent();
          } catch {
            await cleanupAndRedirect();
            return '';
          }
        }
        return user?.access_token ?? '';
      },
    })
    .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
    .configureLogging(LogLevel.Warning)
    .build();

  // Forward incoming notifications to all registered callbacks
  connection.on('ReceiveNotification', (notification) => {
    callbacks.forEach((cb) => cb(notification));
  });

  connection.onreconnecting(() => {
    if (import.meta.env.DEV) console.debug('[SignalR] Reconnecting...');
  });

  connection.onreconnected(() => {
    consecutiveFailures = 0;
    if (import.meta.env.DEV) console.debug('[SignalR] Reconnected');
  });

  connection.onclose(() => {
    if (import.meta.env.DEV) console.debug('[SignalR] Connection closed');
  });

  return connection;
}

/** Start the SignalR connection if not already connected — circuit-breaker guarded. */
export async function startSignalR(): Promise<void> {
  // Breaker open — skip until pause window elapses
  if (Date.now() < pauseUntil) {
    if (import.meta.env.DEV) {
      const remainingMs = pauseUntil - Date.now();
      console.debug(`[SignalR] Breaker open — skip start (${Math.round(remainingMs / 1000)}s remaining)`);
    }
    return;
  }

  // No authenticated user → don't attempt negotiate (would just send empty token)
  const user = await userManager.getUser().catch(() => null);
  if (!user?.access_token || user.expired) {
    if (import.meta.env.DEV) console.debug('[SignalR] No active user session — skip start');
    return;
  }

  const conn = getConnection();
  if (conn.state !== HubConnectionState.Disconnected) return;

  const MAX_RETRIES = 3;
  for (let attempt = 0; attempt < MAX_RETRIES; attempt++) {
    try {
      await conn.start();
      consecutiveFailures = 0;
      if (import.meta.env.DEV) console.debug('[SignalR] Connected to /hubs/notifications');
      return;
    } catch (err) {
      if (attempt === MAX_RETRIES - 1) {
        consecutiveFailures += 1;
        if (consecutiveFailures >= FAIL_THRESHOLD) {
          pauseUntil = Date.now() + PAUSE_MS;
          consecutiveFailures = 0;
          console.warn(
            `[SignalR] ${FAIL_THRESHOLD} consecutive start failures — circuit breaker open for ${PAUSE_MS / 1000}s`,
          );
        } else {
          console.error(
            `[SignalR] Failed to connect after retries (${consecutiveFailures}/${FAIL_THRESHOLD}):`,
            err,
          );
        }
      } else {
        await new Promise((r) => setTimeout(r, 2000 * (attempt + 1)));
      }
    }
  }
}

/** Stop the SignalR connection */
export async function stopSignalR(): Promise<void> {
  consecutiveFailures = 0;
  pauseUntil = 0;
  if (connection && connection.state !== HubConnectionState.Disconnected) {
    await connection.stop();
  }
}

/** Register a callback to receive real-time notifications */
export function onNotification(cb: NotificationCallback): () => void {
  callbacks.add(cb);
  return () => callbacks.delete(cb);
}
