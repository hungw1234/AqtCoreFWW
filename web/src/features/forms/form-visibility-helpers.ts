// form-visibility-helpers.ts — evaluate visibility rules against current form values
// Pure functions, no React dependencies

import type { VisibilityRules, VisibilityCondition } from './form-types';

/** Parse visibilityRulesJson string → VisibilityRules | null */
export function parseVisibilityRules(json?: string | null): VisibilityRules | null {
  if (!json) return null;
  try {
    return JSON.parse(json) as VisibilityRules;
  } catch {
    return null;
  }
}

/** Evaluate a single condition against the form values map.
 *  Shared by visibility rules and requiredIf conditions. */
export function evalCondition(
  cond: VisibilityCondition,
  values: Record<string, unknown>
): boolean {
  const raw = values[cond.fieldKey];
  const strVal = raw !== null && raw !== undefined ? String(raw) : '';
  const condVal = cond.value ?? '';

  switch (cond.operator) {
    case 'eq':       return strVal === condVal;
    case 'neq':      return strVal !== condVal;
    case 'gt':       return Number(strVal) > Number(condVal);
    case 'lt':       return Number(strVal) < Number(condVal);
    case 'gte':      return Number(strVal) >= Number(condVal);
    case 'lte':      return Number(strVal) <= Number(condVal);
    case 'contains': return strVal.includes(condVal);
    case 'empty':    return strVal === '' || raw === null || raw === undefined;
    case 'notEmpty': return strVal !== '' && raw !== null && raw !== undefined;
    default:         return true;
  }
}

/**
 * Returns true if the field should be VISIBLE.
 * If no rules are defined → always visible.
 */
export function isFieldVisible(
  visibilityRulesJson: string | null | undefined,
  values: Record<string, unknown>
): boolean {
  const rules = parseVisibilityRules(visibilityRulesJson);
  if (!rules || !rules.conditions.length) return true;

  const results = rules.conditions.map((c) => evalCondition(c, values));
  return rules.match === 'all'
    ? results.every(Boolean)
    : results.some(Boolean);
}
