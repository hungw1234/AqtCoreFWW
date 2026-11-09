using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Domain.Enums;
using AqtCoreFW.Workflow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AqtCoreFW.Workflow.Integration.Tests.Helpers;

/// <summary>
/// Builds a generic 2-state workflow definition (Draft → Submitted) inline from a test fixture.
/// Replaces the Cases-coupled CaseProcessDefinitionSeeder removed in PR #71 (2026-05-01).
/// Each call generates a unique DefinitionKey so concurrent / repeated test runs do not collide
/// on the shared Testcontainers database.
/// Phase-4b: WorkflowDefinition is T2 (PublicId Guid AK, Id long PK).
///           WorkflowState/WorkflowTransition are T1 (Id long PK only, no Guid AK).
///           SeededDefinition fields are all long so callers can pass them to
///           WorkflowInstance.Create(gen, long definitionId, long initialStateId, ...).
/// </summary>
internal static partial class InlineWorkflowDefinitionFactory
{
    public sealed record SeededDefinition(
        long DefinitionId,
        long DraftStateId,
        long SubmittedStateId,
        long SubmitTransitionId);

    /// <summary>
    /// Returned by <see cref="CreateTwoStateWithRoleAssignmentAsync"/>. Extends the base
    /// seeded-definition data with the WorkflowDefinition.PublicId (Guid AK) needed by
    /// the CreateWorkflowInstance HTTP endpoint, and the role name baked into the ByRole clause.
    /// </summary>
    public sealed record SeededDefinitionWithPublicId(
        long DefinitionId,
        Guid DefinitionPublicId,
        long DraftStateId,
        long SubmittedStateId,
        long SubmitTransitionId,
        string RoleName);

    /// <summary>
    /// Returned by <see cref="CreateThreeStateWithMixedAssignmentsAsync"/>.
    /// 3-state def: Draft → InReview → Approved.
    /// T1 (Draft→InReview) carries a ByPermission clause (so the transition actor can pass
    /// the ExecuteTransition authz gate via effective permission codes).
    /// T2 (InReview→Approved) carries a ByRole clause (so the factory creates an InReview
    /// pool task whose WorkflowTaskCandidateIndex rows are ByRole).
    /// </summary>
    public sealed record SeededThreeStateDefinition(
        long DefinitionId,
        Guid DefinitionPublicId,
        long DraftStateId,
        long InReviewStateId,
        long ApprovedStateId,
        long DraftToInReviewTransitionId,
        long InReviewToApprovedTransitionId,
        string PermissionCode,
        string RoleName);

    /// <summary>
    /// Seeds a 2-state Draft → Submitted definition with a single "Submit" transition.
    /// Persists via WorkflowDbContext on the supplied service provider scope. Returns the seeded ids.
    /// </summary>
    public static async Task<SeededDefinition> CreateAsync(
        IServiceProvider rootServices,
        Guid tenantId,
        Guid createdBy,
        CancellationToken ct = default)
    {
        await using var scope = rootServices.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        // IGuidGenerator resolves V7GuidGenerator registered by the host — generates PublicId for WorkflowDefinition (T2).
        var gen = scope.ServiceProvider.GetRequiredService<IGuidGenerator>();

        var definitionKey = $"covr1-{Guid.NewGuid():N}";
        var definition = WorkflowDefinition.Create(
            gen: gen,
            name: $"COVR-1 Test Workflow {definitionKey}",
            description: "Inline 2-state workflow for ExecuteTransition + concurrency coverage",
            tenantId: tenantId,
            createdBy: createdBy,
            definitionKey: definitionKey,
            entityType: "IntegrationTest");

        // Phase 1: persist definition so EF populates definition.Id (bigint IDENTITY).
        db.WorkflowDefinitions.Add(definition);
        await db.SaveChangesAsync(ct);

        // Phase 2: create states using the real definition.Id; persist to populate draft.Id / submitted.Id.
        var draft = WorkflowState.Create(
            definitionId: definition.Id,
            name: "Draft",
            displayNameVi: "Nháp",
            displayNameEn: "Draft",
            isInitial: true,
            isFinal: false,
            color: "#808080",
            sortOrder: 0);

        var submitted = WorkflowState.Create(
            definitionId: definition.Id,
            name: "Submitted",
            displayNameVi: "Đã nộp",
            displayNameEn: "Submitted",
            isInitial: false,
            isFinal: true,
            color: "#1890ff",
            sortOrder: 1);

        db.Set<WorkflowState>().AddRange(draft, submitted);
        await db.SaveChangesAsync(ct);

        // Phase 3: create transition using real state IDs; persist to populate submit.Id.
        var submit = WorkflowTransition.Create(
            definitionId: definition.Id,
            fromStateId: draft.Id,
            toStateId: submitted.Id,
            actionName: "Submit",
            actionLabelVi: "Nộp",
            actionLabelEn: "Submit",
            conditionsJson: null,
            sortOrder: 0);

        db.Set<WorkflowTransition>().Add(submit);
        await db.SaveChangesAsync(ct);

        return new SeededDefinition(definition.Id, draft.Id, submitted.Id, submit.Id);
    }

    /// <summary>
    /// Seeds a <see cref="WorkflowTransitionAssignment"/> with a single <see cref="AssignmentClauseMode.ByPermission"/>
    /// clause on a specific transition. Call this AFTER <see cref="CreateAsync"/> on the specific tests
    /// that need to pass authz-of-T (Slice-5 fail-CLOSED gate). Do NOT call for tests that must remain
    /// unauthorized (e.g. SimultaneousRace concurrency test — leave that test without assignment seeding).
    ///
    /// Mirrors the seeding pattern in WorkflowFineResolverGateTests (canonical Slice-5 reference).
    /// </summary>
    public static async Task SeedAuthorizingAssignmentAsync(
        IServiceProvider rootServices,
        long transitionId,
        string permissionCode,
        CancellationToken ct = default)
    {
        await using var scope = rootServices.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();

        var assignment = WorkflowTransitionAssignment.Create(
            workflowTransitionId: transitionId,
            combineMode: AssignmentCombineMode.Union);
        // ByPermission clause: the StubIdentityClient returns the permission set configured via
        // WithPermissionStub — this matches when the stub includes `permissionCode`.
        assignment.AddClause(AssignmentClauseMode.ByPermission, $"""["{permissionCode}"]""");

        db.WorkflowTransitionAssignments.Add(assignment);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Seeds a <see cref="WorkflowTransitionAssignment"/> with a single <see cref="AssignmentClauseMode.ByRole"/>
    /// clause on a specific transition. RefsJson is a JSON array containing one canonical role name.
    /// Call after CreateAsync / CreateTwoStateWithRoleAssignmentAsync on the specific tests that need
    /// a ByRole pool task. ByRole has no Q2 identity hop at Save time — any canonical string is accepted.
    /// </summary>
    public static async Task SeedByRoleAssignmentAsync(
        IServiceProvider rootServices,
        long transitionId,
        string roleName,
        CancellationToken ct = default)
    {
        await using var scope = rootServices.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();

        var assignment = WorkflowTransitionAssignment.Create(
            workflowTransitionId: transitionId,
            combineMode: AssignmentCombineMode.Union);
        // ByRole clause: role name is already canonical (Trim+ToUpperInvariant) per GraphValidator contract.
        assignment.AddClause(AssignmentClauseMode.ByRole, $"""["{roleName}"]""");

        db.WorkflowTransitionAssignments.Add(assignment);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Creates a 2-state Draft → Submitted definition with a ByRole assignment on the Submit
    /// transition, then returns both the seeded IDs and the WorkflowDefinition.PublicId needed
    /// by the CreateWorkflowInstance HTTP endpoint.
    /// Use for pool-task producer end-to-end tests that drive instance creation via HTTP.
    /// </summary>
    public static async Task<SeededDefinitionWithPublicId> CreateTwoStateWithRoleAssignmentAsync(
        IServiceProvider rootServices,
        Guid tenantId,
        Guid createdBy,
        string roleName,
        CancellationToken ct = default)
    {
        var seeded = await CreateAsync(rootServices, tenantId, createdBy, ct);
        await SeedByRoleAssignmentAsync(rootServices, seeded.SubmitTransitionId, roleName, ct);

        // Fetch PublicId from DB — CreateAsync scopes its own DbContext and returns only long Ids.
        await using var scope = rootServices.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        var def = await db.WorkflowDefinitions.FindAsync(new object[] { seeded.DefinitionId }, ct)
            ?? throw new InvalidOperationException($"WorkflowDefinition {seeded.DefinitionId} not found post-seed.");

        return new SeededDefinitionWithPublicId(
            seeded.DefinitionId,
            def.PublicId,
            seeded.DraftStateId,
            seeded.SubmittedStateId,
            seeded.SubmitTransitionId,
            roleName);
    }

    /// <summary>
    /// Creates a 3-state Draft → InReview → Approved definition.
    /// T1 (Draft→InReview): ByPermission clause with <paramref name="permissionCode"/>.
    ///   The transition actor supplies this code via GetEffectivePermissionCodesAsync,
    ///   which lets the ExecuteTransition authz gate pass.
    /// T2 (InReview→Approved): ByRole clause with <paramref name="roleName"/>.
    ///   When T1 executes, the factory resolves T2's pool (HandlerResolution.Pool([ByRole:roleName]))
    ///   and materializes WorkflowTaskCandidateIndex rows for the new InReview task.
    /// Use for pool-task producer end-to-end tests on the ExecuteTransition path.
    /// </summary>
    public static async Task<SeededThreeStateDefinition> CreateThreeStateWithMixedAssignmentsAsync(
        IServiceProvider rootServices,
        Guid tenantId,
        Guid createdBy,
        string permissionCode,
        string roleName,
        CancellationToken ct = default)
    {
        await using var scope = rootServices.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        var gen = scope.ServiceProvider.GetRequiredService<IGuidGenerator>();

        var definitionKey = $"pool-3s-{Guid.NewGuid():N}";
        var definition = WorkflowDefinition.Create(
            gen: gen,
            name: $"Pool 3-State Workflow {definitionKey}",
            description: "3-state def for pool-task producer e2e tests",
            tenantId: tenantId,
            createdBy: createdBy,
            definitionKey: definitionKey,
            entityType: "IntegrationTest");

        db.WorkflowDefinitions.Add(definition);
        await db.SaveChangesAsync(ct);

        // States
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

        // Transitions
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

        // T1 assignment: ByPermission — transition actor's permission codes must include permissionCode.
        var t1Assignment = WorkflowTransitionAssignment.Create(
            workflowTransitionId: draftToInReview.Id,
            combineMode: AssignmentCombineMode.Union);
        t1Assignment.AddClause(AssignmentClauseMode.ByPermission, $"""["{permissionCode}"]""");

        // T2 assignment: ByRole — factory creates the InReview task pool with this role predicate.
        var t2Assignment = WorkflowTransitionAssignment.Create(
            workflowTransitionId: inReviewToApproved.Id,
            combineMode: AssignmentCombineMode.Union);
        t2Assignment.AddClause(AssignmentClauseMode.ByRole, $"""["{roleName}"]""");

        db.WorkflowTransitionAssignments.AddRange(t1Assignment, t2Assignment);
        await db.SaveChangesAsync(ct);

        return new SeededThreeStateDefinition(
            definition.Id,
            definition.PublicId,
            draft.Id,
            inReview.Id,
            approved.Id,
            draftToInReview.Id,
            inReviewToApproved.Id,
            permissionCode,
            roleName);
    }
}
