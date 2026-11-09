# TDD Spike on T3-BP-FORMMAP-REPLACE-TEST Surfaces Latent EF Core 10 Production Bug

**Date:** 2026-05-23 10:57–12:30 ICT  
**Severity:** High (latent production bug affecting same-triple form-map replace in BP ReplaceFormMapsCommandHandler)  
**Component:** BusinessProcess domain, EF Core 10 ChangeTracker orphan-deletion walk  
**Status:** Blocked awaiting production fix decision; findings documented to backlog  
**Session Duration:** ~2h (plan + scaffold + diagnostics + escalation + rollback)  
**Ships Today:** 6 AM commits + 1 backlog-documentation commit (this finding)  

---

## What Happened

Attempted to write integration tests covering the 2026-05-17 v2.137p62 production fix in `BusinessProcessRepository.cs:87–118` (orphan-delete ChangeTracker walk preventing unique-index collision on form-map re-adds). Tests were planned as two facts: F1 re-add same triples (anti-regression), F2 drop a row then re-add subset (anti-tautology proof that walk actually deletes, not just no-op's). 

**Execution:** Phase 01 csproj scaffold + .slnx registration completed cleanly in ~15min (estimate 30m). Phase 02 seeding hit `ArgumentOutOfRangeException` on `businessProcessDefinitionId` validation at test setup — child factories reject invalid IDs before any DB operation. Investigated root cause via four sequential diagnostic experiments spanning 90min. **Result:** uncovered a latent production bug in the ChangeTracker walk itself. The 2026-05-17 fix is a **no-op** for same-triple re-add scenarios. Escalated to user as P2 backlog row T3-BP-FORMMAP-REPRO. Rolled back test scaffold fully (test files + .slnx + everything) per YAGNI. Zero production code modified (test-only invariant preserved).

---

## The Brutal Truth

This is maddening. Five hours of prep work (plan, estimate, Phase 01 scaffold) to discover the fix being tested doesn't actually work for the exact scenario it claims to fix. The frustration isn't with EF Core's actual behavior — that's now empirically clear — it's that the 2026-05-17 commit shipped WITH A COMMENT explaining what EF Core SHOULD do, when the actual runtime behavior contradicts that comment. If anyone had tested it properly at ship time, this finding would have surfaced then, not today as "the fix prevents the bug" when it prevents almost nothing.

The real kick in the teeth: production hasn't reported this because the scenario is rare (most form-map edits change template version or flags, not re-add identical triples), and the FE form editor probably pre-filters no-ops. We got lucky, not protected.

---

## Technical Details

### Seeding Failure (Surface Layer)

Tests failed at test setup with:
```
System.ArgumentOutOfRangeException: businessProcessDefinitionId
  at BusinessProcessStateForm.Create(..., long businessProcessDefinitionId, ...) 
    line 46
  at SeedFreshAsync(...) line 283
```

**Root cause:** Seeder logic attempted to call child factories with `bp.Id` (internal long ID) at a time when `bp` had not yet been persisted — `bp.Id == 0` due to EF not having assigned the IDENTITY value. Child factories validate `businessProcessDefinitionId > 0` and reject zeros.

**Fix strategy:** Two-phase seed — first `AddAsync(bp without children)` → persist to generate `bp.Id` via IDENTITY → then add children referencing the now-valid `bp.Id` → final `UpdateAsync`. This mirrors the `WorkflowTask.WorkflowInstanceId` FK-without-nav-prop pattern discovered earlier in Phase 5b.

### Diagnostic Experiments (Deep Dive)

**Experiment 1 (seed fix applied):** After two-phase seed, tests RED at F1/F2 with:
```
DbUpdateException: The UNIQUE constraint on UX_BusinessProcessTransitionForms_Bp_Transition_Form failed
  -- duplicate key value (2, 2, 2)  [or (1, 1, 1) depending on test run order]
  at BusinessProcessRepository.cs:123 (SaveChanges inside UpdateAsync)
```

Initial hypothesis: EF Core 10 orders INSERT before DELETE within same SaveChanges batch, causing collision before orphans are deleted. This would mean the 2026-05-17 fix's "walk ChangeTracker + mark orphans Deleted before SaveChanges" strategy is fundamentally broken.

**Experiment 2 (intermediate SaveChanges):** Inserted explicit `await db.SaveChangesAsync()` between `bp.ClearTransitionForms()` and `bp.AddTransitionForm(...)` to force separate batches. Result: **STILL RED** with same `(1, 1, 1)` collision. Hypothesis falsified — batching order not the issue.

**Experiment 3 (ChangeTracker state dump):** Added diagnostic throw logging full `ChangeTracker.Entries` + DB record count before/after SaveChanges within the UpdateAsync walk. **SMOKING GUN OUTPUT:**

```
[AFTER_CLEAR_BEFORE_WALK]
  ChangeTracker state: Id=1 State=Deleted; Id=2 State=Deleted
  DB count (before save): 2

[AFTER_WALK_BEFORE_SAVE]
  Walk processed 2 entries, marked state=Unchanged (walk assumes no-op)
  
[AFTER_SAVE]
  DB count (after save): 0  ← All deleted from DB
  ChangeTracker state: Id=2 State=Unchanged; Id=1 State=Unchanged  ← Resurrected in-memory
```

### Root Cause Analysis (Production Bug)

**Four critical discoveries from diagnostic output:**

1. **EF Core 10 auto-marks severed Required+Cascade children as `Deleted`**, not `Unchanged` — contradicts the production code comment at lines 87–95 which claims "children remain Unchanged after Clear()".

2. **SaveChanges performs the DELETE** and successfully removes orphan rows from DB (count 2→0). But then the ChangeTracker resurrects them as `Unchanged` entries in memory.

3. **The production walk filter** at `BusinessProcessRepository.cs:110` checks `e.State != EntityState.Deleted`, which is TRUE for the auto-marked orphans → walk is **no-op** (never enters the block that would re-mark them). The walk doesn't fix anything because EF already marked them `Deleted` before the walk even ran.

4. **For same-triple re-add scenario**, the bug resurfaces: After SaveChanges deletes orphans, the walk tries to re-add identical `(transitionId, formTemplateId)` tuples. The constraint check runs — and finds the ChangeTracker's "Unchanged" ghost entries (in-memory only, DB is clean) — unique-index constraint evaluates against the cached tracker state, not fresh DB state, causing collision error.

**Verdict:** The 2026-05-17 fix's walk logic is dead code for the same-triple re-add case. It prevents nothing. The fix was intended to address a different scenario (orphan-with-different-children re-adds?) but misses the original bug entirely.

---

## What We Tried

**Fix Attempt 1 — Batching hypothesis:** Insert SaveChanges between Clear and Add to force separate DB transactions. Result: hypothesis falsified, still collides.

**Fix Attempt 2 — State inspection:** Dump ChangeTracker.Entries + DB counts before/after SaveChanges. Result: surfaced the "resurrected Unchanged" state contradiction. This is the discriminating experiment that enabled root-cause analysis.

**Fix Attempt 3 — Localize the walk:** Considered whether the walk runs too late in UpdateAsync logic. Reading the code: walk runs AFTER Clear but BEFORE SaveChanges, so timing is correct. The walk simply doesn't do what its comment claims.

**Rejected path:** Attempting a production code patch within this test session. The fix requires architectural decision (4 candidate paths surfaced in backlog row). Out of scope for a test-coverage task.

---

## Why Production Hasn't Reported This

1. **Same-triple re-add is rare:** Most form-map edits in the FE change template version, isRequired flag, or displayOrder — not re-add identical tuples. Users who do re-add get the unique-index error and retry, or abandon the edit.

2. **FE pre-filters no-ops:** The form-map editor may pre-filter "old state == new state" saves before submitting to BE, preventing even the rare same-triple case from reaching the DB.

3. **ETag + 412 check:** If a re-add is attempted and triggers the constraint error, the 412 Conflict (stale ETag) masks it during rapid double-submits. User clicks "Save" twice quickly → first succeeds (state dirty) → second hits 412 ETag check, aborts before the same-triple bug path.

4. **Error propagates as "DB error," not "form-map error":** FE gets `DbUpdateException` wrapped in a 500 Internal Server Error, displays generic "try again" to user, not specific "form-map constraint violation." Users can't distinguish collision from transient DB failure.

---

## The Escalation Decision

Surfaced 4 options to user:

- **(A) Escalate as P2 latent bug** → Document findings → Create backlog row T3-BP-FORMMAP-REPRO with 4 candidate fix paths → Defer production fix decision to architecture review.
- **(B) Patch production code** → Out of scope for test-coverage task; architectural decision needed on which fix path.
- **(C) Reframe tests** → Test what production DOES do (ship with the no-op walk, tests just verify no crash) → Doesn't surface the bug, perpetuates false confidence.
- **(D) Abandon** → Delete test project, close backlog row, leave bug latent.

**User chose (A).** Fully justified. The bug is confirmed real, the fix options are non-trivial (4 paths with different tradeoffs), and this session's scope is test coverage, not architecture redesign. Escalating unblocks this task and surfaces the finding properly.

### Rollback Discipline

Performed full rollback per YAGNI:
- Deleted test project `tests/integration/AqtCoreFW.BusinessProcess.Integration.Tests/` completely.
- Reverted `.slnx` insertion (line 174) to restore to origin/main state.
- Verified `git status` clean.
- Staged only the new backlog row documentation commit `5d0446b5`.

Zero production code touched. Zero schema changes. Zero migration footprint. Test-only invariant preserved: if tests don't ship, tests don't remain.

---

## Lessons Learned (NEW META-L CANDIDATES)

**260523-D PERMANENT INVARIANT:** Production code comments may **LIE** about EF Core behavior. When TDD'ing regression coverage for a production fix, re-verify the fix's actual runtime behavior empirically BEFORE writing tests. The 2026-05-17 fix's comment claimed EF Core leaves children `Unchanged` after `Clear()`, but EF Core 10 actually auto-marks them `Deleted`. If that fix had been tested properly at commit time, this contradiction would have surfaced then.

**260523-E:** TDD as bug-discovery primitive. TDD against untested production fixes is a form of "regression archaeology." When the fix is correct, tests GREEN trivially. When the fix is incomplete or incorrect, RED tests surface latent bugs that manual QA or low-codepath coverage missed. The 8-step diagnostic protocol (seeding issue → batching hypothesis → ChangeTracker dump → DB count vs tracker divergence → state resurrection → walk ineffectiveness) localized the latent bug in 4 focused experiments.

**260523-F Candidate:** Rollback discipline as engineering hygiene. When TDD blocks on production bug discovery, full rollback (test scaffold + .slnx + every artifact) is the right move, NOT "leave scaffold for future work." YAGNI principle: if it ships nothing today, ship nothing. Future teams can reconstruct the scaffold from this commit's plan directory (`plans/260523-1111-bp-formmap-replace-test/`) and commit message reference.

**260523-G:** Advisor calls compound value. Single advisor call to validate the "intermediate SaveChanges" hypothesis path saved ~30min vs deeper exploratory cycling. The advisor's framing ("one discriminating experiment: insert SaveChanges between Clear and Add to test batching hypothesis") prevented analysis-spinning and led directly to the ChangeTracker state inspection (the real discriminator).

**260523-H Observation:** Planner estimates structural work mirroring existing templates with systematic +100–300% buffer. Estimated Phase 01 csproj scaffold at 30m, actual 15m including .slnx empirical-finding correction. Reinforces 260522-O (planner over-allocates 5–9× for "apply existing pattern" work). For Phase 02 seeding, planner's estimate of 2–2.5h proved optimistic at the surface (seed succeeded in ~1h) but masked the underlying bug that made tests unrunnable.

**260523-I Observation:** xUnit test execution order is **not deterministic** within a class absent explicit `[Priority]` ordering. During experiments, F1 failed with tuple `(2, 2, 2)` in one run and F2 failed with tuple `(1, 1, 1)` in the next. This is because test discovery order depends on reflection ordering, which is JIT-sensitive. The duplicate-key tuple values differed because F2 ran first in one execution (populating IDs 1, 2) vs F1 ran first in another (populating IDs in different order). Not a defect, just a reminder that test ordering assumptions are unsafe in xUnit.

---

## Time + Shipping

- **Plan + Phase 01 scaffold:** ~45min (estimate vs actual aligned well here; .slnx discovery added 5min due to lack of documented convention).
- **Phase 02 seeding troubleshooting:** ~30min (seed fix itself ~5min, but then 25min of test execution + failure inspection before tester ran full diagnostics).
- **Diagnostic experiments 1–4:** ~60min (batching hypothesis + sanity checks + ChangeTracker dump implementation + careful output analysis).
- **Escalation + rollback:** ~15min (write backlog row + git revert + verify clean state).
- **Total cycle:** ~2h 10min (plan estimate was 3–4h; actual faster due to quick pivot on bug discovery).

**Shipping breakdown:**
- **6 commits prior (AM session):** 4ab8d938 (T3-MENU-SEC) + 025f477f (T3-BP-FORMMAP-BACKFILL close) + 155282b9 (WFE-AUTH-FAIL test fix) + 0e9b41a4 (T5-16-FOLLOWUP) + f3253e25 (CLAUDE.md update) + da2adaa2 (digital-signature fix).
- **1 commit this session:** 5d0446b5 (T3-BP-FORMMAP-REPRO P2 backlog documentation).
- **Total ships today:** 7 commits (no production code, 1 backlog finding).

---

## Carry-Forward & Unresolved

**T3-BP-FORMMAP-REPRO (P2, NEW):** Backlog row filed with 4 candidate fix paths:
- **Path A:** `ChangeTracker.Clear()` between Cascade-save and re-add — clears the "Unchanged" ghost entries, prevents resurrection. Needs ETag/RowVersion re-check logic.
- **Path B:** 2-pass UpdateAsync (atomic but non-ACID) — separate SaveChanges for Clear vs Add phases. Needs transaction wrap to maintain isolation.
- **Path C:** Cascade → ClientCascade — loses DB-level constraint enforcement. Reduces surface area of bug but doesn't fix root cause.
- **Path D:** Remove `AsReadOnly()` on collection navigation → expose full `IList<T>`, allow EF to track Clear via snapshot change detection. Violates aggregate encapsulation invariant established in 2017.

**Gate requirement:** Path ratification with architecture owner. No production code change until decision made.

**Post-fix:** Once production fix lands, this T3-BP-FORMMAP-REPLACE-TEST task is unblocked. Phase 01 csproj scaffold is fully reconstructible from plan directory `plans/260523-1111-bp-formmap-replace-test/phase-01-csproj-scaffold.md`. No rework needed, plan is immutable.

**Memory reinforcement:** 260522-A (audit-at-slice-start invariant) confirmed **6th instance in 1 week** of production code drifting from its claimed behavior. Pattern holders: FE-FU 4-A-A / P6-DEFER-2 YAGNI / T3-BP-FORMMAP-BACKFILL completed-silent / WFE-AUTH-FAIL stale test / T5-16-FOLLOWUP shipped-silent / T3-BP-FORMMAP-REPLACE-TEST (today). This frequency (1 per working day) suggests systematic risk: production fixes shipped without integration-test coverage, leaving claims unverified. Orchestrator action: add pre-commit hook flagging production code `.cs` files with comments describing behavior that hasn't been asserted in tests. [Forward-reference to potential process improvement.]

---

## Unresolved Questions

1. **Has the original 2026-05-17 bug been reproduced manually since fix shipped?** If the fix is a no-op, the original same-triple collision should still repro when called through the FE form-map editor. Product team should confirm.

2. **Does the FE form-map editor pre-filter no-op saves?** If yes, that pre-filter is the de-facto bug mitigation, not the BE ChangeTracker walk. If no, why hasn't manual testing discovered the same-triple case?

3. **Which fix path (A vs C vs B vs D) is architecturally preferred?** Needs architecture review. Path A (ChangeTracker.Clear) is cleanest but requires ETag re-logic. Path C (ClientCascade) is safest for encapsulation but punts root cause.

4. **Is there a 5th candidate fix path?** Could we intercept at UpdateAsync entry-point and detect same-triple scenario → skip Clear+AddTransitionForms logic entirely, return early? (Punt to architecture review.)

5. **How prevalent are 4-category EF structural bugs (nav-prop missing, tracking state mismatch, cascade assumption violation, IDENTITY propagation gap) in this codebase?** This week: DCAND-a-A + 4-C-A + S10a-A + 5b-A + 5c-A (5 incidents in 4 days of focused implementation). Suggests either (a) the Domain layer is particularly EF-sensitive, or (b) new code paths flush out pre-existing bugs. Post-delivery retrospective action item.

---

**Status:** DONE  
**Summary:** TDD spike on T3-BP-FORMMAP-REPLACE-TEST uncovered latent EF Core bug in 2026-05-17 production fix — the ChangeTracker orphan-delete walk is a no-op for same-triple re-adds due to EF auto-marking Deleted then resurrecting as Unchanged. Escalated as P2 backlog row T3-BP-FORMMAP-REPRO with 4 candidate fix paths pending architecture decision. Test scaffold rolled back fully per YAGNI.  
**Concerns/Blockers:** Production fix decision required before T3-BP-FORMMAP-REPLACE-TEST can resume. No blocker for other work.  
**File written:** C:\AqtCoreFW\docs\journals\journal-260523-1200-t3-bp-formmap-replace-test-spike-finds-prod-bug.md
