using AqtCoreFW.Forms.Application.DTOs;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Application.Pagination;
using Dapper;
using FluentResults;
using MediatR;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AqtCoreFW.Forms.Application.Queries.ListFormSubmissions;

public sealed class ListFormSubmissionsQueryHandler(IReadDbConnection db)
    : IRequestHandler<ListFormSubmissionsQuery, Result<PagedResult<FormSubmissionListItemDto>>>
{
    public async Task<Result<PagedResult<FormSubmissionListItemDto>>> Handle(
        ListFormSubmissionsQuery request,
        CancellationToken cancellationToken)
    {
        var (page, pageSize) = PaginationDefaults.Normalize(request.Page, request.PageSize, PaginationDefaults.AdminMax);

        // Phase-07 O-2: JOIN forms.FormTemplates to project t.PublicId as FormTemplateId (Guid T2 AK).
        // All WHERE clauses qualify s.* to avoid ambiguity after the JOIN.
        var where = new List<string> { "s.TenantId = @TenantId", "s.IsDeleted = 0" };
        var p = new DynamicParameters();
        p.Add("TenantId", request.TenantId);

        // Resolve optional TemplatePublicId (Guid AK) → internal long Id for SQL filter
        if (request.TemplatePublicId.HasValue)
        {
            const string resolveSql = """
                SELECT Id FROM forms.FormTemplates
                WHERE PublicId = @TemplatePublicId AND TenantId = @TenantId AND IsDeleted = 0
                """;
            var internalId = await db.QueryFirstOrDefaultAsync<long?>(
                resolveSql,
                new { request.TemplatePublicId, request.TenantId },
                cancellationToken);

            // If template not found, return empty result rather than unfiltered list
            if (internalId is null)
            {
                var emptyMeta = new PaginationMeta(page, pageSize, 0,
                    NextCursor: null, PrevCursor: null, HasNextPage: false);
                return Result.Ok(new PagedResult<FormSubmissionListItemDto>([], 0, emptyMeta));
            }

            where.Add("s.FormTemplateId = @TemplateId");
            p.Add("TemplateId", internalId.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            where.Add("s.Status = @Status");
            p.Add("Status", request.Status);
        }

        // Optional substring search on ReviewComment (only meaningful text column in list projection)
        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            var escaped = request.SearchTerm.EscapeSqlLike();
            p.Add("SearchPattern", $"%{escaped}%");
            where.Add("(s.ReviewComment LIKE @SearchPattern ESCAPE '\\')");
        }

        // Field-value filters — use JSON_VALUE for Json-mode SubmissionData
        if (!string.IsNullOrWhiteSpace(request.FieldFiltersJson))
        {
            try
            {
                var filters = JsonSerializer.Deserialize<List<SubmissionFieldFilter>>(
                    request.FieldFiltersJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (filters is { Count: > 0 })
                {
                    for (var i = 0; i < filters.Count; i++)
                    {
                        var filter = filters[i];
                        // Prevent SQL injection: validate FieldKey is a safe identifier
                        if (!Regex.IsMatch(filter.FieldKey, @"^[a-zA-Z_][a-zA-Z0-9_]{0,127}$"))
                            continue;
                        var paramName = $"FilterVal_{i}";
                        // JSON_VALUE extracts scalar value from SubmissionData JSON
                        where.Add($"JSON_VALUE(s.SubmissionData, '$.{filter.FieldKey}') = @{paramName}");
                        p.Add(paramName, filter.Value);
                    }
                }
            }
            catch (JsonException)
            {
                // Ignore malformed filter JSON — return unfiltered results
            }
        }

        var whereClause = string.Join(" AND ", where);
        p.Add("Offset", (page - 1) * pageSize);
        p.Add("PageSize", pageSize);

        // Phase-07 O-2: JOIN FormTemplates to project t.PublicId AS FormTemplateId (Guid T2 AK).
        // s.PublicId AS Id — external Guid addressing for FormSubmission.
        const string fromJoin = """
            FROM forms.FormSubmissions s
            INNER JOIN forms.FormTemplates t ON t.Id = s.FormTemplateId
            """;
        var countSql = $"SELECT COUNT(*) {fromJoin} WHERE {whereClause}";
        var dataSql = $"""
            SELECT s.PublicId AS Id, t.PublicId AS FormTemplateId, s.FormTemplateVersion,
                   s.SubmittedBy, s.SubmittedAt, s.Status,
                   s.ReviewedBy, s.ReviewedAt, s.ReviewComment
            {fromJoin}
            WHERE {whereClause}
            ORDER BY s.SubmittedAt DESC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
            """;

        var total = await db.QuerySingleAsync<int>(countSql, p, cancellationToken);
        var items = (await db.QueryAsync<FormSubmissionListItemDto>(dataSql, p, cancellationToken)).ToList();

        var totalPages = (int)Math.Ceiling(total / (double)pageSize);
        var meta = new PaginationMeta(
            page, pageSize, totalPages,
            NextCursor: null, PrevCursor: null,
            HasNextPage: page < totalPages);

        return Result.Ok(new PagedResult<FormSubmissionListItemDto>(items, total, meta));
    }
}
