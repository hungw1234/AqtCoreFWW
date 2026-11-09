using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Domain.Enums;
using AqtCoreFW.Workflow.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace AqtCoreFW.Workflow.Integration.Tests.Helpers;

/// <summary>
/// PreviousHandler-mode seed helper. Kept in a separate partial so the base
/// InlineWorkflowDefinitionFactory file stays focused on the pool/ByRole paths.
/// </summary>
internal static partial class InlineWorkflowDefinitionFactory
{
    /// <summary>
    /// Returned by <see cref="CreateThreeStateWithPreviousHandlerAsync"/>.
    /// 3-state def: Draft → InReview → Approved.
    /// T1 (Draft→InReview): ByPermission clause — lets the transition actor pass the
    ///   ExecuteTransition authz-of-T gate via its effective permission codes.
    /// T2 (InReview→Approved): PreviousHandler clause (ref-less) — when T1 executes, the
    ///   factory resolves the new InReview task's assignee to whoever completed the
    ///   most-recent prior task on the instance. This is a single-assignee producer path
    ///   (direct AssigneeId), NOT a candidate-index pool path.
    /// </summary>
    public sealed record SeededPreviousHandlerDefinition(
        long DefinitionId,
        Guid DefinitionPublicId,
        long DraftStateId,
        long InReviewStateId,
        long ApprovedStateId,
        long DraftToInReviewTransitionId,
        long InReviewToApprovedTransitionId,
        string PermissionCode);

    public static async Task<SeededPreviousHandlerDefinition> CreateThreeStateWithPreviousHandlerAsync(
        IServiceProvider rootServices,
        Guid tenantId,
        Guid createdBy,
        string permissionCode,
        CancellationToken ct = default)
    {
        await using var scope = rootServices.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        var gen = scope.ServiceProvider.GetRequiredService<IGuidGenerator>();

        var definitionKey = $"prevh-3s-{Guid.NewGuid():N}";
        var definition = WorkflowDefinition.Create(
            gen: gen,
            name: $"PreviousHandler 3-State Workflow {definitionKey}",
            description: "3-state def for PreviousHandler producer e2e tests",
            tenantId: tenantId,
            createdBy: createdBy,
            definitionKey: definitionKey,
            entityType: "IntegrationTest");

        db.WorkflowDefinitions.Add(definition);
        await db.SaveChangesAsync(ct);

        var draft = WorkflowState.Create(
            definitionId: definition.Id,
            name: "Draft",
            displayNameVi: "Nháp",
            displayNameEn: "Draft",
            isInitial: true,
            isFinal: false,
            color: "#808080",
            sortOrder: 0);

        var inReview = WorkflowState.Create(
            definitionId: definition.Id,
            name: "InReview",
            displayNameVi: "Đang xét duyệt",
            displayNameEn: "In Review",
            isInitial: false,
            isFinal: false,
            color: "#faad14",
            sortOrder: 1);

        var approved = WorkflowState.Create(
            definitionId: definition.Id,
            name: "Approved",
            displayNameVi: "Đã duyệt",
            displayNameEn: "Approved",
            isInitial: false,
            isFinal: true,
            color: "#52c41a",
            sortOrder: 2);

        db.Set<WorkflowState>().AddRange(draft, inReview, approved);
        await db.SaveChangesAsync(ct);

        var draftToInReview = WorkflowTransition.Create(
            definitionId: definition.Id,
            fromStateId: draft.Id,
            toStateId: inReview.Id,
            actionName: "StartReview",
            actionLabelVi: "Bắt đầu xét duyệt",
            actionLabelEn: "Start Review",
            conditionsJson: null,
            sortOrder: 0);

        var inReviewToApproved = WorkflowTransition.Create(
            definitionId: definition.Id,
            fromStateId: inReview.Id,
            toStateId: approved.Id,
            actionName: "Approve",
            actionLabelVi: "Phê duyệt",
            actionLabelEn: "Approve",
            conditionsJson: null,
            sortOrder: 0);

        db.Set<WorkflowTransition>().AddRange(draftToInReview, inReviewToApproved);
        await db.SaveChangesAsync(ct);

        // T1 assignment: ByPermission — transition actor's effective codes must include permissionCode.
        var t1Assignment = WorkflowTransitionAssignment.Create(
            workflowTransitionId: draftToInReview.Id,
            combineMode: AssignmentCombineMode.Union);
        t1Assignment.AddClause(AssignmentClauseMode.ByPermission, $"""["{permissionCode}"]""");

        // T2 assignment: PreviousHandler — ref-less mode; resolver reads the instance's last
        // completed task's CompletedBy at ExecuteTransition time.
        var t2Assignment = WorkflowTransitionAssignment.Create(
            workflowTransitionId: inReviewToApproved.Id,
            combineMode: AssignmentCombineMode.Union);
        t2Assignment.AddClause(AssignmentClauseMode.PreviousHandler, null);

        db.WorkflowTransitionAssignments.AddRange(t1Assignment, t2Assignment);
        await db.SaveChangesAsync(ct);

        return new SeededPreviousHandlerDefinition(
            definition.Id,
            definition.PublicId,
            draft.Id,
            inReview.Id,
            approved.Id,
            draftToInReview.Id,
            inReviewToApproved.Id,
            permissionCode);
    }
}
