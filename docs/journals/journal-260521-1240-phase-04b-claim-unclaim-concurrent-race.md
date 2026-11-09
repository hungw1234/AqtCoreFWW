# Phase 4-B: Claim + Unclaim + Concurrent-Race Integration Test

**Date:** 2026-05-21 12:40
**Severity:** Medium
**Component:** Workflow.Application / Workflow.Infrastructure — claim & unclaim task commands
**Status:** Resolved (LOCAL, awaiting user gate for push + PR)

## What Happened

Phase 4-B shipped 2 new domain commands (Claim + Unclaim) with full Application/Infrastructure/Presentation wiring + 6 TDD facts proving concurrent-race safety. Single commit `6eeaa057` (180 LOC production, 340 LOC tests). All gates GREEN; 1 High advisory (F3 race mechanism non-deterministic) + 3 Medium (exception-collapse, cosmetic duplication) + 3 Low carryover.

## The Brutal Truth

The race mechanism is elegant but *non-deterministic*. F3 uses `Task.WhenAll` across two independent HttpClient scopes, which may serialize via test scheduler quirk. Both code paths (domain exception vs RowVersion catch) return identical 409 + `GOV_WFL_016`, so the *contract* is provable — but which *mechanism* fires is luck. This feels incomplete until 4-C adds a deterministic unit test via `ISaveChangesInterceptor` to force the RowVersion path explicitly. For now: acceptable with advisory comment per codebase precedent (`SimultaneousRace` already uses the same shape).

The dual-metadata pattern on `AlreadyClaimed` error code is solid, but discovery via code-review highlights a recurring pain: `ConflictError` base ctor does NOT set `"ErrorCode"` metadata, so new error factories must explicitly set both `"ErrorCode"` (response body) + `"WorkflowErrorCode"` (provenance) or silent-default to `GOV_SYS_000`. This will repeat for every new error code until the base class is fixed. Mental overhead: non-zero.

## Technical Details

**Dual-metadata pattern (resolved):**
- New `WorkflowErrors.AlreadyClaimed(long taskId)` factory sets `"ErrorCode" = "GOV_WFL_016"` AND `"WorkflowErrorCode" = "AlreadyClaimed"` in metadata.
- `ApiResponse.Fail` reads `"ErrorCode"` from metadata for response body; missing key falls back to `GOV_SYS_000`.
- Pattern mirrors `StateConflict` precedent (S7) but was not obvious during impl — grep-verified after writing.
- **Invariant:** Any new `SharedKernel.Contracts` error code MUST set both keys or face silent `GOV_SYS_000` fallback in API responses.

**Race mechanism (F3):**
```
Two concurrent Claim requests:
A: fetch task (AssigneeId=null) → domain Claim succeeds locally → save (RowVersion mismatch) → catch DbUpdateConcurrencyException
B: fetch task (AssigneeId=null) → domain Claim succeeds locally → save (RowVersion mismatch) → catch DbUpdateConcurrencyException
                                 OR
B: fetch task (AssigneeId=A) → domain Claim throws InvalidOperationException → caught as AlreadyClaimed
```
Both paths return 409 + `GOV_WFL_016`. User-facing contract verified. Mechanism-which-fires is non-deterministic.

**Exception-collapse (M1, deferred):**
- `ClaimTaskCommandHandler` catches `InvalidOperationException` broadly (lines 72–76). Domain `Claim()` throws for two invariants: (a) already claimed (F2) and (b) task is Done/Cancelled (not in any current test).
- Both collapse to `GOV_WFL_016` (AlreadyClaimed). Caller cannot distinguish "already has assignee" from "task is terminal." Rare edge case but semantically wrong.
- Carry-forward to 4-C: split catch or pre-check `Status` before `Claim()`.

**Pool-eligibility gate (clean):**
- Handler fires `IsPoolEligibleAsync` (lines 60–65) BEFORE domain `Claim()` (lines 68–76).
- Returns 403 if caller lacks permission code AND task is pool-eligible (pre-state check not leaked as 409). Matches Q2 user decision.
- F6 proves via anonymous 401 baseline that 403 comes from handler logic, not route misconfiguration. Excellent anti-tautology.

**S7 D1-α honored:**
- `UpdateWithClaimGuardAsync` isolates `DbUpdateConcurrencyException` catch at Infrastructure boundary.
- Application never sees EF types. Zero EF leakage to layer above. Pattern reused from defer-save split (S7).

**Unclaim idempotency (F5):**
- Domain `Unclaim()` early-returns when `!AssigneeId.HasValue` (no-op).
- Handler skips `assignmentRepository.AddAsync` when state unchanged (lines 56–66).
- Audit row not written for no-op unclaim. Test asserts audit count stays at baseline.

## What We Tried

1. **Deterministic race test (Option B):** requires injecting `ISaveChangesInterceptor` into sealed `WorkflowDbContext` via `DbContextOptionsBuilder.AddInterceptors()`. Infrastructure test infra doesn't exist today. 15–30 min build-out cost.

2. **Option A (silent gap):** skip the comment. Violates S5 META-L (c) "test must prove which code path executes." Unacceptable.

3. **Option C (advisory comment + carry-forward):** add 3-line NOTE in F3 acknowledging mechanism is non-deterministic, cite precedent (`SimultaneousRace`), defer deterministic unit test to 4-C. **Chosen.**

## Root Cause Analysis

**Dual-metadata discovery late (design gap):**
- No lint rule catches missing metadata keys. `ConflictError` base ctor is silent on `"ErrorCode"` assignment. Until this slice, all error factories were single-metadata or inherited full set from exception type. Pattern not documented; had to read `ApiResponse.Fail` source to confirm the fallback.
- **Why:** `ConflictError` predates API response refactoring; base class should enforce metadata completeness.

**Race mechanism non-determinism (test infrastructure limitation):**
- `Task.WhenAll` across two independent request scopes is the standard concurrent-smoke shape in codebase (`SimultaneousRace` established precedent S5).
- Test scheduler may serialize the HttpClient calls, causing second request to see dirty state (first's mutation committed) instead of concurrent collision.
- No deterministic injection point in current infrastructure. Would require `ISaveChangesInterceptor` on DbContext to force RowVersion conflict artificially.

**Exception-collapse (design gap in domain method):**
- `Claim()` throws `InvalidOperationException` for two distinct invariants without discriminating. Application caller forced to catch broad exception or accept collapsed error code.
- Pattern acceptable for edge case (Done/Cancelled claim is rare), but deferred to 4-C for tighter contract.

## Lessons Learned

**(4-B-A) Dual-metadata pattern mandatory for new error codes:** When adding `SharedKernel.Contracts` error code, grep `ApiResponse.Fail` usage + verify the `"ErrorCode"` key is explicitly set in `WorkflowErrors.X` factory. Default `ConflictError` base ctor does NOT set it; missing key silently falls back to `GOV_SYS_000` in response body. **Invariant:** Future error factories MUST set both `"ErrorCode"` (body) + `"WorkflowErrorCode"` (provenance) in metadata dict or audit-log on commit will catch silent default. Consider adding a lint rule or base-class enforcement.

**(4-B-B) Concurrent race test = anti-tautology partial proof:** `Task.WhenAll` contract is provable (exactly 1 succeeds, others conflict with correct error code) but mechanism-which-fires is non-deterministic (domain exception vs RowVersion catch). Precedent (`SimultaneousRace`) shows this is acceptable for smoke; deterministic unit test via `ISaveChangesInterceptor` is higher bar and belongs in focused concurrency matrix (4-C scope). Test author MUST acknowledge gap with advisory comment.

**(4-B-C) S5 META-L (c) applies to new features:** Test precondition assertions MUST prove WHICH code path executes. F1–F5 all carry DB state assertions (task status, audit counts, assignment records) proving mutation occurred. F6 adds anonymous 401 baseline to exclude route misconfiguration. F3 lacks mechanism clarity; mitigation = comment + carry-forward. Pattern refined from S5 (resolver fail-CLOSED path confusion).

## Next Steps

1. **User gate (BLOCKING):** 
   - Option C (advisory comment + carry-forward) vs Option B (deterministic unit test now)?
   - Record H1/M1 carry-forward in plan or accept as deferred?
   - Confirm plan-file diffs in branch are intentional before commit.

2. **If approved (LOCAL ship):**
   - Add 3-line NOTE comment to `WorkflowTasksClaimTests.cs` F3 per reviewer Option C recommendation.
   - Create commit `feat(workflow): Claim + Unclaim commands + concurrent-race integration test (Phase 4-B)` with 6/6 F1–F6 GREEN.
   - Mark task #9 complete in TaskList.
   - Push to `feat/phase-04-slice-b-claim-unclaim-concurrent` + create PR #10X.

3. **Carry-forward to 4-C (Inbox + Concurrent Matrix):**
   - Deterministic RowVersion unit test via `ISaveChangesInterceptor` (close H1 gap).
   - Split `Claim()` exception handling to discriminate AlreadyClaimed from CannotClaimCompleted (close M1 gap).
   - Reuse `IsPoolEligibleAsync` predicate shape in inbox EXISTS subquery (ByPermission MVP).
   - Adopt F5 audit-count idempotency assertion pattern in reassign/delegate tests.

## Metrics

- **Build:** 0E / 0W across Workflow.Application/Infrastructure/Presentation
- **Application unit tests:** 493/493 (+2 from baseline)
- **Domain unit tests:** 145/145
- **Infrastructure unit tests:** 62/62
- **Integration tests:** 33p / 1f / 2skip (sole fail = pre-existing `SimultaneousRace` flake)
- **Code review:** APPROVE-WITH-NITS 9.2/10
  - Critical: 0
  - High: 1 (H1 F3 race mechanism — Option C advisory comment)
  - Medium: 3 (M1 exception-collapse, M2/M3 cosmetic duplication)
  - Low: 3 (L1 comment historical-literal, L2 N/A, L3 seed brittleness)

## Unresolved Questions

1. **H1 race mechanism:** Option C (advisory comment + carry-fwd 4-C) vs Option B (deterministic unit test now)? Recommendation: **Option C**, codebase precedent established.
2. **M1 exception-collapse:** Accept "rare edge case" rationalization or split on 4-B? Recommendation: **Carry-fwd to 4-C**, acceptable risk (Done/Cancelled claim is edge case).
3. **Plan-file diffs:** Were phase doc edits intentional staging or accidental? Confirm before commit.
4. **Anti-tautology baseline:** Should F3 assert audit count at race end? Already does (`claimAuditCount.Should().Be(1)`); verified.
