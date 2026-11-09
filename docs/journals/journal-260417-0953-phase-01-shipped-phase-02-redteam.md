# Phase-01 Shipped + Phase-02 Red-Team Save

**Date**: 2026-04-17 09:53
**Severity**: High (Phase-02 correctness, prevented 3-4h rework)
**Component**: Workflow.Application (Phase-02 plan), Workflow.Domain (ExecuteTransitionCommandHandler discovery)
**Status**: Completed (Phase-01 merged, Phase-02 plan revised)

## Session Overview

Two distinct outcomes: (1) Phase-01 shipped clean via PR #26 squash merge to main as commit 9322440c; (2) Phase-02 red-team checkpoint found 13 issues including 3 criticals before code was written. Red-team investment: ~45 min. Prevented rework estimate: ~3-4h.

## Outcome 1 — Phase-01 Merged

**Context**: PR #26 (feat/phase-01-seed-caseprocess-definition) had M1 + M2 tech debt fixes awaiting merge. Code-reviewer approved 9.5/10 APPROVED_WITH_MINOR, nits fixed pre-commit (1edcee8a).

**Action**: Squash merged to main as 9322440c (Phase-00 workflow baseline + M1 seeder comment fix + M2 startup health check, all bundled). Post-merge cleanup: `git fetch --prune origin` removed 4 stale remote refs (phase-00, phase-01, t4-06, t5-20-workflow-designer-ux).

**Status**: Clean. Branch empty. Ready for Phase-02 entry.

---

## Outcome 2 — Phase-02 Red-Team Discovery (The Real Story)

### Starting Point

User finalized Phase-01 merge and immediately said "start Phase-02 now". Plan existed: `plans/260415-1737-cases-workflow-refactor/phase-02-extend-workflow-client-contract.md` (written 2026-04-15, 2 days old). Was about to proceed directly to implementation.

Self-caught **3 orientation issues** during branch/task setup:
- Contract scope vagueness (where does IWorkflowModuleClient live vs Workflow.Application?)
- Cache complexity (DTO round-trip through JSON → need aggregate serialization)
- RowVersion behavior (handler docstring says "concurrency guard" but Phase-01 shipped without RowVersion mapping)

User paused: **"đã red-team cho plan phase 2 chưa?"** — forced explicit red-team check on Phase-02 plan before starting.

### Red-Team Execution

**Discovery**: NO red-team existed for Phase-02. Only Phase-00 had 3 red-team rounds. Overall plan.md had generic Red-Team section but no Phase-02 deep-dive.

**Action**: Spawned planner in red-team mode to interrogate Phase-02 plan against actual codebase.

**Report**: `plans/reports/red-team-260417-0925-phase-02-contract.md` — 13 findings.

### Critical Findings (Would Have Cost 3-4h Rework)

**F01: Duplicate Logic — ExecuteTransitionCommandHandler Already Exists**
- Plan prescribes: "Create ExecuteTransitionCommandHandler in Workflow.Application"
- Reality: `/src/modules/workflow/AqtCoreFW.Workflow.Application/Features/Transitions/ExecuteTransitionCommand.cs` already has ExecuteTransitionCommandHandler (215 LOC)
- Handler implements: parallel-branch fan-out + ConditionsJson evaluation + attachment attachments + rollback on condition failure
- Plan's design: would have silently **bypassed all of this** by delegating client → new handler via IMediator without understanding existing handler's orchestration complexity
- **Decision locked**: Delegate client method → use existing command via IMediator, don't rewrite handler
- **Why critical**: Silent duplication = 2 paths to execute transitions, conflicting business logic, observer validation nightmare

**F02: IPermissionService Does Not Exist**
- Plan prescribes: "handler injects IPermissionService.HasPermissionAsync()"
- Reality: No such service in any module
- Codified pattern: `IIdentityModuleClient` in SharedKernel.Contracts is the ONLY cross-module auth-lookup path
- Missing method: `IIdentityModuleClient.GetEffectivePermissionCodesAsync(actorId, tenantId)` — doesn't exist yet
- **Decision locked**: Refactor handler authz to call new IIdentityModuleClient method (new method to be implemented in Phase-02 alongside handler refactor)
- **Why critical**: Inventing `IPermissionService` violates module boundary codification; would need separate service module or leak IdentityModule impl into Workflow

**F03: Wrong Error-Code Prefix**
- Plan prescribes: throw `GOV_WORKFLOW_*` error codes
- Reality: Phase-01 seeded `GOV_WFL_NNN` codes (6 codes: SUBMIT, REVIEW, APPROVE, REJECT, RETURN, CLOSE)
- Prefix mismatch = runtime exception handling misses codes
- **Decision locked**: Reuse GOV_WFL_NNN prefix, not GOV_WORKFLOW_*
- **Why critical**: Error handling contract downstream (API exception mappers, FE error dialogs) keys on prefix; wrong prefix = silent failures or 500 responses

### Significant Findings

- **F04**: RowVersion dropped in Phase-01 — handler docstring still says "concurrency guard" but field not mapped. Defer reinstate to Phase-02b.
- **F05**: Authz model mismatch — existing handler uses role-equality `ActorRoleCode == RoleCode.Reviewer`; Phase-01 seeded permission codes. Two systems. Handler refactor must unify to permission codes.
- **F06**: Outcome shape missing observable error modes — what does client return on permission denied vs condition failed vs invalid state? Plan silent on error surfaces.
- **F07**: Phase-03 plan also has stale method name `FindByEntityAsync` (should be `FindByEntity` per actual codebase).
- **F08**: Cache DTO absent — plan assumes aggregate can round-trip JSON but no contract DTO defined. Will serialize all properties (PII risk).

### Decision Table (B.2 Locked)

| Issue | Plan Prescribed | Red-Team Found | Decision Locked |
|-------|-----------------|-----------------|-----------------|
| ExecuteTransitionCommandHandler | Create new | Already exists (215 LOC) | Delegate→existing via IMediator |
| IPermissionService | New service injection | Doesn't exist; use IIdentityModuleClient | Add GetEffectivePermissionCodesAsync to IIdentityModuleClient |
| Error prefix | GOV_WORKFLOW_* | GOV_WFL_NNN seeded in Phase-01 | Use GOV_WFL_NNN |
| RowVersion | Handler uses concurrency guard | Not mapped in Phase-01 | Defer to Phase-02b backlog |
| Authz model | Permission codes (plan) | Role equality (existing handler) | Refactor handler: drop role checks, use permission codes from IIdentityModuleClient |
| Contract outcome | Return FluentResults object | Violates SharedKernel.Contracts philosophy | Pure C# class (no FluentResults dep) |
| Cache layer | Plan in Phase-02 | Deferred for complexity | Defer to Phase-02b backlog |

### Plan Revision

Spawned planner to rewrite `phase-02-extend-workflow-client-contract.md`:

**Output**: 371-line revised plan with:
- Revision header: "Revised 2026-04-17 post-red-team-findings"
- Red-team resolution table (issues + decisions)
- 5 implementation steps (ExecuteTransition entry-point → IIdentityModuleClient enhancement → handler authz refactor → outcome contract → integration tests)
- D1-D5 defer list (RowVersion reinstate, cache layer, OTel, field renames, outcome error-code coverage)
- New contract outcome spec: "pure C# class, no FluentResults, observable error modes (Unauthorized, ConditionFailed, InvalidState)"

**Status**: Ready for `/cook` entry in next session. No code written; clean boundary.

---

## Key Learnings

### 1. Red-Team ROI on Phase Plans: ~30min = 6-8× Rework Avoidance
When a plan has factual density (specific service names, type signatures, code references), red-team checkpoint catches:
- **Duplicates** (existing handlers we'd rewrite)
- **Invented abstractions** (services that don't exist)
- **Stale references** (codes/methods from pre-Phase-01 state)

Without red-team: would have written handler → merged code → discovered F01 during review → rework cost 3-4h.

### 2. "Existing Code First" Discovery Pattern
**Never ask "can we build X?"** Always ask **"does X already exist?"** If a plan prescribes building something, grep the codebase before designing.

ExecuteTransitionCommandHandler hid in plain sight. Only surface of IWorkflowModuleClient was checked (currently has CreateInstance + SetActiveState). The command handler implementation was on-path but not in the interface — easy to miss in shallow scan.

### 3. Plan-Code Temporal Coupling
Phase-02 plan was written 2 days before Phase-01 shipped. By merge day, references became stale:
- RowVersion behavior (assumed missing, actually Phase-01 decided NOT to map it yet)
- Permission codes (plan said "no codes seeded", Phase-01 seeded 6)
- Error prefix (plan invented, Phase-01 defined actual)

**Pattern**: Cross-phase plans written before prior phases ship are time-bombs. Red-team after each phase lands catches drift.

### 4. Module Boundary Codification — IIdentityModuleClient Pattern
`IIdentityModuleClient` in SharedKernel.Contracts is the ONLY cross-module auth-lookup path. Inventing new services (IPermissionService, IAuthorizationService) violates this without intent.

When a plan prescribes a new service, cross-check: does a shared-kernel contract already exist for this role? If yes, extend it. If no, is it worth adding a new module boundary? Usually no.

### 5. Clean-Boundary Sessions > Shipping Partial Code
Pausing after plan revision (no code written) is better than shipping half-implemented code across context-exhausted sessions.

Phase-02 is now correctly shaped. Starting fresh in next session with revised plan is faster than context-switching mid-implementation when F01 surfaces during integration testing.

---

## Technical Details — Red-Team Report Location

**Report**: `C:\AqtCoreFW\plans\reports\red-team-260417-0925-phase-02-contract.md`

Contains:
- Finding index (F01-F13)
- Code references (file paths, line numbers)
- Decision precedent (what was decided in Phase-00/01)
- Resolution table (per-issue decision locked)
- Risk matrix (critical/significant/minor)

---

## Next Steps

1. **Phase-02 entry** (next session): `/cook` with revised plan; implementation should be clean now that contradictions resolved
2. **IIdentityModuleClient.GetEffectivePermissionCodesAsync** — new method to design in Phase-02 Step 2
3. **Phase-03 plan audit** — F07 found stale method name in Phase-03 plan; needs similar red-team pass before implementation
4. **RowVersion reinstate** — Phase-02b backlog item; will add concurrency guard back to handler after Phase-02a core authz refactor

---

## Metrics

| Item | Value |
|------|-------|
| Phase-01 commit (squash merge) | 9322440c |
| Phase-02 red-team findings | 13 (3 critical, 5 significant, 5 minor) |
| Red-team execution time | ~45 min |
| Plan revision lines | 371 (rewrite) |
| Code written for Phase-02 | 0 (clean boundary) |
| Rework prevented (estimate) | 3-4h |
| ROI (findings/time) | 6-8× |

---

## Emotional Reality

Red-team felt defensive at first — "we're not ready, we haven't started coding yet." But finding F01 (existing handler that would have been silently duplicated) made it click: this is exactly what red-team catches.

**The bruise**: Plan was solid conceptually (extend IWorkflowModuleClient, refactor handler authz, define outcome contract). But it was written in isolation — before Phase-01 shipped, before the actual code lived on main. Two days of code divergence is enough for a plan to drift.

**The relief**: Better to find this *before* writing code than *after* shipping PR. The pause decision ("đã red-team chưa?") was the right call. Session discipline: if a plan has factual references, red-team it. Cost is 45 min. Payoff is not having to rewrite a handler.

**Confidence level**: HIGH on Phase-02 direction now. Plan is synchronized with actual codebase state. IIdentityModuleClient new method is a small, well-scoped addition. Handler refactor scope is clear. No surprises.

---

## Sync Status

| Item | Status |
|------|--------|
| backlogs/backlog.md | Phase-01 complete (ticked ✓); Phase-02 ready to start |
| memory/project-state.md | v2.104 → v2.105 (Phase-01 merged) |
| docs/dependency-map.md | Verified: Phase-01 entities on main; Phase-02 IIdentityModuleClient extension noted |
| plans/260415-1737-cases-workflow-refactor/phase-02-* | Revised + red-team findings documented |
| git | Main branch: 9322440c (Phase-01 landed clean) |
