# Phase-04b/Workflow Application Layer Guid→long Migration — Three Handlers Exposed by Tests

**Date:** 2026-05-05 12:30 → 16:07
**Severity:** Medium (architectural, fixed pre-merge)
**Component:** Workflow Application layer (query/command handlers), Presentation routes
**Status:** Resolved (commit `708e5585` on `feature/phase-04b-workflow-followup`)

## What Shipped

Completed Workflow Application/Presentation Guid→long migration mirroring Phase-04b/B2 identity and B5b rules patterns. Single commit, 23 files, +560/-111 lines:

- **12 query handlers** migrated from Guid-based WHERE clauses to PublicId-resolve patterns (Strategy A: `SELECT PublicId AS Id` joins; Strategy B: `GetByPublicIdAsync` repository lookup)
- **1 command handler** (`CreateWorkflowDefinitionCommandHandler`): TransactionScope wrap + two-pass save fix (state IDENTITY not yet assigned pre-SaveChanges)
- **1 DTO migration** (`WorkflowDefinitionSummaryDto`): positional record→class form, `DateTimeOffset→DateTime CreatedAt` for Dapper datetime2 mapping
- **Test suite cleanup**: 9 Definition/Rbac tests un-skipped; 6 new Instance API tests added
- **Test delta**: 94 PASS/9 SKIP/0 FAIL → 110 PASS/0 SKIP/0 FAIL (architecture 26/26 PASS)

## The Brutal Truth

**Suite GREEN was a false positive.** Phase-04e reported 94 tests passing — implied Workflow handlers were correct. But the green mask hid three residual handler failures that only surfaced when we actually exercised them via integration tests:

1. `WorkflowDefinitionSummaryDto.CreatedAt` was `DateTimeOffset` but DB column is `datetime2` — Dapper ctor threw on materialization → 500 on List endpoints
2. `CreateWorkflowDefinitionCommandHandler` captured `state.Id = 0` before SaveChanges completed → transitions saved with `FromStateId=0/ToStateId=0` → GetAvailableTransitions returned empty (1 test dead on arrival)
3. `ExportInstanceAuditQueryHandler` SQL had stale reference `h.Id` but Phase-04a renamed the column to `h.InternalId` — one line breaks CSV export

**All three were dormant in Phase-04e's skipped test suite.** Running Handler-layer tests only validates handler compilation and schema shape — not handler *correctness* against real data flow. The 9 skipped tests existed pre-04b; the 6 new Instance tests I added exposed gaps the suite didn't know to check.

Worst part: none would have been caught by static analysis. Only live endpoint + data round-trip surface these. Reinforces hybrid test strategy at finalize gate — skip in DEV, run ALL at merge.

## Technical Details

**Dapper datetime2 mapping residual:**
```csharp
// WRONG (DateTimeOffset, but Dapper reads datetime2 as DateTime)
public record WorkflowDefinitionSummaryDto(
    Guid Id, 
    DateTimeOffset CreatedAt, ...);

// CORRECT (class form, DateTime from Dapper + SELECT clause alias)
public class WorkflowDefinitionSummaryDto
{
    public Guid Id { get; init; }
    public DateTime CreatedAt { get; init; }
}
// Handler: SELECT PublicId AS Id, CreatedAt, ...
```

**Two-pass save + IDENTITY gap:**
```csharp
// Phase-04a B4 creates Workflow entity, assigns state references PRE-SaveChanges:
var state = new WorkflowState(...);
var definition = new WorkflowDefinition(..., stateId: state.Id); // state.Id = 0, IDENTITY pending

// Phase-04b must wrap in TransactionScope to ensure both passes committed atomically:
using (var txn = new TransactionScope(
    TransactionScopeOption.Required,
    new TransactionOptions { IsolationLevel = IsolationLevel.ReadCommitted },
    TransactionScopeAsyncFlowOption.Enabled))
{
    await context.SaveChangesAsync(); // state.Id assigned by DB
    // Now transitions can reference state.Id correctly
    await context.SaveChangesAsync(); // transitions with valid FromStateId
    txn.Complete();
}
```

**Code review scoring:** 8.5/10 APPROVE_WITH_MINOR — 0 P0, 2 P1 (both fixed in-PR), 4 P2, 5 P3 deferred.

## What We Tried

1. **Phase-04a + Phase-04e skip-and-defer:** Shipped with 9 Workflow tests marked Skip, masking the three handler residuals. Suite reported "all active tests passing" but never tested the broken paths.

2. **Scout initial claim:** "9 tests likely created in this phase, not found in repo" — contradicted my memory. Grep verified: tests existed, were just skipped. Scout's directory scan (`tests/integration/**/`) missed actual path (`src/tests/AqtCoreFW.Tests.Integration/Workflow/`).

3. **Handler-layer-only validation:** Accepted Phase-04e report ("94 PASS") as proof-of-correctness. Forgot that Handler tests compile but don't touch DB. Operand-type clashes and Dapper ctor exceptions only surface when HTTP request → handler → repository → Dapper ctor.

## Root Cause Analysis

1. **Skip test strategy without verification gate:** Phase-04e deferred Workflow tests to Phase-04b, but never verified deferred tests are *recoverable*. If the tests were deleted between phases, we'd never know. Pattern: any skip-and-defer MUST be gated on "test runs green when un-skipped"; confirm in finalize before merging.

2. **Phase-04a domain refactor touched column names (`Id → InternalId`) but did NOT update all SQL.** The ExportInstanceAuditQueryHandler residual is a merge-conflict-like artifact: two commits touched the same schema, one updated the code, one missed. Lesson: grep for all `SELECT * FROM workflow_*` and `JOIN` patterns post-domain-refactor, not just repositories.

3. **Dapper DTOs inherit from past DTO generation patterns without verification.** `WorkflowDefinitionSummaryDto` was copied from a template without checking: "is DateTimeOffset a valid Dapper target for datetime2?" Answer: no. Lesson: when copy-pasting DTOs, spot-check the mapping pragmas (`.HasColumnType("datetime2")` on the entity) vs. DTO property types.

4. **Two-pass save (state → transitions) needed isolation guarantee.** Phase-04a B4 split state and transition creation into two logical passes; Phase-04b didn't add the TransactionScope glue. Code reviewer caught it as P1 ("state.Id might be 0 on transition creation if transaction rolls back"). Fixed in-PR.

## Lessons Learned

1. **"All tests pass" ≠ "code is correct."** Passing tests measure coverage of *active code paths*. Skipped tests don't measure anything. Phase-04e had 94 PASS (on a filtered suite) + 9 SKIP (on the full suite). True statement: "94 passing tests don't know if 9 skipped tests would pass." Corollary: finalize must run ALL tests, skip none.

2. **Scout file-location confidence varies.** Scout is strong at pattern matching within declared scope (`tests/integration/**`), weak at discovering actual directory structure if layout diverges from convention. When scout claims "file not found, likely created," verify with direct Grep before accepting the claim.

3. **Domain refactors require *all* SQL materialization checks.** Phase-04a renamed `Id → InternalId` in the entity; Phase-04b found 1 query handler still using `h.Id`. Probably more lurking in Dapper helpers or one-off SQL. Pattern: after entity rename, regex-search all `.FromSqlInterpolated`, `.FromSql`, and hand-written SQL for the old column name.

4. **Schema column type ↔ DTO property type must match.** Dapper defaults to mapping `datetime2(7)` columns to `DateTime`, not `DateTimeOffset`. When copying DTOs, verify: `[Column]` attribute on entity (usually missing) must match DTO property type. Better: keep DTOs simple (DateTime, int, string, Guid) and do conversions in handlers if needed.

5. **Sub-agent self-commit prevention held.** All 4 subagent delegations (cook, tester, code-reviewer, tester again) had explicit "no self-commit" clauses in prompts. Only git-manager (controller) created the final commit. Pattern enforcement successful across 8 sub-calls.

## Next Steps

1. **Branch merged** (pending user approval) — single commit `708e5585`, ready for PR merge to `main`.

2. **Follow-up backlog (deferred by code reviewer):**
   - **PHASE4B-WF-FU1:** `ReplaceGraphAsync` command handler — same two-pass save pattern needed (P2)
   - **PHASE4B-WF-FU2:** `CloneWorkflowDefinitionCommandHandler` — same pattern (P2)
   - **PHASE7-FE-WF-DTO:** `WorkflowDefinitionSummaryDto` breaking change (DateTimeOffset → DateTime) requires FE Orval regeneration. Not a code change, but Orval schema sync needed (P2).
   - **WORKFLOW-AUDIT-CSV-TEST:** `ExportInstanceAuditQueryHandler` only tested via integration test (POST, then /export). No unit-level CSV shape validation. Add vitest for CSV column order + quoting (P3).

3. **Architecture test enrollment:** Workflow Domain + Application now fully mirrored to Identity/Rules patterns. No new architecture gates needed; existing 26-test suite covers the shape.

4. **Phase-06 gate status:** Phase-04b complete. Phase-06 (6A + 6B + 6C + 6D) shipped concurrently on 2026-05-05; Phase-04e shipped 2026-05-05 (9 tests deferred to this phase, now resolved). No blocking deps remain.

**Lessons for future phases:** Skip + defer only when *recovery plan exists and is costed*. Otherwise, run full suite at finalize even if it means higher effort estimate. False-green suites burn time later.

---

**Status:** RESOLVED
**Commit:** `708e5585` on `feature/phase-04b-workflow-followup`
**Branch Status:** Ready for PR merge to `main` (user decision pending)
