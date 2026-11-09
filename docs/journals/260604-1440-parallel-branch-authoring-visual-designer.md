# Parallel-Branch Authoring (Visual Designer) — Definition-Time Fork/Join Editor

**Date**: 2026-06-04 14:40
**Severity**: Medium (5 phases, architectural decision point, real gotchas)
**Component**: Workflow Visual Designer (BE contract + FE authoring UI + integration)
**Status**: Resolved — commit 4c5f9edb shipped (NOT pushed yet), 5 phases closed, DLL-lock resolved

## What Happened

Parallel branches (fork → N children → continuation join) have had a runtime engine for months. This week closed the authoring gap: users can now define fork points, assign child branches (conditions + target states), define resolution rules (all-complete, any-complete, N-of-M), and save the graph. Involved 5 phases: BE contract + persistence, BE validator + read-back, FE contract + wire-up, BE integration tests, FE gateway node + editor. Plan in `plans/260604-1440-parallel-branch-authoring/`.

**Key decision:** branches persist NAME-KEYED (not raw IDs), folded into the existing `ReplaceGraphAsync` replace-all transaction. Why: `WorkflowParallelBranch` columns are bare `bigint` with NO FK to WorkflowStates; the graph save re-generates fresh IDENTITY values, so name-keying + post-save `stateIdByName` map (mirrors transition handling) defeats dangling references. Manual pre-delete of branches prevents orphans; children + resolutions cascade via real FKs.

**Shipping scope:** 42 parallel-branch files only (BE contract/commands/handlers/validators, FE types/hooks/editor components, 1 migration). i18n keys are UNCOMMITTED (branch UI defaults use inline fallbacks) pending BP-run commit landing (en.json/vi.json have pre-existing unrelated changes).

## The Brutal Truth

**GOTCHA #1 hit hard:** integration tests failed 7/7 with 422 (not 500s or validation soft-errors). Cost 1.5h to diagnose. Root cause: `SaveDefinitionGraphCommandValidator` enforces an EntityType-scoped state-NAME vocabulary. For "Generic" the allowed set is hardcoded to 10-code taxonomy [Draft, Submitted, UnderReview, Assigned, Approved, Rejected, Returned, ReturnedForRevision, Closed, Cancelled]. My test graphs used arbitrary names (Fork/TaskA/TaskB/Done). Validator rejected everything, including backward-compat tests with zero branches. The 422 response body lists allowed names—should have captured that FIRST instead of guessing at validator intent.

**GOTCHA #2 was mechanical agony:** DLL-lock. The running `dotnet run` watcher (PID holding Workflow DLLs) blocked building `test` projects (MSB3027). Stray `dotnet watch` auto-restart re-acquired the lock mid-test-run. Had to stop BOTH the watcher process AND the background API exe, run the full BE build + integration tests, then restart. This ate 30 min of "why won't it compile" before realizing lock errors are MSB3027 (not CS errors).

**LESSON from both gotchas:** when tests fail ALL-7 with 422 + MSB3027, the real problem isn't in the code you wrote—it's environmental. Capture the response body early (422 always has meaningful detail). Stop the watcher process explicitly, don't assume "stop" = "stopped everywhere".

## Technical Details

**The spine decision — name-keying in replace-all:**
```
SaveDefinitionGraphCommand
├─ ReplaceGraphAsync (EXISTING: state replace-all in tx)
│  ├─ DELETE branches WHERE DefinitionId=X (manual)
│  ├─ DELETE states (cascade kills children+resolutions)
│  ├─ INSERT fresh states (new IDENTITY values)
│  └─ Parse branches by NAME + resolve to new state ids
│      └─ INSERT branches with fresh ids
└─ stateIdByName map (post-save, mirrors transitions)
```

Why NOT separate endpoints: Each branch holds `BranchingStateId` (the fork point) + `ContinuationStateId` (the join point). Both are `bigint` columns with zero FK to enforce them. If you `POST /states` and get new IDs, then separately `POST /branches`, the branch insert uses stale IDs. Replace-all INSIDE one TransactionScope guarantees atomicity; name-keying defers resolution until after fresh IDs are assigned.

**Migration 20260604083725:**
- `WorkflowStates.ForkPositionX/Y` (nullable float, EF ValueTuple, FE computed)
- The single draggable fork diamond (continuation IS visual join; nulls → FE auto-derives position)

**Orphan obsolete `CreateParallelBranchCommand`:**
- Long-keyed insert-only, incompatible with replace-all semantics
- Marked `[Obsolete]` (not deleted; breaks ref history)

**Contract asymmetry (GET vs POST):**
- GET returns `long` state IDs (raw from DB)
- POST/save sends `string` state names (FE resolves from visual node labels)
- Matches existing transition asymmetry pattern

**Name vocabulary gotcha (CRITICAL TEST-FIXTURE RULE):**
```
// WRONG (was my first attempt):
states: [
  { name: 'Fork', type: 'decision' },
  { name: 'TaskA', type: 'activity' },
  { name: 'Done', type: 'join' }
]
// Result: 422 "Name 'Fork' not in allowed set"

// CORRECT:
states: [
  { name: 'Draft', type: 'decision' },
  { name: 'UnderReview', type: 'activity' },
  { name: 'Approved', type: 'join' }
]
// Result: 200 OK (matches Generic entity-type taxonomy)
```

## What We Tried

1. **Integration run #1:** All 7 tests 422'd → assumed branch name-keying or resolution logic broken → profiled validator logs
2. **Captured 422 body:** listed 10 allowed names → realized tests used forbidden names (Fork, TaskA, Done)
3. **Refactored all test graphs:** remapped to canonical taxonomy (Draft, UnderReview, Assigned, Approved)
4. **Re-ran after MSB3027 lock:** stopping `dotnet watch` + exe → full rebuild → tests 7/7 pass
5. **Code-review flagged unique-index gotcha:** two indexes had NO pre-persist dedup guards → DbUpdateException → raw 500
6. **Added seenResolutionTypes/seenChildNames dedup guards** in `BuildBranchSpecs` handler (mirrors RB3 state guard)

## Root Cause Analysis

**Why GOTCHA #1 happened:**
Test fixture authoring is a contract-discovery problem, not a logic problem. The validator enforces vocabulary **by design** (entity-type-scoped status taxonomy prevents invalid state names). But "by design" wasn't surfaced in the code comments—only in the 422 body. Lesson: read response bodies first, not stack traces.

**Why GOTCHA #2 happened:**
Integration tests touch the domain assembly (Workflow.dll). Running `dotnet run` or `dotnet watch` in a separate terminal acquires a file lock (even after "stop" in the CLI—the background process persists). MSBuild can't overwrite locked files. The "just rebuild" instinct doesn't work when the build target is locked. Must explicitly kill the process, not just type Ctrl+C.

**Why index dedup guards were missing:**
The code review spotted two indexes (`UX_WorkflowBranchResolutions_ChildId_ResolutionType`, `UX_WorkflowBranchChildren_ParallelBranchId_Name`) reachable from the UI without pre-insert validation. Default new resolution to 'Approved' was two clicks away. A second click would reuse the same resolution type → DbUpdateException → 500. The fix (pre-persist dedup guard) mirrors the proven pattern (RB3 state guard) but isn't obvious from the schema alone—needs code review to spot.

## Lessons Learned

1. **Workflow test fixtures MUST use entity-type canonical vocabulary.** Draft/UnderReview/Assigned/Approved etc. are NOT arbitrary names; they're the allowed set per entity type. If a test uses Fork/Done/Custom, the validator rejects it with 422. Capture the response body; it lists the allowed set. Don't guess at validation rules.

2. **Every DB unique index reachable from a write path needs pre-persist dedup.** The UI can two-click to violate a unique index (new resolution, submit, repeat). If unguarded, the DbUpdateException becomes a raw 500. Pre-persist guards (seenResolutionTypes + seenChildNames) make it a clean 422 instead. Code review catches this; schema alone doesn't.

3. **Process locks beat retry logic.** MSB3027 "file is locked" can't be solved by rebuilding. Must stop the owning process (dotnet watch, dotnet run). The lock persists after CLI "stop" if the background task wasn't fully terminated. Verify no related exe's linger in Task Manager.

4. **Name-keying in replace-all avoids dangling references.** When IDs are regenerated atomically (replace-all in one tx), use the post-action name→id map to resolve references. This isn't a performance hack; it's a correctness requirement for any entity with no FK to guard the reference.

5. **Response body details (422 list, 500 stack) should be captured before deeper diagnostics.** A 422 response body explicitly lists what went wrong; a 500's inner exception reveals the real failure. Reading these first-order facts saves time on speculation.

## Verification

**BE source:** 0 errors, 0 warnings
**Workflow.Application:** 616/616 tests pass (validator RB1-RB4 new guards + handler name/dup guards)
**Infrastructure:** ReplaceGraph integration tests 4/4 pass
  - Cascade-delete: raw-SQL COUNT(orphans) = 0
  - State ID re-resolution: branch FK points to fresh IDENTITY after re-save
**Integration `SaveDefinitionGraphBranchHttpTests`:** 7/7 pass (backward-compat + 3-child fork + resolution types)
**FE tsc:** 0 errors, 0 warnings
**FE vitest:** 43/43 pass
**FE eslint:** clean (1 missing-dep warning in Phase 05 gateway node caught + fixed by subagent)

## Unresolved Questions / Carry-Forward

- **Browser E2E:** hand-run author fork → save → reload → re-render once API is up; optional Playwright @smoke (non-gating)
- **TransactionScope atomic-rollback:** untested (all 7 integration tests are happy-path). Worth a fault-injection test (honest coverage gap, mirrors proven assignment-path gap)
- **i18n keys:** `workflow.branch.*` keys in en.json/vi.json are UNCOMMITTED (inline fallbacks active). Must commit when BP-run commit lands (currently blocked by pre-existing unrelated keys in en.json/vi.json)

---

**Status:** DONE | **Summary:** Parallel-branch definition-time authoring shipped (5 phases, 42 files). Spine decision: name-keyed branches in atomic replace-all transaction. Two gotchas (vocabulary validation + DLL-lock) fixed; code-review flagged + fixed index dedup guards. 7 keystones passing (integration tests, TS compile, vitest, eslint). Ready for hand-run E2E once API restarts. i18n deferred pending BP-run commit.
