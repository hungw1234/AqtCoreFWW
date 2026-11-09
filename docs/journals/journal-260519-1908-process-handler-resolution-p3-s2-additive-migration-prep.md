# Process-Handler-Resolution P3 SESSION-2: Additive Migration Prep

**Date**: 2026-05-19 19:08  
**Severity**: Medium  
**Component**: Workflow.Domain, EF migrations, RPC→Assignment data migration  
**Status**: Resolved + Ready for P3 SESSION-3  

## What Happened

SESSION-2 executed the additive-only phase of the RPC→assignment cutover (commit a38f93e6, stack a38f93e6→8e796d0a→ae3f1ddb→bb73ab12, NOT pushed, v2.137p74). Delivered single EF migration 20260519102348_RequiredPermissionCodeToAssignmentPrep: data-copy phase only, column WorkflowTransition.RequiredPermissionCode LEFT UNTOUCHED. No field/DTO/handler removal yet—those live in SESSION-3.

## The Brutal Truth

First tester ran WRONG project (Workflow.Integration in-process instead of scoped AqtCoreFW.Tests.Integration), missed the 403 merge-gate blocker + known pre-existing failure. Re-dispatch forced precision. Then tester asserted "only 1 shown = logger artifact" instead of showing name-level evidence—blind trust failed. Discovered a REAL NEW regression (SchemaUniversalsTests.PageCompression): backup table created as HEAP, not PAGE-compressed, causing invariant fail. False-green discipline got bent; source-level diff demand corrected it. Frustrating because the architecture was sound; the regression was structural (compression mode on heap), not conceptual.

## Technical Details

**Migration SQL behavior:**  
Each non-null `WorkflowTransition.RequiredPermissionCode` → `WorkflowTransitionAssignment(CombineMode=Union, AllowAssignorOverride=false)` + 1 clause(ByPermission, RefsJson round-trip System.Text.Json exactly). Backup table `[workflow].[WorkflowTransitionRequiredPermissionCodeBackup]` created WITH (DATA_COMPRESSION=PAGE) to pass SchemaUniversalsTests invariant. Idempotent: dedupe (transitionId,code), UX-unique-per-transition, one-ByPermission-clause-per-assignment → rerun is no-op.

**Regression root cause:**  
Migration initially created backup as implicit HEAP (no compression specified). apply-page-compression.sql does ALTER INDEX ALL REBUILD which skips heaps → 1 uncompressed partition → SchemaUniversalsTests.PageCompression_Script_Applies_To_All_App_Tables failed (8 baseline→9 current). Fix inlined in migration SQL constant WITH (DATA_COMPRESSION=PAGE).

**Verification final:**  
Build 0E; 2 Testcontainers prep facts PASS; 403 gate 5/5 unregressed; PageCompression PASS post-fix; full AqtCoreFW.Tests.Integration 8 fail ALL worktree-proven pre-existing identical to 8e796d0a baseline → 0 NEW regressions. Snapshot zero-drift. Code review APPROVE-WITH-NITS (1 LOW doc nit applied).

## Lessons Learned

1. **False-green discipline non-negotiable**: Never accept "logger artifact" reason-assertion; demand name-level FQN diff (got it; caught regression).
2. **Subagent root-cause from source, not reasoning**: Tester blamed "businessprocess schema added" (FALSE—pre-existing BP MVP, unrelated). True cause: compression mode on heap. Verify from ALTER/CREATE statements, not narrative.
3. **Architecture-is-sound pattern works**: Decoupling column-drop from data-copy (P2b precedent) eliminates session-lock hazard. Additive migration = low-risk, reversible Down.

## Next Steps

SESSION-3 cutover (needs explicit user confirm):  
- M2 column-drop + field/DTO/Create/Clone/SaveGraph/NewTransitionSpec removal  
- Resolver fail-CLOSED gate (ExecuteTransitionCommandHandler:97-104 + GetAvailableTransitionsQueryHandler:57-79 + ResolveBranchChildCommandHandler wrap—RT3-M1-F1 carry-forward)  
- EscalationCheckJob pool-safety+aging (RT3-M3 rides cutover)  
- DELETE QueryAllowedTransitionsAsync + 3-layer thread override + FE RPC removal/orval  

Phase 3 remains In Progress until full PR merged. User-ruled "ONE migration = atomic-AT-MERGE" (403 gate enforces) proved correct; multi-session additive split de-risks the cutover.

---

**Status**: RESOLVED + VERIFIED  
**Summary**: P3 SESSION-2 additive migration delivered (data-copy phase, RPC→assignment population, backup + page-compression fix). 0 new regressions. Ready for SESSION-3 cutover confirmation.
