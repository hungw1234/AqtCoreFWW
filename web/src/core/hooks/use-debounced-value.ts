import { useState, useEffect } from 'react';

/**
 * useDebouncedValue — delays propagating a value change by `delay` ms.
 * Prevents firing API calls on every keystroke in search inputs.
 *
 * @param value  The source value to debounce
 * @param delay  Debounce delay in ms (default 300)
 * @returns      The debounced value — only updates after `delay` ms of no changes
 *
 * Usage:
 *   const [search, setSearch] = useState('');
 *   const debouncedSearch = useDebouncedValue(search, 300);
 *   // pass debouncedSearch to API; setSearch on every keystroke
 */
export function useDebouncedValue<T>(value: T, delay = 300): T {
  const [debounced, setDebounced] = useState<T>(value);

  useEffect(() => {
    const timer = setTimeout(() => setDebounced(value), delay);
    return () => clearTimeout(timer);
  }, [value, delay]);

  return debounced;
}
