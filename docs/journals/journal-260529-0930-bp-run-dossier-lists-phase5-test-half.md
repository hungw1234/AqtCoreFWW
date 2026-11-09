# BP-Run Dossier Lists — Phase 5 Test-Half Shipped

**Date**: 2026-05-29 09:30
**Severity**: Low
**Component**: Workflow / MyProcessedInstances endpoint + integration tests
**Status**: Resolved (test-only slice; perf-half deferred)

## What Happened

Completed test-half of Phase 5 carry-forward. NEW `WorkflowInstancesMyProcessedTests.cs` with 14 integration facts for `GET /workflow/instances/my-processed` (View 2: history-keyset). All 14 facts pass real-SQL (TestContainers), build 0W/0E, code-review 8.5/10. Test-only slice — ZERO production change. 2 commits unpushed on main, 2 ahead origin.

## The Brutal Truth

Perf-half deferred explicitly. Query-scale validation unmeasurable on dev DB (~2 history rows); without bulk-seed helper + SHOWPLAN + k6 load rig, cannot distinguish seek vs scan behavior or prove p95 latency. Silencing this in the test count would be lying — flagged carry-forward instead.

Scope-slippage nearly happened twice: (1) View 1 re-test proposal was redundant (already covered by `WorkflowTasksInboxTests` F1–F5), (2) orval-generated hook deletion trap almost landed — manual deletion of auto-generated code is regen-ephemeral + noisy.

## Technical Details

**14 Integration Facts Coverage (View 2 — `GET /workflow/instances/my-processed`):**

Authz layer: 401 (unauthenticated), 403 (no `BUSINESSPROCESS.BP.VIEW`), 200 (granted). Start-task excluded from history (visit-only). DISTINCT on transition sequence (N transitions → 1 output row). Participation filter: caller's own transitions vs system-actor (`Guid.Empty`) transitions (mixed-set test). Tenant isolation via instance JOIN — history table has NO `TenantId` column; tenant scoped through instance FK. `EntityType='BusinessProcess'` filter enforced. Soft-deleted instance excluded (`i.IsDeleted=0`). Final/completed state still appears (no terminal status filter). Current-state name + IsFinal coalesced across LEFT-JOIN arms (both definition + fallback row). Keyset pagination DESC by (InstanceId, TransitionedDate); page-2 no duplicate/skip at boundary. `hasNextPage` correct at exact `pageSize`. Malformed cursor → 400.

**Seeding Hybrid:**
- API-driven: definition creation → instance creation → initial state (FK-valid + baseline).
- EF-direct: history rows materializing later states.
- Raw-SQL: soft-delete / final-state mutations.
- Fresh `Guid` tenant + userId per test → participation set exact, no DB pollution.

**Bug Found by Running (Reinforces 260528-A):**

Two multi-definition tests hit 500 `GOV_SYS_000`. Root cause: `WorkflowDefinitionConfiguration` has unique index `(TenantId, Name)`; create handler lacks pre-existence check → DB unique violation bubbles unhandled. Ten single-definition tests passed identical payload → near-conclusive discrimination. Confirmed by reading config. **Fix:** definition `Name` appends `Guid.NewGuid():N` per seed.

**Code-Review Med-1 (Anti-Tautology Catch):**

`SystemActorTransition_NotVisibleToUser` original passed vacuously — caller had 0 own history so result empty regardless; query has NO special `Guid.Empty` predicate, just `TransitionedBy=@UserId`. Reworked: seed caller's instance + a `Guid.Empty`-only instance, assert caller sees own but NOT system-actor (non-empty set → genuine discrimination). Med-2: added `isFinal==false` to cover 2nd LEFT-JOIN COALESCE arm. Low-2 + Obs-1 declined (KISS + sibling-consistency + YAGNI).

## What We Tried

1. **Scope-guard (260522-A empirical):** Grepped all named artifacts (endpoint, controller action, indices, migration, test base) — all exist, row fresh (today), audit light but verified.
2. **Scope-correction A:** Proposed re-test of View 1 (inbox) — found already fully covered by `WorkflowTasksInboxTests` F1–F5 (assignee/pool arms, terminal, cross-tenant, authz). Dropped redundancy.
3. **Scope-correction B:** Advisor caught orval-hook deletion trap — hook in auto-generated file (`web/src/api/generated/.../workflow-instances.ts`); deletion ephemeral + regen-noisy. Kept hook, dropped fold-in.
4. **Perf-half assessment:** Query-plan SHOWPLAN + bulk-seek proof + k6 load → all require seeding infrastructure not yet built. Dev DB scale (2 history rows) makes seek vs scan + p95 latency unmeasurable. Explicit deferral + carry-forward ticket.

## Root Cause Analysis

**Unique-index 500:** Create handler never validated definition pre-existence before INSERT. Single-definition tests masked this (idempotent payload on fresh tenant). Multi-definition branch exposed it immediately on TestContainers + real DB schema.

**Tautology in original assertion:** Test payload design (caller 0 own transitions) made the filter-discrimination unexercisable. Requires deliberately un-exclusive seeded set to make the assertion load-bearing.

**Perf-deferral necessity:** Dev workstation cannot replicate production scale. Seek vs scan discrimination requires thousands of history rows; p95 latency requires sustained load + DB warm cache. Attempting to measure on 2 rows would be cargo-cult testing.

## Lessons Learned

- **(Reinforce 260528-A)** Tester "pass" only after RUNNING. Unique-index 500 was invisible to static review; surfaced only on real-DB multi-definition tests. Build-clean ≠ test-clean.
- **(Advisor value)** Advisor caught orval-hook deletion trap + pushed explicit perf-deferral *before* substantive code work. Both blocked noise + wasted effort.
- **(260522-A at-depth)** Exclusion checks ("actor not visible") are vacuous unless result set otherwise non-empty. Requires deliberately mixed seeding + non-empty assertion set for genuine discrimination.
- **(plans/ gitignored)** Correct design — never force-add plan working artifacts to git. Local sync-back only; phase status reflects what shipped, not plan draft.

## Next Steps

1. **Carry-forward:** `BP-RUN-DOSSIER-LISTS-PHASE-5-PERF` — bulk-seed helper (seed 1000+ history rows) → SHOWPLAN seek proof → k6 load test → read-time vs projection verdict.
2. **Push decision:** 2 commits (`b54480db` + `dfdb9d60`) waiting on user. Main is 2 ahead origin, tests 14/14 pass, review 8.5/10.
3. **Backlog state:** Phase 5 test-half complete; perf-half + perf-verdict deferred to carry-forward ticket (unblocks other work, p95 latency non-blocking).

## Unresolved Questions

- Does user approve push of `b54480db` + `dfdb9d60` now, or defer with perf-half?
- Perf-half: bulk-seed scale (1k? 10k rows?) — advised by DBA or arbitrary?
