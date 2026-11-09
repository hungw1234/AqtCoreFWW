# Journal — 2026-04-16 18:20 — Phase 00 Chunk 4b-2: Workflow Actions Admin CRUD Page

## Session Goal

Implement Chunk 4b-2 (Actions Admin CRUD Page) — mirror of C4b-1 (States CRUD). Deliver Create + Update flows for workflow action taxonomy admin page. Same component pattern + hook structure as C4b-1, minus Category/Color form fields. Unblocks C4c (Delete/Restore dialogs). Continuation of C4b-1 session (18:00 → 18:20, same branch).

## What Shipped

### Actions Admin Page + Mutation Hooks (1 commit, 10 new tests, 138/138 workflow suite pass)

**Artifacts:**
- 1 component: `workflow-action-taxonomy-admin-page.tsx` (Table: Code, DisplayName_en, Status; Drawer with Form for Create/Edit — no Category/Color fields per spec)
- 2 mutation hooks: `useCreateWorkflowActionTaxonomy` + `useUpdateWorkflowActionTaxonomy` (same TanStack Query + error mapping pattern as C4b-1)
- 2 payload types: `CreateWorkflowActionTaxonomyPayload` + `UpdateWorkflowActionTaxonomyPayload` (match BE CreateCommand/UpdateCommand)
- 1 admin route: `/admin/master-data/workflow-actions` registered under `adminRoute`
- 3 new i18n keys: `workflow.taxonomy.admin.actionsTitle`, `createActionTitle`, `editActionTitle` (vi + en); reused `create`/`edit`/`code`/`nameVi`/`nameEn` keys from C4b-1
- 10 new vitest: 5 mutation hook tests + 5 component tests (mirror structure of C4b-1)
- Zero typecheck warnings (prod + test build pass)

**Code Review Quality:** 9.5/10 APPROVED_WITH_MINOR. Same M1/M2 as C4b-1 (extractErrorCode path stability + validation fallback); 4 low findings all non-blocking. Commit `40424021` pushed to PR #25.

**Test Coverage:** Red→Green TDD applied C4b-1 patterns: mutation tests first (5/5 green), component tests second (5/5 green with fixture value tuning). No additional gotchas — antd testing practices from C4b-1 carried forward.

## Key Insights

### 1. Rule-of-3 Inflection Point Reached: extractErrorCode Duplication

**Status:** 2 instances now exist (states + actions admin pages). Not yet extracted.

**Rationale:** YAGNI principle — 2 instances tolerated; duplication pain at 3rd consumer. When C4c adds Archive/Restore dialog, `extractErrorCode` will appear in 3 locations. **Defer extraction to C4c start** → create `web/src/features/workflow/workflow-taxonomy-error-helpers.ts`, move both page implementations + dialog logic in one refactor. Net effect: 1 refactor session covers all 3 consumers atomically.

**Same principle applies to "I18n Key" hardcoded English label** (both states + actions pages). Extract to `workflow.taxonomy.admin.i18nKey` when touching either page next.

### 2. Mirror Delivery Velocity — 40min from impl to commit

**C4b-1 baseline:** 50min (TDD strict red→green + antd gotcha fixes).
**C4b-2 mirror:** 40min (same pattern, smaller scope, no Category/Color form complexity).

**Efficiency gain:** Copied C4b-1 test structure verbatim, adapted fixtures to action domain. One typo fix in test file name (actions_hook.test.ts → action_taxonomy.test.ts). Zero new architectural decisions. Validates "specific component first, generic after 3rd consumer" strategy — had we generalized upfront at C4b-1, this session would have spent time tuning abstractions rather than shipping pages.

## Technical Details

### Component Architecture (Drawer + Form — Simplified vs C4b-1)

**Table render:**
- Columns: Code (sortable), DisplayName_en, Status (IsActive badge)
- Row click → Drawer open with form populated
- Table refresh via `queryClient.invalidateQueries(['workflow-taxonomies', 'actions'])`

**Drawer + Form:**
- Trigger: "Add Action" button + table row click (edit mode)
- Form fields: Code (input), DisplayName (input en + vi), isActive (hidden on PUT)
- **Omitted vs C4b-1:** No Category select, no Color picker (actions simpler domain)
- Submit → mutation (POST Create or PUT Update)
- Success → toast + table refresh + drawer close
- Error → toast with error code mapping (same `extractErrorCode` function as C4b-1)

### Payload Types (Minimal vs States)

```typescript
type CreateWorkflowActionTaxonomyPayload = {
  code: string;
  displayNameVi: string;
  displayNameEn: string;
};

type UpdateWorkflowActionTaxonomyPayload = CreateWorkflowActionTaxonomyPayload & {
  id: string;
  rowVersion: string;
};
```

### Error Handling (Reused Pattern)

Component maps BE error codes:
- `"DUPLICATE_CODE"` → `workflow.taxonomy.error.duplicate_code`
- `"VALIDATION_FAILED"` → Generic `common.error`
- `"CONFLICT_ROW_VERSION"` → Retry hint

**M1 Finding (code review):** Same extractErrorCode path stability question as C4b-1. Verified against BE contract — stable.

## Test Details: Red→Green Cycles (Mirror of C4b-1)

**Phase 1 — Mutation Hooks (5 tests):**
1. useCreateWorkflowActionTaxonomy success → POST called
2. useCreateWorkflowActionTaxonomy error (DUPLICATE_CODE) → error toast
3. useUpdateWorkflowActionTaxonomy success → PUT called
4. useUpdateWorkflowActionTaxonomy error (CONFLICT_ROW_VERSION) → retry toast
5. Loading state → mutation.isPending true

**Phase 2 — Component (5 tests):**
1. Table renders with actions list
2. Row click opens Drawer in edit mode
3. "Add Action" button opens Drawer in create mode
4. Form submit calls mutation
5. Success → table refreshed, drawer closed

**Fixture tuning:** Used unique Vietnamese action names (avoid "Draft" duplication collision from C4b-1).

## Scope Boundary

**IN (C4b-2):**
- Create action + drawer + form + mutation hook
- Update action + drawer + form + mutation hook
- List + table display
- Error handling + validation

**OUT (Deferred to C4c):**
- Delete action modal
- Restore action modal
- Archive/soft-delete workflow

## Test Status

| Suite | Pass | Delta |
|-------|------|-------|
| AqtCoreFW.Workflow.FE (vitest) | 138/138 | +10 new |
| Type checking (prod + test) | 0 warn/0 err | unchanged |
| **Total** | **138/138** | +10 |

## Effort

- **Estimated:** 2-3h
- **Actual:** ~40min wall-clock
- **Efficiency:** Exceeded due to (a) C4b-1 pattern reuse reduced design iteration, (b) antd testing patterns from C4b-1 carried forward without new gotchas, (c) simpler payload (no Category/Color) meant fewer form fields to test

## Lessons Learned

1. **Mirror delivery amplifies pattern reuse ROI.** Copying component structure + test scaffolding from C4b-1 reduced cognitive load. Fixture adaptation (states → actions) was mechanical.

2. **Defer extraction until rule-of-3 triggers.** Two instances of extractErrorCode still reasonable duplication. Extract when C4c arrives with 3rd consumer. Prevents over-engineering.

3. **Simpler domain = faster delivery.** Actions taxonomy lacks Category + Color complexity. Form validation + mutation tests proportionally smaller. 40min vs 50min delta reflects payload scope, not process efficiency.

## Remaining Work

**C4c Delete/Restore/Archive Dialogs** — Estimated 3-4h.
- Delete modal (confirmCode + reason)
- Restore modal (soft-delete recovery)
- Archive modal (IsActive toggle)
- **Triggers rule-of-3 refactoring:** Extract `extractErrorCode` + "I18n Key" label to shared helpers when implementing C4c dialogs

**C5 Finalize + E2E** — Estimated ~8h.
- E2E test suite (Playwright) covering admin workflow (states + actions create → update → delete → restore)
- Dependency map update
- Docs + journal entries

## Related Artifacts

- **Branch:** `feat/phase-00-workflow-masterdata-taxonomy` (15 commits — 5 C1 + 6 C2 + 1 C3 + 1 C4a + 1 C4b-1 + 1 C4b-2)
- **Code review:** 9.5/10 APPROVED_WITH_MINOR (M1/M2 deferred, L1-L4 non-blocking)
- **Commit:** `40424021` — WorkflowActionAdmin component + mutation hooks (10 tests, pushed to PR #25)
