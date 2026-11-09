import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { renderHook, act } from '@testing-library/react';
import { useDebouncedValue } from '@/core/hooks/use-debounced-value';

// TC-FE-HOOK-001: useDebouncedValue delays update
// TC-FE-HOOK-002: useDebouncedValue resets on rapid changes

beforeEach(() => {
  vi.useFakeTimers();
});

afterEach(() => {
  vi.useRealTimers();
});

describe('useDebouncedValue — TC-FE-HOOK-001: delays update', () => {
  it('returns initial value immediately without delay', () => {
    const { result } = renderHook(() => useDebouncedValue('initial', 300));
    expect(result.current).toBe('initial');
  });

  it('does not propagate new value before delay elapses', () => {
    const { result, rerender } = renderHook(
      ({ value }) => useDebouncedValue(value, 300),
      { initialProps: { value: 'first' } }
    );

    rerender({ value: 'second' });
    // Still should be 'first' — timer has not fired
    expect(result.current).toBe('first');
  });

  it('propagates new value after delay elapses', () => {
    const { result, rerender } = renderHook(
      ({ value }) => useDebouncedValue(value, 300),
      { initialProps: { value: 'first' } }
    );

    rerender({ value: 'second' });

    act(() => {
      vi.advanceTimersByTime(300);
    });

    expect(result.current).toBe('second');
  });

  it('uses default delay of 300ms when no delay arg provided', () => {
    const { result, rerender } = renderHook(
      ({ value }) => useDebouncedValue(value),
      { initialProps: { value: 'a' } }
    );

    rerender({ value: 'b' });
    // Before 300ms — still old value
    act(() => { vi.advanceTimersByTime(299); });
    expect(result.current).toBe('a');

    // After 300ms — new value
    act(() => { vi.advanceTimersByTime(1); });
    expect(result.current).toBe('b');
  });
});

describe('useDebouncedValue — TC-FE-HOOK-002: resets on rapid changes', () => {
  it('cancels pending update when value changes again before delay', () => {
    const { result, rerender } = renderHook(
      ({ value }) => useDebouncedValue(value, 300),
      { initialProps: { value: 'a' } }
    );

    // Rapid changes: a → b → c within the delay window
    rerender({ value: 'b' });
    act(() => { vi.advanceTimersByTime(100); }); // still within debounce window

    rerender({ value: 'c' });
    act(() => { vi.advanceTimersByTime(100); }); // timer for 'b' was cancelled

    // Only 200ms elapsed since 'c' was set — should still be 'a'
    expect(result.current).toBe('a');

    // Advance remaining 200ms to complete debounce for 'c'
    act(() => { vi.advanceTimersByTime(200); });
    expect(result.current).toBe('c');
  });

  it('only fires once after burst of changes, settling on final value', () => {
    const { result, rerender } = renderHook(
      ({ value }) => useDebouncedValue(value, 500),
      { initialProps: { value: 'start' } }
    );

    ['x', 'xy', 'xyz'].forEach((v) => {
      rerender({ value: v });
      act(() => { vi.advanceTimersByTime(100); });
    });

    // 300ms elapsed total — not enough for 500ms debounce
    expect(result.current).toBe('start');

    act(() => { vi.advanceTimersByTime(500); });
    expect(result.current).toBe('xyz');
  });
});
