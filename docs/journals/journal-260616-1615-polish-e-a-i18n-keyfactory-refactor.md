# POLISH-E + POLISH-A: i18n Key Collision & QueryKey Factory Refactor

**Date:** 2026-06-16 16:15 ICT · **Slice:** POLISH-E-A (Themes E audit + A nit from polish-stale-data-cache slice) · **Scope:** FE-only, 0 BE↔FE contract change · **Commit:** `72eb6c8d` (5 files), pushed origin/main, 0 ahead.

## What Happened

Audit revealed two unrelated i18n + query-key issues in Workflow/Forms:
1. **POLISH-E**: Task detail drawer status/priority Tags hardcoded to English; adjacent Labels exposed a deeper i18n key-type collision (value-object keyed as string returned raw key instead of crashing).
2. **POLISH-A nit**: Query-key factory `fieldOptionsAll(templateId)` refactored from literal `['forms','fieldOptions',templateId]` to use factory function; byte-parity must be preserved or TanStack prefix-invalidation silently breaks.

## The Brutal Truth

The real bug sat one line from what the row flagged. POLISH-E row targeted hardcoded English enum maps in task-detail-drawer. Auditing the adjacent render revealed the Labels used `t('workflow.inbox.status')` and `t('workflow.inbox.priority')` — but those keys are value-OBJECTS ({open, inProgress, ...}), not strings. i18next `returnObjects` defaults false, so `t(objectKey)` returns the raw key STRING, not a crash. The label was **silent gibberish**. Similar footgun: the status/priority value maps covered all 4 status + all 4 priority, but the row named only 3 status — partial i18n would have been visually inconsistent while looking "fixed."

POLISH-A: Refactoring `['forms','fieldOptions',id]` literal to factory function was a pure DRY win, but query-key shape (order, length, type) is behavior-sensitive for TanStack prefix-invalidation. Mismatch = invalidateQueries stops matching the key = cache doesn't flush = stale data. Functionally identical refactors of query-keys require empirical verification.

## Technical Details

### POLISH-E: i18n Key-Type Collision

**workflow/components/task-detail-drawer.tsx:**

Status/Priority Tags hardcoded enum→English string maps:
```typescript
const statusMap: Record<TaskStatus, string> = {
  open: 'Open',
  inProgress: 'In Progress',
  onHold: 'On Hold',
  completed: 'Completed',
};
```

Fixed: Map to i18n key-suffixes → `t('workflow.inbox.status.' + status)`:
```typescript
const statusKey = `workflow.inbox.status.${status}` as const; // 'workflow.inbox.status.open'
const statusLabel = t(statusKey);
```

**Adjacent label bug** (pre-existing, revealed by audit):
```typescript
// OLD (BROKEN)
<Tooltip title={t('workflow.inbox.status')}> {/* Returns raw string "workflow.inbox.status" */}
  <Tag>{statusLabel}</Tag>
</Tooltip>

// FIXED
<Tooltip title={t('col.status')}> {/* Plain string, not an object-key */}
  <Tag>{statusLabel}</Tag>
</Tooltip>
```

The i18n structure uses `col.status` / `col.priority` as plain-string labels (inventory of column titles). The value-object keys (`workflow.inbox.status.*`, `workflow.inbox.priority.*`) are children of those namespaces but are NOT themselves labels. Lesson: when a label looks like raw key-text, suspect a returnObjects type-mismatch. Check the i18n init config + the key definition in locale JSONs.

### POLISH-A: QueryKey Factory Refactor

**web/src/features/forms/form-queries-factory.ts:**

Existing literal usage (from stale-data-cache slice):
```typescript
invalidateQueries(queryKey: ['forms', 'fieldOptions', id])
```

Refactored to factory:
```typescript
export const fieldOptionsAll = (templateId: string) => ['forms', 'fieldOptions', templateId] as const;

// Usage
invalidateQueries(queryKey: fieldOptionsAll(id))
```

**Behavior-sensitive verification:**
- Array order: `['forms', 'fieldOptions', templateId]` — exact match required.
- Array length: 3 elements — TanStack prefix-match assumes consistent structure.
- Type stability: `as const` pins the literal tuple type; mismatch with raw array type breaks type inference.

Shipped: Renamed from literal 5 call-sites; vitest confirms prefix-invalidation still works on store mutations (queryClient call order preserved, no flake, cache clears as expected).

### Durable i18n Lessons

1. **Key collision (same path used for label string + value-object)** returns raw key-string when returnObjects=false. Symptom: label looks like a key path ("workflow.inbox.status"). Fix: Separate label keys (col.status) from value namespaces (workflow.inbox.status.*).

2. **Always grep existing i18n before adding keys**. The status/priority value labels already existed in both locales (used by inbox filter dropdowns) — the fix was pure wiring (enum→key-suffix maps), zero new keys. Checked en.json + vi.json; no duplicates added.

3. **DRY on query-key factories**. Query shape is data structure, not configuration. When converting literal→factory, verify shape byte-identity via snapshot test or prefix-invalidation test. Cosmetic refactors of query keys are behavior-sensitive.

## What We Tried

**Audit-at-slice-start**: Traced status/priority Tag hardcodes to enum maps (intentional), then examined adjacent label usage. Found `t('workflow.inbox.status')` — realized key type mismatch (label needs string key, 'workflow.inbox.status' is object namespace). Verified i18n init config (`returnObjects=false` by default). Grepped both locale JSONs for label candidates; found `col.status` + `col.priority` already defined (reuse, not add).

**QueryKey factory refactor**: Verified factory array equals original literal at all call-sites (grep + manual spot-check). Added inline comment pinning the 3-element, ordered invariant. Ran vitest on forms + workflow to confirm prefix-invalidation behavior unchanged (stale-data-cache fixes still work).

**Tester**: vitest 1075/1075 (+115 files in scope, 0 new test-failures). attachment-preview 4/4 (pre-existing smoke tests, still pass with refactored factory).

## Root Cause Analysis

**i18n key-type collision**: i18next `returnObjects=false` is a safe default (prevents accidental object-leaks to templates). But when the SAME key path is used for both a label string and a value-object namespace (`workflow.inbox.status` = {label: string, status: {open: {label, icon}, ...}}), calling `t(objectKey)` returns the raw key path, not a crash. Silent-failure UX defect. Solution: enforce label/value namespace separation in i18n design (col.* for labels, feature.field.* for values).

**QueryKey factory refactor**: Shape is data contract. Reordering, renaming, or changing length breaks TanStack matching. Literal→factory conversion is a refactor that preserves intent but changes function-call flow. Must verify shape invariant holds at all call-sites.

**Row file:line drift again**: Row claimed `task-detail-drawer.tsx:12`; actual file is `features/workflow/components/task-detail-drawer.tsx`. Status/priority hardcodes were not fully enumerated (row named 3 of 4 status). Partial i18n fixes lead to inconsistent UX. Audit ensured full enum coverage before wiring keys.

## Lessons Learned

1. **i18n key-collision is silent (INVARIANT 260522-A, 11th payoff)**. When a label renders as raw key-text ("workflow.inbox.status"), don't just swap the key — check the i18n structure. Likely root cause: returnObjects=false + object-key used as string. Separate label namespace from value namespaces in design.

2. **Query-key factories must preserve byte-identity with original literal**. Cosmetic refactors are behavior-sensitive for TanStack prefix-invalidation. Add snapshot test or explicit length/order comment to prevent silent cache-miss regression.

3. **Always verify the full enum domain, not just row-quoted examples**. Row named 3 of 4 statuses. Partial i18n hardcodes = inconsistent UX. Grep the enum definition + check all consumers to ensure coverage.

4. **DRY dividend from grep-first approach**. Status/priority value labels already existed in locale JSONs (used by inbox filter). Fix was pure wiring, not new keys. Grep existing i18n BEFORE proposing new entries.

5. **Scope audit-across-layers**: Task detail Label + Tag render together but live in different logic scopes (Tag = enum→key, Label = hardcoded path). Auditing one revealed the other.

## Verification

- `npm run typecheck` (app) 0E · eslint 0E (2 pre-existing react-refresh warnings, not regression).
- `npm run vitest -- workflow forms` 1075/1075 (+115 files in scope, 0 new test-failures). attachment-preview 4/4 smoke tests still passing.
- i18n shape: en.json `col.status` + `col.priority` (string labels) verified; workflow.inbox.status.* + workflow.inbox.priority.* (value objects) verified; no new keys added.
- QueryKey factory: 5 call-sites verified; prefix-invalidation behavior unchanged (stale-data-cache fixes still clear cache on form publish + BP state transition).
- Code-review: APPROVE (0 Crit/High/Med). 2 Low non-blocking: (L1) one i18n key cross-ref in my slice description inaccurate (`workflow.inbox.col.*` is drawer-only, inbox table uses `page.inbox.col.*` — resolved via doc correction); (L2) pre-scan cache optimization (YAGNI, filed optional follow-up).

---

**Status:** DONE

**Summary:** FE-only polish completed. POLISH-E fixed hardcoded status/priority Tags + revealed adjacent i18n key-type collision in Labels. POLISH-A refactored query-key literal to factory (DRY), verified byte-identity. All tests pass; no BE changes.

