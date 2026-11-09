using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Errors;
using AqtCoreFW.Workflow.Application.DTOs;
using AqtCoreFW.Workflow.Domain.Enums;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Queries.GetWorkflowDefinition;

public sealed class GetWorkflowDefinitionQueryHandler(IReadDbConnection db)
    : IRequestHandler<GetWorkflowDefinitionQuery, Result<WorkflowDefinitionDto>>
{
    public async Task<Result<WorkflowDefinitionDto>> Handle(
        GetWorkflowDefinitionQuery request,
        CancellationToken cancellationToken)
    {
        // Match tenant-specific first, else global template (TenantId=Guid.Empty) —
        // mirrors WorkflowDefinitionRepository.GetLatestByKeyAsync fallback semantics.
        // SELECT PublicId AS Id — surfaces Guid AK (T2) to DTO; InternalId (long bigint PK)
        // used for States/Transitions FK joins (DefinitionId is long).
        const string defSql = """
            SELECT Id AS InternalId, PublicId AS Id,
                   Name, Description, IsActive, TenantId, CreatedAt,
                   DefinitionKey, Version, IsLatest, EntityType
            FROM workflow.WorkflowDefinitions
            WHERE PublicId = @Id
              AND (TenantId = @TenantId OR TenantId = @GlobalTenantId)
              AND IsDeleted = 0
            """;

        const string statesSql = """
            SELECT Id, Name, DisplayNameVi, DisplayNameEn, IsInitial, IsFinal, Color, SortOrder,
                   SlaHours, AutoTransitionOnTimeoutId, PositionX, PositionY
            FROM workflow.WorkflowStates
            WHERE DefinitionId = @DefinitionId
            ORDER BY SortOrder
            """;

        const string transitionsSql = """
            SELECT Id, FromStateId, ToStateId, ActionName, ActionLabelVi, ActionLabelEn,
                   ConditionsJson, SortOrder, IsRecall,
                   SourceHandle, TargetHandle
            FROM workflow.WorkflowTransitions
            WHERE DefinitionId = @DefinitionId
            ORDER BY SortOrder
            """;

        var row = await db.QueryFirstOrDefaultAsync<DefinitionRow>(
            defSql,
            new { request.Id, request.TenantId, GlobalTenantId = Guid.Empty },
            cancellationToken);

        if (row is null)
            return Result.Fail(new NotFoundError($"WorkflowDefinition '{request.Id}' not found."));

        // Use internal long Id (bigint PK) for FK joins — States/Transitions.DefinitionId is bigint.
        var states = (await db.QueryAsync<WorkflowStateDto>(
            statesSql, new { DefinitionId = row.InternalId }, cancellationToken)).ToList();

        var transitions = (await db.QueryAsync<WorkflowTransitionDto>(
            transitionsSql, new { DefinitionId = row.InternalId }, cancellationToken)).ToList();

        // Re-project the persisted assignment rules onto each transition. The save path writes
        // WorkflowTransitionAssignments + Clauses, and the save RESPONSE projects them from the
        // in-memory spec — so a freshly-saved graph looks complete, but every subsequent GET
        // dropped them, making clauses vanish on reload. This read-back closes that gap. Scoped to
        // this definition transitively (definition→tenant, transition→definition, assignment→transition),
        // so no extra tenant predicate is needed.
        var transitionsWithAssignment = await AttachAssignmentsAsync(db, transitions, cancellationToken);

        // Parallel-branch read-back: the save path persists branches + children + resolutions, but
        // without this the GET would drop them and the visual designer would lose the fork on reload
        // (same gap the assignment read-back closes). Scoped transitively by DefinitionId.
        var branches = await AttachBranchesAsync(db, row.InternalId, cancellationToken);

        return Result.Ok(new WorkflowDefinitionDto(
            row.Id, row.Name, row.Description, row.IsActive, row.TenantId, row.CreatedAt,
            row.DefinitionKey, row.Version, row.IsLatest, row.EntityType,
            states, transitionsWithAssignment, branches));
    }

    // 2-query + in-memory join, mirroring WorkflowDefinitionRepository.GetOutgoingWithAssignmentsAsync
    // (the runtime resolver's read pattern) but over Dapper rather than EF.
    private const string AssignmentsSql = """
        SELECT Id, WorkflowTransitionId, CombineMode, AllowAssignorOverride
        FROM workflow.WorkflowTransitionAssignments
        WHERE WorkflowTransitionId IN @TransitionIds
        """;

    private const string ClausesSql = """
        SELECT AssignmentId, Mode, RefsJson
        FROM workflow.WorkflowTransitionAssignmentClauses
        WHERE AssignmentId IN @AssignmentIds
        ORDER BY Id
        """;

    /// <summary>
    /// Returns the transitions with their assignment rule (+clauses) attached. Returns the input
    /// list unchanged when no transition carries an assignment. Clause order is preserved by Id ASC
    /// so the designer renders clause rows in their original sequence.
    /// </summary>
    private static async Task<List<WorkflowTransitionDto>> AttachAssignmentsAsync(
        IReadDbConnection db,
        List<WorkflowTransitionDto> transitions,
        CancellationToken ct)
    {
        var transitionIds = transitions.Select(t => t.Id).ToList();
        if (transitionIds.Count == 0) return transitions;

        var assignments = (await db.QueryAsync<AssignmentRow>(
            AssignmentsSql, new { TransitionIds = transitionIds }, ct)).ToList();
        if (assignments.Count == 0) return transitions;

        var clausesByAssignment = new Dictionary<long, List<WorkflowTransitionAssignmentClauseDto>>();
        var clauseRows = await db.QueryAsync<ClauseRow>(
            ClausesSql, new { AssignmentIds = assignments.Select(a => a.Id).ToList() }, ct);
        foreach (var c in clauseRows)
        {
            if (!clausesByAssignment.TryGetValue(c.AssignmentId, out var bucket))
                clausesByAssignment[c.AssignmentId] = bucket = new List<WorkflowTransitionAssignmentClauseDto>();
            bucket.Add(new WorkflowTransitionAssignmentClauseDto(c.Mode, c.RefsJson));
        }

        var assignmentByTransitionId = new Dictionary<long, WorkflowTransitionAssignmentDto>(assignments.Count);
        foreach (var a in assignments)
        {
            var clauses = clausesByAssignment.TryGetValue(a.Id, out var list)
                ? list
                : new List<WorkflowTransitionAssignmentClauseDto>();
            assignmentByTransitionId[a.WorkflowTransitionId] =
                new WorkflowTransitionAssignmentDto(a.CombineMode, a.AllowAssignorOverride, clauses);
        }

        return transitions
            .Select(t => assignmentByTransitionId.TryGetValue(t.Id, out var a)
                ? t with { Assignment = a }
                : t)
            .ToList();
    }

    // 3-query + in-memory assemble for parallel branches, mirroring AttachAssignmentsAsync.
    private const string BranchesSql = """
        SELECT Id, BranchingStateId, ContinuationStateId, BranchType, RequiredCount,
               AllowCancelOnAnyReject, TimeoutMinutes, PositionX, PositionY
        FROM workflow.WorkflowParallelBranches
        WHERE DefinitionId = @DefinitionId
        ORDER BY Id
        """;

    private const string BranchChildrenSql = """
        SELECT Id, ParallelBranchId, TargetStateId, Name, DisplayNameVi, DepartmentId,
               DefaultAssigneeId, SortOrder
        FROM workflow.WorkflowBranchChildren
        WHERE ParallelBranchId IN @BranchIds
        ORDER BY SortOrder, Id
        """;

    private const string BranchResolutionsSql = """
        SELECT Id, BranchChildId, ResolutionType, ResultingStateId
        FROM workflow.WorkflowBranchResolutions
        WHERE BranchChildId IN @ChildIds
        ORDER BY Id
        """;

    private const string BranchChildAssignmentsSql = """
        SELECT Id, BranchChildId, CombineMode, AllowAssignorOverride
        FROM workflow.WorkflowBranchChildAssignments
        WHERE BranchChildId IN @ChildIds
        ORDER BY Id
        """;

    private const string BranchChildAssignmentClausesSql = """
        SELECT Id, AssignmentId, Mode, RefsJson
        FROM workflow.WorkflowBranchChildAssignmentClauses
        WHERE AssignmentId IN @AssignmentIds
        ORDER BY Id
        """;

    /// <summary>
    /// Returns the definition's parallel branches with children + resolutions assembled. Returns an
    /// empty list when none are configured. Children ordered by SortOrder so the designer renders them
    /// in author order. Guards empty IN-lists (Dapper rejects empty <c>IN ()</c>) like AttachAssignmentsAsync.
    /// </summary>
    private static async Task<List<WorkflowBranchDto>> AttachBranchesAsync(
        IReadDbConnection db,
        long definitionInternalId,
        CancellationToken ct)
    {
        var branchRows = (await db.QueryAsync<BranchRow>(
            BranchesSql, new { DefinitionId = definitionInternalId }, ct)).ToList();
        if (branchRows.Count == 0) return [];

        var childRows = (await db.QueryAsync<BranchChildRow>(
            BranchChildrenSql, new { BranchIds = branchRows.Select(b => b.Id).ToList() }, ct)).ToList();

        var resolutionsByChild = new Dictionary<long, List<WorkflowBranchResolutionDto>>();
        var assignmentByChild = new Dictionary<long, WorkflowTransitionAssignmentDto>();
        if (childRows.Count > 0)
        {
            var childIds = childRows.Select(c => c.Id).ToList();

            var resolutionRows = await db.QueryAsync<BranchResolutionRow>(
                BranchResolutionsSql, new { ChildIds = childIds }, ct);
            foreach (var r in resolutionRows)
            {
                if (!resolutionsByChild.TryGetValue(r.BranchChildId, out var bucket))
                    resolutionsByChild[r.BranchChildId] = bucket = [];
                bucket.Add(new WorkflowBranchResolutionDto(r.ResolutionType, r.ResultingStateId));
            }

            // Per-child assignment rule: assignments by child, then clauses by assignment. At most one
            // assignment per child (UX_WorkflowBranchChildAssignments_BranchChildId) — last-wins is moot.
            var assignmentRows = (await db.QueryAsync<BranchChildAssignmentRow>(
                BranchChildAssignmentsSql, new { ChildIds = childIds }, ct)).ToList();
            if (assignmentRows.Count > 0)
            {
                var clausesByAssignment = new Dictionary<long, List<WorkflowTransitionAssignmentClauseDto>>();
                var clauseRows = await db.QueryAsync<BranchChildAssignmentClauseRow>(
                    BranchChildAssignmentClausesSql,
                    new { AssignmentIds = assignmentRows.Select(a => a.Id).ToList() }, ct);
                foreach (var cl in clauseRows)
                {
                    if (!clausesByAssignment.TryGetValue(cl.AssignmentId, out var bucket))
                        clausesByAssignment[cl.AssignmentId] = bucket = [];
                    bucket.Add(new WorkflowTransitionAssignmentClauseDto(cl.Mode, cl.RefsJson));
                }

                foreach (var a in assignmentRows)
                {
                    var clauses = clausesByAssignment.TryGetValue(a.Id, out var list)
                        ? list
                        : new List<WorkflowTransitionAssignmentClauseDto>();
                    assignmentByChild[a.BranchChildId] = new WorkflowTransitionAssignmentDto(
                        a.CombineMode, a.AllowAssignorOverride, clauses);
                }
            }
        }

        var childrenByBranch = new Dictionary<long, List<WorkflowBranchChildDto>>();
        foreach (var c in childRows)
        {
            var resolutions = resolutionsByChild.TryGetValue(c.Id, out var list)
                ? list
                : new List<WorkflowBranchResolutionDto>();
            var assignment = assignmentByChild.TryGetValue(c.Id, out var a) ? a : null;
            if (!childrenByBranch.TryGetValue(c.ParallelBranchId, out var bucket))
                childrenByBranch[c.ParallelBranchId] = bucket = [];
            bucket.Add(new WorkflowBranchChildDto(
                c.Id, c.TargetStateId, c.Name, c.DisplayNameVi, c.DepartmentId,
                c.DefaultAssigneeId, c.SortOrder, resolutions, assignment));
        }

        return branchRows
            .Select(b => new WorkflowBranchDto(
                b.Id, b.BranchingStateId, b.ContinuationStateId, b.BranchType, b.RequiredCount,
                b.AllowCancelOnAnyReject, b.TimeoutMinutes, b.PositionX, b.PositionY,
                childrenByBranch.TryGetValue(b.Id, out var kids) ? kids : []))
            .ToList();
    }

    private sealed class BranchRow
    {
        public long Id { get; set; }
        public long BranchingStateId { get; set; }
        public long ContinuationStateId { get; set; }
        public ParallelBranchType BranchType { get; set; }
        public int? RequiredCount { get; set; }
        public bool AllowCancelOnAnyReject { get; set; }
        public int TimeoutMinutes { get; set; }
        public double? PositionX { get; set; }
        public double? PositionY { get; set; }
    }

    private sealed class BranchChildRow
    {
        public long Id { get; set; }
        public long ParallelBranchId { get; set; }
        public long TargetStateId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string DisplayNameVi { get; set; } = string.Empty;
        public string? DepartmentId { get; set; }
        public Guid? DefaultAssigneeId { get; set; }
        public int SortOrder { get; set; }
    }

    private sealed class BranchResolutionRow
    {
        public long Id { get; set; }
        public long BranchChildId { get; set; }
        public BranchResolutionType ResolutionType { get; set; }
        public long? ResultingStateId { get; set; }
    }

    private sealed class BranchChildAssignmentRow
    {
        public long Id { get; set; }
        public long BranchChildId { get; set; }
        public AssignmentCombineMode CombineMode { get; set; }
        public bool AllowAssignorOverride { get; set; }
    }

    private sealed class BranchChildAssignmentClauseRow
    {
        public long Id { get; set; }
        public long AssignmentId { get; set; }
        public AssignmentClauseMode Mode { get; set; }
        public string? RefsJson { get; set; }
    }

    // Dapper read rows for the assignment join. Mode/CombineMode are int columns mapped to enums.
    private sealed class AssignmentRow
    {
        public long Id { get; set; }
        public long WorkflowTransitionId { get; set; }
        public AssignmentCombineMode CombineMode { get; set; }
        public bool AllowAssignorOverride { get; set; }
    }

    private sealed class ClauseRow
    {
        public long AssignmentId { get; set; }
        public AssignmentClauseMode Mode { get; set; }
        public string? RefsJson { get; set; }
    }

    // T2 WorkflowDefinition: InternalId=long bigint PK (for States/Transitions FK joins), Id=Guid PublicId (AK).
    private sealed class DefinitionRow
    {
        public long InternalId { get; set; }
        public Guid Id { get; set; }            // PublicId — surfaced to DTO as Guid AK
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public Guid TenantId { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public string DefinitionKey { get; set; } = string.Empty;
        public int Version { get; set; }
        public bool IsLatest { get; set; }
        public string EntityType { get; set; } = string.Empty;
    }
}
