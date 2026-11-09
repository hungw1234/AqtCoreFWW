using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Domain.Enums;
using AqtCoreFW.Workflow.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace AqtCoreFW.Workflow.Integration.Tests.Helpers;

/// <summary>
/// Parallel-branch extension methods for <see cref="InlineWorkflowDefinitionFactory"/>.
/// Slice 6: seeds AND/OR/N-of-M branching workflow definitions for A1 (rollback) + A2 (happy-path) tests.
///
/// Workflow shape:
///   Draft  ──Submit──→  Branching  ──[parallel fan-out]──→  Continuation  ──Complete──→  Final
///
/// Transition assignments are seeded via <see cref="InlineWorkflowDefinitionFactory.SeedAuthorizingAssignmentAsync"/>
/// so callers that hold the permission stub pass the fail-CLOSED gate.
/// </summary>
internal static partial class InlineWorkflowDefinitionFactory
{
    /// <summary>
    /// Permission code seeded as each branch child's ByPermission pool. A holder of this code can
    /// claim + resolve the fan-out child tasks. The fork-entry gate (fail-CLOSED) requires every child
    /// to carry a non-empty pool or a default assignee, so this rule is mandatory for the fork to stage.
    /// </summary>
    public const string ChildPoolPermission = "WORKFLOW.TEST.CHILDPOOL";

    /// <summary>
    /// Seeded ids for a 4-state parallel-branch workflow definition.
    /// Draft → Branching (parallel fan-out) → Continuation → Final
    ///
    /// Transition assignment seeding required before use:
    ///   SeedAuthorizingAssignmentAsync(services, SubmitTransitionId, permission) — for Draft→Branching authz.
    ///   SeedAuthorizingAssignmentAsync(services, ContinueTransitionId, permission) — for Branching→Continuation
    ///     WF_NO_HANDLER pre-check (target=Branching is non-final, must have at least one outgoing assignment).
    /// </summary>
    public sealed record SeededParallelDefinition(
        long DefinitionId,
        long DraftStateId,
        long BranchingStateId,
        long ContinuationStateId,
        long FinalStateId,
        long SubmitTransitionId,     // Draft → Branching
        long ContinueTransitionId,   // Branching → Continuation (allows WF_NO_HANDLER pre-check to pass)
        long CompleteTransitionId,   // Continuation → Final
        long ParallelBranchId,
        long BranchChildId1,
        long BranchChildId2);

    /// <summary>
    /// Seeds a 4-state parallel-branch workflow with AND/OR/N-of-M semantics.
    /// Two branch children are created so all join strategies can be tested.
    /// </summary>
    public static async Task<SeededParallelDefinition> CreateWithParallelBranchAsync(
        IServiceProvider rootServices,
        Guid tenantId,
        Guid createdBy,
        ParallelBranchType branchType,
        int? requiredCount = null,
        CancellationToken ct = default)
    {
        await using var scope = rootServices.CreateAsyncScope();
        var db  = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        var gen = scope.ServiceProvider.GetRequiredService<IGuidGenerator>();

        var key = $"branch-{branchType}-{Guid.NewGuid():N}";
        var definition = WorkflowDefinition.Create(gen,
            name:         $"ParallelBranch Test {key}",
            description:  "Inline 4-state parallel-branch workflow for Slice 6 tests",
            tenantId:     tenantId,
            createdBy:    createdBy,
            definitionKey: key,
            entityType:   "IntegrationTest");

        db.WorkflowDefinitions.Add(definition);
        await db.SaveChangesAsync(ct);

        // States
        var draft = WorkflowState.Create(definition.Id, "Draft", "Nháp", "Draft",
            isInitial: true,  isFinal: false, "#808080", 0);
        var branching = WorkflowState.Create(definition.Id, "Branching", "Phân nhánh", "Branching",
            isInitial: false, isFinal: false, "#faad14", 1);
        var continuation = WorkflowState.Create(definition.Id, "Continuation", "Tiếp tục", "Continuation",
            isInitial: false, isFinal: false, "#1890ff", 2);
        var final = WorkflowState.Create(definition.Id, "Final", "Hoàn thành", "Final",
            isInitial: false, isFinal: true,  "#52c41a", 3);

        db.Set<WorkflowState>().AddRange(draft, branching, continuation, final);
        await db.SaveChangesAsync(ct);

        // Transitions
        var submit = WorkflowTransition.Create(definition.Id, draft.Id, branching.Id,
            "Submit",    "Nộp",        "Submit",    conditionsJson: null, sortOrder: 0);
        // Continue: Branching → Continuation. Required so WF_NO_HANDLER pre-check passes
        // (Branching is non-final; resolver needs at least one outgoing assignment to return non-empty pool).
        var continueT = WorkflowTransition.Create(definition.Id, branching.Id, continuation.Id,
            "Continue",  "Tiếp tục",  "Continue",  conditionsJson: null, sortOrder: 0);
        var complete = WorkflowTransition.Create(definition.Id, continuation.Id, final.Id,
            "Complete", "Hoàn thành", "Complete",  conditionsJson: null, sortOrder: 0);

        db.Set<WorkflowTransition>().AddRange(submit, continueT, complete);
        await db.SaveChangesAsync(ct);

        // Parallel branch definition (BranchingState → fan-out; ContinuationState after join)
        var branch = WorkflowParallelBranch.Create(
            definitionId:          definition.Id,
            branchingStateId:      branching.Id,
            continuationStateId:   continuation.Id,
            branchType:            branchType,
            requiredCount:         requiredCount,
            allowCancelOnAnyReject: false,
            timeoutMinutes:        0);

        db.ParallelBranches.Add(branch);
        await db.SaveChangesAsync(ct);

        // Two branch children
        var child1 = WorkflowBranchChild.Create(branch.Id, branching.Id,
            "branch-child-1", "Nhánh 1", departmentId: null, sortOrder: 0);
        var child2 = WorkflowBranchChild.Create(branch.Id, branching.Id,
            "branch-child-2", "Nhánh 2", departmentId: null, sortOrder: 1);

        db.BranchChildren.AddRange(child1, child2);
        await db.SaveChangesAsync(ct);

        // Each child carries a ByPermission pool. The fork-entry gate refuses to stage a fork when any
        // child has neither a non-empty pool nor a default assignee (an AND-join could never complete for
        // an unhandlable child). A ByPermission clause resolves to a candidate predicate (a non-empty pool),
        // so the fork stages and the child tasks become claimable by holders of ChildPoolPermission.
        foreach (var child in new[] { child1, child2 })
        {
            var assignment = WorkflowBranchChildAssignment.Create(child.Id);
            assignment.AddClause(AssignmentClauseMode.ByPermission, $"[\"{ChildPoolPermission}\"]");
            child.SetAssignment(assignment);
            db.Set<WorkflowBranchChildAssignment>().Add(assignment);
        }

        await db.SaveChangesAsync(ct);

        return new SeededParallelDefinition(
            DefinitionId:        definition.Id,
            DraftStateId:        draft.Id,
            BranchingStateId:    branching.Id,
            ContinuationStateId: continuation.Id,
            FinalStateId:        final.Id,
            SubmitTransitionId:    submit.Id,
            ContinueTransitionId:  continueT.Id,
            CompleteTransitionId:  complete.Id,
            ParallelBranchId:    branch.Id,
            BranchChildId1:      child1.Id,
            BranchChildId2:      child2.Id);
    }

    /// <summary>
    /// Pre-seeds a conflicting <see cref="WorkflowInstanceBranchStatus"/> row for the given instance
    /// and parallel branch. Because <c>UX_WorkflowInstanceBranchStatuses_InstanceId_BranchId</c> is
    /// a unique index on (InstanceId, ParallelBranchId), inserting a second row during the transition
    /// causes a DB constraint violation — triggering rollback and GOV_WFL_015 mapping (A1 seam).
    /// </summary>
    public static async Task PreSeedConflictingBranchStatusAsync(
        IServiceProvider rootServices,
        long instanceInternalId,
        long parallelBranchId,
        CancellationToken ct = default)
    {
        await using var scope = rootServices.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();

        // Insert a status row that will collide with the one InitiateBranchingAsync tries to insert.
        var conflicting = WorkflowInstanceBranchStatus.Create(instanceInternalId, parallelBranchId);
        db.InstanceBranchStatuses.Add(conflicting);
        await db.SaveChangesAsync(ct);
    }
}
