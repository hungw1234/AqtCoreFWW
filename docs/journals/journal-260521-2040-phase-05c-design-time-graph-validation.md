---
date: 2026-05-21 20:40 ICT
phase: PHR-V2 Phase 5c
version: v2.137p90
ship-type: feat (new validator surface + 3-handler wiring)
slice: 5c (independent of 5a/5b — design-time prevention)
pr: 112
merge-commit: 3d27c258
---

# Phase 5c Design-Time Graph Validation R1-R4 + 3-Handler Wiring

## What

Single shared `IWorkflowGraphValidator` (pure-function over `WorkflowDefinition` entity graph) wired into THREE publish surfaces:

- **SaveDefinitionGraphCommandHandler** — R1 reachability only (Save command does NOT carry assignment config per ratified Q3 split; R2-R4 unenforceable at Save).
- **ToggleDefinitionActiveCommandHandler** — R1-R4 on Activate path; Deactivate skips (legal to deactivate a known-broken graph).
- **ActivateBusinessProcessCommandHandler** — R1-R4 via `IWorkflowModuleClient.ValidateDefinitionGraphAsync` hop per Architecture rule (BP MUST NOT directly reference Workflow.Application).

Rules:
- **R1** reachability: each non-final state has ≥1 human-actionable outgoing transition (not all IsRecall + AutoTimeout).
- **R2** assignment present: each actionable transition has `WorkflowTransitionAssignment` row with ≥1 clause.
- **R3** ByPermission canonical-at-write: codes Trim + ToUpperInvariant; HashSet lookup vs `PermCodes.All` (126 codes); **validator writes back canonical RefsJson via new Domain mutator** `WorkflowTransitionAssignmentClause.SetCanonicalRefsJson` — persists via new tracking-load + `SaveChangesAsync` on valid graph (`InProcessWorkflowModuleClient.ValidateDefinitionGraphAsync` Option A).
- **R4** reject NotSupported: clause Mode ∈ {PreviousHandler, ByRole, ByGroup, ByOrgUnit} OR CombineMode = Intersect.
- **R5** ByOrgUnit warning channel skeleton (XMLdoc only per plan §YAGNI — no producer until ByOrgUnit graduates from R4 reject).

**Endpoints:** none new (3 existing handlers gated)
**Error code:** `GOV_WFL_019 = GraphValidationFailed` (422 ValidationError); structured `metadata["GraphErrors"]` per-rule violations (RuleId/StateName/TransitionName/Message) for FE designer inline-highlighting.
**Tests:** V1-V11 + V5b validator unit (12 facts); W-S1/S2 + W-T1/T2/deactivate-skip + W-A1/A2 handler-wiring (anti-tautology Received(N) preconditions per S5 META-L (c)); C-T1/C-T2 client SaveChanges anti-tautology; I5/I6/I7 integration full HTTP path with `.IgnoreQueryFilters().AsNoTracking()` ground-truth reads per 4-C-A invariant.
**Behavior change:** publish/activate now reject malformed graphs at design-time (was: silently allow → fail at runtime resolver). Existing valid graphs unaffected.

## Why

1. **Design-time prevention** — Phase 3 fail-CLOSED resolver returns 403 on runtime misconfig; 5c catches malformed graphs at the publish gate BEFORE runtime ever sees them. Closes class of UX dead-end ("instance with no actionable task").
2. **R3 canonical-at-write** — runtime resolver at `WorkflowHandlerResolver.cs:69-74` passes `code` from `ParseRefs(clause.RefsJson)` STRAIGHT to `CandidatePredicate` without canonicalization. If DB stores mixed-case, runtime lookup vs canonical `PermCodes.All` (uppercase) could false-deny (UX bug, fail-CLOSED). 5c canonicalizes AT publish to eliminate the false-deny class entirely.
3. **Cross-module hop discipline** — BP Activate validates via `IWorkflowModuleClient`. Architecture invariant preserved: BP module does NOT reference Workflow.Application directly. Verified via Architecture.Tests 101/101.

## Code changes

| File | Change |
|------|--------|
| `SharedKernel.Contracts/WorkflowErrorCodes.cs` | NEW `GraphValidationFailed = "GOV_WFL_019"` (next slot after `NotImplementedMode = "GOV_WFL_018"`) |
| `Workflow.Contracts/IWorkflowModuleClient.cs` | NEW `ValidateDefinitionGraphAsync(long, ct) → Task<WorkflowGraphValidationOutcome>` + DTOs `WorkflowGraphValidationOutcome` + `WorkflowGraphViolationDto` |
| `Workflow.Domain/Entities/WorkflowTransitionAssignmentClause.cs` | NEW `SetCanonicalRefsJson(string)` Domain mutator (only public mutation surface; preserves private setter contract) |
| `Workflow.Domain/Errors/WorkflowErrors.cs` | NEW factory `GraphValidationFailed(IReadOnlyList<GraphRuleViolation>)` — dual-metadata `"ErrorCode"` + `"WorkflowErrorCode"` = `"GOV_WFL_019"` per 4-B-A INVARIANT |
| `Workflow.Domain/Validation/WorkflowGraphValidationReport.cs` | NEW Domain records `WorkflowGraphValidationReport` + `GraphRuleViolation` (placed in Domain to avoid Domain→Application cycle when referenced from `WorkflowErrors`) |
| `Workflow.Application/Validation/IWorkflowGraphValidator.cs` | NEW interface — two methods `ValidateForSaveAsync` (R1 only) + `ValidateForActivateAsync` (R1-R4) |
| `Workflow.Application/Validation/WorkflowGraphValidator.cs` | NEW pure-function impl; static lazy `_permCodeSet` HashSet (OrdinalIgnoreCase+Trim, 126 codes from `PermCodes.All`); static `_notSupportedModes` set; `CheckByPermissionCodesAndCanonicalize` in-memory writeback |
| `Workflow.Application/Repositories/IWorkflowDefinitionRepository.cs` | NEW `GetForValidationAsync(long, ct)` tracking-load + `SaveChangesAsync(ct)` (distinct from existing NoTracking `GetOutgoingWithAssignmentsAsync` used by Slice 5 runtime resolvers — preserved) |
| `Workflow.Application/WorkflowApplicationModule.cs` | `+services.AddScoped<IWorkflowGraphValidator, WorkflowGraphValidator>()` |
| `Workflow.Application/Commands/SaveDefinitionGraph/SaveDefinitionGraphCommandHandler.cs` | Inject `IWorkflowGraphValidator`; call `ValidateForSaveAsync` (R1-only filter) on persisted graph; fail → rollback via existing SaveChanges failure path |
| `Workflow.Application/Commands/ToggleDefinitionActive/ToggleDefinitionActiveCommandHandler.cs` | Split `HandleActivateAsync` / `HandleDeactivateAsync`; activate path: load definition via `GetForValidationAsync` (tracking) → `ValidateForActivateAsync` (R1-R4) → `definition.Activate(...)` → SaveChanges. R3 canonical writeback rides existing SaveChanges atomically with Activate mutation. |
| `Workflow.Infrastructure/Persistence/WorkflowDefinitionRepository.cs` | Impl `GetForValidationAsync` — 2 tracked queries with Include/ThenInclude chain (states + outgoing transitions + assignments + clauses); impl `SaveChangesAsync` |
| `Workflow.Infrastructure/Clients/InProcessWorkflowModuleClient.cs` | Impl `ValidateDefinitionGraphAsync` — uses `GetForValidationAsync` (tracking); calls `SaveChangesAsync` ONLY when `report.IsValid` (Option A); Domain→Contracts DTO mapping |
| `BusinessProcess.Application/Commands/ActivateBusinessProcess/ActivateBusinessProcessCommandHandler.cs` | Inject `IWorkflowModuleClient`; gate via `ValidateDefinitionGraphAsync(bp.WorkflowDefinitionId, ct)` AFTER intake-form check BEFORE `bp.Activate(...)`. Error build: ValidationError + `Metadata["ErrorCode"]` overwrite + `WithMetadata("WorkflowErrorCode")` + `WithMetadata("GraphErrors")` (per M1 fold — mirrors Domain factory shape) |

## Test gates

| Suite | Baseline | Final | Delta |
|-------|---------|-------|-------|
| Build | 0E/0W | 0E/0W | unchanged |
| Workflow.Application.Tests | 510/510 | 527/527 | +17 (V1-V11 + V5b + W-S1/S2 + W-T1/T2 + deactivate-skip) |
| Workflow.Infrastructure.Tests | 66/66 | 68/68 | +2 (C-T1/C-T2 client SaveChanges anti-tautology) |
| BusinessProcess.Application.Tests | 90/90 | 94/94 | +4 (W-A1/A2 + existing ctor patches) |
| Architecture.Tests | 101/101 | 101/101 | unchanged (verified NO BP→Workflow.Application direct ref) |
| Workflow.Integration.Tests (G3) | baseline preserved | I5/I6/I7 3/3 PASS | +3 (Testcontainers SQL, 15s) |
| G1 WorkflowExecuteTransitionAuthzGateTests | 5/5 | 5/5 | preserved |
| G5 WorkflowFineResolverGateTests | 5/5 | 5/5 | preserved |
| A6/A7 wire-format spec-lock | preserved | preserved | unchanged |
| EF has-pending-model-changes | false | false | no DbContext/Entity/Migration files in diff |

## Code-review cycle

- **Pass 1 (9.2/10 APPROVE-WITH-NITS):** M1 BP-side error missing `GraphErrors` metadata key + M2 R3 canonical-at-write writeback NOT implemented (plan §38 spec gap). Both fold-now ratified.
- **Pass 2 (9.5/10 APPROVE):** M1 + M2 CLOSED. 4 Low items all carry-fwd Phase 6: L-NEW-1 end-to-end DB-roundtrip canonical persistence proof; L-NEW-2 phase-doc carry-fwd notes housekeeping; L-NEW-3 stale "NoTracking" comment (CLEANED in-session orchestrator-direct, 1 line); L-NEW-4 2 tracking SELECT round-trips on cold admin path (negligible).

## Decisions (ratified by user)

| ID | Decision | Rationale |
|----|----------|-----------|
| Q3 (5c context) | R1-only at Save; R2-R4 at Activate paths | Save command does NOT carry assignment config — R2-R4 unenforceable until Activate |
| Q4 | Pre-flight audit kit ratified (ops responsibility) | Audit kit at `plans/reports/audit-260521-1715-phase-05c-graph-validation-preflight.md`; ops re-syncs Q1 `@ValidCodes` to actual 126 before prod run |
| Slicing | Monolithic 5c (1 PR) | User 3rd consecutive fresh-context override on 7-ship/19:47 day; 5a precedent (9.6/10 clean outcome under similar override) |
| M1 fold | Fold now per 4-A-B | Trivial 1-line + W-A1 test mirror; same pattern (4-B-A); no new decision |
| M2 fold | Scope-expand to make canonicalization actually persist | Empirical primary-source override: F1 initial-scope was phantom feature (NoTracking load → silent no-op); user ratified scope-expand to new tracking-load method preserving NoTracking-path callers |

## New META-LESSONS

**(5c-A) PERMANENT INVARIANT — Plan structural claims about EF tracking/persistence MUST grep `Ctx.Query` vs `Ctx.TrackingQuery` in the specific repository method BEFORE committing.** 4th incident in 3 days (joins DCAND-a-A nav-prop migration-free claim / 4-C-A model-cache contamination / S10a-A nav-prop pending-changes). Plan §38 said "validator writes back; handler persists" assuming EF tracking; reality at `WorkflowDefinitionRepository.cs:134` (`Ctx.Query<>().AsNoTracking()` via `ModuleDbContext.cs:92` shared helper) + `InProcessWorkflowModuleClient.ValidateDefinitionGraphAsync` lacked SaveChanges → writeback would be silent no-op. Future-slice planner MUST grep the EXACT repository method that feeds the validator BEFORE asserting "persists naturally". Documented as orchestrator INVARIANT 4.

**(5c-B) Implementation agents may surface primary-source contradictions to plan claims — orchestrator MUST reconcile, not override.** F1 first impl-agent run halted with BLOCKED status citing file:line evidence (NoTracking load + no SaveChanges). Orchestrator initially staged scope-expand workaround via second impl-agent run; that succeeded by adding a NEW dedicated tracking-load method `GetForValidationAsync` instead of modifying the existing NoTracking method (3 Slice 5 callers preserved). The pattern: when impl-agent presents primary-source file:line evidence contradicting plan/advice, re-ratify scope with user BEFORE forcing the original path. Trust empirical evidence over plan assumptions.

**(5c-C) Two-pass code review on fold-now requests yields 9.5/10 with closure evidence; matches 5a precedent.** Pass 1 (9.2/10) flagged 2 Med + 4 Low; user ratified M1+M2 fold-now. Pass 2 (9.5/10) verified closure with explicit invariant check table (10 invariants, 9 ✓ + 1 ⚠ for unspecified-required Phase-6 carry-fwd documentation gap); 4 NEW Low carry-fwd Phase 6, zero NEW Critical/High/Med. Cycle time pass-1 → fold → pass-2 = ~35min; vs single-pass-with-deferred-Meds = ~5min savings but lose Medium closure proof. Future-slice trade-off: 2-pass when Meds are folded; 1-pass when Meds defer.

## Carry-forward (Phase 6)

1. **L-NEW-1 end-to-end DB-roundtrip canonical persistence proof:** add Testcontainers I-fact in `WorkflowGraphPublishValidationTests.cs` (or new file) — full chain: HTTP POST activate → real DbContext load → validator mutates clause RefsJson → SaveChanges → re-query confirms canonical form on disk. ~15min.
2. **M3 NotFound vs 422:** `InProcessWorkflowModuleClient.ValidateDefinitionGraphAsync` collapses definition-not-found into `GraphValidationFailed` (422) — should be 404 NotFound. Low frequency; Phase 6 surface refinement.
3. **M4 DI lifetime cosmetic:** `IWorkflowGraphValidator` registered Scoped; static fields are thread-safe so Singleton would work. Cosmetic, no behavioral diff.
4. **L-NEW-2 phase-doc carry-fwd notes housekeeping:** phase-05c-...md should enumerate L-NEW-1 + M3 + M4 in §Carry-forward section.
5. **L-NEW-4 perf:** activate path issues 2 tracking SELECT round-trips on cold admin path; EF identity-map dedups entity but not query. Negligible at admin write rate; profile if hot.

## Cycle metrics

- **Start:** 2026-05-21 19:36 ICT (`/cook Phase 5c --tdd` invocation)
- **End:** 2026-05-21 20:40 ICT (merge-commit `3d27c258`)
- **Total cycle:** ~1h05min (vs plan estimate 1d = 8× faster; budget similar to 4-A/4-B/4-C sub-hour cycles)
- **Today's ship count:** **8 PR-cluster ships** — DCAND-a (#107) + 4-A (#108) + 4-B (#109) + 4-C (#110) + 5a (#111) + 5c (#112) + their docs/journal pairs. **Phase 5 progress: 2/3** code-shipped (5a + 5c); 5b remaining.
- **User override of fatigue heuristic:** 3rd consecutive (4-C / 5a / 5c). 9.5-9.6/10 clean outcomes across all 3.

## Invariants honored

- ✓ S9-A: orchestrator owns ALL finalize gates (code-reviewer x2 / commits x2 / docs / journal / memory / git-manager). Impl agents code only, no commit/docs/journal/review.
- ✓ S5 META-L (c): anti-tautology — every W-test has `Received(N)` precondition assert BEFORE behavior assert; C-T1/C-T2 prove client SaveChanges path executes/doesn't.
- ✓ 4-A-A class-level `[Authorize]` audit: 3 publish gates use distinct policies (`Workflow.Def.Manage` for WF Save+ToggleActive; `BusinessProcess.Bp.Activate` for BP Activate) — RT2-14 design-surface escalation pre-resolved.
- ✓ 4-B-A dual-metadata: `WorkflowErrors.GraphValidationFailed` factory sets BOTH `"ErrorCode"` AND `"WorkflowErrorCode"` to `"GOV_WFL_019"`; BP-side error construction mirrors via `WithMetadata` chain.
- ✓ 4-C-A model-cache: integration I5/I6/I7 ground-truth reads use `.IgnoreQueryFilters().AsNoTracking()`.
- ✓ Architecture: BP→Workflow.Application no direct ref; hop via `IWorkflowModuleClient` (Contracts SharedKernel); Architecture.Tests 101/101.
- ✓ Pre-impl error code grep: `GOV_WFL_019` slot verified FREE before slice (last used `GOV_WFL_018 = NotImplementedMode` at WorkflowErrorCodes.cs:73).
- ✓ S10b carve-out (env-class fail): N/A this slice (no Vault/DB-down/network signal).
- ✓ DCAND-a-A: nav-prop grep — N/A this slice (no new nav-prop).

## Status

**Phase 5c COMPLETE.** PHR-V2 Phase 5 progress 2/3 (5a + 5c shipped; 5b first-task-on-creation pending — depends 5a `IWorkflowTaskFactory`). Phase 6 unblocked for tests-docs consolidation; carry-fwd queue documented above.

Branch deleted local + remote. main HEAD = `3d27c258`.
