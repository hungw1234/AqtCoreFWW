// use-formula-values.ts — reactive orchestration for Formula fields.
//
// Watches form values, debounces (300ms), then calls the injected BE evaluate mutation and
// exposes the returned `results` map for display-only rendering.
//
// FEEDBACK-LOOP GUARD (locked): computed values are NEVER written back into the form
// (no form.setFieldsValue). Rendering is via the widget's `value=` prop only. Writing back
// would re-trigger useWatch → re-compute → infinite loop. This hook is the single compute
// source; there is no client-side expression evaluation anywhere in the FE.

import { useEffect, useRef, useState } from 'react';
import { useDebouncedValue } from '@/shared/hooks/use-debounced-value';
import type { FormulaEvaluationResult } from './form-types';

interface UseFormulaValuesArgs {
  /** Watch-all form values (may be undefined pre-mount → treated as {}). */
  formValues: Record<string, unknown> | undefined;
  /** True only when the template has ≥1 active Formula field — else the hook no-ops. */
  hasFormula: boolean;
  /** Injected mutation call: sends current values to BE, resolves to the batch result. */
  evaluate: (values: Record<string, unknown>) => Promise<FormulaEvaluationResult>;
  /** Debounce window in ms (default 300). */
  delayMs?: number;
}

/**
 * Returns `computedValues` — the latest BE-computed `{ fieldKey: value }` map. Empty until the
 * first debounced evaluate resolves. Skips all work when `hasFormula` is false.
 */
export function useFormulaValues({
  formValues,
  hasFormula,
  evaluate,
  delayMs = 300,
}: UseFormulaValuesArgs): Record<string, unknown> {
  const [computedValues, setComputedValues] = useState<Record<string, unknown>>({});

  // Serialize the debounce input so a value-EQUAL re-render (new object identity, same content)
  // does not restart the timer or fire a redundant call.
  const serialized = hasFormula ? JSON.stringify(formValues ?? {}) : '';
  const debouncedSerialized = useDebouncedValue(serialized, delayMs);

  // Ref the evaluate fn so its changing identity (recreated each render by the caller) does not
  // re-fire the effect — only a settled value change drives a new call.
  const evaluateRef = useRef(evaluate);
  evaluateRef.current = evaluate;

  useEffect(() => {
    if (!hasFormula || debouncedSerialized === '') return;

    let cancelled = false;
    const values = JSON.parse(debouncedSerialized) as Record<string, unknown>;

    evaluateRef
      .current(values)
      .then((res) => {
        if (!cancelled) setComputedValues(res?.results ?? {});
      })
      .catch(() => {
        // Graceful degradation: on error, leave prior computed values untouched (widget stays
        // blank/last-known). No throw — a failed compute must not break form filling.
      });

    return () => {
      cancelled = true;
    };
  }, [debouncedSerialized, hasFormula]);

  return computedValues;
}
