using AqtCoreFW.Forms.Application.DTOs;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Application.Pagination;
using Dapper;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Forms.Application.Queries.GetFormTemplates;

public sealed class GetFormTemplatesQueryHandler(IReadDbConnection db)
    : IRequestHandler<GetFormTemplatesQuery, Result<PagedResult<FormTemplateDto>>>
{
    public async Task<Result<PagedResult<FormTemplateDto>>> Handle(
        GetFormTemplatesQuery request,
        CancellationToken cancellationToken)
    {
        // P7-FU1.1-FU defense-in-depth + ARCH-FU3 canonical helper.
        // PickerMax tier: FormTemplate picker (FE shared client useFormTemplatePicker) requests
        // pageSize=500; 1000-cap = 2x headroom.
        var (page, pageSize) = PaginationDefaults.Normalize(
            request.Page, request.PageSize, PaginationDefaults.PickerMax);

        var clauses = new List<string>
        {
            "TenantId = @TenantId",
            "Status <> 'Inactive'",
            "IsDeleted = 0"
        };
        var p = new DynamicParameters();
        p.Add("TenantId", request.TenantId);

        // Optional full-text substring search across Name, NameVi, and Code columns
        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            var escaped = request.SearchTerm.EscapeSqlLike();
            p.Add("SearchPattern", $"%{escaped}%");
            clauses.Add("(Name LIKE @SearchPattern ESCAPE '\\' OR NameVi LIKE @SearchPattern ESCAPE '\\' OR Code LIKE @SearchPattern ESCAPE '\\')");
        }

        var whereClause = string.Join(" AND ", clauses);
        p.Add("Offset", (page - 1) * pageSize);
        p.Add("PageSize", pageSize);

        // List view: excludes Inactive templates, no fields loaded (use GetFormTemplate for full schema)
        var countSql = $"SELECT COUNT(*) FROM forms.FormTemplates WHERE {whereClause}";

        // SELECT PublicId as the external Id projected into FormTemplateDto
        var dataSql = $"""
            SELECT PublicId, TenantId, Name, NameVi, Code, StorageMode, Status, Version, CreatedAt
            FROM forms.FormTemplates
            WHERE {whereClause}
            ORDER BY CreatedAt DESC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
            """;

        var total = await db.QuerySingleAsync<int>(countSql, p, cancellationToken);
        var rows = (await db.QueryAsync<FormTemplateRow>(dataSql, p, cancellationToken)).ToList();

        var items = rows
            .Select(r => new FormTemplateDto(
                r.PublicId, r.TenantId, r.Name, r.NameVi,
                r.Code, r.StorageMode, r.Status,
                r.Version, r.CreatedAt, []))
            .ToList();

        var totalPages = (int)Math.Ceiling(total / (double)pageSize);
        var meta = new PaginationMeta(
            page, pageSize, totalPages,
            NextCursor: null, PrevCursor: null,
            HasNextPage: page < totalPages);

        return Result.Ok(new PagedResult<FormTemplateDto>(items, total, meta));
    }

    // Use class (not positional record) — Dapper requires parameterless ctor for nullable fields
    private sealed class FormTemplateRow
    {
        public Guid PublicId { get; set; }   // external AK projected into DTO.Id
        public Guid TenantId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string NameVi { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string StorageMode { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public int Version { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
    }
}
