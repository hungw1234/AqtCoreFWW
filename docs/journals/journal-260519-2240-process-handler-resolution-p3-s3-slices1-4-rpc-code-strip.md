# Process-Handler-Resolution P3 SESSION-3: Slices 1–4 RPC Code-Strip + G1 Reconciliation

**Date**: 2026-05-19 22:40  
**Severity**: High  
**Component**: Workflow.Domain entity, EF model, write-side handlers, FE orval DTOs  
**Status**: Resolved + Ready for P3 SESSION-5  

## What Happened

SESSION-3 executed cutover removal phase (commit 575ca997, v2.137p75, unmerged feature branch). Delivered Slices 1–4 bundled in single commit: FE RPC property strip (9 orval files hand-edited 4-line fix), entity/EF-config/DTO field deletion (WorkflowTransition.RequiredPermissionCode), write-side handler removal (Create/Clone/Save/NewTransitionSpec/WorkflowDefinitionRepository calls, all refs excised), legacy query handler (`DeleteQueryAllowedTransitionsAsync`, 0 prod callers per RT2-6), schema/migration housekeeping. Database column REMAINS LIVE (deferred to Slice 8 M2 irreversible cutover). ExecuteTransitionCommandHandler fine-grained check stripped (breadcrumb comment preserved; resolver fail-CLOSED = Slice 5 hard blocker).

## The Brutal Truth

**G1 reframing conflict resolved explicitly.** Planner report claimed G1 merge-gate must stay RED until Slice 11 ("partial policy, incomplete, gate fails until all removals done"). Two independent agent verifications + memory history proved G1 has been 5/5 GREEN since session-1 (ae3f1ddb coarse policy applied). **G1 is not a "flag to keep red"—it is a permanent invariant floor.** It goes RED only if `[Authorize(Policy=PermCodes.Workflow.Task.Process)]` on 4 execute surfaces is weakened. Decision: Prepended CORRECTION banner to planner report. Slice 5 (fresh-context) must NOT interpret "failed G1 in plan-doc" as permission to revert coarse policy.

Exhaustion factor: 8-ship day, late ICT. Surfaced context-consumed + clean-STOP-exists twice. User ruled "tới Slice 5 fresh-context" (correct call). Mid-PR fine-grained authz gap accepted-by-design (coarse policy floors all 4 surfaces + G1 prevents partial-merge + branch unmerged/unpushed).

## Technical Details

**Scope deleted:**
- Entity field: `WorkflowTransition.RequiredPermissionCode` (string)
- EF config: `ToJsonProperty("requiredPermissionCode")`, `.IsRequired(false)`
- Domain DTOs: all 3 (create/response/item) RPC properties
- FE orval files ×9: strip property from interfaces (4-line hand-edit per file, full regen deferred Slice 10—BE openapi spec unchanged until M2)
- i18n keys: "工作流.转移.必需权限代码" cleaned
- QueryHandlers: `GetAvailableTransitionsQueryHandler` ctor identityClient param → REMOVED (CS9113 "unused parameter warning" logged as Slice-5 cleanup)
- Write-side: `WorkflowDefinitionRepository.Create/Clone/Save`, `NewTransitionSpecificationCommand`, `ExecuteTransitionCommandHandler.Execute` (fine-grained RPC check stripped, replaced with comment "Slice 5: resolver fail-CLOSED")

**Snapshot behavior:**
- `WorkflowTransitionDto` **serialization includes** `RequiredPermissionCode` (string, nullable) as carry-forward
- Data still round-trips correctly; DB column untouched
- M2 (Slice 8) will drop column + snapshot prop → final cleanup

**Verification (tester + independent code-reviewer):**
- Build: 0 errors, 1 expected CS9113 (unused identityClient, Slice-5 stopgap logged)
- Unit tests: 663/663 (App 456 / Domain 145 / Infra 62)
- Architecture 101/101 (G2 pass; no PermCodes allowlist work in S3)
- G1 merge-gate: WorkflowExecuteTransitionAuthzGateTests 5/5 GREEN (floor invariant verified, no regression)
- G3 Workflow.Integration: 24 passed / 0 failed / 2 skipped
- G4 SchemaUniversalsTests.PageCompression: 1/1 (backup table from S2 verified)
- Code-reviewer: 9/10, Critical 0, High 0, 3 Low (E2E seed authz loosening, asymmetric DTO populate, 23 pre-existing test fixes) all accepted-by-design mid-PR

**0 new regressions** (verified against worktree parent 5e7c64b3 docs commit).

## Decisions & Lessons

**DECISION 1: G1 Is Floor Invariant, Not "Red Flag"**  
Planner aspirational claim "G1 must stay RED until Slice 11" contradicted by:
- S1 commit ae3f1ddb applied coarse policy → G1 immediately 5/5 GREEN
- Memory: "session-1 403 gate RED→GREEN after coarse policy"
- S2 unregressed: "5/5 unregressed"
- Two agent verifications: independent proof G1 has been GREEN since S1

**Lesson**: Memory beat plan-doc (memory = recent ship-state; plan-doc = pre-ship aspirational). Reconciliation explicit: CORRECTION banner prepended to planner report so Slice 5 does not dangerously attempt to "restore failing-default."

**DECISION 2: Fine-Grained AuthZ Gap Accepted-By-Design**  
ExecuteTransitionCommandHandler line 97–104 (RPC permission check) removed without replacement. Mid-PR state: authz gap exists at command-layer.
- **Mitigation**: Coarse policy floors all 4 HTTP surfaces (G1); G1 prevents partial-merge; branch unmerged/unpushed
- **Blocker**: Slice 5 (resolver fail-CLOSED replacement on 3 handlers including ResolveBranchChildCommandHandler RT3-M1-F1) is **mandatory before merge**. Non-optional.

**DECISION 3: Orval Full Regen Deferred (Slice 10)**  
Hand-stripped 9 FE files (4-line fix per file—remove property). Full orval regen deferred because BE openapi spec unchanged until M2 column-drop. Adds minimal re-work end-of-PR.

**DECISION 4: Multi-Session Discipline Held**  
Twice surfaced scope/exhaustion conflict to user (8-ship day, late ICT). User confirmed "tới Slice 5 fresh-context." At clean S3 boundary, re-surfaced context-consumed reality + clean-STOP-exists → user finalized session. **Prevents context-exhausted reckless commits** (memory: ctx-exhaustion → dangerous mid-PR decisions).

## Carry-Forward for Slice 5

- **G1 MUST stay 5/5 GREEN** (floor invariant—do NOT revert coarse policy under any circumstance)
- **Resolver fail-CLOSED is mandatory blocker** before merge (3 handlers: ExecuteTransitionCommandHandler, GetAvailableTransitionsQueryHandler, ResolveBranchChildCommandHandler)
- **Q1 decision pending**: Add `IWorkflowDefinitionRepository.GetOutgoingWithAssignmentsAsync` graph-load method (resolver needs it; pending user confirmation)
- **Slices 5/6/7/8 each require fresh-context** per plan STOP markers (security-critical code; memory: stacking slices → reckless)
- **M2 (Slice 8) irreversible cutover** (column drop + fail-CLOSED gate + EscalationCheckJob): gated by Q3 (Down=documented-refuse, backup table LIVE for recovery)

**Docs committed**: dependency-map (IWorkflowModuleClient 9→8), housekeeping (3 journals + planner + code-reviewer reports + plan sync + changelog).

---

**Status**: RESOLVED + VERIFIED  
**Summary**: P3 S3 Slices 1–4 delivered (RPC entity/handler/FE strip, G1 reconciliation explicit, 0 new regressions). G1 floor invariant corrected in plan-doc. Slice 5 fresh-context mandatory (resolver fail-CLOSED blocker).
