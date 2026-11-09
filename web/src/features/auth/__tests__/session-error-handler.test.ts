// Unit tests for session-error-handler — covers setSessionErrorHandler,
// triggerSessionError, and the unified cleanupAndRedirect() helper that
// 5 entry points (api-client, AuthProvider handleExtend, copilot SSE,
// notification-signalr factory, use-chat-signalr factory) now delegate to.

import { describe, it, expect, vi, beforeEach } from 'vitest';

const { removeUser } = vi.hoisted(() => ({ removeUser: vi.fn() }));

vi.mock('../auth-provider', () => ({
  userManager: { removeUser },
}));

import {
  setSessionErrorHandler,
  triggerSessionError,
  cleanupAndRedirect,
} from '../session-error-handler';

describe('session-error-handler', () => {
  beforeEach(() => {
    removeUser.mockReset();
    removeUser.mockResolvedValue(undefined);
    setSessionErrorHandler(null);
  });

  describe('triggerSessionError', () => {
    it('invokes the registered handler', () => {
      const handler = vi.fn();
      setSessionErrorHandler(handler);
      triggerSessionError();
      expect(handler).toHaveBeenCalledTimes(1);
    });

    it('is a no-op when no handler registered', () => {
      expect(() => triggerSessionError()).not.toThrow();
    });

    it('unregisters via setSessionErrorHandler(null)', () => {
      const handler = vi.fn();
      setSessionErrorHandler(handler);
      setSessionErrorHandler(null);
      triggerSessionError();
      expect(handler).not.toHaveBeenCalled();
    });
  });

  describe('cleanupAndRedirect', () => {
    it('calls removeUser then triggers registered handler in order', async () => {
      const calls: string[] = [];
      removeUser.mockImplementation(async () => {
        calls.push('removeUser');
      });
      const handler = vi.fn(() => {
        calls.push('handler');
      });
      setSessionErrorHandler(handler);

      await cleanupAndRedirect();

      expect(removeUser).toHaveBeenCalledTimes(1);
      expect(handler).toHaveBeenCalledTimes(1);
      expect(calls).toEqual(['removeUser', 'handler']);
    });

    it('swallows removeUser rejection — still fires handler', async () => {
      removeUser.mockRejectedValueOnce(new Error('storage cleared'));
      const handler = vi.fn();
      setSessionErrorHandler(handler);

      await expect(cleanupAndRedirect()).resolves.toBeUndefined();
      expect(handler).toHaveBeenCalledTimes(1);
    });

    it('does not throw when no handler registered', async () => {
      await expect(cleanupAndRedirect()).resolves.toBeUndefined();
      expect(removeUser).toHaveBeenCalledTimes(1);
    });
  });
});
