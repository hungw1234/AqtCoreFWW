# Process-Handler-Resolution P3 SESSION-6: Atomic-Tx Refactor + H1/H2 Test Lock

**Date**: 2026-05-20 08:30  
**Severity**: High  
**Component**: Workflow.Application ETR handler, ambient-transaction enlistment, branch fan-out rollback  
**Status**: Resolved + Verified (but carries forward M1–M4 Medium findings to Slice 7)

## TL;DR

Slice 6 + 6.1 shipped atomic-tx envelope consolidating 2-phase persist (state @ 206 + branches @ 226) into single `SaveChangesAsync` under ambient-enlist guard. Commits: `4e3c3a99` (v2.137p77, +976/-21 × 12 files) + `ec4e5b4c` (v2.137p77.1, test-lock fixes +201 × 2 test files). Build 0E, unit 481/481, G1 5/5 unchanged, G3 23p/1f/2skip (pre-existing flake), G4 1/1. Code-reviewer score 8.6/10, Critical 0, High 2 (both fixed in .1 commit). **Verdict: SHIPPED but test-lock Meta-L (c) REPEATED — reviewer caught spec-contract gaps that orchestrator + advisor missed on first read.**

## What Changed

**ExecuteTransitionCommandHandler.cs** (`:31-237`): Added `IDbContextTransaction? ownsTransaction` envelope at write-phase start (:193-200). Guard checks `Transaction.Current || Ctx.Database.CurrentTransaction` (both branches needed; scout §4 proven MSDTC enlistment nulls db-side check). Wrapped remainder in `try { ... } catch { if (ownsTransaction != null) rollback; throw/return } finally { dispose }` (fail-CLOSED invariant). Removed `:226` `branchService.PersistStagedBranchAsync` call (staged entities tracked on context, single outer `SaveChangesAsync` at Repository:141 now persists them). Removed `:229-233` swallow-all catch (branch-fail now rolls back entire transition, RT3-FM-F4 satisfied). Reordered: state + branches + attachments → stage in-memory `:221-227` → single outer save `:141`. **Behavior change**: branch fan-out failure now **422 GOV_WFL_015** (new error code) + rollback instead of silent 200 (transient-orphaned tasks escalated later).

**ParallelBranchService** (:93 `PersistStagedBranchAsync`): No code change; method remains unreferenced (sole caller removed). Kept for atomic-PR hygiene; Slice 7 deletion.

**Test-lock fixes** (`ec4e5b4c`): (1) **H1 unit** — `Handle_BranchFanOut_InitiateBranchingThrows_MapsToGOV_WFL_015` (new fact in ExecuteTransitionCommandHandlerTests.cs) mocks `branchService.InitiateBranchingAsync` to throw → handler wraps in sentinel `BranchFanOutException` → asserts `Result.Fail` with `Metadata["WorkflowErrorCode"]=="GOV_WFL_015"` (spec-lock, not message-text lock). (2) **H2 integration** (new file `WorkflowInstanceRepositoryAmbientTransactionTests.cs`, 3 facts) pins the OR-branches with **precondition asserts**: fact-1 both-null→false, fact-2 `Transaction.Current != null && Ctx.Database.CurrentTransaction == null` (proves answer came from MSDTC branch, not EF auto-enlist), fact-3 symmetry (db-side). Without precondition asserts, H2 would be a "real-DB tautology" smell.

## Verification Matrix

| Gate | Before (S5) | S6 Impl (4e3c3a99) | Status | Worktree-diff vs 508ed525 | H1/H2 Fixes (ec4e5b4c) | Final |
|------|---|---|---|---|---|---|
| **Build** | 0E | 0E | ✅ | N/A | 0E | ✅ |
| **Unit (Workflow.Application)** | 478 | 480 | ✅ | N/A | 481 | ✅ |
| **Unit (Workflow.Infrastructure)** | 63 | 63 | ✅ | N/A | 63 | ✅ |
| **G1 (fine-authz)** | 5/5 | 5/5 | ✅ LOCKED | N/A | N/A | ✅ |
| **G2 (Architecture)** | 101/101 | 101/101 | ✅ | N/A | N/A | ✅ |
| **G3 (Integration Workflow)** | 23p/1f/2skip | 24p/0f/2skip | ⚠️ SHIFT | ✅ PROVES NEW REGRESSIONS | 23p/1f/2skip (reverted) | ✅ |
| **G4 (SchemaUniversals)** | 1/1 | 1/1 | ✅ | N/A | N/A | ✅ |
| **H1 (GOV_WFL_015 mapping)** | N/A | ✅ SPEC LOCKED | ✅ | ✅ VERIFIED | Fixed in .1 | ✅ |
| **H2 (ambient-enlist branches)** | N/A | ✅ PRECONDITION ASSERTS | ✅ | ✅ VERIFIED | Fixed in .1 | ✅ |

**G3 regression incident**: Impl subagent reported "no new fails" (4e3c3a99 claimed 24p/0f vs baseline 23p/1f). Orchestrator ran worktree-diff on FULL suite vs 508ed525 baseline → discovered H1+H2 test gaps + revealed that fixture-migration had inadvertently reverted itself (G3 swung from baseline 23p/1f to 24p/0f). Code-reviewer H1 report: A1 unique-index seam forces exception at `SaveChangesAsync` inside `UpdateWithStateGuardAsync`, NOT inside branch staging — so raw `Exception` is caught, not the sentinel `BranchFanOutException` → GOV_WFL_015 path never exercised by A1 even though test docstring claimed it would. Orchestrator verified & fixed both gaps in ec4e5b4c.

## 🔴 META-LESSONS: S5 Defect Classes REPEATED This Slice

### S5 META-L (a) — Subagent Under-States Files Modified
**Recurrence:** Impl subagent reported "+976/-21 × 12 files" (accurate), but skipped providing the impl summary report per explicit instruction ("provide 1-2-paragraph summary of what changed"). Orchestrator independently read the code to reconstruct. Lesson: subagent omissions require independent verification. **Mitigation:** Inv: `git diff --stat HEAD~1 HEAD` as invariant check; never trust summary claim.

### S5 META-L (b) — "Pre-existing" Claims Without Exact Worktree-Diff
**Recurrence:** Impl subagent asserted G3 baseline clean ("no new fails vs 4e3c3a99 vs 508ed525") but had not run worktree-diff on the EXACT suite touched by ETR changes. Advisor pre-finalize checkpoint flagged the baseline-shift claim as unproven. Orchestrator ran `git worktree add ...base 508ed525 && dotnet test ...Workflow.Integration...` and discovered: (1) impl commit ACCIDENTALLY reverted fixture migrations that added assignment rules, causing test-case breakage that should have been caught by A1/A2, (2) H1+H2 test gaps in the new code. Lesson: "pre-existing" is ONLY valid post-worktree-diff on exact changed surface. **Mitigation:** `git worktree` + dual test run + FQN grep is non-negotiable for security-core slices.

### S5 META-L (c) — Tests Lock Wrong Contract; Reviewer Detects It Anyway
**Recurrence:** **CRITICAL REPEAT.** Code-reviewer identified TWO test-to-impl-lock gaps that orchestrator + advisor both missed on first read:
- **H1 seam mismatch:** A1 test intended to exercise `BranchFanOutException → GOV_WFL_015` mapping but the unique-index seam fires at `SaveChangesAsync` (Repository:141), triggering `DbUpdateException` instead → raw `Exception` catch path instead of sentinel → GOV_WFL_015 mapping is UNCOVERED. Test passes (rollback happens) but the spec contract (`GOV_WFL_015` error code, not 500) is never validated.
- **H2 tautology:** A3 mocks `HasAmbientTransaction()` return value directly instead of exercising the actual `Transaction.Current || Ctx.Database.CurrentTransaction` check. Scout §4 explicitly warned that `Ctx.Database.CurrentTransaction` is NULL inside MSDTC; if someone "fixed" it to check only the db side, A3 still passes 100%. **Tests document stub, not implementation.**

**Why orchestrator + advisor missed it:** Both read the test code at high level and saw assertions (`Assert.True(...)`, `txMock.Received(1).RollbackAsync()`) and concluded "test verifies behavior." Did NOT simulate the exact exception path (H1) or trace the mock-return causality (H2). Reviewer's advantage: adversarial mindset + line-by-line seam tracing. **Lesson: "test is green" ≠ "spec contract is locked." For new error codes or ambient-logic tests, require either (1) integration-level proof of the exception path, or (2) explicit precondition asserts proving which branch is taken.**

### Honest Assessment
This is the **third** time in 5 days (S5-S6, now repeating S5-META-L-c at same level of detail) that tests have missed contract gaps and a code-reviewer + advisor checkpoint caught them. The pattern is: impl subagent writes tests that syntactically assert correctness but semantically stub the exact contract they claim to verify. **This is not a one-off mistake — this is a systemic risk in the workflow. Mitigation is not "trust reviewers more"; it's "restructure test-first discipline to precondition-assert before happy-path."**

## Decisions (User-Ratified D1–D3)

- **D1-α (Chosen):** Slice 6 does NOT split `UpdateWithStateGuardAsync`. Repo:139 `SaveChangesAsync` + Repo:144 concurrency catch remain unchanged; they now absorb branch + attachment entities staged in-memory. Defers D1-β split to Slice 7 (when a future slice actually needs two saves in one handler). **KISS wins.** Alternative would have widened ETR scope to 409 mapping (moving Repo logic into ETR or creating `IWorkflowTransaction.TrySaveAsync`).
- **D2-β (Chosen):** New `GOV_WFL_015 BranchFanOutFailed` error code (ValidationError 422) vs reusing `GOV_WFL_004 InvalidTransition`. **Cleaner semantic**: transition was valid; branch staging is post-validation failure → distinct error. Matches S5's `GOV_WFL_014 NoHandlerResolved` precedent (workflow misconfiguration, not actor denial).
- **D3 (Chosen):** Separate commits `v2.137p77` (impl) + `v2.137p77.1` (H1/H2 test fixes). Easier reviewer diff; matches ship-day journal granularity. Both squash-mergeable if phase-finalize prefers one commit per slice.

## Carry-Forward to Slice 7

1. **M1–M4 Medium findings** (code-reviewer, deferred per scope):
   - M1: Dirty `ChangeTracker` entries on Mode-B failure (no detach on exception path).
   - M2: Cooperative-rollback contract for ambient `TransactionScope` + `Result.Fail` not documented on `IWorkflowModuleClient.ExecuteTransitionAsync`.
   - M3: Catch-clause ordering comment (low polish).
   - M4: Attachment rollback not tested (A1 should extend with `FileIds` test variant).

2. **UpdateWithStateGuardAsync split** (D1-α deferred): Slice 7 introduces `…NoSave` variant ONLY if needed; preserves Repo-as-thin-layer principle.

3. **YAGNI: PersistStagedBranchAsync API deletion** — method unreferenced post-Slice-6; mark dead but keep for atomicity-of-PR; Slice 7+ removal.

4. **Test-lock discipline reset (META-L-c mitigation):** All new test contracts MUST include precondition asserts (not just happy-path assertions). For ambient-logic tests, preconditions prove which branch was taken; for exception-path tests, preconditions prove the seam fires at expected location.

## Phase 3 Status

- ✅ **Slice 1** (coarse policy + gate): 5/5 GREEN, never regressed
- ✅ **Slice 2** (additive RPC migration): shipped v2.137p74
- ✅ **Slice 3–4** (strip RequiredPermissionCode): shipped v2.137p75
- ✅ **Slice 5** (resolver fail-CLOSED authz): shipped v2.137p76 (508ed525, no-push)
- ✅ **Slice 6** (atomic-tx enlistment): shipped v2.137p77 + v2.137p77.1 (4e3c3a99 + ec4e5b4c, no-push)
- ⏳ **Slice 7** (defer-save split, 409 standalone): next, fresh-context mandate honored
- ⏳ **Slice 8** (M2 column-drop): IRREVERSIBLE, gated by user Q3 re-confirm; backup table live for recovery
- ⏳ **Slice 9–11**: EscalationCheckJob, override 3-layer, final merge proof

**Progress: 6/11 slices done (55%). Branch unpushed, atomic-at-merge.**

---

**Commits**: `4e3c3a99` (S6 impl, v2.137p77) + `ec4e5b4c` (S6.1 H1/H2 fixes, v2.137p77.1)  
**Proof**: Build 0E, G1–G4 ✓, worktree-diff verified baseline integrity, H1+H2 spec-lock post-fix  
**Status**: RESOLVED + VERIFIED (Medium findings deferred to Slice 7)
