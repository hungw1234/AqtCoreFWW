// use-idle-timeout.ts — detects user inactivity with cross-tab sync + visibility handling
// RT-1: localStorage sync across tabs | RT-5: explicit click only | RT-6: pause when hidden

import { useEffect, useRef, useCallback } from 'react';

const STORAGE_KEY = 'aqt:lastActivity';
const THROTTLE_MS = 1000; // max 1 activity event per second
const CHECK_INTERVAL_MS = 10_000; // check every 10 seconds

interface IdleTimeoutOptions {
  /** Idle threshold in milliseconds */
  idleTimeoutMs: number;
  /** Called when idle threshold reached — show countdown */
  onWarning: () => void;
  /** Called when user should be logged out */
  onTimeout: () => void;
  /** Disable hook entirely (e.g. not authenticated) */
  enabled: boolean;
}

export function useIdleTimeout({ idleTimeoutMs, onWarning, onTimeout, enabled }: IdleTimeoutOptions) {
  const lastEventTs = useRef(0);
  const warningShown = useRef(false);
  const onWarningRef = useRef(onWarning);
  const onTimeoutRef = useRef(onTimeout);
  onWarningRef.current = onWarning;
  onTimeoutRef.current = onTimeout;

  // Reset idle timer — writes to localStorage for cross-tab sync (RT-1)
  const resetActivity = useCallback(() => {
    const now = Date.now();
    localStorage.setItem(STORAGE_KEY, String(now));
    warningShown.current = false;
  }, []);

  useEffect(() => {
    if (!enabled || idleTimeoutMs <= 0) return;

    // Seed initial activity
    resetActivity();

    // Throttled DOM event handler
    const handleActivity = () => {
      const now = Date.now();
      if (now - lastEventTs.current < THROTTLE_MS) return;
      lastEventTs.current = now;
      resetActivity();
    };

    // RT-1: Listen for activity from other tabs via storage event
    const handleStorage = (e: StorageEvent) => {
      if (e.key === STORAGE_KEY && e.newValue) {
        warningShown.current = false; // other tab active → dismiss warning state
      }
    };

    // RT-6: On visibility change, check elapsed time
    const handleVisibility = () => {
      if (!document.hidden) {
        // Tab became visible — check how long we were away
        const last = parseInt(localStorage.getItem(STORAGE_KEY) ?? '0', 10);
        const elapsed = Date.now() - last;
        if (elapsed >= idleTimeoutMs && !warningShown.current) {
          warningShown.current = true;
          onWarningRef.current();
        }
      }
    };

    // Periodic idle check
    const checkInterval = setInterval(() => {
      // RT-6: skip check when tab is hidden — timer effectively paused
      if (document.hidden) return;

      const last = parseInt(localStorage.getItem(STORAGE_KEY) ?? '0', 10);
      const elapsed = Date.now() - last;
      if (elapsed >= idleTimeoutMs && !warningShown.current) {
        warningShown.current = true;
        onWarningRef.current();
      }
    }, CHECK_INTERVAL_MS);

    // Register DOM event listeners
    const events: (keyof DocumentEventMap)[] = ['mousemove', 'keydown', 'scroll', 'click', 'touchstart'];
    events.forEach((evt) => document.addEventListener(evt, handleActivity, { passive: true }));
    window.addEventListener('storage', handleStorage);
    document.addEventListener('visibilitychange', handleVisibility);

    return () => {
      clearInterval(checkInterval);
      events.forEach((evt) => document.removeEventListener(evt, handleActivity));
      window.removeEventListener('storage', handleStorage);
      document.removeEventListener('visibilitychange', handleVisibility);
    };
  }, [enabled, idleTimeoutMs, resetActivity]);

  return { resetActivity };
}
