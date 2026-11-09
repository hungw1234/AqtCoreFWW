/**
 * use-debounced-value.test.ts — debounce timing contract (fake timers).
 *
 * Locks: old value held until delay elapses; rapid changes collapse to the last value.
 */

import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { renderHook, act } from '@testing-library/react';
import { useDebouncedValue } from './use-debounced-value';

describe('useDebouncedValue', () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  it('returns the initial value immediately', () => {
    const { result } = renderHook(() => useDebouncedValue('a', 300));
    expect(result.current).toBe('a');
  });

  it('holds the old value until the delay elapses, then emits the new one', () => {
    const { result, rerender } = renderHook(({ v }) => useDebouncedValue(v, 300), {
      initialProps: { v: 'a' },
    });

    rerender({ v: 'b' });
    // Before the window elapses, still the old value.
    act(() => void vi.advanceTimersByTime(299));
    expect(result.current).toBe('a');

    // After the full window, the new value settles.
    act(() => void vi.advanceTimersByTime(1));
    expect(result.current).toBe('b');
  });

  it('collapses rapid changes to only the final value', () => {
    const { result, rerender } = renderHook(({ v }) => useDebouncedValue(v, 300), {
      initialProps: { v: 'a' },
    });

    rerender({ v: 'b' });
    act(() => void vi.advanceTimersByTime(100));
    rerender({ v: 'c' });
    act(() => void vi.advanceTimersByTime(100));
    rerender({ v: 'd' });
    // Only 200ms since last change — nothing settled yet.
    act(() => void vi.advanceTimersByTime(200));
    expect(result.current).toBe('a');

    // Cross the window from the LAST change → only 'd' emitted (never 'b'/'c').
    act(() => void vi.advanceTimersByTime(100));
    expect(result.current).toBe('d');
  });
});
