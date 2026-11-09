using AqtCoreFW.Forms.Application.DTOs;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Errors;
using Dapper;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Forms.Application.Queries.GetViewByEntityType;

public sealed class GetViewByEntityTypeQueryHandler(IReadDbConnection db)
    : IRequestHandler<GetViewByEntityTypeQuery, Result<ViewDefinitionDto>>
{
    public async Task<Result<ViewDefinitionDto>> Handle(
        GetViewByEntityTypeQuery request,
        CancellationToken cancellationToken)
    {
        // Prefer IsDefault=1; fallback to first created
        const string viewSql = """
            SELECT TOP 1 Id, TenantId, Name, EntityType, Type, DefaultSortField,
                   DefaultSortDir, PageSize, IsDefault, CreatedAt
            FROM forms.ViewDefinitions
            WHERE EntityType = @EntityType AND TenantId = @TenantId AND IsDeleted = 0
            ORDER BY IsDefault DESC, CreatedAt ASC
            """;

        var row = await db.QueryFirstOrDefaultAsync<ViewRow>(
            viewSql, new { request.EntityType, request.TenantId }, cancellationToken);

        if (row is null)
            return Result.Fail(new NotFoundError(
                $"Không tìm thấy view cho EntityType '{request.EntityType}'."));

        const string colSql = """
            SELECT Id, ViewDefinitionId, FieldName, Label, Width,
                   Sortable, Filterable, DisplayOrder, Formatter
            FROM forms.ViewColumns
            WHERE ViewDefinitionId = @Id AND IsDeleted = 0
            ORDER BY DisplayOrder
            """;

        const string filterSql = """
            SELECT Id, ViewDefinitionId, FieldName, Operator, DefaultValue, IsRequired
            FROM forms.ViewFilters
            WHERE ViewDefinitionId = @Id AND IsDeleted = 0
            """;

        var columns = (await db.QueryAsync<ViewColumnRow>(
            colSql, new { row.Id }, cancellationToken))
            .Select(c => new ViewColumnDto(c.Id, c.ViewDefinitionId, c.FieldName, c.Label,
                c.Width, c.Sortable, c.Filterable, c.DisplayOrder, c.Formatter))
            .ToList();

        var filters = (await db.QueryAsync<ViewFilterRow>(
            filterSql, new { row.Id }, cancellationToken))
            .Select(f => new ViewFilterDto(f.Id, f.ViewDefinitionId, f.FieldName,
                f.Operator, f.DefaultValue, f.IsRequired))
            .ToList();

        return Result.Ok(new ViewDefinitionDto(
            row.Id, row.TenantId, row.Name, row.EntityType, row.Type,
            row.DefaultSortField, row.DefaultSortDir, row.PageSize, row.IsDefault,
            row.CreatedAt, columns, filters));
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

    // Dapper requires parameterless ctor — positional records with nullable params fail materialization
    private sealed class ViewColumnRow
    {
        public long Id { get; set; }
        public long ViewDefinitionId { get; set; }
        public string FieldName { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public int? Width { get; set; }
        public bool Sortable { get; set; }
        public bool Filterable { get; set; }
        public int DisplayOrder { get; set; }
        public string Formatter { get; set; } = string.Empty;
    }

    // Dapper requires parameterless ctor — positional records with nullable params fail materialization
    private sealed class ViewFilterRow
    {
        public long Id { get; set; }
        public long ViewDefinitionId { get; set; }
        public string FieldName { get; set; } = string.Empty;
        public string Operator { get; set; } = string.Empty;
        public string? DefaultValue { get; set; }
        public bool IsRequired { get; set; }
    }
}
