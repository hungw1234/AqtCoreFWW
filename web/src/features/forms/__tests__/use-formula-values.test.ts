/**
 * use-formula-values.test.ts — reactive orchestration contract (fake timers).
 *
 * Locks the FEEDBACK-LOOP GUARD: a settled change fires the injected evaluate EXACTLY ONCE;
 * rapid changes collapse to a single call with the latest values; hasFormula=false never calls;
 * undefined formValues is treated as {}. The hook is the single compute source — no client eval.
 */

import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { renderHook, act, waitFor } from '@testing-library/react';
import { useFormulaValues } from '../use-formula-values';
import type { FormulaEvaluationResult } from '../form-types';

function makeEvaluate(results: Record<string, unknown> = { total: 42 }) {
  return vi.fn(
    async (_values: Record<string, unknown>): Promise<FormulaEvaluationResult> => ({ results })
  );
}

describe('useFormulaValues', () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  it('fires evaluate EXACTLY ONCE per settled change, with the latest values', async () => {
    const evaluate = makeEvaluate({ total: 42 });
    const { result, rerender } = renderHook(
      ({ formValues }) => useFormulaValues({ formValues, hasFormula: true, evaluate }),
      { initialProps: { formValues: { qty: 1 } as Record<string, unknown> } }
    );

    // Initial value computes once on mount (shows computed value immediately for the first render).
    expect(evaluate).toHaveBeenCalledTimes(1);
    expect(evaluate).toHaveBeenLastCalledWith({ qty: 1 });

    rerender({ formValues: { qty: 5 } });
    // Within the debounce window the change is NOT yet dispatched (still only the mount call).
    act(() => void vi.advanceTimersByTime(299));
    expect(evaluate).toHaveBeenCalledTimes(1);

    act(() => void vi.advanceTimersByTime(1));
    expect(evaluate).toHaveBeenCalledTimes(2);
    expect(evaluate).toHaveBeenLastCalledWith({ qty: 5 });

    // Resolve the mutation → computed map exposed.
    await act(async () => {
      await Promise.resolve();
    });
    expect(result.current).toEqual({ total: 42 });
  });

  it('collapses rapid changes into a single call with only the final values', () => {
    const evaluate = makeEvaluate();
    const { rerender } = renderHook(
      ({ formValues }) => useFormulaValues({ formValues, hasFormula: true, evaluate }),
      { initialProps: { formValues: { qty: 1 } as Record<string, unknown> } }
    );

    // One call for the initial mount value.
    expect(evaluate).toHaveBeenCalledTimes(1);

    // A burst of edits inside a single debounce window → collapses to ONE additional call.
    rerender({ formValues: { qty: 2 } });
    act(() => void vi.advanceTimersByTime(100));
    rerender({ formValues: { qty: 3 } });
    act(() => void vi.advanceTimersByTime(100));
    rerender({ formValues: { qty: 4 } });
    act(() => void vi.advanceTimersByTime(300));

    expect(evaluate).toHaveBeenCalledTimes(2);
    expect(evaluate).toHaveBeenLastCalledWith({ qty: 4 });
  });

  it('never calls evaluate when hasFormula is false', () => {
    const evaluate = makeEvaluate();
    const { rerender } = renderHook(
      ({ formValues }) => useFormulaValues({ formValues, hasFormula: false, evaluate }),
      { initialProps: { formValues: { qty: 1 } as Record<string, unknown> } }
    );

    rerender({ formValues: { qty: 2 } });
    act(() => void vi.advanceTimersByTime(1000));

    expect(evaluate).not.toHaveBeenCalled();
  });

  it('does not fire when the value content is unchanged (new object identity, same content)', () => {
    const evaluate = makeEvaluate();
    const { rerender } = renderHook(
      ({ formValues }) => useFormulaValues({ formValues, hasFormula: true, evaluate }),
      { initialProps: { formValues: { qty: 1 } as Record<string, unknown> } }
    );

    // Settle the first value.
    act(() => void vi.advanceTimersByTime(300));
    expect(evaluate).toHaveBeenCalledTimes(1);

    // New object, identical content → no additional call (guards against render-loop churn).
    rerender({ formValues: { qty: 1 } });
    act(() => void vi.advanceTimersByTime(300));
    expect(evaluate).toHaveBeenCalledTimes(1);
  });

  it('treats undefined formValues as {} and still computes', () => {
    const evaluate = makeEvaluate({ total: 0 });
    renderHook(() =>
      useFormulaValues({ formValues: undefined, hasFormula: true, evaluate })
    );

    act(() => void vi.advanceTimersByTime(300));
    expect(evaluate).toHaveBeenCalledTimes(1);
    expect(evaluate).toHaveBeenCalledWith({});
  });

  it('keeps prior computed values on evaluate error (graceful degradation, no throw)', async () => {
    vi.useRealTimers(); // async rejection needs real microtask flushing
    const evaluate = vi
      .fn<(v: Record<string, unknown>) => Promise<FormulaEvaluationResult>>()
      .mockResolvedValueOnce({ results: { total: 7 } })
      .mockRejectedValueOnce(new Error('boom'));

    const { result, rerender } = renderHook(
      ({ formValues }) => useFormulaValues({ formValues, hasFormula: true, evaluate, delayMs: 0 }),
      { initialProps: { formValues: { qty: 1 } as Record<string, unknown> } }
    );

    await waitFor(() => expect(result.current).toEqual({ total: 7 }));

    rerender({ formValues: { qty: 2 } });
    await waitFor(() => expect(evaluate).toHaveBeenCalledTimes(2));
    // Rejection swallowed → last-known value retained, no crash.
    expect(result.current).toEqual({ total: 7 });
  });
});
