// cascade-dependencies.ts — pure helpers for cross-field cascading data-source (v1, top-level fields).
//
// A field's DataSourceJson may carry a "dependencies":[{dependsOnFieldKey, filterParamName}] list
// (spike §1). These helpers parse that list and assemble the parent-value map the options endpoint
// expects (?p[filterParamName]=value). Absent/empty dependencies ⇒ the field is on the OLD static path.
//
// Kept dependency-free + pure so the cascade semantics are unit-testable without React/antd.

import type { FormFieldDto } from './form-types';

/** One parsed cascade dependency: this field's option-list is filtered by `dependsOnFieldKey`'s value. */
export interface CascadeDependency {
  dependsOnFieldKey: string;
  filterParamName: string;
}

/**
 * Extract the cascade dependency list from a field's dataSourceJson.
 * Returns [] for absent/empty/malformed JSON or a missing/empty `dependencies` array (backward path).
 */
export function parseCascadeDependencies(dataSourceJson?: string | null): CascadeDependency[] {
  if (!dataSourceJson) return [];
  try {
    const parsed = JSON.parse(dataSourceJson) as { dependencies?: unknown };
    const deps = parsed?.dependencies;
    if (!Array.isArray(deps)) return [];
    return deps
      .filter(
        (d): d is CascadeDependency =>
          !!d &&
          typeof d === 'object' &&
          typeof (d as CascadeDependency).dependsOnFieldKey === 'string' &&
          typeof (d as CascadeDependency).filterParamName === 'string' &&
          (d as CascadeDependency).dependsOnFieldKey.length > 0 &&
          (d as CascadeDependency).filterParamName.length > 0,
      )
      .map((d) => ({ dependsOnFieldKey: d.dependsOnFieldKey, filterParamName: d.filterParamName }));
  } catch {
    return [];
  }
}

/** True if the field declares ≥1 cascade dependency (i.e. its options depend on another field). */
export function hasCascadeDependencies(field: Pick<FormFieldDto, 'dataSourceJson'>): boolean {
  return parseCascadeDependencies(field.dataSourceJson).length > 0;
}

/**
 * Assemble the parent-value map ({ filterParamName: parentValue }) the options endpoint filters by,
 * from the current form values keyed by fieldKey.
 *
 * Returns { parentValues, allParentsSet }:
 *  - `parentValues` includes only the parents that currently have a non-empty value.
 *  - `allParentsSet` is true only when EVERY declared parent has a non-empty value — the caller
 *    should fetch filtered options ONLY when all parents are set (multi-parent gate, spike §2).
 */
export function buildParentValues(
  deps: CascadeDependency[],
  formValues: Record<string, unknown>,
): { parentValues: Record<string, string>; allParentsSet: boolean } {
  const parentValues: Record<string, string> = {};
  let allParentsSet = deps.length > 0;

  for (const dep of deps) {
    const raw = formValues[dep.dependsOnFieldKey];
    if (raw === undefined || raw === null || raw === '') {
      allParentsSet = false;
      continue;
    }
    parentValues[dep.filterParamName] = String(raw);
  }

  return { parentValues, allParentsSet };
}
