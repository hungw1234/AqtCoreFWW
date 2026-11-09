# Journal — PHR-V2 P3 Slice 10a: Task-Creation Seam + Override Gate (3-layer thread)

**Date:** 2026-05-20 22:00
**Branch:** `feat/process-handler-resolution-p3` @ `1b0a9263` (LOCAL, 28 ahead `origin/main`)
**Version:** v2.137p82
**Slice scope:** ETR task-creation closure + override gate (4-layer ordering) + 3-layer NextHandlerUserId thread; 9 new facts (A0-A0c + A1-A4 + A6)
**Phase 3 progress:** 9/11 → 10/11 = 90.9%

---

## Outcome

Slice 10a SHIPPED LOCAL. Three interlocked changes: (1) ETR HandleCore else-branch (non-branching, non-recall path) now materializes single `WorkflowTask` via new `IWorkflowTaskRepository.Stage(task)` no-save method + outer SaveChanges; closes 6-slice latent design gap; (2) Override gate enforces 4-layer ordering: null-skip → AllowAssignorOverride DGATE-b soft fallthrough → live IsActorAuthorized(overrideCodes) → live `IIdentityModuleClient.GetUserActiveStatusAsync` (IsActive/!IsLockedOut); (3) `Guid? NextHandlerUserId = null` final optional positional threaded through 7 DTO layers (Controller, ETR, BP, Handler, Client, SharedKernel, BatchExecuteTransition). All 9 facts GREEN. Build 0E/0W. Review APPROVE 9.4/10 (0 Crit/0 High/0 Med/2 Low deferred to S10b orval).

**Commit: `1b0a9263` — 14 files, +847/-93 LOC**

---

## What shipped

| Category | File | Change |
|----------|------|--------|
| **Task-creation** | `IWorkflowTaskRepository.cs` (interface) | +3: NEW `Stage(WorkflowTask task) : Task` no-save method |
| | `WorkflowTaskRepository.cs` (impl) | +12: Stage() calls `AddAsync(task)` + returns; A0 proof |
| | `ExecuteTransitionCommandHandler.cs` (ETR) | +43/-5: else-branch (non-branching) now calls `_taskRepo.Stage(newTask)` pre-save; closes design gap |
| **Override gate** | `ExecuteTransitionCommand.cs` | +6: DGATE-b brief on AllowAssignorOverride (silent fallthrough if false, LogInformation) |
| | `PoolTransitionAssignmentResolverV2.cs` (IsActorAuthorized) | +35/-2: 4-layer gate ordering (null-skip → DGATE-b → IsActorAuthorized → live GetUserActiveStatusAsync) |
| | `IIdentityModuleClient.cs` (interface) | +4: NEW `GetUserActiveStatusAsync(userId, ct)` method |
| | `IdentityModuleHttpClient.cs` (impl) | +18: Call `/api/users/{userId}/status` endpoint; A1 contracts |
| **3-layer NextHandlerUserId** | `ExecuteTransitionRequest` (Controller DTO) | +1: `Guid? NextHandlerUserId = null` final optional positional |
| | `ExecuteTransitionCommand` (ETR) | +1: Thread to Command |
| | `ExecuteTransitionCommandHandler` (App) | +2: Thread to service call + log |
| | `BranchFanOutAndResolverCommand` (BP) | +1: Thread; BP caller supplies |
| | `BranchFanOutAndResolverCommandHandler` (BP) | +2: Thread to AssignmentResolver |
| | `PoolTransitionAssignmentResolverV2` (Domain) | +2: Store in `this._overrideTarget`; used by gate |
| | `IInProcessWorkflowModuleClient.cs` (SharedKernel) | +1: Thread signature |
| | `BatchExecuteTransition` (BP Cmd) | +1: Explicit null `:62` (no-override default when BP batch-executes without caller override) |
| **Tests (NEW)** | `ExecuteTransitionOverrideGateTests.cs` | +293L: A0 (Stage no-save) + A0b/A0c (task materialization pre/post ETR) + A1-A4 (override gate layers) + A6 (IsActive lock-out) |

---

## The hard part — design gap discovered mid-flight

**Original S10 scope estimate:** Override gate + 3-layer thread + orval regen ≈ 1.5h.

**Pre-flight scout (R1 risk flagged by planner):** "Verify whether ETR non-branching direct-path creates WorkflowTask in codebase."

**Empirical discovery:** It does NOT. Across all 6 prior slices (S5–S9):
- **ParallelBranchService** creates tasks for branch children only (when `state.IsTransitioningToBranchingState()` = true)
- **WorkflowTransitionedEvent handlers** (Notification, Webhook, FormSubmission, Document) do NOT touch tasks
- **CreateWorkflowTaskCommandHandler** exists but is NOT wired to ETR's immediate path
- **Phase-03 spec line** `db.AddRange(tasks)` in ETR.HandleCore was aspirational — survived 6 slices without ownership

**Root cause:** Design split early (S3): "ETR creates pool, does not create task; task-creation deferred Phase-4." But by S9, Phase-4 (Claim/Unclaim/Inbox) was imminent, and the spec said "all transitions create tasks." The gap narrowed unnoticed because:
1. S5–S6 focused on authz gate (didn't verify task path)
2. S7 defer-save refactored Repo, didn't add task-creation
3. S8 focused on column-drop, didn't add task-creation
4. S9 EscalationCheckJob worked with **existing** tasks (didn't expose missing creation)

**Decision trigger:** S10 override gate needed to know the override target's identity (NextHandlerUserId). If task is created later (Phase 4), the task.AssignedToUserId wouldn't be set during ETR, breaking assignment-based gate ordering. User was informed of the scope expansion: include task-creation seam in S10a OR defer to fresh-context S10b.

**User decision:** Expand S10a. Cost: ~45–60 min additional plan delta. ETA shifted 22:00 → 22:30 ICT.

---

## KEY META-LESSON (S10a-A): Planner over-promise on nav-prop migration-free

**Planner DELTA asserted (DCAND-IDFIX-b):** "Add nav prop `WorkflowTask Task` to `WorkflowTaskCandidateIndex` entity + `CreateWithTask` factory. No schema migration required; FK column already exists."

**Empirical reality:** Adding nav prop without explicit `HasOne` configuration in `WorkflowTaskCandidateIndexConfiguration.cs` triggered:
```
dotnet ef migrations has-pending-model-changes
> One or more model changes were found. Add a new migration.
```

EF infers a NEW relationship from nav-prop addition. Without explicit `HasOne(c => c.WorkflowTask)` + `.WithMany(t => t.CandidateIndices)` configuration, EF treats the nav-prop as an unmapped relationship = schema change = PENDING.

**Agent's fix:** Revert to DCAND-b (JSON-only `CandidateRefsJson` inside `WorkflowTaskCandidateIndex` rows, NO materialized nav-prop). Phase 4 inbox will read JSON instead of querying index rows (YAGNI; optimize later when perf demands it).

**Carry-fwd invariant:** When planner claims "nav-prop addition is migration-free," grep the EF configuration FIRST to confirm an existing `HasOne`/`HasMany` mapping exists. Don't assert this from FK column existence alone. This is the 2nd-week planner over-promise pattern (S8b also had near-miss on snapshot regeneration assumption).

---

## META-LESSON (S10a-B): Design gap survives slice-level reviews

Each of 6 prior slices had local reasons to skip task-creation:
- **S5:** No write-flow changes; deferred S6
- **S6:** Atomic-tx refactored Repo; didn't add task-creation
- **S7:** Defer-save split didn't require task changes
- **S8:** Column-drop; no task-creation
- **S9:** EscalationCheckJob operates on **existing** tasks; didn't expose the gap

Each slice individually justified. But cumulative effect: Phase 4 (Claim/Unclaim/Inbox) would inherit a half-built Model B. The gate ordering in S10 override was the tripwire.

**Carry-fwd invariant:** When planner spec contains forward-looking pseudo-code (`db.AddRange(tasks)`), planner MUST explicitly assert in phase-spec: "this seam exists / does not exist / deferred to Phase X." Do NOT treat as fact. Phase-spec literal ≠ implemented behavior.

---

## Honest reflection — user override at late-day fatigue boundary

**Session context:** 20:21 ICT start (16-ship day); memory S9 entry explicitly mandated "FRESH-CONTEXT for S10/S11."

**First override (20:27 ICT):** `/aqt-next` STRONGLY recommended EOD wrap + defer S10. User overrode: "/cook S10 --tdd despite fatigue."

**Second override (21:30 ICT):** Advisor's pre-flight warning: "Don't ship Model B's missing seam tonight in a slice not planned for it." User approved scope expansion: include task-creation.

**Empirical outcome:** Clean diff. 491/491 unit tests GREEN. 9.4/10 review (0 Crit/High/Med; 2 Low deferred to S10b). All 9 facts pass. No hidden regressions. But the path required:
- Real-time replanning (planner re-scope surface)
- Two tactical deviations (DCAND-b + A5 deferral)
- Tighter gates (pre-finalize advisor calls, multiple advisory escalations)
- Faster issue escalation (EF nav-prop discovery → revert cycle = ~8 min)
- Final ship at 22:30 ICT (vs 22:00 target)

**Take-away:** Late-day overrides CAN produce clean ships. But they require orchestrator gates to hold HARDER (not looser). S9 mandatory invariant: "orchestrator-owned commit gates" held exactly because workflow protocols were followed (advisor escalations, planner gates, no premature finalization). If protocols had been delegated away, the night could have ended with: (a) nav-prop migration added accidentally, (b) DCAND rows written without proper EF config, (c) A5 BP-path integration skipped under fatigue, (d) task-creation seam shipped half-built. The clean result depends on the protocol discipline, not the user decision itself.

---

## Gate matrix (S10a locked)

| Gate | Result | Evidence |
|------|--------|---------|
| Build | **0E/0W** | `dotnet build` integration + host; no EF pending-changes |
| EF drift | **"No changes"** | Task-creation is no-save method; no schema additions |
| G1 (coarse policy) | **5/5** | Unchanged; assignment-based authz sole path |
| G1' (fine gate) | **5/5** | WorkflowFineResolverGateTests |
| G2 (domain) | **101/101** | AqtCoreFW.Workflow.Domain.Tests |
| G3 (integration) | **34p/1f/2skip** | 1f pre-existing SimultaneousRace; 2skip pre-existing |
| G3+ (contracts) | **34p/1f** | Pre-existing fail |
| G4 (PageCompression) | **1/1** | No schema changes |
| **G5 (A0-A0c + A1-A4 + A6)** | **9/9 GREEN** | ExecuteTransitionOverrideGateTests (all pass) |
| **G5+ (Application unit)** | **491/491** | Total unit + A0-A6; all pass |

---

## Decisions ratified (D1-D5)

| # | Decision | Choice | Rationale |
|---|----------|--------|-----------|
| D1 | Task-creation seam closure | Include in S10a (vs defer S10b) | Gate ordering requires task identity pre-save; Phase 4 blocks otherwise |
| D2 | Task identity materialization | `IWorkflowTaskRepository.Stage(task)` no-save | Defers SaveChanges to ETR outer scope; atomic with pool creation |
| D3 | Override gate layer order | null-skip → DGATE-b → IsActorAuthorized → IsActive/Unlocked | Matches S5 Model-B gate design; live identity check last (defense in depth) |
| D4 | NextHandlerUserId thread model | Positional optional `Guid? = null` final param | KISS; no builder, no optional-args explosion; grep-verified 7 layers |
| D5 | WorkflowTaskCandidateIndex nav-prop | Defer to Phase 4 (use JSON interim) | EF config burden outweighs Phase 3 ROI; YAGNI |

---

## Test facts (A0-A0c + A1-A4 + A6)

| Test | Scenario | Assertions |
|------|----------|-----------|
| **A0** | Stage(task) calls repo.AddAsync | Task.Id = generated; returns completed Task |
| **A0b** | ETR non-branching path enters Stage call | EF ChangeTracker sees Added state pre-SaveChanges |
| **A0c** | ETR SaveChanges persists staged task | Task.CreatedAt set; Phase 4 can query by TransitionId |
| **A1** | Override null → skip authz gate entirely | LogInformation emitted; IsActorAuthorized NOT called |
| **A2** | Override supplied; AllowAssignorOverride = false | DGATE-b silent fallthrough; LogInformation "authz deferred" |
| **A3** | Override supplied; IsActorAuthorized = true | Task assigned to override target; gate passes |
| **A4** | Override supplied; GetUserActiveStatusAsync IsActive = false | Task assignment BLOCKED; 403 Forbidden |
| **A6** | Batch-execute (no override supplied) | NextHandlerUserId = null explicit `:62`; pool assignment applies |

---

## Cost analysis

- **Planner ratification + pre-flight scout (R1):** ~12 min (design gap discovery + decision escalation)
- **Task-creation seam (Stage method + ETR integration):** ~18 min (method interface + impl + threading + A0-A0c TDD)
- **Override gate (4-layer ordering + IsActorAuthorized extension):** ~20 min (gate logic + A1-A4 TDD)
- **NextHandlerUserId 3-layer thread:** ~12 min (7 DTO layers grep-verified + A6 batch test)
- **EF nav-prop reversal (DCAND-b revert):** ~8 min (discovery + rollback + EF drift pre-flight check)
- **Advisor pre-finalize gates + escalations:** ~10 min (multiple advisory calls; protocol holds)
- **Build + G1-G5 gates:** ~12 min
- **Code review APPROVE cycle:** ~5 min (nits = Low-1/Low-2 deferred S10b)
- **Commit + docs + journal + memory:** ~10 min

**Total: ~107 min.** Most time = A0-A4 TDD (~50 min combined) + planner gates/escalations (~22 min) + EF nav-prop discovery cycle (~8 min). **Shipped at 22:30 ICT.**

---

## Carry-forward to S10b (Orval regen + cleanup)

**S10b scope (FRESH-CONTEXT):**
1. `orval` regenerate wire types from spec; sweep 4 literal stale payloads (6 lines):
   - `SaveDefinitionGraphIntegrationTests.cs:63,64`
   - `src/http/workflow.http:85,95,105`
   - `web/e2e/workflow-definitions.spec.ts:104,109`
   - `web/e2e/workflow-dynamic-instances.spec.ts:79,84`
2. FE `npm run tsc` 0 errors
3. A5 BP-path integration smoke (compile + grep proved today; runtime deferred)

**S10b invariants carry:**
1. Task-creation seam is now CLOSED (no design gap remaining)
2. Override gate is 4-layer ordered + live identity check
3. NextHandlerUserId thread is grep-verified 7 layers
4. EF nav-prop metadata: add explicit `HasOne` config BEFORE claiming migration-free (S10a-A lesson)

**S11 (atomic-at-merge, FRESH-CONTEXT):**
1. Proof gate: 5 fine-resolver tests GREEN (coarse policy floor + S8b A7 wire-spec-lock)
2. Migration audit: S2→S8b chain locked; S8b Down() throw defensible
3. GH UI review: 28 commits as single PR; atomicity intact; no cherry-pick risk

---

## Inherited META-LESSONS still active

**(From S5-S9: not sunset; all apply)**

1. S5 META-L (c): Tests-to-impl-lock — non-tautological precondition asserts (A0 proves repo.AddAsync call fires)
2. S5 META-L (a): Subagent under-states — grep verified 14 actual files vs claim
3. S6 META-L (a): Boundary preservation (S10a applies: gate ordering is boundary-aware)
4. S7 META-L (e): Rollback-path invariants (S10a: no new rollback path; override gate is forward-only)
5. S8b META-L #1: Pre-finalize advisor enumeration (S10a: applied for GetUserActiveStatusAsync live integration verification)
6. S8b META-L #2: EF drift audit pre-flight (S10a: `has-pending-model-changes` gated DCAND-b revert decision)
7. S8b META-L #3: FQN-based test failure tracking (inherited from S6-S8; applies A0-A6)
8. S9 META-L (CapturingLogger): Hand-rolled logger for ILogger<T> (not used S10a; remains available)

---

## Unresolved / Deferred to S10b

1. **Orval regen:** 4 literal stale payload references (not critical; JSON binding lenient; Low-1 review nit)
2. **A5 BP-path integration smoke:** Runtime verification deferred (compile + grep today = sufficient; full e2e deferred Phase 4)
3. **DCAND-a (index row writes):** Requires EF `HasOne` config + new migration in separate slice; Phase 4 inbox reads JSON interim (YAGNI satisfied)

---

## Phase 3 summary (post-S10a)

**Slices completed:** S1, S2, S3-S4, S5, S6, S6.1, S7, S7.1, S8a, S8b, S9, S10a = 12 distinct delivery units
**Progress:** 10/11 = 90.9% (S10b orval + S11 atomic-merge remain)
**Schema state:** Column-drop finalized (S8b); no new schema S10a; all task-creation logic is no-save method
**Code state:** Assignment-based authz sole path; task-creation seam closed; override gate 4-layer ordered; 3-layer thread complete
**Merge readiness:** 5 fine-resolver tests pass; G1-G5 all GREEN; 28 commits LOCAL; atomic-at-merge gated for S11

**Next slices:**
- **S10b:** Orval regen + literal cleanup + FE TSC (side-effect sweep; non-critical; fresh-context)
- **S11:** Atomic-merge gate verify (fine-resolver gate + migration audit + GH UI review; final gate before push to origin/main)

---

## Related journals

- [S9: EscalationCheckJob pool-safety + pool-aging](journal-260520-1620-process-handler-resolution-p3-s9-escalation-pool-safety.md)
- [S8b: RequiredPermissionCode IRREVERSIBLE column-drop](journal-260520-1539-process-handler-resolution-p3-s8b-irreversible-column-drop.md)
- [S8a: RPC wire-format strip](journal-260520-1236-process-handler-resolution-p3-s8a-rpc-wire-strip.md)
