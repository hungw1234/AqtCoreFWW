# Journal — EXTRACT-API-ERROR-CODE-DEDUP slice

**Date:** 2026-06-23 18:25 · **Workflow:** `/cook` (after `/aqt-next`) · **Branch:** main
**Commits (pushed origin+bitbucket):** `9d90d5c3` code · `f7cd5ec3` docs · code-reviewer DONE (0 concern)

## What

Closed `EXTRACT-API-ERROR-CODE-DEDUP` (T4 FE nit, the lowest-risk actionable row after `/aqt-next` flagged end-of-day fatigue at 26 commits). Deleted feature-local `workflow-taxonomy-helpers.ts` (`extractTaxonomyErrorCode`, read only `response.data.errors[0].code`) and repointed the 2 workflow taxonomy admin pages (6 catch-site calls) to the canonical `extractApiErrorCode` from `@/core/api`.

## Why it was worth more than dedup

The row framed it as pure DRY ("no drift risk, YAGNI-defer"), but reading the two extractors showed the swap also **closes a latent UX gap**: those 6 catch-sites receiving a `NormalizedApiError` (the *default* axios-interceptor rejection shape — no `.response` envelope) previously got `undefined` from the taxonomy helper → fell to a generic toast even when the backend returned a specific `GOV_*` code. `extractApiErrorCode`'s `?? e.code` fallback now surfaces the specific message. Strict improvement, never a regression — all 6 sites are positive `code === 'GOV_X'` equality with else→`common.error`, so the old extractor was a strict subset of the new one's first operand.

## Lessons

- **A "pure DRY nit" can hide a correctness win.** Verifying the superset claim by reading both functions (not trusting the row's "no drift" label) surfaced that the consolidation isn't cosmetic — it makes specific error messages reachable for the most common error shape. INVARIANT 260522-A pays off even on T4 rows.
- **Behavior-preservation for error-handling swaps hinges on the call-site idiom.** All 6 sites being positive-equality (`=== 'GOV_X' ? specific : generic`) is what makes "return more codes than before" strictly safe — no site has a `!code`/`=== undefined` negative branch that a newly-populated code would flip. Confirmed by reading every site before swapping.

## Verify

typecheck clean · eslint 0 · vitest 12/12 (extract-api-error) + 29/29 (5 workflow-taxonomy files, incl. both admin-page component tests exercising the changed catch-blocks) · no public-contract change · net file count −1.

## Unresolved

None. (Session-level: MEMORY.md index bloat 348KB still pending a dedicated compaction pass — flagged at EOD.)
