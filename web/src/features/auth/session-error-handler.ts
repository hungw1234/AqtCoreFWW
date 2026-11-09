// session-error-handler.ts — module singleton bridging api-client → AuthProvider
// Decouples 401 retry path (api-client) from modal UX (AuthProvider) without
// DOM events or extra deps. AuthProvider registers on mount; api-client triggers
// when silent renew retry fails.

import { userManager } from './auth-provider';

type SessionErrorHandler = () => void;

let handler: SessionErrorHandler | null = null;

export function setSessionErrorHandler(fn: SessionErrorHandler | null): void {
  handler = fn;
}

export function triggerSessionError(): void {
  handler?.();
}

// cleanupAndRedirect — unified entry point for all "session ended" code paths
// (api-client 401-after-retry, AuthProvider handleExtend, copilot SSE 401,
// SignalR accessTokenFactory expired-renew-failure, future). Drops the stale
// user so AuthProvider.onUserUnloaded → RouteGuard.signinRedirect fires
// (hardpath, no UI dependency), AND signals the AuthProvider-registered handler
// for the countdown-toast UX. Both idempotent.
//
// Note: this module is imported by auth-provider for setSessionErrorHandler;
// the reverse import here creates a TS-acceptable cycle since both bindings
// are values consumed at runtime (well after module-init).
export async function cleanupAndRedirect(): Promise<void> {
  await userManager.removeUser().catch(() => {});
  triggerSessionError();
}
