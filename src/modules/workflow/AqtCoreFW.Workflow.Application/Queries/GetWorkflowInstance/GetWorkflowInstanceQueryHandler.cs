using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.SharedKernel.Errors;
using AqtCoreFW.Workflow.Application.DTOs;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Queries.GetWorkflowInstance;

public sealed class GetWorkflowInstanceQueryHandler(
    IReadDbConnection db,
    IIdentityModuleClient identityClient)
    : IRequestHandler<GetWorkflowInstanceQuery, Result<WorkflowInstanceDto>>
{
    public async Task<Result<WorkflowInstanceDto>> Handle(
        GetWorkflowInstanceQuery request,
        CancellationToken cancellationToken)
    {
        // Query by PublicId (Guid AK — T2 entity). Returns internal long PKs for FK columns.
        // SELECT PublicId AS Id — surfaces Guid AK to DTO; internal long Id used for history join.
        // DefinitionId: long FK to T2 WorkflowDefinition.Id internal bigint PK.
        // CurrentStateId: long FK to T1 WorkflowState.Id bigint PK.
        const string instanceSql = """
            SELECT Id AS InternalId, PublicId AS Id,
                   DefinitionId, EntityType, EntityId, CurrentStateId,
                   TenantId, StartedAt, CompletedAt, DueAt, CurrentStateEnteredAt
            FROM workflow.WorkflowInstances
            WHERE PublicId = @Id AND TenantId = @TenantId AND IsDeleted = 0
            """;

        // WorkflowInstanceHistory is T3: MessageId is public Guid AK; InternalId is long PK.
        // InstanceId: long FK to WorkflowInstance.Id (internal bigint PK).
        // FromStateId/ToStateId: long FK to T1 WorkflowState.Id.
        const string historySql = """
            SELECT MessageId AS Id, FromStateId, ToStateId, ActionName,
                   TransitionedBy, Comment, TransitionedAt
            FROM workflow.WorkflowInstanceHistories
            WHERE InstanceId = @InstanceId
            ORDER BY TransitionedAt
            """;

        var row = await db.QueryFirstOrDefaultAsync<InstanceRow>(
            instanceSql, new { request.Id, request.TenantId }, cancellationToken);

        if (row is null)
            return Result.Fail(new NotFoundError($"WorkflowInstance '{request.Id}' not found."));

        var history = (await db.QueryAsync<WorkflowInstanceHistoryDto>(
            historySql, new { InstanceId = row.InternalId }, cancellationToken)).ToList();

        if (history.Count > 0)
            history = await EnrichHistoryAsync(history, row.DefinitionId, request.TenantId, cancellationToken);

        return Result.Ok(new WorkflowInstanceDto(
            row.Id, row.DefinitionId, row.EntityType, row.EntityId,
            row.CurrentStateId, row.TenantId, row.StartedAt, row.CompletedAt,
            row.DueAt, row.CurrentStateEnteredAt, history));
    }

    // Enrich raw IDs with display names — state names from local workflow schema,
    // actor names via cross-module identity client (same pattern as GetOrgUnitMembers).
    // definitionId: long (internal FK to T2 WorkflowDefinition.Id — states/transitions keyed by this).
    private async Task<List<WorkflowInstanceHistoryDto>> EnrichHistoryAsync(
        List<WorkflowInstanceHistoryDto> history,
        long definitionId,
        Guid tenantId,
        CancellationToken ct)
    {
        const string statesSql = """
            SELECT Id, COALESCE(DisplayNameVi, Name) AS DisplayName
            FROM workflow.WorkflowStates
            WHERE DefinitionId = @DefinitionId
            """;

        const string transitionsSql = """
            SELECT FromStateId, ToStateId, ActionName,
                   COALESCE(NULLIF(ActionLabelVi, ''), ActionName) AS ActionLabel
            FROM workflow.WorkflowTransitions
            WHERE DefinitionId = @DefinitionId
            """;

        var stateRows = await db.QueryAsync<StateNameRow>(
            statesSql, new { DefinitionId = definitionId }, ct);
        var stateMap = stateRows.ToDictionary(s => s.Id, s => s.DisplayName);

        var transitionRows = await db.QueryAsync<TransitionLabelRow>(
            transitionsSql, new { DefinitionId = definitionId }, ct);
        // Composite key (FromStateId, ToStateId, ActionName) uniquely identifies a transition
        var transitionMap = transitionRows.ToDictionary(
            t => (t.FromStateId, t.ToStateId, t.ActionName),
            t => t.ActionLabel);

        var actorIds = history.Select(h => h.TransitionedBy).Distinct().ToList();
        var userMap = await identityClient.GetUserInfoByIdsAsync(actorIds, tenantId, ct);

        return history.Select(h => h with
        {
            FromStateName = stateMap.GetValueOrDefault(h.FromStateId),
            ToStateName = stateMap.GetValueOrDefault(h.ToStateId),
            ActorName = userMap.TryGetValue(h.TransitionedBy, out var user) ? user.FullName : null,
            ActionLabel = transitionMap.GetValueOrDefault((h.FromStateId, h.ToStateId, h.ActionName)),
        }).ToList();
    }

    // T1 WorkflowState: long Id PK (no Guid AK).
    private sealed class StateNameRow
    {
        public long Id { get; set; }
        public string DisplayName { get; set; } = string.Empty;
    }

    // T1 WorkflowTransition: FromStateId/ToStateId are long FK to T1 WorkflowState.Id.
    private sealed class TransitionLabelRow
    {
        public long FromStateId { get; set; }
        public long ToStateId { get; set; }
        public string ActionName { get; set; } = string.Empty;
        public string ActionLabel { get; set; } = string.Empty;
    }

    // T2 WorkflowInstance: InternalId=long bigint PK (for history JOIN), Id=Guid PublicId (AK).
    // DefinitionId: long FK to T2 WorkflowDefinition.Id internal PK.
    // CurrentStateId: long FK to T1 WorkflowState.Id.
    private sealed class InstanceRow
    {
        public long InternalId { get; set; }   // internal bigint PK — used for history InstanceId JOIN
        public Guid Id { get; set; }           // PublicId — surfaced to DTO as Guid AK
        public long DefinitionId { get; set; }
        public string EntityType { get; set; } = string.Empty;
        public Guid EntityId { get; set; }
        public long CurrentStateId { get; set; }
        public Guid TenantId { get; set; }
        public DateTimeOffset StartedAt { get; set; }
        public DateTimeOffset? CompletedAt { get; set; }
        public DateTimeOffset? DueAt { get; set; }
        public DateTimeOffset CurrentStateEnteredAt { get; set; }
    }
}
