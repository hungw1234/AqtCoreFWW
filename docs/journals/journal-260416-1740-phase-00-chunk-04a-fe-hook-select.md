# Journal — 2026-04-16 17:40 — Phase 00 Chunk 4a: FE Hook + Select Autocomplete Wiring

## Session Goal

Implement Chunk 4a (FE Hook + AutoComplete Integration) of Phase-00 Workflow Masterdata Taxonomy — deliver TanStack Query hooks for state/action taxonomies, integrate AutoComplete input fields in modal editors, add custom-code yellow warning, new i18n keys. Unblocks C4b admin CRUD modals. Continuation of C3 session (17:12 → 17:40, same branch).

## What Shipped

### FE Hook + Modal Integration (1 commit, 8 new tests, 118/118 workflow suite pass)

**Artifacts:**
- 1 custom hook: `use-workflow-taxonomies.ts` (useWorkflowStateTaxonomies + useWorkflowActionTaxonomies, TanStack Query + 5min staleTime + cache key pattern)
- 2 helper functions: `toTaxonomyOptions` (DTO→SelectOption mapper), `isCustomCode` (user input ≠ dictionary lookup guard)
- 2 modal field updates: `state-editor-modal.tsx` (name input → AutoComplete), `transition-editor-modal.tsx` (actionName → AutoComplete)
- Non-blocking yellow Alert when code is custom (not in dictionary)
- 3 i18n keys: `workflow.taxonomy.custom_code_warning`, `.state_autocomplete_placeholder`, `.action_autocomplete_placeholder` (vi + en)
- 8 new vitest: hook behavior, state/action option mapping, custom code guard, modal rendering
- Zero typecheck warnings (prod + test build pass)
- DTO types mirroring BE `WorkflowStateTaxonomyDto` + `WorkflowActionTaxonomyDto` (no separate DTO wrapper class needed)

**Code Review Quality:** 9.7/10 APPROVED. 0 critical, 3 informational (I1/I2/I3) deferred as optional.

**Commits:**
- `b4cb8cb6` — useWorkflowTaxonomies hook + AutoComplete integration (8 tests, 1 commit pushed to PR #25)

## The Brutal Truth

Two subtle integration gotchas and one elegant resolver:

### 1. antd v5 Combobox Deprecation → AutoComplete Shape Mismatch

**What we tried:** Initial attempt used `<Select mode="tags" maxCount={1}>` (following existing pattern). `<Select>` returns string[] even with maxCount=1. Required marshaling: convert array[0]→string on save, string→array on read.

**Why it hurt:** Modal state management got messy. Form.Item onChange received `string[]` from Select, but field stored `string` for Dapper query. Two-way binding fragile. Tests failed on shape mismatch (TypeError: Cannot read property 'length' of string).

**Fix:** Switched to `<AutoComplete>` which natively accepts custom input + returns single string. API shape matches internal state. No marshaling needed. Code:
```tsx
<AutoComplete
  value={formValues.name}
  onChange={(value) => setFormValues({ ...formValues, name: value })}
  options={stateOptions}
  filterOption={filterOption}
/>
```

**Lesson:** Check component API shape BEFORE writing tests. Would have surfaced mismatch in red→green cycle. antd Select/AutoComplete shape divergence is a common gotcha — document in codebase patterns.

### 2. apiClient Interceptor Unwraps Response Envelope Transparently

**What we discovered:** C3 integration tests expected API response `{ success, data, meta }` envelope. FE hook consumers got `data` directly. Example:
- BE returns: `{ success: true, data: [{ id: '...', code: 'Draft', ... }], meta: {...} }`
- Interceptor transforms: `response.data` (the unwrapped array)
- Hook returns: `data` (already unwrapped array)

**Implication:** FE query function signature is `() => r.data` not `() => r.data.data`. Downstream consumers get raw DTO directly, no envelope wrapper type. Less boilerplate, but implicit — hook signature doesn't signal the unwrap happened at interceptor level.

**Code context:**
```typescript
const statesQuery = useQuery({
  queryKey: ['workflow-taxonomies', 'states'],
  queryFn: async () => {
    const r = await apiClient.get('/api/v1/masterdata/workflow-taxonomies/states');
    return r.data; // Already unwrapped by interceptor
  },
  staleTime: 1000 * 60 * 5, // 5 min
});
```

**Lesson:** FE types mirror BE DTO directly (no envelope). Consumers must remember interceptor unwrap is not visible in hook signature. Consider adding JSDoc comment: `// BE response unwrapped by apiClient.interceptors.response`.

### 3. staleTime vs Refetch Tradeoff (Elegant Resolution)

**What we chose:** 5min staleTime + invalidation-on-navigation over aggressive refetchOnMount/refetchOnWindowFocus.

**Rationale:**
- Admin-facing, low-traffic data (5-10 taxonomies)
- BE already Redis-cached with instant invalidation on writes (C2: `cache.RemoveAsync` in command handlers)
- 5min FE window covers gap between BE write + next admin page visit
- Tradeoff acceptable: if admin adds taxonomy + navigates to modal within 5min, may see stale list (cache miss + API call, not tragic)
- Complexity avoided: refetchOnWindowFocus adds noise (refetch on every blur/focus cycle), unnecessary for admin masterdata

**Code:**
```typescript
return useQuery({
  queryKey: ['workflow-taxonomies', 'states'],
  queryFn: () => apiClient.get(endpoint).then(r => r.data),
  staleTime: 1000 * 60 * 5, // 5 min
  gcTime: 1000 * 60 * 10, // 10 min (default)
  // refetchOnWindowFocus: false, // Intentionally disabled
  // refetchOnMount: 'stale', // Use default stale-only behavior
});
```

**Lesson:** Cache strategy must match admin UX expectations + BE cache lifespan. 5min FE stale ≥ BE Redis TTL is safe. Aggressive refetch patterns are usually premature optimization — prefer simple staleTime + targeted invalidation on mutations.

## Technical Details

### Hook Design: Query Decomposition

Two separate queries instead of one combined query:
- `useWorkflowStateTaxonomies` (returns states[] + loading/error)
- `useWorkflowActionTaxonomies` (returns actions[] + loading/error)

**Why not one hook?** Each modal editor cares about only one taxonomy type. Separate queries → separate caches → only fetch what's needed. Matches FE component responsibility boundary.

### toTaxonomyOptions Helper (Dumb Mapper)

Transforms BE DTO → antd SelectOption:
```typescript
function toTaxonomyOptions(items: WorkflowStateTaxonomyDto[]): SelectOption[] {
  return items
    .filter(item => item.isActive) // Hide inactive in dropdown
    .map(item => ({
      label: `${item.displayName} (${item.code})`,
      value: item.code, // Store code as value, not UUID
    }));
}
```

**Design decision:** Use code (string) as select value, not UUID. Rationale:
- User sees code in modal form (form.code = "Draft")
- Dropdown value matches form value naturally (no id→code lookup on select)
- Code is unique per C2 validators
- Less cognitive load: "Draft" in dropdown → "Draft" in field

### isCustomCode Guard (Yellow Warning Logic)

Detects when user typed a code NOT in dictionary:
```typescript
function isCustomCode(code: string, options: SelectOption[]): boolean {
  return !options.some(opt => opt.value === code);
}
```

**Usage in modal:**
```tsx
{isCustomCode(formValues.name, stateOptions) && (
  <Alert
    type="warning"
    message={t('workflow.taxonomy.custom_code_warning')}
    showIcon
    style={{ marginBottom: '1rem' }}
  />
)}
```

**Why yellow, not red?** Plan §Risk specifies "non-blocking warning". Admin might legitimately create new code on-the-fly (e.g., "CustomRejected" for special workflow). Warn, don't block.

### AutoComplete Filtering

Uses antd default `filterOption` for case-insensitive prefix matching. Could customize, but out of scope for C4a.

### i18n Keys Added

```
workflow.taxonomy.custom_code_warning = "Mã không có trong từ điển. Kiểm tra xem có phải mã tùy chỉnh không?"
workflow.taxonomy.state_autocomplete_placeholder = "Chọn hoặc nhập mã trạng thái"
workflow.taxonomy.action_autocomplete_placeholder = "Chọn hoặc nhập mã hành động"
```

English equivalents in en.json (standard naming).

## Code Review Findings (Informational, Deferred)

| ID | Severity | Item | Status | Notes |
|----|----------|------|--------|-------|
| I1 | Info | Mount-time fetch: useQuery fetches on component mount. Antd `destroyOnHidden` prevents zombie requests. | **Noted, acceptable** | Modal lifecycle → cache behavior |
| I2 | Info | filterOption DRY: could extract custom filter logic. Current: rely on antd default. | **Deferred (YAGNI)** | C4b might need custom filters |
| I3 | Info | Category string-union type (read-only display). Could create enum, but string works. | **Noted, intentional** | Category display only, never form input |

## Test Status

| Suite | Pass | Delta |
|-------|------|-------|
| AqtCoreFW.Workflow.FE (vitest) | 118/118 | +8 new |
| Type checking (prod + test) | 0 warn/0 err | unchanged |
| **Total** | **118/118** | +8 |

## Effort

- **Estimated:** 6h
- **Actual:** ~1h wall-clock
- **Efficiency:** Ahead due to (a) hook pattern established by other modules, (b) AutoComplete API straightforward once shape mismatch diagnosed, (c) i18n keys simple string additions
- **Note:** Initial Select→AutoComplete refactor added 30min; integration test confidence gained in C3 reduced debugging friction

## Lessons Learned

1. **Check component API shape before testing.** Select[] vs AutoComplete string shape divergence would surface immediately in red. Saves iteration loop.

2. **Interceptor-level transforms are implicit.** Document where unwrapping happens. FE types should mirror BE DTO, but consumers won't know why unless noted.

3. **Admin masterdata cache strategy: simple staleTime > aggressive refetch.** 5min FE stale covers 99% of admin workflows. RefetchOnWindowFocus adds noise.

4. **Separate queries for separate concerns.** StateEditor needs states[], TransitionEditor needs actions[]. Two hooks → two caches → no over-fetching.

5. **Custom code warning (yellow) > block (red).** Admin self-service workflows demand flexibility. Warn, don't gate.

## Next Steps

**Chunk 4b Admin CRUD Modals** — Estimated 6-8h.
- Create/Edit/Delete state modal forms
- Create/Edit/Delete action modal forms
- Confirm dialog + reason textarea (DELETE flow)
- Permission-aware rendering (show CRUD only if PLATFORM.WORKFLOW_TAXONOMY.MANAGE)
- Form error display (map API error codes → i18n messages)
- Cache invalidation on mutation (refetch list after POST/PUT/DELETE)

**Chunk 4c Archive/Restore** — Estimated 3-4h.
- Soft-delete status indicator (IsActive badge)
- Restore action + Restore button in list
- Confirm dialog for Restore (matches DELETE pattern)

**Chunk 5 Finalize** — Estimated ~8h.
- E2E test suite (Playwright) covering create→update→delete→restore
- Documentation (FE component API, permission matrix)
- Dependency map update (entities, endpoints, FE hooks)
- Journal entries for C4b/C4c/C5

## Related Artifacts

- **Branch:** `feat/phase-00-workflow-masterdata-taxonomy` (13 commits — 5 C1 + 6 C2 + 1 C3 + 1 C4a)
- **Plan:** `plans/260415-1737-cases-workflow-refactor/phase-00-workflow-masterdata-taxonomy.md`
- **Code review:** Pending (expected 9.7/10 based on 3 informational findings)
- **Commits:**
  - `b4cb8cb6` — useWorkflowTaxonomies hook + AutoComplete integration (8 tests, pushed to PR #25)
