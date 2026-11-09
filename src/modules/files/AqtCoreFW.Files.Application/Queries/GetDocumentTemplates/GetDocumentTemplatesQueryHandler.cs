using AqtCoreFW.Files.Application.DTOs;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Application.Pagination;
using Dapper;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Files.Application.Queries.GetDocumentTemplates;

public sealed class GetDocumentTemplatesQueryHandler(IReadDbConnection db)
    : IRequestHandler<GetDocumentTemplatesQuery, Result<PagedResult<DocumentTemplateDto>>>
{
    public async Task<Result<PagedResult<DocumentTemplateDto>>> Handle(
        GetDocumentTemplatesQuery request,
        CancellationToken cancellationToken)
    {
        var clauses = new List<string>
        {
            "TenantId = @TenantId",
            "IsDeleted = 0"
        };
        var p = new DynamicParameters();
        p.Add("TenantId", request.TenantId);

        if (request.Status.HasValue)
        {
            clauses.Add("Status = @Status");
            p.Add("Status", request.Status.Value);
        }

        // Optional substring search across Name and Description
        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            var escaped = request.SearchTerm.EscapeSqlLike();
            p.Add("SearchPattern", $"%{escaped}%");
            clauses.Add("(Name LIKE @SearchPattern ESCAPE '\\' OR Description LIKE @SearchPattern ESCAPE '\\')");
        }

        var whereClause = string.Join(" AND ", clauses);
        var (page, pageSize) = PaginationDefaults.Normalize(request.Page, request.PageSize, PaginationDefaults.AdminMax);
        var offset = (page - 1) * pageSize;
        p.Add("Offset", offset);
        p.Add("PageSize", pageSize);

        var countSql = $"SELECT COUNT(*) FROM files.DocumentTemplates WHERE {whereClause}";
        // Phase-4b B3b: T1 entity — Id is bigint; Dapper casts to long directly (no PublicId projection needed).
        // OutputFormat/Status are int columns → Dapper materializes the enum members directly.
        // CreatedAt is datetime2 → DateTimeOffset via the global DapperTypeHandlers handler.
        var dataSql = $"""
            SELECT Id, Name, Code, Description, OutputFormat, TemplateContent,
                   Status, TenantId, CreatedAt, CreatedBy
            FROM files.DocumentTemplates
            WHERE {whereClause}
            ORDER BY Name
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
            """;

        var total = await db.QuerySingleAsync<int>(countSql, p, cancellationToken);
        var rows = await db.QueryAsync<DocumentTemplateDto>(dataSql, p, cancellationToken);

        var totalPages = (int)Math.Ceiling(total / (double)pageSize);
        var meta = new PaginationMeta(page, pageSize, totalPages, null, null, page < totalPages);

        return Result.Ok(new PagedResult<DocumentTemplateDto>(rows.ToList(), total, meta));
    }
}
