# RT2-15 Phase 04 ByOrgUnit Shipped (v2.137p114)

**Date**: 2026-05-26 11:30
**Severity**: Medium
**Component**: Workflow / Assignment / OrgUnit
**Status**: Resolved

## What Happened

RT2-15 Phase 04 ByOrgUnit clause mode shipped end-to-end: producer + read-side + FE mode-6 multiselect. Single commit `68287c52` (25 files, fail-CLOSED per 260523-D). Closes 4/4 AssignmentClauseMode producer arc (PreviousHandler p108 + ByGroup p112/p113 + ByRole p110 + ByOrgUnit p114). Phase 05 cross-mode validator now unblocked.

## The Brutal Truth

Orchestrator's slice-start grep was too narrow. Searched for `IOrganizationModuleClient` (module-name convention), found 0 hits. Prepared to create new client interface. Planner expanded grep to concept-name variants, discovered EXISTING `IOrgUnitModuleClient` already living in `src/shared/AqtCoreFW.OrgUnit.Contracts/`. Codebase has BOTH `Organization` module folder AND `OrgUnit` contracts namespace — naming inconsistency that silently defeated grep-based discovery. Would have duplicated 5 Dapper methods, violated DRY, and created unnecessary confusion. Lesson: **audit cross-module client surfaces with multiple naming variants (singular/plural + module-name vs concept-name)** — this is 260526-A INVARIANT candidate.

## Technical Details

**Producer Shape**: Extended `IOrgUnitModuleClient.ResolveActiveUsersInOrgUnitsAsync` with temporal M:N filter (UserOrgUnitAssignment ValidFrom/ValidTo range check + TimeProvider injected). R4 lift caught ValidTo-exclusive boundary (>= ValidFrom AND < ValidTo). Resolver predicate-pool extended with `?codes` arm. Candidate-index OrgUnit writer: `IWorkflowTaskCandidateIndexService.RebuildOrgUnitAsync(tenantId, orgUnitCode)`.

**Reader**: `IIdentityModuleClient.GetUserOrgUnitCodesAsync(userId, tenantId, cancellationToken)` Dapper direct-read `UserOrgUnitAssignment` (no temporal filter — schema IS temporal but query ignores; temporal-only filter at producer). `WorkflowTaskRepository` 4th OR-arm (ByRole | ByGroup | ByOrgUnit). Handler wiring symmetric to ByGroup p113.

**Tests**: WF.App 577/577 (+3 OU-P1..P3) / WF.Infra 84/84 / Org.Tests 133/133 (+5 OU-U1..U5) / Org.Integration +5 OU-I1..I5 / WF.Integration +4 anti-tautology OU-I1..I4 / Architecture 105/105 / Build 0E/0W / FE typecheck 0. OU-I5 cross-tenant exclusion test locks 260525-E security INVARIANT (mandatory tenantId on new cross-module methods).

**Code Review Finding**: REQUEST-CHANGES 8.7/10 → APPROVE 9.4/10. M1: validator shipped dead `IOrgUnitModuleClient` dependency (leftover from eager-resolve draft, plan ratified structural-only). M2: FE `?activeOnly=true` query param silently ignored by BE (read-side Dapper has no temporal filter). Both mechanical ~17 LOC fix.

## Pattern & Speed

ByGroup p112/p113 twin established shape. ByOrgUnit applied pattern with temporal-filter variation (TimeProvider Q4). **~3h actual vs 6-8h estimate** — 3rd validation of 260522-O pattern-mirror discount (5-9× faster for "extend existing module" work).

## Lessons

1. **Grep discipline (260526-A CANDIDATE)**: Cross-module client discovery MUST iterate concept-name variants. Codebase inconsistency (Organization folder vs OrgUnit namespace) defeated single-pattern grep. Audit rule: `IOrganization*`, `IOrgUnit*`, `I[ModuleName]ModuleClient`.
2. **260525-E honored**: New cross-module methods carry mandatory `Guid tenantId`. ByGroup p112 cross-tenant leak (C1) was precedent; mirror prevention worked, zero recurrence.
3. **260525-G test-layer symmetry**: Mirrored ByGroup twin test counts (5 unit + 5 integration + 4 anti-tautology) prevents test-layer drift.
4. **Mid-ship PM failure (observational)**: Project-manager subagent access transiently denied mid-run. Orchestrator recovered by direct backlog/plan edits (idempotent). PM had completed most work before failure. Recovery cost ~10min.

## Next Steps

- **RT2-15-PHASE-04-FU**: BtcOrphanPurgerTests OU-column drift (carry-fwd ~20min).
- **Phase 05**: Cross-mode validator E2E (Phase 06 unblocked when Phase 05 ships).
- **Grep INVARIANT reinforcement**: Audit other cross-module client surfaces in codebase for singular/plural naming gaps (low-risk but hygiene).

---

**Status:** DONE
**File**: C:\AqtCoreFW\docs\journals\journal-260526-1130-rt2-15-phase-04-byorgunit-ship.md
**Summary**: ByOrgUnit clause mode shipped with pattern-mirror discipline; planner-driven grep-variant discovery prevented duplicate client creation; 3h actual vs 6-8h estimate validates 260522-O discount; cross-tenant security INVARIANT 260525-E honored via test OU-I5.
