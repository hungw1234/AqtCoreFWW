# Process-Handler-Resolution v2 Phase 1: Domain Model & Migration Complete

**Date:** 2026-05-19 09:56  
**Severity:** Medium (additive, inert)  
**Component:** Workflow (domain + EF migration)  
**Status:** Shipped (commit c9dfd94d, branch feat/process-handler-resolution-p1)

## What Happened

Phase 1 of the per-process handler-resolution plan (Model B outgoing-driven, rule-on-WorkflowTransition) landed successfully — all schema, all tests, zero blockers for P2/P3. Three new entities, four deltas, one migration, 21 new TDD tests passing. Feature inert until resolver service (P2) and execute integration (P3) wire it.

## The Brutal Truth

This phase felt clean because the red-team caught all the architectural land-mines in the *plan* (RT2-1 through RT2-16), not the code. We shipped a solid foundation, but honest gripe: the plan **over-claimed** some victories. Two specific pain points:

1. **RT2-8 tenant-RLS ambiguity was real.** The red team caught that `WorkflowTransition` is NOT `ITenantScoped`, yet we're baking rules on it. Initially the plan said "RLS on assignment" — that's wrong and would have leaked cross-tenant data. User ruled RT2-8 *correctly*: assignment is config (not RLS), only runtime candidate-index gets tenant isolation. But this took three full review rounds to lock. A pre-plan decision would have saved time.

2. **RowVersion ripple was understated.** RT2-12 flagged that this lands on ~19 files touching `WorkflowTask`/`TaskAssignment`. We mitigated it by leveraging EF's change-tracker (existing handlers already tracked-read), but the obligation to enumerate + regression-test every path is now P3/P4/P5's burden. It stings.

## Technical Details

**Delivered (commit c9dfd94d):**

- **WorkflowTransitionAssignment** (separate entity, NOT owned, FK→WorkflowTransition):
  - `CombineMode` enum: {Union, Intersect}
  - `AllowAssignorOverride` bool (default FALSE — fail-closed posture)
  - Config-scope, inherits definition tenancy (global templates `TenantId=Guid.Empty` supported)

- **WorkflowTransitionAssignmentClause** (child):
  - `Mode` enum: {SpecificUser, Initiator, PreviousHandler, ByPermission, ByRole, ByGroup, ByOrgUnit}
  - `RefsJson` nullable string (userId | roleId[] | groupId[] | orgUnitId[] | permissionCode)

- **WorkflowTask delta:**
  - `RowVersion` byte[] (optimistic-concurrency token, auto-populates on existing rows)
  - `CandidateRefsJson` string? (resolver output snapshot)
  - `ClaimedAt` DateTimeOffset? (audit trail)
  - Domain methods: `Claim(userId)` (tracked-read + assert null → WF_ALREADY_CLAIMED; RowVersion = race-tiebreak), `Unclaim()`, `SetCandidate(json)`
  - **Status stays Open** (user decision 2026-05-19): Claim locks assignee; `Start()` still does Open→InProgress

- **TaskAssignment enum:** Type += Claim (5), Unclaim (6)

- **WorkflowTaskCandidateIndex** (read-model, write-once):
  - T1 entity: `(WorkflowTaskId, ClauseId, CombineMode, PrincipalType, PrincipalRefId, TenantId)`
  - Runtime tenant-RLS'd only: `pol_workflow_WorkflowTaskCandidateIndex_Tenant`
  - RLS count: 40→41

- **EF migration:** `20260519015732_AddWorkflowTransitionAssignmentAndPoolTask` (Designer+Snapshot, Windows case-correct). Single `WorkflowDbContext` only, no cross-context.

**Tests:** 21/21 new TDD tests green (WorkflowTransitionAssignmentTests 98 assertions, WorkflowTaskClaimTests 149, WorkflowTaskCandidateIndexTests 56).

**Blockers caught pre-P1:**
- 2 pre-existing `WorkflowDefinitionVersioningTests` Clone failures (proven via throwaway worktree on clean HEAD f90c12d6 — zero P1 regression). Full Workflow.Domain: 141/143 passing.

## What We Tried

1. **RowVersion design** — Plan initially vague on "Status stays Open + RowVersion." Spec was: "tracked-read + assert AssigneeId==null → DbUpdateConcurrencyException → 409." Confirmed this works: concurrent winners hit the race-tiebreak, losers get `DbUpdateConcurrencyException`, HTTP layer maps to 409. No retry-logic needed (fail-closed is intentional — winner gets it, loser re-asks). Valid per H3 red-team ruling.

2. **RowVersion safety audit** — Verified WorkflowTask has NO tracked child navigation (unlike `WorkflowInstance` which has bug 33d4973f: rowversion on root propagates Modified to children). Documented inline in entity. Safe.

3. **Claim semantic shift** — Plan's "sets ...status" was ambiguous. Surfaced to user, got explicit ruling: Status stays Open (Claim = ownership lock only; Start() does Open→InProgress). Locked into entity + TDD tests so P4/P5 inherit the contract, not silent assumptions.

4. **Plan-doc drift hunt** — Phase-01 §Success Criteria claimed "increment policy count in SchemaUniversalsTests." Read the test file. **It doesn't exist.** No RLS-policy-count assertion in that test. Flagged it; deferred to P6 integration phase (plan now documents the omission).

5. **Tooling workaround** — dotnet-ef local-tool resolution broke under harness sandbox (nuget global-packages relocated to `C:\NuGetPackages`, dotnet-tool manifest not found in CWD). Workaround: `dotnet tool install dotnet-ef --tool-path .\.tooltmp` + invoke binary directly. Usable for future P2/P3/P6 migrations.

## Root Cause Analysis

The real issue: **plan-doc divergence was caught by red-team, but we didn't frontload design decisions.** Three items:

1. **RT2-8 (tenant-RLS):** Plan said assignment gets RLS. Red-team asked: "Is WorkflowTransition tenant-scoped?" Answer: no (it's part of the definition graph, can be global). Correction: assignment = config, not RLS. Took full RT3 round to stabilize.

2. **RT2-9 (owned-vs-separate):** Plan said "owned-type." Red-team said: "Can't query it efficiently; resolver needs transiton→assignment lookup." Flipped to separate entity. Should have done this before the red-team read.

3. **RT3-S2 (dead schema):** Plan added `PrecedingTaskId`/`SourceTransitionId` columns (unused — only `PreviousHandler` lineage uses them, which is NotSupported/stubbed). Red-team: "Avoid dead schema in P1." Removed them. Good catch, but indicates plan wasn't "verify-all-columns-have-consumers" tight.

## Lessons Learned

1. **Pre-plan design-decision lock beats red-team revision rounds.** Three decisions (RT2-8/RT2-9/RT3-S2) would have cost <30min with a 15-minute walkthrough. Instead, they consumed 3× red-team reviews. Next time: lock tenant-scope, EF mapping, and schema-completeness BEFORE the plan enters red-team.

2. **"Documented anti-pattern" is scoped to its cause.** RowVersion on WorkflowTask is safe (no tracked children); RowVersion on WorkflowInstance is not (bug 33d4973f uses IsConcurrencyToken scalar to dodge propagation). Code comment in entity + architectural note prevents future devs from cargo-culting the pattern. Worth doing.

3. **Prove pre-existing failures with evidence, don't reason-assert.** The 2 Clone test failures were genuinely pre-existing (proven via throwaway worktree on HEAD). If we'd just said "these were there before," a skeptical reviewer would doubt us. The worktree proof is cheap; the trust payoff is real.

4. **Plan-doc assertions need spot-checks.** Phase-01 claimed SchemaUniversalsTests has an RLS-policy-count check. It doesn't. A 30-second grep would have caught this. Ditto for "enumerate 19 files/57 occ" (RT2-12) — we listed the obligation but didn't pre-enumerate. Plan precision matters for implementation morale.

## Next Steps

**P2 (Handler Resolver Service):** ~2d, standalone, no authz. Implements `IWorkflowHandlerResolver` service: ingest `WorkflowInstancePoolContext` (definition + current state + transition rules), output `Pool(S) = resolved handlers` set. No blocker from P1; pure Workflow module.

**P3 (RPC Subsume + Authz Gate + Execute Integration):** 5d mega-PR (user deferred ParallelBranchService refactor to separate P2b prep PR). Removes `RequiredPermissionCode`, adds coarse endpoint policy, atomizes execute path + EscalationCheckJob H4 fix. Fail-by-default 403 integration test as merge gate (authz-bypass window = zero).

**P6 gap:** Cross-tenant RLS behavior test for `WorkflowTaskCandidateIndex` (plan-doc drift: SchemaUniversalsTests has no policy-count assertion). Integration suite will verify that a task in TenantA cannot leak to TenantB.

---

**Status:** DONE

**Journal:** `docs/journals/journal-260519-0956-process-handler-resolution-p1-domain-migration.md`

**Commit:** c9dfd94d4e2834d39048885b77c261773699bbeb  
**Branch:** feat/process-handler-resolution-p1  
**Diff:** 22 files, 2788 insertions(+), 6 deletions(-)
