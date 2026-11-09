using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Application.Pagination;
using Dapper;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Queries.GetWorkflowDefinitions;

public sealed class GetWorkflowDefinitionsQueryHandler(IReadDbConnection db)
    : IRequestHandler<GetWorkflowDefinitionsQuery, Result<PagedResult<WorkflowDefinitionSummaryDto>>>
{
    public async Task<Result<PagedResult<WorkflowDefinitionSummaryDto>>> Handle(
        GetWorkflowDefinitionsQuery request,
        CancellationToken cancellationToken)
    {
        // P7-FU1.1-FU defense-in-depth + ARCH-FU3 canonical helper.
        // PickerMax tier: WorkflowDefinition picker (FE shared client useWorkflowDefinitionPicker)
        // requests pageSize=500; 1000-cap = 2x headroom.
        var (page, pageSize) = PaginationDefaults.Normalize(
            request.Page, request.PageSize, PaginationDefaults.PickerMax);

        var p = new DynamicParameters();
        p.Add("TenantId", request.TenantId);
        p.Add("GlobalTenantId", Guid.Empty);
        p.Add("IsActive", request.IsActive);
        p.Add("Offset", (page - 1) * pageSize);
        p.Add("PageSize", pageSize);

        // Include global templates (TenantId=Guid.Empty) alongside tenant-specific rows —
        // mirrors WorkflowDefinitionRepository.GetLatestByKeyAsync fallback semantics.
        var clauses = new List<string>
        {
            "(TenantId = @TenantId OR TenantId = @GlobalTenantId)",
            "IsDeleted = 0"
        };

        if (request.IsActive.HasValue)
            clauses.Add("IsActive = @IsActive");

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            var escaped = request.SearchTerm.EscapeSqlLike();
            p.Add("SearchPattern", $"%{escaped}%");
            clauses.Add("(Name LIKE @SearchPattern ESCAPE '\\' OR Description LIKE @SearchPattern ESCAPE '\\')");
        }

        var where = string.Join(" AND ", clauses);

        var countSql = $"SELECT COUNT(*) FROM workflow.WorkflowDefinitions WHERE {where}";

        // T2 WorkflowDefinition: project PublicId (Guid AK) as Id for DTO contract.
        // WorkflowDefinitionSummaryDto.Id is Guid — must not use bigint PK column directly.
        var dataSql = $"""
            SELECT PublicId AS Id, Name, Description, IsActive, CreatedAt,
                   DefinitionKey, Version, IsLatest, EntityType
            FROM workflow.WorkflowDefinitions
            WHERE {where}
            ORDER BY CreatedAt DESC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
            """;

        var total = await db.QuerySingleAsync<int>(countSql, p, cancellationToken);
        var data = (await db.QueryAsync<WorkflowDefinitionSummaryDto>(
            dataSql, p, cancellationToken)).ToList();

        var totalPages = (int)Math.Ceiling(total / (double)pageSize);
        var meta = new PaginationMeta(
            page, pageSize, totalPages,
            null, null, page < totalPages);

        return Result.Ok(new PagedResult<WorkflowDefinitionSummaryDto>(data, total, meta));
    }
}
