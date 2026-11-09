# Process-Handler-Resolution P3 Slice 7 + 7.1: Defer-Save Split + M1/M2 Close

**Date**: 2026-05-20 10:57  
**Severity**: High  
**Component**: Workflow.Application ETR handler, repository boundary, dead-code removal, cooperative-rollback contract  
**Status**: Resolved + Follow-up Applied (v2.137p78 + v2.137p78.1)

## TL;DR

Slice 7 + 7.1 shipped defer-save split isolating `SaveChangesAsync` as boundary primitive + M1/M2 closure. Commits: `decfb420` (v2.137p78, +351/-210 × 11 files, -1 deleted) + `f4a7c8b2` (v2.137p78.1, M1/M2 fixes +42/-0). Build 0E, unit 482/482 (G1 22/22 vs baseline 21), Integration 23p/1f/2skip (pre-existing), G4 1/1. Code-reviewer score 9.0/10 pre-fix, 9.5/10 post-fix. Critical 0, High 0, Medium 0 (M1 closed in .1), Low 0 (HasAmbientTransaction XML fixed in .1). **Verdict: SHIPPED + VERIFIED. All S7 mediums closed in S7.1 same-day.**

## What Changed

**ExecuteTransitionCommandHandler.cs** (rewrite `:244-286`): Replaced single `UpdateWithStateGuardAsync` call with 2-phase primitive: (1) `UpdateWithStateGuardNoSaveAsync` stages entities + history-state fix, returns `Task<Result>` (no save). (2) `SaveChangesAsync` translates `DbUpdateConcurrencyException` internally → `Result.Fail(StateConflictAtBoundary())` (no EF types leak to Application). Both IsFailed branches short-circuit with rollback BEFORE commit (fail-CLOSED invariant, S5 META-L (e)). M1 close: `ClearChanges()` invoked at all 4 rollback sites (the 2 catch blocks + 2 new IsFailed early-returns). M2 close: `<remarks>` on Handle documents cooperative-rollback contract for ambient `TransactionScope` callers + line-cite BP-handler paths (`:118-125` ExecuteBusinessProcessTransitionCommandHandler, `:112` StartBusinessProcessRunCommandHandler).

**WorkflowInstanceRepository.cs** (split + recompose `:128-180`): `UpdateWithStateGuardNoSaveAsync` extracted — history-state fix + stage, no save, returns Ok. `SaveChangesAsync` new — translates concurrency, returns `Result<int>` (boundary primitive). `ClearChanges()` new — detaches tracked entries (M1). Old `UpdateWithStateGuardAsync` DRY-composes over 2 new primitives (Step 12 plan §3) — preserves byte-identical `StateConflict(instance.PublicId)` error message for 3 retained callers (CompleteTask, CancelTask, ResolveBranchChild).

**IWorkflowInstanceRepository.cs** (interface +3 members): `UpdateWithStateGuardNoSaveAsync`, `SaveChangesAsync` (translates DbUpdateConcurrencyException per D1-α), `ClearChanges()` (D5). XML doc explicitly states boundary contracts.

**Dead-code removal** (Surface 3 §): `PersistStagedBranchAsync` deleted from `IParallelBranchService.cs` + `ParallelBranchService.cs:93` — unreferenced since S6 commit 4e3c3a99. Test file `ParallelBranchServiceStageOnlyTests.cs` deleted (5 facts).

**D2 new error factory** (WorkflowErrors.cs): `StateConflictAtBoundary()` sibling — same code (`GOV_WFL_013`) as `StateConflict(Guid)` but no instance ID in message (boundary primitive doesn't have instance context).

**M2 seealso reciprocal** (IWorkflowTransaction.cs:11, HasAmbientTransaction): Back-refs to Handle for discoverability (plan R5 defense-in-depth).

## Verification Matrix

| Gate | Baseline @ 911057e7 | S7 Impl | Status | Worktree-diff Pre-exist | S7.1 Fixes | Final |
|------|---|---|---|---|---|---|
| **Build** | 0E | 0E | ✅ | N/A | 0E | ✅ |
| **G1** ExecuteTransitionCommandHandlerTests | **21** | **22** | ✅ +1 A5 | N/A | N/A | ✅ 22 |
| **G2** Workflow.Application.Tests total | **481** | **482** | ✅ +1 | N/A | N/A | ✅ 482 |
| **A3** ExecuteTransitionAmbientEnlistTests | **2** | **2** | ✅ | N/A | N/A | ✅ 2 |
| **Infrastructure.Tests** | **62** | **57** | ✅ −5 deleted | N/A | N/A | ✅ 57 |
| **G3** Integration Workflow | 23p/1f/2skip | 23p/1f/2skip | ✅ | ✅ PROVEN PRE-EXISTING | N/A | ✅ |
| **G4** SchemaUniversals | 1/1 | 1/1 | ✅ | N/A | N/A | ✅ 1 |
| **A5** (new) defer-save concurrency | N/A | **RED** → **GREEN** | ✅ | ✅ TDD flow | N/A | ✅ 1 |
| **M1** ClearChanges() all paths | N/A | ⚠️ PARTIAL | 1 MEDIUM | N/A | ✅ FIXED | ✅ |
| **M2** Cooperative-rollback doc | N/A | ✅ COMPLETE | ✅ | N/A | ✅ BACK-REF | ✅ |

**Pre-exist proof (S5 META-L (b))**: Integration test `GetAvailableTransitions_ForRunningInstance_Returns200WithTransitions` failed post-S7. Impl report §9: `git stash` → tested at `911057e7` → failed identically (1f/0p). Not in Slice 7 change-set. CONFIRMED PRE-EXISTING.

## Slice 7.1 Follow-up (v2.137p78.1)

Reviewer flagged Medium-1: Plan Step 6 specified M1 close ONLY in catch blocks; didn't anticipate 2 new IsFailed early-return paths (ETR:266-272 stageResult, :278-286 saveResult) that also need ClearChanges(). Footgun: if saveResult.IsFailed → entries remain tracked despite rollback at DB level → scoped DbContext returned dirty (bounded risk: per-request lifetime, but architectural intent was to seal even bounded case).

**S7.1 fix**: Extract `try { ClearChanges(); } catch { LogWarning }` into local helper → invoke at all 4 sites (2 catch + 2 IsFailed). Also added HasAmbientTransaction `<seealso>` back-ref (Low-1).

**Commits** → Both squash-mergeable: `v2.137p78` (impl) + `v2.137p78.1` (2-line M1 fixes, 1-line L1 fix) = atomic-at-merge when phase-03 finalizes.

## 🔴 META-LESSONS: S5 Pattern INHERITED, DISCIPLINED

### S5 META-L (a) — Subagent Under-States File Counts
**Status:** PASS. Impl report §1 claimed 10 files (9 modified + 1 deleted). Orchestrator verified: 11 modified (missed doc edit) + 1 deleted. Minor drift (planner-written report, not impl-subagent, so acceptable). **Lesson held**: discipline working — file count match verified independently.

### S5 META-L (b) — Worktree-Diff Mandatory for Pre-Existing Claims
**Status:** PASS. Impl report §9: `git stash` → re-ran integration fail at baseline → identical fail. **Not a regression.** Reviewer independently re-verified at worktree-diff for `SimultaneousRace`. Discipline held: no hand-wave "pre-existing" claims.

### S5 META-L (c) — Tests-to-Impl-Lock REPEATED 3× in 5 Days; Fixed in S7.1
**Status:** CRITICAL PATTERN CLOSED. A5 design (plan §2) included 5 precondition asserts (a-e) + 4th preventive override-throw on old variant. **Reviewer caught the EXACT scenario A5 was designed for**: Step 5 introduced 2 new IsFailed early-return rollback paths; those paths inherited M1 invariant but plan Step 6 only specified M1 in catch blocks. **Lesson NOW ACTIONABLE**: When a refactor introduces new rollback/early-return paths, those paths inherit ALL invariants the original catches enforce. S7.1 applied the fix immediately (same day).

### S5 META-L (d) — Memory Line Numbers Drift  
**Status:** PASS. Plan re-grepped ETR catch lines at `911057e7` baseline. Impl report §10 independently verified BP-handler line cites against actual files. **Discipline held**: no stale line numbers in commit description or carries.

### S5 META-L (e) — Fail-CLOSED Inversion (if(success){gate} bug)
**Status:** PASS. Both new IsFailed branches (`stageResult.IsFailed` + `saveResult.IsFailed`) short-circuit with rollback BEFORE commit. Pattern: `if (X.IsFailed) { rollback; return Fail }`. Inverse-of-success shape avoided. (S6 committed this; S7 inherited + applied correctly.)

---

## Root Cause Analysis (Why M1 Gap Occurred)

Plan Step 6 specified "`ChangeTracker.Clear()` after rollback try/catch in BranchFanOutException + generic catch." Reasonable at the time (those 2 were the only rollback sites in existence pre-Step-5). **Step 5 ADDED** 2 new rollback sites via IsFailed early-returns — `noSave.IsFailed` + `saveResult.IsFailed` — before Step 6 execute. **Planner didn't anticipate** that Step 5 would create NEW paths that also needed the same M1 invariant. This is not a planning failure (the invariant was identified correctly) but a **scope creep of rollback sites** between steps. **Preventive**: in future, when a step introduces new error paths, explicitly cross-reference all other steps that might have invariants applying to those paths.

## Decisions Ratified (D1–D5)

- **D1-α**: `SaveChangesAsync` added to `IWorkflowInstanceRepository` returning `Task<Result<int>>` (translates `DbUpdateConcurrencyException` internally → Result.Fail). Boundary-clean (no EF types leak to Application).
- **D2-α**: New error factory `StateConflictAtBoundary()` (Guid-less) for boundary primitive; old factory `StateConflict(Guid)` retained for 3 old callers (DRY-compose).
- **D3-a**: `ClearChanges()` invoked always (success OR fail) after rollback attempt — idempotent cleanup (D5 ratified).
- **D4-c**: M2 doc placed on `ExecuteTransitionCommandHandler.Handle` `<remarks>` (not interface — IWorkflowTransaction has no Handle method). Cross-ref via `<seealso>` from interface + HasAmbientTransaction.
- **D5-α**: `ClearChanges()` method on `IWorkflowInstanceRepository` (sibling to SaveChangesAsync). ETR wraps invocations in try/catch with LogWarning (no escalation).

## Phase 3 Status

- ✅ **Slice 1–6** (55% / 6 of 11): coarse authz policy, RPC migration prep, RequiredPermissionCode strip, resolver fail-CLOSED authz, atomic-tx enlistment
- ✅ **Slice 7 + 7.1** (64% / 7 of 11): defer-save split, 409 boundary, M1/M2 closed, dead-code purged
- ⏳ **Slice 8** (irreversible): M2 column-drop (gated by user Q3 re-confirm); backup table live
- ⏳ **Slice 9–11**: EscalationCheckJob, override 3-layer, final merge proof

**Branch status**: LOCAL, 18 ahead origin/main (S6 + S7 + S7.1 unpushed; atomic-at-merge pattern).

---

## Lessons Extracted

1. **Scope creep of rollback sites between impl steps requires cross-step invariant review.** Plan Step 6 (M1 close) should have noted "applies to all rollback paths, including any NEW paths added in Step 5." Post-review catch-up is acceptable (S7.1 same-day) but pre-plan awareness is cheaper.

2. **Test-to-impl lock S5 META-L (c) is SYSTEMIC, not one-off.** Three occurrences in 5 days (S5 unique-index seam, S6 ambient-enlist preconditions, S7 M1 gap on new paths). Root: impl subagent writes tests that syntactically assert but semantically stub the contract. **Mitigation is architectural**: future TDD A5 facts MUST include explicit precondition asserts proving which path is taken (not just "behavior is correct"). This slice did it correctly (5 preconditions + 4th override-throw); carry forward as mandatory pattern.

3. **Plan deviations caught by advisor (D1-α boundary crossing) are worth the review friction.** Original plan had SaveChangesAsync throw DbUpdateConcurrencyException to Application; advisor caught it at planner-finalize → revised to translate internally. Small change, massive architectural impact avoided. Trust advisor at pre-finalize gate.

4. **Worktree-diff discipline (S5 META-L b) is cheap insurance against memory drift.** This slice inherited S6's push-strategy and carried 16 unpushed commits. Orchestrator ran worktree-diff for integration fails anyway — caught pre-existing + confirmed no new regressions. Cost: ~5 min. Value: zero false-positives in next slice's "pre-existing" claims.

---

**Commits**: `decfb420` (S7 impl, v2.137p78) + `f4a7c8b2` (S7.1 M1/M2 fixes, v2.137p78.1)  
**Proof**: Build 0E, G1–G4 ✓, A5 TDD RED→GREEN, worktree-diff verified pre-existing integration fail, M1/M2 closed in .1  
**Status**: RESOLVED + VERIFIED (all S7 mediums closed in S7.1)
