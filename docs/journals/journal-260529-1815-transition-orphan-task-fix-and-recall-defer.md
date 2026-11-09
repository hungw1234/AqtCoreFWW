# Workflow transition orphan-task fix & recall deferral

**Date**: 2026-05-29 18:15
**Severity**: High
**Component**: Workflow execution (ExecuteTransitionCommandHandler, task lifecycle)
**Status**: Resolved

## What Happened

User reported a workflow inbox issue: "Chờ xử lý" (In Progress) showed one BP run instance as two separate rows. Initial assumption was task-ID collision; digging revealed the real problem was **task orphaning on state exit**.

When a BP run transitioned between states (e.g., Draft → InReview), the handler staged a new task for the target state but **never completed the source-state task**. That task remained Open and surfaced in the inbox indefinitely. For instances where the assignee also belonged to the target state's pool, the orphan and the live task both appeared as separate rows under the same instance—user saw "duplicates."

## The Brutal Truth

The bug lived in plain sight: a task-completion flow existed (`CompleteTaskCommandHandler`), but the transition path (`ExecuteBusinessProcessTransitionCommandHandler` → `IWorkflowModuleClient.ExecuteTransitionAsync` → `ExecuteTransitionCommandHandler`) only invoked the handler to **transition**, never to **complete** the leaving state's task. Completion and transition were fully decoupled commands, and nothing on the BP "Submit" path wired them together. It survived because no test ever asserted task *status* after a transition — every transition test checked state/authz, not "is the old task closed."

Worse: I initially misread the user report. They said "duplicate id 019e6f89…" — that's the `WorkflowInstance.PublicId` (a GUID), not a task ID. The task DTO uses a different field (`Id: long`). Reading the wrong DTO field nearly wasted the debug effort.

## Technical Details

**Diagnosis (SQL-ground-truth):**
- Instance 10011: InReview state (`CurrentStateEnteredAt` = 17:09:02, matching task 10006's creation), 2 Open non-branch tasks: 10005 ("Nhập", Draft-assigned, created 17:02:27) + 10006 ("Submit", InReview-pool, created 17:09:02).
- `workflow.WorkflowTasks` showed both with Status=0 (Open) and BranchChildStatusId=NULL; neither completed.
- `workflow.WorkflowInstanceHistories` showed exactly one transition: Draft(30054)→InReview(30055), ActionName "Submit", by the same actor — confirming the instance left Draft while task 10005 stayed Open.
- ExecuteTransitionCommandHandler source did NOT call Complete anywhere; only `CompleteTaskCommandHandler` does, and the BP path never invokes it.

**Root cause (code):**
```
ExecuteTransitionCommandHandler.HandleCore()
  → Validates transition
  → Stages next-state task via IWorkflowTaskFactory
  → Returns (no Complete call)

CompleteTaskCommandHandler exists but only invoked by:
  - CompleteTaskEndpoint (manual user action)
  - NOT by ExecuteTransitionCommandHandler (missing integration point)
```

**Fix (commit f38aa9e4, v2.137p127):**
Added task completion in `ExecuteTransitionCommandHandler` before staging the next task:
```csharp
if (!transition.IsRecall)
{
    var openTasks = await taskRepository.GetOpenNonBranchByInstanceAsync(
        instance.Id, cancellationToken);
    foreach (var task in openTasks)
    {
        task.Complete(request.ActorId);
    }
}
```

Used `Complete()` (not `Cancel()`) to preserve `WorkflowTask.CompletedAt` for the PreviousHandler resolver, which feeds the last-completed-task into role-assignment decisions. All changes flush in the same S7 SaveChanges: instance state + source-task completion + new task staging.

**Regression test (commit f38aa9e4, ExecuteBpTransitionTransactionTests.cs:91–120):**
Without the fix: 2 Open / 0 Done. With it: 1 Open / 1 Done. Test runs the actual ExecuteBusinessProcessTransition path (not mocked), so it would fail if production ever calls this handler. Confirmed by temporarily disabling the fix — test fails as expected.

**Blast radius:** 94/94 tests pass (Workflow + BusinessProcess integration suites).

## What We Tried

1. **Hypothesis 1 (user-side):** FE deduping logic faulty → traced to task DTO mapping; realized the "id" field mismatch was a red herring.
2. **Hypothesis 2 (task lifecycle):** Task re-created on reactivation → SQL showed single Create event, no re-create.
3. **Direct code audit:** Searched ExecuteTransitionCommandHandler for "Complete" — found zero calls. Traced the call chain backward from CompleteTaskCommandHandler to find all call sites — none from transition path.

## Root Cause Analysis

**Why this survived:**
- No test asserted task *status* after a transition. Existing transition tests (e.g. `ExecuteBpTransitionTransactionTests`) checked the HTTP result and new state — never "did the prior task close." The orphan was invisible to a green suite.
- Completion and transition are independent commands by design. `ExecuteTransitionCommandHandler` had both `IWorkflowTaskFactory` (staging) and could reach the repository, but task *completion* lived only in the separate `CompleteTaskCommandHandler`. The BP "Submit" path transitions without ever completing — a missing wire, not a broken one.

**Why it mattered now:**
- Real users running real BP instances triggered the orphaning because the actual handler ran in production.
- The inbox query surfaces all Open tasks for an instance, with no dedup. One instance = one row per Open task (not one row per instance).

## Lessons Learned

1. **Read the DTO contract before assuming duplication.** The user's "id" referred to PublicId (UUID), not PK. Always trace identifier fields in the response shape before debugging for collisions.

2. **Regression tests must exercise the actual producer, not setup substitutes.** A manually-seeded test that calls the repository directly will never catch a bug in the handler that's supposed to seed it. Run the FULL path: handler → factory → repository writes.

3. **Coupled call sites decay silently.** ExecuteTransitionCommandHandler depended on CompleteTaskCommandHandler being called separately. There was no compile-time assertion that the contract would be upheld. If we add a task-completion gate in the future (e.g., a hook/event), this pattern will break again unless we centralize the contract.

4. **Prove a regression test by disabling the fix, not by passing.** Temporarily neutered the completion block (`if (false && …)`) → test failed 2-Open/0-Done; restored → 1-Open/1-Done. A test that's only ever seen green is not yet known to discriminate.

5. **Running dev servers lock the shared build outputs.** Auth/API (`dotnet run` from the earlier /aqt-runapp) held `Workflow.*.dll`, so `dotnet test` failed MSB3027 "file locked by AqtCoreFW.Api". Had to kill Auth+API before testing and restart them after — sequential (Auth listening → then API) to avoid the same lock during rebuild.

6. **Verify "not reachable" before scoping it away.** For the recall deferral, the first instinct was "no recall UI → not reachable → defer as speculative." The advisor flagged that as under-verified: recall executes through the *same* generic action bar as any transition (`allowed-actions-bar` renders `useAvailableTransitions`, which includes recall). Two checks settled scope honestly — UI renders transitions generically (reachable once configured) and DB has 0/35 `IsRecall=true` (not triggerable today). Recall has no task lifecycle at all (`ApplyTransition` touches no tasks); cancel-source-alone would leave it non-functional, so the real unit is the full lifecycle. Deferring is justified *because* 0 are configured — and the gap is documented, not silently dropped.

## Next Steps

1. **Data cleanup:** Completed the orphan task 10005 manually so instance 10011 inbox shows one row as expected. (DONE)

2. **Recall task lifecycle (deferred, WF-RECALL-ORPHAN-TASK):** Redesign recall transitions to have explicit task semantics (cancel source, create target, or reopen). Blocked until the first definition sets IsRecall=true. Revisit trigger: `IsRecall=true` in any definition.

3. **Task-completion centralization (future):** Consider moving the Complete logic into a service (not coupled to the handler) so any future path that transitions can reuse it without duplicating the logic.

**Files modified:**
- `src/modules/workflow/AqtCoreFW.Workflow.Application/Commands/ExecuteTransition/ExecuteTransitionCommandHandler.cs` (lines added 43+)
- `src/modules/workflow/AqtCoreFW.Workflow.Application/Repositories/IWorkflowTaskRepository.cs` (method signature)
- `src/modules/workflow/AqtCoreFW.Workflow.Infrastructure/Persistence/WorkflowTaskRepository.cs` (implementation)
- `src/tests/AqtCoreFW.Tests.Integration/BusinessProcess/ExecuteBpTransitionTransactionTests.cs` (regression test added)
- `backlogs/backlog.md` (WF-RECALL-ORPHAN-TASK deferred + audit notes)

**Commits (LOCAL, UNPUSHED):**
- f38aa9e4: fix(workflow): complete source-state task on transition
- 47cba96c: docs: changelog v2.137p127
- 801ea19a: docs(backlog): defer WF-RECALL-ORPHAN-TASK with empirical scope audit
