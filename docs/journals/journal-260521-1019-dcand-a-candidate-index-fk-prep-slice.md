# DCAND-a Prep Slice Shipped: Missing FK Constraint Found and Fixed

**Date:** 2026-05-21 10:19
**Severity:** Medium
**Component:** Workflow.Domain / Workflow.Infrastructure (EF migration)
**Status:** Complete (LOCAL, not pushed)

## What Happened

Shipped **DCAND-a prep slice** (`feat/dcand-a-candidate-index-fk`, commit `3dcac33c`) — explicit `HasOne(...).WithMany().HasForeignKey(WorkflowTaskId).OnDelete(Restrict)` EF Core configuration + migration `20260521033251_AddWorkflowTaskCandidateIndexForeignKey` on `WorkflowTaskCandidateIndex→WorkflowTask` one-way nav-prop. Prep for Phase 4 Inbox query refactor (replaces `CandidateRefsJson` JSON-read fallback with idiomatic `EXISTS`-over-nav-prop pattern).

## The Brutal Truth

**Memory got the premise wrong.** S10a memory stated FK constraint "already existed" because the C# property `WorkflowTaskCandidateIndex.WorkflowTaskId` was present. Wrong. Planner scout caught it by reading the actual initial migration `20260519015732` — zero `table.ForeignKey()` was emitted. The column existed but was **orphaned at the DB level**. This slice adds the real schema-level FK that was missing for 2 days post-S1. Fortunately, Phase 4 doesn't land until the column is actively used; the gap was discoverable only via migration file audit, not runtime.

## Technical Details

**Migration Up (lines 13-21):**
```sql
AddForeignKey(
    name: "FK_WorkflowTaskCandidateIndex_WorkflowTasks_WorkflowTaskId",
    schema: "workflow",
    table: "WorkflowTaskCandidateIndex",
    column: "WorkflowTaskId",
    principalSchema: "workflow",
    principalTable: "WorkflowTasks",
    principalColumn: "Id",
    onDelete: ReferentialAction.Restrict);
```

**Entity nav-prop (lines 21-27):**
```csharp
/// One-way nav-prop to parent WorkflowTask (DCAND-a, plan 260521-1008).
/// Child→parent direction only — NO reverse ICollection on WorkflowTask
/// (preserves rowversion safety invariant; see WorkflowTask.cs:43-48
/// remarks on bug-33d4973f).
public WorkflowTask? WorkflowTask { get; private set; }
```

**Test gates:**
- F1: FK exists in `sys.foreign_keys` post-`MigrateAsync`
- F2: orphan INSERT `WorkflowTaskId=999999` rejected with `SqlException.Number=547` (anti-tautology per S5 META-L(c))
- F3: round-trip Up/Down/Up restores FK count=1 (hardcoded predecessor migration `20260520074505_DropWorkflowTransitionRequiredPermissionCode` verified)
- F4: `COUNT(*)` orphans post-apply=0 sanity gate
- F5: reflection-asserts no reverse `ICollection<WorkflowTaskCandidateIndex>` on `WorkflowTask`

**Build:** 0 errors/0 warnings; Workflow.Infrastructure 62/62 (+5 new); Domain 145/145; Application 491/491.

## What We Tried

N/A — this is a prep slice with clear scope: add FK constraint discovered to be missing, mirror P2b single-concern discipline, no runtime behavior changes.

## Root Cause Analysis

**Memory-stated premise skipped the migration file audit.** S10a noted "FK column exists" → "no migration" inference without grepping the initial migration's `Up` method. The column was additive in S1 but shipped without its DB-level FK constraint. Subtle because EF Core allows orphaned FK columns (deferred-save patterns can tolerate them temporarily), but Phase 4 requires an idiomatic EXISTS join over the nav-prop — which demands the explicit FK config exists.

## Lessons Learned

### NEW META-LESSON (DCAND-a-A): Memory-stated DB structural claims need scout migration-file verification
When memory asserts a DB-level fact (FK constraint, index, trigger, default), planner subagent MUST `grep` the migration files (not just snapshot), verify the Up/Down methods directly. Symptom: "column exists → no migration needed" is conflation of C# property presence with DB schema presence. Mirrors **S5 META-L(d)** (memory drift) and **S5 META-L(a)** (subagent under-estimates without grep evidence).

**Preventive:** Add checklist item to planner prompt: "If memory claims a structural migration skip, read the initial migration file Up method; verify the operation count via grep (ForeignKey/Index/Default)."

### NEW META-LESSON (DCAND-a-B): Prep-slice discipline (P2b precedent) sustains quality under scope pressure
Single concern (FK only), additive only (one migration), no behavior changes (Phase 4 query work deferred), ~0.5d effort. Resists "while I'm in here" temptation (e.g., adding JSON-read replacement fallback, reshaping Phase 4 schema ahead of time). Reusable pattern: when a forward-phase needs a small foundation fix, file separate prep-slice with own plan dir + branch + MR.

### Reinforced: One-way nav-prop is non-negotiable for rowversion safety
`WorkflowTask.cs:43-48` remarks explicitly forbid reverse `ICollection<WorkflowTaskCandidateIndex>`. EF deferred-save pattern on `ClaimTaskCommandHandler` reads `Task.RowVersion` atomically; if Task had a loaded collection, the collection's stale state would invalidate the read. `WithMany()` (not `WithMany(t => t.Candidates)`) enforces one-way at EF config time.

### OnDelete=Restrict is the only safe choice
Both `WorkflowTask` and `WorkflowTaskCandidateIndex` soft-delete (have `DeletedAt`). Hard-delete is never called. `Restrict` fails-loud if anyone attempts a hard orphaning (rare, but defensive). Alternative (`SetNull` or `Cascade`) would hide bugs.

## Next Steps

1. **User gate for push + PR creation** on `feat/dcand-a-candidate-index-fk` (awaiting user decision)
2. **Phase 4 Inbox slice** to replace `GetTasksByAssigneeQueryHandler` JSON-read with `EXISTS`-over-nav-prop query using the new nav-prop
3. **Phase 4 Claim slice** to add runtime concurrent-Claim integration test (F5 here is structural-only; true concurrency race → `DbUpdateConcurrencyException` → 409 per RT2-5)
4. **Deployment notes:** add out-of-band orphan pre-flight runbook step (DBA query pre-apply to validate 0 orphans — F4 is post-apply sanity, not true pre-flight)
5. **Future resilience:** consider replacing hardcoded `PreviousMigrationName` with `_ctx.Database.GetMigrations().SkipLast(1).Last()` if migration interleaves occur (defer; current value verified correct at slice time)

## Code Review Verdict

**APPROVE 9.4/10** (0 Critical / 0 High / 3 Medium / 3 Low — all carry-forward to Phase 4, non-blocking).

- Rowversion safety invariant preserved (one-way nav only)
- Migration scope honesty (exactly 2 schema ops)
- Anti-tautology test discipline (F2 orphan + F4 sanity)
- Plan adherence 100%

---

## Unresolved Questions

1. F4 "pre-flight gate" — should the XML doc be reworded to "post-apply sanity check" to clarify that `MigrateAsync` runs before F4 executes? (Cosmetic; non-blocking.)
2. Dynamic migration predecessor lookup (F3 `PreviousMigrationName` hardcoded) — should future Phase 4 slices use `_ctx.Database.GetMigrations()` instead to survive interleaves? (Defer; not in DCAND-a scope.)
3. Phase 4 concurrency test ownership — does the concurrent-Claim integration test (F5 strengthen) belong to the Inbox slice or a separate Claim slice? (Planner decision; out of scope.)

