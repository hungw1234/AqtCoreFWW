// cascade-builder.ts — pure helpers backing the builder's cascade dependency editor (P7).
// Add/remove a dependency in a DataSourceJson string, and a SOFT client-side cycle check that
// mirrors the BE publish-time gate (P2) for early UX feedback. The BE remains the authoritative reject.

import { parseCascadeDependencies, type CascadeDependency } from './cascade-dependencies';

/** Read the current dependencies[] out of a DataSourceJson string (empty when absent/malformed). */
export function getDependencies(dataSourceJson?: string | null): CascadeDependency[] {
  return parseCascadeDependencies(dataSourceJson);
}

/** Serialize a new dependencies[] into an existing DataSourceJson string, preserving other keys. */
export function withDependencies(
  dataSourceJson: string | undefined,
  dependencies: CascadeDependency[],
): string {
  let base: Record<string, unknown> = {};
  if (dataSourceJson) {
    try {
      base = JSON.parse(dataSourceJson) as Record<string, unknown>;
    } catch {
      base = {};
    }
  }
  if (dependencies.length === 0) {
    // Drop the key entirely when empty → the field returns to the backward static path (discriminator).
    delete base.dependencies;
  } else {
    base.dependencies = dependencies;
  }
  return JSON.stringify(base);
}

/** keyed-Enum parentFilterMap: parentValue → allowed enum values. */
export type ParentFilterMap = Record<string, string[]>;

/** Read the keyed-Enum parentFilterMap out of a DataSourceJson string (empty when absent/malformed). */
export function getParentFilterMap(dataSourceJson?: string | null): ParentFilterMap {
  if (!dataSourceJson) return {};
  try {
    const parsed = JSON.parse(dataSourceJson) as { parentFilterMap?: unknown };
    const map = parsed?.parentFilterMap;
    if (!map || typeof map !== 'object' || Array.isArray(map)) return {};
    const out: ParentFilterMap = {};
    for (const [k, v] of Object.entries(map as Record<string, unknown>)) {
      if (Array.isArray(v)) out[k] = v.filter((x): x is string => typeof x === 'string');
    }
    return out;
  } catch {
    return {};
  }
}

/** Serialize a new parentFilterMap into an existing DataSourceJson string, preserving other keys. */
export function withParentFilterMap(
  dataSourceJson: string | undefined,
  map: ParentFilterMap,
): string {
  let base: Record<string, unknown> = {};
  if (dataSourceJson) {
    try {
      base = JSON.parse(dataSourceJson) as Record<string, unknown>;
    } catch {
      base = {};
    }
  }
  // Drop empty rows; drop the whole key when nothing remains (keeps DataSourceJson lean + backward-safe).
  const cleaned: ParentFilterMap = {};
  for (const [k, v] of Object.entries(map)) {
    if (k.trim() !== '' && v.length > 0) cleaned[k] = v;
  }
  if (Object.keys(cleaned).length === 0) {
    delete base.parentFilterMap;
  } else {
    base.parentFilterMap = cleaned;
  }
  return JSON.stringify(base);
}

/**
 * Insert or update a dependency (keyed by dependsOnFieldKey — one edge per parent). A blank
 * dependsOnFieldKey is ignored. Returns the new dependency list.
 */
export function upsertDependency(
  current: CascadeDependency[],
  dep: CascadeDependency,
): CascadeDependency[] {
  if (!dep.dependsOnFieldKey) return current;
  const next = current.filter((d) => d.dependsOnFieldKey !== dep.dependsOnFieldKey);
  next.push({ dependsOnFieldKey: dep.dependsOnFieldKey, filterParamName: dep.filterParamName });
  return next;
}

/** Remove the dependency on the given parent fieldKey. */
export function removeDependency(
  current: CascadeDependency[],
  dependsOnFieldKey: string,
): CascadeDependency[] {
  return current.filter((d) => d.dependsOnFieldKey !== dependsOnFieldKey);
}

/**
 * SOFT client-side cycle check: would adding an edge childKey → parentKey create a cycle, given the
 * whole template's current child→parents adjacency? Mirrors the BE Kahn check (P2) for early UX only;
 * the BE publish gate is authoritative. `adjacency[child] = [parentKeys]`.
 */
export function wouldCreateCycle(
  adjacency: Record<string, string[]>,
  childKey: string,
  newParentKey: string,
): boolean {
  if (childKey === newParentKey) return true; // self-dependency

  // Walk parents transitively from newParentKey; a cycle exists if we reach childKey.
  const stack = [newParentKey];
  const seen = new Set<string>();
  while (stack.length > 0) {
    const node = stack.pop()!;
    if (node === childKey) return true;
    if (seen.has(node)) continue;
    seen.add(node);
    for (const parent of adjacency[node] ?? []) stack.push(parent);
  }
  return false;
}

/**
 * Build the child→parents adjacency map for a whole template from each field's fieldKey + dataSourceJson,
 * for the soft cycle check. Only fields carrying dependencies contribute edges.
 */
export function buildAdjacency(
  fields: Array<{ fieldKey: string; dataSourceJson?: string | null }>,
): Record<string, string[]> {
  const adj: Record<string, string[]> = {};
  for (const f of fields) {
    const deps = parseCascadeDependencies(f.dataSourceJson);
    if (deps.length > 0) adj[f.fieldKey] = deps.map((d) => d.dependsOnFieldKey);
  }
  return adj;
}
