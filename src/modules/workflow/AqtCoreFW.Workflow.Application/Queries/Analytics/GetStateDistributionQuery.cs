using AqtCoreFW.SharedKernel.Application.Caching;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.Workflow.Application.DTOs;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Queries.Analytics;

/// <summary>Count of active workflow instances per state for a definition, with percentage share.</summary>
public sealed record GetStateDistributionQuery(
    Guid DefinitionId,
    Guid TenantId) : IRequest<Result<IReadOnlyList<StateDistributionDto>>>;

public sealed class GetStateDistributionQueryHandler(
    IReadDbConnection db,
    ICacheService cache)
    : IRequestHandler<GetStateDistributionQuery, Result<IReadOnlyList<StateDistributionDto>>>
{
    public async Task<Result<IReadOnlyList<StateDistributionDto>>> Handle(
        GetStateDistributionQuery request,
        CancellationToken cancellationToken)
    {
        var cacheKey = $"wf:analytics:{request.TenantId}:{request.DefinitionId}:state-dist";
        var cached = await cache.GetAsync<List<StateDistributionDto>>(cacheKey, cancellationToken);
        if (cached != null)
            return Result.Ok<IReadOnlyList<StateDistributionDto>>(cached);

        // Tenant scoped: only instances for this tenant + definition.
        // T2 WorkflowDefinition: DefinitionId request param is Guid (PublicId) — join wd to resolve bigint PK.
        const string sql = """
            SELECT
                wi.CurrentStateId                                                   AS StateId,
                COALESCE(ws.DisplayNameVi, ws.Name, '')                             AS StateName,
                COUNT(*)                                                            AS Count,
                CAST(
                    100.0 * COUNT(*) / SUM(COUNT(*)) OVER ()
                    AS DECIMAL(5,2))                                                AS Percentage
            FROM workflow.WorkflowInstances wi
            INNER JOIN workflow.WorkflowDefinitions wd ON wd.Id = wi.DefinitionId
            LEFT JOIN workflow.WorkflowStates ws
                ON ws.Id = wi.CurrentStateId
            WHERE wd.PublicId = @DefinitionId
              AND wi.TenantId = @TenantId
              AND wi.CompletedAt IS NULL
            GROUP BY wi.CurrentStateId, ws.DisplayNameVi, ws.Name
            ORDER BY Count DESC
            """;

        var rows = await db.QueryAsync<StateDistributionDto>(
            sql,
            new { request.DefinitionId, request.TenantId },
            cancellationToken);

        var result = rows.ToList();
        await cache.SetAsync(cacheKey, result, TimeSpan.FromMinutes(15), cancellationToken);
        return Result.Ok<IReadOnlyList<StateDistributionDto>>(result);
    }
}
