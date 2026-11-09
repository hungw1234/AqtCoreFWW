---
date: 2026-05-21 18:30 ICT
phase: PHR-V2 Phase 5a
version: v2.137p89
ship-type: refactor (DRY)
slice: 5a (FIRST of Phase 5 — 1/3; blocks 5b, independent of 5c)
---

# Phase 5a Shared WorkflowTaskFactory + ETR Refactor

## What

Extract inline task-creation block from `ExecuteTransitionCommandHandler:316-338` into a reusable Application service `IWorkflowTaskFactory` / `WorkflowTaskFactory`. Refactor ETR to consume it. Foundation for Phase 5b (first-task on instance creation) — second caller lands tomorrow.

**Endpoints:** none (pure refactor)
**Error codes:** none
**Tests:** 7 new TDD facts F1-F7 in `WorkflowTaskFactoryTests.cs`
**Behavior change:** zero — byte-equivalent stage call path; existing ETR tests preserved via transitive mock delegation

## Why

1. **DRY foundation for 5b** — without extraction, Phase 5b would duplicate the 23-line task-creation block in `CreateWorkflowInstanceCommandHandler` + `StartBusinessProcessRunCommandHandler` (2 more copies). Single source of truth eliminates drift risk.
2. **DTITLE policy externalized** — ETR passed `transition.ActionName`; instance creation (5b) has no transition. Factory accepts `string title` arg — no policy in factory; caller supplies.
3. **S10a seam payoff** — DSEAM-a Slice 10a already concentrated the inline block; this extraction was the natural next step.

## Code changes

| File | Change |
|------|--------|
| `Services/IWorkflowTaskFactory.cs` | NEW interface (52L) — `StageFirstOrNextTask(instanceInternalId, title, tenantId, createdBy, overrideAssignee, resolution) → WorkflowTask` |
| `Services/WorkflowTaskFactory.cs` | NEW `internal sealed` impl (55L) — verbatim lift of ETR `:316-338`; DASSIGN-a override precedence; S7 single-SaveChanges invariant |
| `WorkflowApplicationModule.cs` | `+services.AddScoped<IWorkflowTaskFactory, WorkflowTaskFactory>()` |
| `ExecuteTransitionCommandHandler.cs` | Ctor dep swap `IWorkflowTaskRepository` → `IWorkflowTaskFactory`; inline block `:316-338` replaced with single `taskFactory.StageFirstOrNextTask(...)` call (net -15 LOC); 1 stale DCAND-b comment block refreshed crediting DCAND-a PR #107 + 4-C PR #110 |
| 4 ETR test files (Ambient/Handler/OverrideGate/TaskCreation) | Each: 1 line — wrap `Substitute.For<IWorkflowTaskRepository>()` in `new WorkflowTaskFactory(...)` at ctor call. All `_taskRepository.Received(1).Stage(...)` assertions preserved via transitive delegation. |
| `WorkflowTaskFactoryTests.cs` | NEW 7 facts (378L) — F1 override accepted / F2 single assignee no override / F3 pool snapshot round-trip via CandidatePrincipalSerializer / F4 empty + override / F5 dumb-pipe empty no override / F6 4-field threading / F7 priority + dueDate defaults |

## Test gates

| Suite | Result |
|-------|--------|
| Workflow.Application.Tests | 510/510 PASS (was 503 baseline + 7 F1-F7) |
| Architecture.Tests | 101/101 PASS (factory pure Application, zero Infrastructure deps verified) |
| G1 `WorkflowExecuteTransitionAuthzGateTests` | 5/5 |
| G5 `WorkflowFineResolverGateTests` | 5/5 |
| Workflow.Integration.Tests G3 | 33p/1f/2skip (sole fail `SimultaneousRace` = pre-existing flake, worktree-diff proven S6) |
| Build all touched projects | 0E / 0W |

## Decisions ratified

| # | Decision | Outcome |
|---|----------|---------|
| Phase 5 scope | BE per canonical plan-doc (NOT FE wiring memory drift) | 3-slice decomposition 5a/5b/5c |
| Q1 pool-aging drop | Already shipped P3 S9 | Confirmed |
| Q2 BP migration policy | Hard-block + ops pre-flight (5b) | Locked |
| Q3 validator placement (5c) | R1 Save + R2-R4 Activate | Locked |
| Q4 pre-flight SQL audit (5c) | Run now | ✅ shipped 5c pre-flight report parallel with 5a impl |
| Today sequencing | 5a only today + pre-flight; 5b/5c tomorrow | Locked |
| Commit strategy | 2-commit split (feat + docs) per 4-A/B/C pattern | Locked |
| GOV_WFL_019 collision fix (5c) | Was GOV_WFL_018 in planner output; collides w/ 4-C NotImplementedMode | Fixed in 5c phase file pre-impl |

## Cycle time

~3h end-to-end (planning → 5a impl → 5c pre-flight in parallel → code-review → docs + plan sync → journal → commit).
- 5a impl agent wall-time: ~30 min (TDD scaffolding + impl + verification + report)
- 5c pre-flight (parallel): ~4 min (read-only scout)
- Code-reviewer: ~5 min (APPROVE 9.6/10 first-pass)
- PM + DM parallel finalize: ~3 min

Cycle within budget (planner estimate 0.5d → real ~30 min on impl).

## NEW META-LESSONS

### (5a-A) Pre-flight audit-kit as parallelizable read-only deliverable

**Pattern:** When a downstream slice needs operational data-hygiene verification (e.g., 5c validator requires no R3/R4 violations in DB), prepare the audit SQL queries as a deliverable artifact IN PARALLEL with the unrelated upstream slice's implementation. Cost: 1 Explore subagent, read-only, ~5 min wall time. Payoff: ops team has actionable queries ready when downstream slice ships; no rework if dev DB exploration discovers schema fact mismatches.

**Invariant for future plans:** any slice whose validator/migration depends on existing data state SHOULD have a `pre-flight audit report` deliverable spawned alongside (not after) the foundation slice. Saves a serial step at deploy time.

### (5a-B) Plan-doc decomposition discipline — 4 closures → 3 slices (drop already-shipped)

**Pattern:** Planner over-allocated Phase 5 = 4 closures per canonical phase-doc (factory + first-task + design-validation + pool-aging). Scout caught that closure #4 (pool-aging) was ALREADY SHIPPED in Phase 3 S9 — phase-doc line 40 even confirms "P5 does NOT touch EscalationCheckJob". Planner correctly dropped it from scope rather than re-implement.

**Invariant:** when decomposing a canonical phase-doc with multiple closures, scout each closure against current code state via grep BEFORE committing to slice count. Don't trust phase-doc enumeration as scope-bounding when the doc is older than recent shipping.

### (5a-C) Error-code collision pre-impl catch

**Pattern:** Planner Q6 proposed `GOV_WFL_018 GraphValidationFailed` for 5c, but `GOV_WFL_018 = NotImplementedMode` was already allocated by Phase 4-C earlier today. Orchestrator pre-impl grep caught the collision, fix-now to `GOV_WFL_019` in 5c phase file before any implementation started.

**Invariant:** when planner proposes a new error code constant, orchestrator MUST grep the constants file PRE-impl to confirm non-collision. Sub-1-day shipping cadence means new constants get allocated rapidly; planner context (from spawn time) may be stale.

## Reviewer findings

- **APPROVE 9.6/10** — 0 Crit / 0 High / 1 Med / 2 Low
- **M1 (folded in 5a commit):** stale 4-line DCAND-b comment block at ETR:310-313 superseded by DCAND-a PR #107 + 4-C PR #110 — refreshed to 1-line credit
- **L1 (folded in 5a commit):** factory comment said `nohResolution` (old internal variable name) — renamed to `resolution` (matches parameter name)
- **5b consumer note:** factory is `internal sealed`; works for same-assembly consumers (5b/5c). If cross-assembly consumer ever needed → promote to `public` or `[InternalsVisibleTo]`

## Carry-fwd to 5b (tomorrow)

1. `CreateWorkflowInstanceCommandHandler` adds factory consumer call after instance Create — uses initial-state `DisplayNameVi` as title (DTITLE per scout).
2. `StartBusinessProcessRunCommandHandler` adds factory consumer call (BP path — runs through `TransactionScope` per RT2-1 pre-existing).
3. `IWorkflowInstanceRepository.Stage()` NEW method (mirrors `IWorkflowTaskRepository.Stage()`) — GenericRepository.AddAsync auto-saves, breaks S7 invariant.
4. Hard-block policy per Q2: empty pool at initial state → return `Result.Fail(NoHandlerResolved)` 422. Block instance creation.
5. 7 unit + 4 integration facts per phase-05b spec.

## Carry-fwd to 5c (tomorrow, parallelizable with 5b)

1. `WorkflowGraphValidator` Application service with R1-R4 rule predicates.
2. Wire into 3 publish/activate surfaces: `SaveDefinitionGraphCommandHandler` (R1 only — assignment not in Save scope), `ToggleDefinitionActiveCommandHandler` (full), `ActivateBusinessProcessCommandHandler` (full via new `IWorkflowModuleClient.ValidateDefinitionGraphAsync`).
3. `WorkflowErrorCodes.GraphValidationFailed = "GOV_WFL_019"` (corrected pre-impl from planner's collision-prone GOV_WFL_018).
4. Ops pre-flight: run 5c SQL audit kit (`plans/reports/audit-260521-1715-...md`) against dev/staging/prod BEFORE deploy. Block if R3/R4 violations found.
5. 10 V + 6 W + 3 I facts per phase-05c spec.

## Phase 5 progress

1/3 slices code-complete (5a APPROVE 9.6/10, awaiting merge as part of this PR).
- 5b pending (depends 5a)
- 5c pending (independent, pre-flight audit kit ready)
- Pool-aging dropped (already shipped P3 S9)

PHR-V2 Phase 5 unblocks Phase 6 (Tests & Docs end-to-end matrix) once 5b+5c land.

## Unresolved questions

None — all 4 BLOCKING gates (Q1-Q4) ratified pre-impl; mid-slice 2-nit fix-now applied; commit strategy + journal timing both user-ratified. Phase 5b + 5c queued for tomorrow with explicit user-ratified deferral.
