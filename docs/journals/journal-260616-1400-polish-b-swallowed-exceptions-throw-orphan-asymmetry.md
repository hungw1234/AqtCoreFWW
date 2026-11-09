# POLISH-B: Swallowed Exceptions & Throw-Orphan Asymmetry

**Date**: 2026-06-16 14:00  
**Severity**: Medium  
**Component**: Forms/BP workflow submission + error handling  
**Status**: Resolved

## What Happened

Audit-at-slice-start (INVARIANT 260522-A) re-verified 5 claimed sites in POLISH-B row. Found 2 were stale/misread, 3 FE gaps + 1 BE asymmetry were real. Shipped fixes: BE orphan-throw symmetry, FE backend-error extraction. Code `1c35d249`, docs `fe5cf8ea`, pushed origin+bitbucket, 0 ahead.

## The Brutal Truth

Row described "silent orphan at line 251" — line 251 was the success-branch. The actual orphan (rejected instance left unlinked) was already fixed via FormSubmissionWorkflowLinkPendingEvent shipped earlier. The second claim ("Forms error masked as BP-not-found") was intentional security (404 for cross-tenant + 422 per-field for Forms). **We were chasing ghosts**. Scout caught this before code hit main.

## Technical Details

### OUT (No Changes)
- **SubmitFormCommandHandler.cs:251**: Success-branch UpdateAsync. Orphan already surfaced via FormSubmissionWorkflowLinkPendingEvent (earlier slice). Row mis-indexed the line.
- **StartBusinessProcessRunCommandHandler.cs:139**: ToFailureResult uses "BP không tìm thấy" ONLY on CrossTenantRejected (intentional existence-leak prevention). Forms errors preserved as 422 per-field. Not a defect.

### Shipped — BE
**SubmitFormCommandHandler + SubmitPublicFormCommandHandler**: catch(Exception) branch previously only logged. Now publishes FormSubmissionWorkflowLinkPendingEvent with new const `ExceptionFailureCode="WF_LINK_EXCEPTION"`, matching the rejection branch. Hard-throw orphans (broker/network/timeout, or post-create link-write throw) now reach the idempotent fail-soft ops handler, not silent log-only. Mutation response already committed; emitting in catch is safe (handler swallows). 2 new unit tests (`*Throws*`) on both handlers.

### Shipped — FE
**web/src/core/api/extract-api-error.ts**: New extractApiErrorMessage/extractApiErrorCode. Reads both NormalizedApiError interceptor (.message/.code) AND raw axios envelope (response.data.errors[0].detailVi/message). Returns undefined for antd validateFields + bare JS Errors (gated on 'code' or 'status' in object) — internal messages never leak to toast.

Wired into:
- **allowed-actions-bar.tsx**: transition catch{} → backend reason
- **definition-webhook-config-tab.tsx**: validateFields silent (inline errors) ≠ mutation catch (surface)
- **definition-assignment-rules-tab.tsx**: added try/catch to handleDelete + handleCreate; previously unhandled promise rejection (row misread as "success toast fires regardless")

## What We Tried

Audit-at-slice-start: grepped 5 sites, traced to live code. Reclassified 2 as stale before implementing anything. No failed attempts — scout-first prevented wasted effort.

Tester reported 2 "pre-existing FE failures" (low confidence). Isolated re-run: 13/13 pass. Classification-page 33s in full suite vs 17.6s solo → jsdom parallel-contention flakes, NOT regression.

## Root Cause Analysis

**Row decay (INVARIANT 260522-A, 9th payoff)**: Backlog snapshot line numbers + descriptions drift faster than ship cadence. The claimed "silent orphan at 251" was already fixed; the "masking" was intentional. Audit-at-slice-start surfaced this before any code.

**FE error routing gap**: New extract-api-error hook unifies two sources (interceptor shape vs raw axios). Prior: mutations in definition-assignment-rules-tab threw unhandled promise rejection. Catch branches existed elsewhere but inconsistently. Asymmetry: same failure, different surfaces depending on which tab.

**BE catch asymmetry**: Exception path (log-only) ≠ rejection path (orphan event). After instance created successfully but link-write throws, orphan was silent. Symmetry fix is cheap (emit same event), safe (already committed), necessary (idempotent consumer exists).

## Lessons Learned

1. **Scout-first audit wins**: 2 of 5 row sites were stale before code. Reading live code with INVARIANT 260522-A checklist prevented gold-plating stale claims.

2. **Catch-branch symmetry**: Fix belongs at the layer that still knows the cause. Exception-catch + rejection-branch both orphan — both must emit the orphan signal. Not "catch should log differently," but "catch must emit the same event."

3. **Verify subagent claims empirically**: Tester claimed 2 pre-existing failures. Isolated re-run proved them flakes (33s→17.6s serial, jsdom parallel contention). Confidence score mattered — I re-ran, didn't trust the low-confidence flag.

4. **Code-review M2 deferred acceptable**: Broad catch also covers create-success + link-write-throw = instance created but unlinked. Naive retry duplicates. Filed SIGN-WFLINK-RETRY-CREATE-IDEMPOTENT (T3, blocked-on retry-job design). Added consumer-note to event XML-doc explaining the hazard.

## Next Steps

- **SIGN-WFLINK-RETRY-CREATE-IDEMPOTENT** (T3): Idempotency key for retry-safe resubmit. Blocked on retry-job architecture.
- **EXTRACT-API-ERROR-CODE-DEDUP** (T4): Nit from code-review. extract-api-error.ts now exported; other error-consumers may reuse vs hand-parse.
- **No blocking issues**: BE 0W/0E (warnaserror) · 261/261 tests (+2) · FE tsc 0E · eslint 0E · vitest 12/12 extract + suite green · review APPROVE_WITH_NITS (0 Crit/High).

---

**Status**: DONE

**File written**: C:\AqtCoreFW\docs\journals\journal-260616-1400-polish-b-swallowed-exceptions-throw-orphan-asymmetry.md
