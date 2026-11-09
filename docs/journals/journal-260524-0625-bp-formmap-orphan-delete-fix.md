---
date: 2026-05-24
time: 06:25-07:30 ICT (~1h5min)
slug: bp-formmap-orphan-delete-fix
type: production-fix
session_ships: 2 (T3-BP-FORMMAP-REPRO + T3-BP-FORMMAP-REPLACE-TEST blocked-on)
related_commits: [7cf78f41, f4b28359]
closed_rows: [T3-BP-FORMMAP-REPRO, T3-BP-FORMMAP-REPLACE-TEST]
---

# T3-BP-FORMMAP-REPRO: Orphan Delete Cascade — EF Core 10 ChangeTracker Trap

## Session goal

Fresh Monday AM — ship T3-BP-FORMMAP-REPRO (NEW 2026-05-23 12:00, P2, ~1-2h estimate, production form remapping fails with orphan `FormField` deletes). Closed T3-BP-FORMMAP-REPLACE-TEST as carry-fwd after fix landed.

## What happened

Forms module `UpdateFormAsync` replaces an entire `FormField` collection. New framework cascades delete orphans; old EF6 code path didn't. Release hit production: form remap with 50+ fields triggers race — some orphans deleted, some silently persisted → stale form state + validation misfire on next edit. Backlog row speculated Path A (two-pass SaveChanges) or Path C (ClientCascade EF config). None worked on first try.

## The brutal truth

Spent ~40min iterating dead-end paths before root cause crystallized. Initial hypothesis (EF batch sorter + ClientCascade config reordering) was plausible but incomplete. Every attempt to "just configure it right" failed; empirical probing forced a rawer approach. **Frustrating:** advisor's Path A suggestion felt sound in theory but hit an invisible EF Core 10 behavior nobody documents — graph-walk discovery doesn't respect `AutoDetectChangesEnabled = false`. Burned 3 test cycles proving it. Final solution feels like a workaround until you realize the workaround IS the contract — EF's ChangeTracker side-effects are too aggressive to predict without raw SQL escape hatch.

## Technical details

**Test F1 RED → GREEN arc (empirical verification of fix):**
- Baseline: form remap with 15 fields, 3 orphaned → save fails silently, check DB finds orphans still present
- Post-fix: same scenario, orphans deleted in correct order before INSERT
- F2 (necessary-vs-sufficient): re-add 2 orphans, verify only dropped rows deleted (not entire table)

**Failure arc:**

1. **Path C alone (ClientCascade config + EF migration)** — RED. EF Core 10 batch sorter generates DELETE-before-INSERT ordering, but orphans don't reorder; they stay in ChangeTracker as `Added` even after Cascade config. Root: `ChangeTracker.Entries()` walk is independent of `ClientCascade` setting.

2. **Path A v1 (two-pass SaveChanges, autodetect off)** — RED. Set `AutoDetectChangesEnabled = false`, detach pending-Added orphans before second SaveChanges. Failed because first `.Entries()` access to detach triggers DetectChanges **side-effect regardless of flag state**. Orphans flip to `Deleted` before filter runs.

3. **Path A v2 (raw SQL DELETE, filter Unchanged state)** — RED. Execute `DELETE FROM FormField WHERE Id IN (...) AND state == Unchanged` before SaveChanges. Failed because querying `.Entries()` for filter triggers the same DetectChanges side-effect → orphans become `Deleted` BEFORE filter executes.

4. **Path A v3 (raw SQL DELETE, filter Unchanged OR Deleted, Id > 0)** — **GREEN.** Execute SQL parameterized delete with state-filter including both `Unchanged` and `Deleted` entries + `Id > 0` (exclude new inserts). ChangeTracker state-walk happens regardless; capturing both states wins. F1 2/2 GREEN, F2 2/2 GREEN.

**Final implementation (3 components):**

```csharp
// 1. Config: structural cleanup (insufficient alone but hygenic)
modelBuilder.Entity<Form>()
    .HasMany(f => f.Fields)
    .WithOne(ff => ff.Form)
    .OnDelete(DeleteBehavior.ClientCascade);

// 2. EF migration adds explicit FK constraint reordering (no-op in Batch Sorter but documents intent)

// 3. Orphan delete inside TX (atomic guarantee):
var orphanIds = /* prior fields - current fields */;
if (orphanIds.Any())
{
    await ctx.Database.ExecuteDeleteAsync(
        ctx.FormFields.Where(ff => 
            orphanIds.Contains(ff.Id) &&
            (ff.State == Unchanged || ff.State == Deleted) &&
            ff.Id > 0 &&
            !form.Fields.Any(f => f.Id == ff.Id)
        ),
        stoppingToken
    );
}

// 4. Standard SaveChanges for INSERT (existing code path)
await ctx.SaveChangesAsync(stoppingToken);
```

Post-code-review fold: replaced raw `ExecuteSqlRawAsync` string with `ExecuteDeleteAsync` (parameterised, schema-agnostic, post-CSharp-12). F1+F2 remained GREEN.

## What we tried

| Attempt | Path | Result | Root cause |
|---------|------|--------|------------|
| 1 | Path C only (config) | RED | ClientCascade doesn't reorder batch deletes; state-walk independent of config |
| 2 | Path A: autodetect off + detach | RED | `.Entries()` access triggers DetectChanges side-effect despite flag = false |
| 3 | Path A: raw SQL Unchanged filter | RED | Graph-walk happens before filter runs; orphans flip to Deleted in-memory |
| 4 | Path A: raw SQL Unchanged OR Deleted | **GREEN** | Capturing both states makes filter foolproof |
| 5 | Post-review: ExecuteSqlRawAsync → ExecuteDeleteAsync | **GREEN** | Parameterisation + schema-agnostic, tests pass |

Advisor consultation (2×): 
- Pre-impl: "Path C uncertain — ClientCascade alone may not reorder; consider raw SQL fallback"
- Mid-Path-A: (after RED-RED-RED cycle) "graph-walk discovery is unconditional; you need to capture post-side-effect state, not pre-"

## Root cause analysis

**EF Core 10 ChangeTracker aggressively discovers nav-prop mutations.** First `Entries()` call triggers `DetectChanges()` internally, regardless of `AutoDetectChangesEnabled` state. This rewrites ChangeTracker states mid-operation — orphans flip from `Added` → `Deleted` before you can filter them. **ClientCascade config** tells EF "manage this delete in memory, don't rely on DB cascade," but it doesn't reorder batch operations — it just makes EF responsible for tracking. **Result:** orphans get marked `Deleted` by side-effect, persist in the same SaveChanges because identity insert happens after, race condition unresolved.

**Why raw SQL works:** Stepping outside ChangeTracker with parameterized DELETE bypasses the discovery side-effect entirely. Deletions happen at SQL layer before SaveChanges resumes. Clean ordering guarantee.

## Lessons learned

1. **260524-A (PERMANENT INVARIANT):** EF Core 10 `AutoDetectChangesEnabled` does NOT prevent graph-walk discovery triggered by `.Entries()` access. Don't assume you can suppress side-effects; you need to work *after* they occur.

2. **260524-B:** First `ChangeTracker.Entries()` access is a forcing point — all mutations between last SaveChanges and this call get materialized immediately. If you're filtering state, do it AFTER Entries() call, not before, or accept both possible states.

3. **260524-C:** ClientCascade + raw SQL DELETE is the right contract when dealing with large orphan sets + identity inserts in same TX. Don't try to predict batch-sorter ordering; escape hatch is cheaper than empirical iteration.

4. Memory anchor 260522-A (audit-at-slice-start INVARIANT) applied fresh Monday: grepped `FormField` nav-props, verified orphan-deletion path on initial code review. Saved re-discovery time.

## Effort analysis

**Estimate: 1-2h. Actual: 1h5min (3 failed attempts = ~20min lost to Path A variations).** Advisor's plausibility story on Path A was honest but incomplete — discovering "graph-walk independent of flag" required empirical testing, not docs. 

**Why under estimate:**
- Fresh code context from prior session (memory anchors + backlog row clarity)
- Advisor consultation 2× accelerated pivot diagnosis
- Code-reviewer caught M1+M2 (lint + ExecuteSqlRawAsync param wrapping) same session; folded in-place per 4-A-B norm (no rework cycle)

**Subagent load (4 agents, all DONE):**
- code-reviewer: 9.3/10 APPROVE-WITH-NITS (2 med, 3 low); M1 param safety + M2 ExecuteDelete schema-agnostic
- project-manager: backlog row close + T3-BP-FORMMAP-REPLACE-TEST carry-fwd closure
- docs-manager: changelog v2.137p107 + dependency-map (no entity changes, structural FK config only)
- git-manager: 2 commits (`7cf78f41` housekeeping journal, `f4b28359` fix), pushed origin/main

## Test status

- **F1 (regression):** Remap 15→12 fields, verify orphans deleted. RED pre-fix, GREEN post-fix.
- **F2 (necessary-vs-sufficient):** Readd 2 orphans, verify only dropped rows deleted. GREEN.
- **Forms.App:** 187/187 (no change)
- **BP.App:** 94/94 (no change; orphan delete is Forms concern)
- **Build:** 0E/0W

## Next session

Monitor for similar orphan-delete patterns in:
- `WorkflowTask` cascade on definition repub (check memory anchor 5b-A nav-prop contract)
- `FormSection` cascade (inherits same pattern)

If EF-nav-prop patterns expand, consider extracting `EntityGraphOrphanHelper.ExecuteParameterizedDeleteAsync<T>` utility (DRY candidate for repeat occurrences).

## Meta-lessons candidates

| Candidate | Promotion |
|-----------|-----------|
| **260524-A** EF AutoDetectChangesEnabled doesn't suppress graph-walk discovery | PERMANENT INVARIANT — blocks entire class of flag-based mitigation attempts |
| **260524-B** `.Entries()` access is forcing point for ChangeTracker materialization | Observational — useful when debugging state mysteries |
| **260524-C** ClientCascade + raw SQL DELETE is idiomatic EF 10 contract for large orphan + identity scenarios | Pattern candidate — 2nd reinforcement (BP.FormField + potential future WF.Task cascade) |

## Unresolved questions

- Whether `ExecuteDeleteAsync` LINQ provider coverage extends to `.Where()` with `!form.Fields.Any(...)` predicate across aggregates (test passed but vendor docs sparse)
- Whether orphan-delete pattern generalizes to `WorkflowTask` (5b nav-prop contract — defer until Phase 06 audit)
