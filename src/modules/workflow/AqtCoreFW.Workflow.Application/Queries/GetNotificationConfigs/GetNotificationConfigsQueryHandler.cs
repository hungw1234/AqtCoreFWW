using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.Workflow.Application.DTOs;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Queries.GetNotificationConfigs;

/// <summary>
/// Dapper read for notification configs per definition.
/// Excludes soft-deleted rows; returns all active/inactive configs (caller filters if needed).
/// </summary>
public sealed class GetNotificationConfigsQueryHandler(IReadDbConnection db)
    : IRequestHandler<GetNotificationConfigsQuery, Result<IReadOnlyList<WorkflowNotificationConfigDto>>>
{
    public async Task<Result<IReadOnlyList<WorkflowNotificationConfigDto>>> Handle(
        GetNotificationConfigsQuery request,
        CancellationToken cancellationToken)
    {
        // T2 WorkflowDefinition: filter by PublicId (Guid AK) via JOIN.
        // wnc.DefinitionId is a bigint FK — cannot compare directly against Guid request param.
        const string sql = """
            SELECT wnc.Id, wnc.DefinitionId, wnc.ActionName, wnc.Channel,
                   wnc.RecipientType, wnc.RecipientValue, wnc.Subject, wnc.BodyTemplate, wnc.IsActive
            FROM workflow.WorkflowNotificationConfigs wnc
            INNER JOIN workflow.WorkflowDefinitions wd ON wd.Id = wnc.DefinitionId
            WHERE wd.PublicId = @DefinitionId
              AND wnc.TenantId = @TenantId
              AND wnc.IsDeleted = 0
            ORDER BY wnc.ActionName, wnc.Channel
            """;

        var rows = await db.QueryAsync<ConfigRow>(
            sql,
            new { request.DefinitionId, request.TenantId },
            cancellationToken);

        var result = rows
            .Select(r => new WorkflowNotificationConfigDto(
                r.Id, r.DefinitionId, r.ActionName, r.Channel,
                r.RecipientType, r.RecipientValue,
                r.Subject, r.BodyTemplate, r.IsActive))
            .ToList();

        return Result.Ok<IReadOnlyList<WorkflowNotificationConfigDto>>(result);
    }

    // WorkflowNotificationConfig is T1: Id (long), DefinitionId (long FK to T2 internal PK)
    internal sealed class ConfigRow
    {
        public long Id { get; set; }
        public long DefinitionId { get; set; }
        public string ActionName { get; set; } = string.Empty;
        public string Channel { get; set; } = string.Empty;
        public string RecipientType { get; set; } = string.Empty;
        public string RecipientValue { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string BodyTemplate { get; set; } = string.Empty;
        public bool IsActive { get; set; }
    }
}
