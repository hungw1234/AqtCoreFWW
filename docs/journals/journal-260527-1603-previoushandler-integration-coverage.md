# Journal — PreviousHandler producer integration coverage (v2.137p122)

**Date:** 2026-05-27 ~16:03 ICT
**Commit:** `7ae78b7d` (pushed origin/main) · **Type:** test (additive, zero production-code change)

## What shipped

Two backlog rows closed in one `/aqt-next`-driven slice:

- **RT2-15-PHASE-01-FU — SHIPPED.** New `WorkflowPreviousHandlerProducerIntegrationTests` (2 tests) + seed helper `InlineWorkflowDefinitionFactory.PreviousHandler.cs`. Drives the real command pipeline (create instance → complete Draft task → execute transition) against SQL Server, exercising the real `WorkflowTaskRepository.GetLastCompletedTaskAsync` query that the mocked unit suite (`WorkflowHandlerResolverTests.cs:400-478`) could not.
  - P1 happy path: PreviousHandler assigns the next task to the prior task's **completer**, using three distinct identities (initiator creates + executes the transition; a different user completes) so the assertion **discriminates PreviousHandler from the Initiator clause mode**. Also asserts single-assignee shape (`CandidateRefsJson` null, zero `WorkflowTaskCandidateIndex` rows).
  - P2 fail-closed: no prior completed task → empty handler pool → `ExecuteTransition` 422 (WF_NO_HANDLER), no next task, instance stays at Draft.
- **RT2-15-PHASE-03-FU — CLOSED stale.** Two read-only audits proved all 6 acceptance points already covered split-layer: workflow producer test (`WorkflowPoolTaskProducerIndexIntegrationTests`, p121) for ByRole producer→index→inbox; Identity `IdentityModuleClientRoleNamesTests:59` for the direct-only role semantic.

Full WF.Integration 74/74 (4 pre-existing skips), build 0W/0E. Review 8.5/10 → hardened (Med-1 applied).

## Decisions / lessons

- **Audit-at-slice-start (260522-A) catches test tautology, not just stale rows.** RT2-15-PHASE-03-FU asked to seed a group-inherited-only user in a *workflow* integration test and assert exclusion. That would be tautological: the workflow test stubs `IIdentityModuleClient`, so the stub returns whatever it's told — it cannot prove the production `GetUserRoleNamesAsync` query is direct-only. The load-bearing assertion correctly lives in the Identity module against the real query. The row's "put both arms in one test" premise was architecturally wrong.
- **Single-identity happy-paths hide clause-mode confusion (review Med-1).** The first P1 draft used one identity for initiator = completer = transition-executor = asserted assignee. `AssigneeId == actorId` would also pass if the resolver bug-routed PreviousHandler into the Initiator branch. Fix: distinct completer ≠ initiator, assert `== completer && != initiator`. Converts "exercises the SQL" into "proves PreviousHandler semantics."
- **Choreography verified empirically before writing:** `task.Complete(userId)` (sets `CompletedBy`) is a SEPARATE command (`CompleteTaskCommandHandler:33`) from `ExecuteTransition` — the latter never completes the current task. PreviousHandler therefore needs a prior `CompleteTask` call to resolve non-empty. The row's named unit file (`WorkflowTransitionAssignmentPreviousHandlerProducerTests.cs`) did not exist (memory/row decay); real coverage was in `WorkflowHandlerResolverTests.cs`.
- **Full-assembly gate re-run on final code** even though Med-1 only changed in-test identities — honoring the documented "filtered-green" anti-pattern (260525-C) rather than rationalizing a skip.

## Residual (Low, non-blocking)

Ordering-with-multiple-completed-tasks (`ORDER BY CompletedAt DESC` with >1 row) not exercised — single completed task makes direction moot; query trivially correct. Optional future hardening if the unit suite's ordering coverage is found thin.
