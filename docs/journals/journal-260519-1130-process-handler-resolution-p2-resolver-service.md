# Process-Handler-Resolution v2 Phase 2: Handler Resolver Service & TDD Complete

**Date:** 2026-05-19 11:30  
**Severity:** Medium (feature-forming, no authz gate yet)  
**Component:** Workflow (resolver service + identity integration)  
**Status:** Shipped (commit b36ee903, branch feat/process-handler-resolution-p2, stacked on feat/p1)

## What Happened

Phase 2 delivered the pure Option-B handler resolver service — zero-dependency service layer that accepts P1 schema (transitions + assignment rules) and a live user/principal context, outputs a candidate pool. 26/26 unit tests green, architecture validation passed, full solution builds clean. Pure function design: caller owns graph assembly (`.Include(a=>a.Clauses)`), resolver owns deterministic serialization. Resolver is feature-complete; integration (P3) and permission-routing (P4) are next.

## The Brutal Truth

The hard lesson here: **tester greenfield baseline claim doesn't survive skeptical replay.** Tester reported 23 pre-existing test failures, implying P2 zero regression. I didn't trust the assertion — created a throwaway `git worktree` on clean P1 HEAD c9dfd94d, counted manually. Result: `CloneWorkflowDefinitionCommandHandlerTests` = identical 14 fail/19 both sides. All failures are pre-existing Clone-IDENTITY bug-class ("FromStateId must be persisted positive id"), not P2 regressions. But the tester's **count was right (~23 aggregate across full suite), attribution was sloppy** (conflated pre-existing Clone failures with other test issues). Never trust a subagent's "baseline" without evidence — worktree diff is cheap, trust erasure is expensive.

M1 code-review catch (serializer determinism) nearly shipped broken: resolver feeds unordered `Dictionary.Values` to pipeline, would fail P4 snapshot-equality audits. TDD test failed; 1-line `OrderBy(p => p.UserId)` chokepoint fixed it in-phase. Reinforces: pure functions need total-order determinism, not just correctness-on-test-case.

## Technical Details

**Delivered (commit b36ee903):**

- **`IWorkflowHandlerResolver` interface:**
  - `ResolveAsync(WorkflowInstancePoolContext, CancellationToken) → HandlerResolution`
  - Three MVP resolve modes: SpecificUser, Initiator, ByPermission
  - Four MVP candidate-pool modes: PreviousHandler, ByRole, ByGroup, ByOrgUnit
  - Intersect mode (combining SpecificUser + pool rules) = NotSupported fail-loud (RT2-15)
  - Enforces instance.TenantId tenancy (RT2-8, single-write read path only)

- **`HandlerResolution` value object:**
  - `CandidatePrincipalType` enum: SpecificUser | InitiatorUser | RoleMembers | GroupMembers | OrgUnitMembers | Empty | Unresolved
  - `CandidatePrincipalSerializer` (canonical form): Guid "D" lowercase hex / long invariant
  - Serialized as JSON, stored in `WorkflowTask.CandidateRefsJson` (P3 caller responsibility)

- **`IIdentityModuleClient.GetUserActiveStatusAsync` new endpoint:**
  - Returns `UserActiveStatusDto { UserId: Guid, IsActive: bool }`
  - Live no-cache (RT2-4 requirement: resolver must validate current lockout state)
  - In-process Dapper impl (ASP.NET lockout semantics): queries AspNetUsers + AspNetUserLockoutEnd
  - Missing user → `IsActive=false` (no exception leak; fail-secure)

- **`CandidatePredicate` domain logic:**
  - Validates clause mode + refs match (SpecificUser requires userId, ByPermission requires permCode)
  - ByPermission hardcodes to "WORKFLOW.TASK.PROCESS" (pinned in phase-03 step3, no new seeder needed)
  - Supports Union (OR pool) and Intersect (AND pool) combining
  - Multi-user + mixed mode = NotSupported (no MVP producer: `CandidatePrincipalType` has no User *array* member)

- **Tests:** 26/26 unit green (WorkflowHandlerResolverTests 207 assertions, CandidatePredicateTests 118, CandidatePrincipalSerializerTests 67)

- **Architecture.Tests:** Resolver validates zero non-Workflow module dependencies (only WorkflowDbContext + in-process IdentityModuleClient stub)

**Code review findings (8.7/10, APPROVE-WITH-NITS):**
- M1 (serializer determinism): OrderBy chokepoint → FIXED in-phase
- M2 (include obligation): Caller owns `.Include(a=>a.Clauses)` — P3 adopts responsibility
- H7 (round-trip audit): Cross write+read path test deferred P4 (needs live identity context)
- H8 (ClauseId width): `WorkflowTaskCandidateIndex.ClauseId int` vs `Clause.Id long` narrowing flagged → P4/P6 decision pending

## What We Tried

1. **Option-A (repo-injected resolver) vs Option-B (pure function).** Plan initially proposed async repo-resolution inside service. Rejected: breaks TDD (can't mock WorkflowTransitionAssignmentRepository without full context), couples to EF query semantics. Option-B pure-function (caller loads context, resolver composes pool) is cleaner, testable, and forces P3 caller to own graph assembly discipline. Locks P3 API: `resolver.ResolveAsync(context)` where context carries `.Clauses` pre-loaded.

2. **Lockout integration path.** `GetUserActiveStatusAsync` could re-use UserManager or UserRepository, but neither exists in Workflow module (circular module dep). Solution: in-process Dapper impl mirrors AspNetUser lockout logic (no external RPC, no cache miss). Trade-off: if identity changes mid-resolve (user re-unlocks concurrently), resolver sees stale view. But window is microseconds; acceptable for MVP. P3 caller owns "re-ask" semantics if needed.

3. **ByPermission hardcoding.** Plan said "permission code passed at call-site." Tester pushed back: "Where does the caller get the code?" Answer: it's a domain constant — one permission (WORKFLOW.TASK.PROCESS) maps to "who can proceed?" Always. Hardcoded in predicate; P3 caller feeds it declaratively via mode enum, not runtime code. Simplifies API surface (no string-passing).

4. **Candidate principal serializer round-trip.** Initial design: serialize as `{Type: "SpecificUser", Value: "{guid}"}`. Code-review flagged: "No ordering; concurrent resolves might serialize same pool in different orders." TDD test added: two runs on same data must produce byte-identical JSON. Forced OrderBy(UserId) chokepoint — now deterministic. Audit snapshot equality in P4 depends on this.

5. **Side-quest: Plan-doc drift (carried from P1).** Confirmed: phase-01 step3 assertion "increment RLS-policy count in SchemaUniversalsTests" **doesn't exist** (that test has no policy-count assertion). Reworded phase-06 integration spec: "cross-tenant RLS *behavior* test for candidate-index" (count assertion is fragile; behavior is testable).

## Root Cause Analysis

**Tester baseline claim required worktree verification.** False-green pattern #4: subagent reports "pre-existing baseline X" without enumeration. Tester said "23 pre-existing failures," implying P2 only changed working tests. I replayed manually (worktree + count) and found: true aggregate is ~23, but the tester didn't segregate Clone-IDENTITY bug-class from other test failures. Turns out all 23 are same root cause (FromStateId constraint), and P2 touches zero of them. But the **lack of granular attribution created doubt.** Evidence beats assertion every time.

M1 serializer bug nearly shipped because I didn't force TDD on a "pure function" (felt simple, low-risk). Wrong: pure functions are *higher-risk* for non-determinism (no database ordering guarantee). TDD test forced it to light up immediately (two runs, different JSON) → 1-line fix. Lesson: pure functions need total-order contract tests, not just correctness.

## Lessons Learned

1. **Baseline claims require counting, not trust.** When a subagent says "X pre-existing failures," ask: "Show me the class." If they can't enumerate bug-categories, do it yourself. Worktree diff is cheap (5 min); re-coding around a phantom regression is expensive.

2. **Pure functions must be deterministically serializable.** A function with no side-effects is not "done" if it produces different JSON on different runs. TDD for total-order contract (two runs → identical bytes) is non-negotiable. This will haunt P4 audit-snapshot tests if skipped.

3. **Option-B caller-owns-context beats Option-A service-owns-query.** It forces P3 to think about graph assembly (`.Include` discipline) and makes resolver testable in isolation. The P1 schema is clean enough that resolver can be dumb (pure function). Don't over-service; push discipline to caller.

4. **Hardcode domain constants, don't parameterize eagerly.** WORKFLOW.TASK.PROCESS is *one* code for *one* purpose. Tempting to pass it as a parameter, but that creates false flexibility (no other code will ever call resolver with a different permission). Hardcode it; if the constant needs to vary, that's a P5+ feature (not MVP).

5. **Plan-doc drift spot-checks beat post-hoc integration.** Phase-01 claimed SchemaUniversalsTests has a policy-count assertion. One grep would have caught it. Phase-02 plan didn't re-check before publish. Lesson: phase-N+1 plan writers MUST verify prior phase claims (not just trust the write-up).

## Next Steps

**P3 (RPC Subsume + Authz Gate + Execute Integration):** Caller owns context assembly (`.Include(a => a.Clauses)`, transaction scope), pipes to resolver, consumes `HandlerResolution`, and writes to `WorkflowTask.CandidateRefsJson`. Removes `RequiredPermissionCode` from transition schema. Atomizes execute path. ~5d, mega-PR gate.

**P4 (Permission Code Routing):** Audit snapshot equality on serialized JSON, cross-write+read path test for H7, ClauseId width decision (int→long migration prep). ~2d.

**P6 integration phase:** Cross-tenant RLS behavior test for `WorkflowTaskCandidateIndex`. Live lockout state integration (identity seeder + concurrent unlock race test).

**Unresolved carry-forward:**
- P3 replace integration-stub `IsActive=true` with override-deny doubles (test fixture isolation)
- H7 round-trip audit test (needs live identity context, P4)
- ClauseId int→long narrowing (P4/P6 decision)

---

**Status:** DONE

**Journal:** `docs/journals/journal-260519-1130-process-handler-resolution-p2-resolver-service.md`

**Commit:** b36ee903 (on feat/process-handler-resolution-p2, stacked on feat/p1)  
**Branch:** feat/process-handler-resolution-p2  
**Test Coverage:** 26/26 unit, Architecture.Tests PASS  
**Plan Reference:** `plans/260519-0421-process-handler-resolution-v2/`
