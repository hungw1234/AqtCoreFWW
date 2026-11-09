using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.Workflow.Application.DTOs;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Queries.GetVersionHistory;

/// <summary>
/// Dapper query: aggregate WorkflowDefinitions by DefinitionKey, LEFT JOIN
/// WorkflowInstances to count active (non-completed) instances per version.
/// Tenant-scoped. Returns ordered by version descending (newest first).
/// </summary>
public sealed class GetVersionHistoryQueryHandler(IReadDbConnection db)
    : IRequestHandler<GetVersionHistoryQuery, Result<IReadOnlyList<VersionHistoryDto>>>
{
    public async Task<Result<IReadOnlyList<VersionHistoryDto>>> Handle(
        GetVersionHistoryQuery request,
        CancellationToken cancellationToken)
    {
        // T2 contract: expose PublicId as external Guid Id (internal wd.Id is bigint long).
        // Join uses internal wd.Id (bigint FK from WorkflowInstances.DefinitionId).
        const string sql = """
            SELECT
                wd.PublicId AS Id,
                wd.DefinitionKey,
                wd.Version,
                wd.IsLatest,
                wd.IsActive,
                wd.CreatedAt,
                COUNT(wi.Id) AS InstanceCount
            FROM workflow.WorkflowDefinitions wd
            LEFT JOIN workflow.WorkflowInstances wi
                ON wi.DefinitionId = wd.Id
               AND wi.CompletedAt IS NULL
               AND wi.IsDeleted = 0
            WHERE wd.DefinitionKey = @DefinitionKey
              AND wd.TenantId = @TenantId
              AND wd.IsDeleted = 0
            GROUP BY wd.PublicId, wd.DefinitionKey, wd.Version, wd.IsLatest, wd.IsActive, wd.CreatedAt
            ORDER BY wd.Version DESC
            """;

        var rows = await db.QueryAsync<VersionHistoryDto>(
            sql,
            new { request.DefinitionKey, request.TenantId },
            cancellationToken);

        return Result.Ok<IReadOnlyList<VersionHistoryDto>>(rows.ToList());
    }
}
