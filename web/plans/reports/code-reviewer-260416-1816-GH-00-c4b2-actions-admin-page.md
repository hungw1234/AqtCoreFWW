# C4b-2 Review — Workflow Actions Admin CRUD Page

**Scope:** 4 new + 4 modified FE files. Mirror of C4b-1 minus Category/Color. Branch `feat/phase-00-workflow-masterdata-taxonomy` (PR #25 draft).

## Parity Verification

| Check | Result |
|---|---|
| Action DTO omits Category + Color | ✓ `WorkflowActionTaxonomyDto` (lines 31-42) |
| Create/Update payloads omit Category + Color | ✓ lines 165-182 of `use-workflow-taxonomies.ts` |
| Code disabled on edit (plan R7) | ✓ `disabled={isEdit}` line 183 |
| RowVersion on Update | ✓ line 87, line 179 |
| 409 GOV_CONCURRENCY_CONFLICT toast | ✓ lines 103-104 |
| Drawer/Table/Form structure matches states page | ✓ 214 LOC vs 258 (diff = Category Select + Color ColorPicker + Alert) |
| No dead imports (ColorPicker/Select/Alert) | ✓ grep 0 matches |
| Route registered under `adminRoute` | ✓ router.ts:138 |
| i18n keys present vi+en | ✓ `actionsTitle`, `createActionTitle`, `editActionTitle` both locales |
| Reuse of shared keys (code/nameVi/nameEn/sortOrder/icon/create/edit/save/cancel/codePattern/concurrencyConflict) | ✓ all nested under same `workflow.taxonomy.admin.*` + `common.*` |
| Tests: 5 mutation + 5 component mirroring C4b-1 | ✓ TC-FE-WFT-AM-001..005 + AADM-001..005 |
| Shared `use-workflow-taxonomies.ts` state hooks untouched | ✓ only additions appended (lines 161+) — no regression risk |
| Typecheck + 138/138 vitest pass | ✓ per session report |

## Findings

### Critical
None.

### Minor (non-blocking)

**M1 — `extractErrorCode` duplication (2 copies).** Same helper verbatim in states page + actions page. Rule-of-3 not yet triggered (only 2 callsites). Defer extraction to `use-workflow-taxonomies.ts` or a shared `api-error.ts` when the 3rd consumer lands (likely C4c Delete/Restore dialog or another admin page). Flag as **suggestion**, not blocking.

**M2 — i18n "I18n Key" label hardcoded.** Line 197 `label="I18n Key"` is not translated — matches states page (same hardcode at parity). Defer to a single follow-up that adds `workflow.taxonomy.admin.i18nKey` and updates both pages together. Not a regression.

### Positive
- Surgical diff — additions only to shared hook file; no mutation of state CRUD hooks, eliminating regression risk.
- Test symmetry with C4b-1 is near-perfect (same IDs, same structure).
- Correct DTO narrowing — no Category/Color leaked into payloads or form state.
- `destroyOnClose` on Drawer prevents stale field state across open/close cycles.
- RowVersion opaquely threaded from query → form state → update mutation; no client-side mutation of server concurrency token.

## Security / Concurrency
- Same ABAC posture as C4b-1: read requires `MASTER.WORKFLOW_TAXONOMY.VIEW`, write `PLATFORM.WORKFLOW_TAXONOMY.MANAGE` enforced on BE controller — FE does not duplicate policy check (correct — FE route-level admin guard + BE authoritative).
- No PII, no secrets, no stack traces surfaced. Generic error fallback `common.error`.

## Score & Verdict

**Score: 9.5/10**
**Verdict: APPROVED_WITH_MINOR**

Rationale: production-ready mirror. M1 (DRY) and M2 (i18n label) are follow-up candidates, not blockers. Auto-approve threshold met (≥9.5, 0 critical).

## Unresolved Questions
- When to schedule `extractErrorCode` extraction — C4c prep or separate micro-refactor PR? (Suggest: bundle into C4c when Delete/Restore adds the 3rd consumer.)
