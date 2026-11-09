using AqtCoreFW.Forms.Application.DTOs;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Application.Pagination;
using Dapper;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Forms.Application.Queries.ListViewDefinitions;

public sealed class ListViewDefinitionsQueryHandler(IReadDbConnection db)
    : IRequestHandler<ListViewDefinitionsQuery, Result<ListViewDefinitionsResult>>
{
    public async Task<Result<ListViewDefinitionsResult>> Handle(
        ListViewDefinitionsQuery request,
        CancellationToken cancellationToken)
    {
        const string countSql = """
            SELECT COUNT(*)
            FROM forms.ViewDefinitions
            WHERE TenantId = @TenantId AND IsDeleted = 0
            """;

        const string listSql = """
            SELECT Id, TenantId, Name, EntityType, Type, DefaultSortField,
                   DefaultSortDir, PageSize, IsDefault, CreatedAt
            FROM forms.ViewDefinitions
            WHERE TenantId = @TenantId AND IsDeleted = 0
            ORDER BY CreatedAt DESC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
            """;

        var (page, pageSize) = PaginationDefaults.Normalize(request.Page, request.PageSize, PaginationDefaults.AdminMax);
        var offset = (page - 1) * pageSize;
        var totalCount = await db.QueryFirstOrDefaultAsync<int>(
            countSql, new { request.TenantId }, cancellationToken);

        var rows = await db.QueryAsync<ViewRow>(
            listSql, new { request.TenantId, Offset = offset, PageSize = pageSize }, cancellationToken);

        var items = rows.Select(r => new ViewDefinitionDto(
            r.Id, r.TenantId, r.Name, r.EntityType, r.Type,
            r.DefaultSortField, r.DefaultSortDir, r.PageSize, r.IsDefault,
            r.CreatedAt,
            Columns: [],
            Filters: [])).ToList();

        return Result.Ok(new ListViewDefinitionsResult(items, totalCount, page, pageSize));
    }

    // Dapper requires parameterless ctor — positional records with nullable params fail materialization
    private sealed class ViewRow
    {
        public long Id { get; set; }
        public Guid TenantId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string EntityType { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string? DefaultSortField { get; set; }
        public string? DefaultSortDir { get; set; }
        public int PageSize { get; set; }
        public bool IsDefault { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
    }
}
