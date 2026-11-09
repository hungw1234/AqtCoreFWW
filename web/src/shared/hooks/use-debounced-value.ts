// use-debounced-value.ts — KISS debounce with no external dependency.

import { useEffect, useState } from 'react';

/**
 * Returns `value` delayed by `delayMs`. Each new `value` restarts the timer, so a burst of
 * rapid changes collapses to a single settled emission after the input goes quiet for `delayMs`.
 * Cleanup clears the pending timer on change/unmount — no stale emission after unmount.
 */
export function useDebouncedValue<T>(value: T, delayMs = 300): T {
  const [debounced, setDebounced] = useState<T>(value);

  useEffect(() => {
    const timer = setTimeout(() => setDebounced(value), delayMs);
    return () => clearTimeout(timer);
  }, [value, delayMs]);

  return debounced;
}
