using AqtCoreFW.Forms.Application.DTOs;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Application.Pagination;
using Dapper;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Forms.Application.Queries.ListPendingWorkflowLinks;

/// <summary>
/// Dapper read-side handler (mirrors <c>ListFormSubmissionsQueryHandler</c>) — admin UI query
/// handlers bypass the EF repository per
/// <c>IFormSubmissionWorkflowLinkPendingAttemptRepository</c>'s doc-comment mandate.
/// </summary>
public sealed class ListPendingWorkflowLinksQueryHandler(IReadDbConnection db)
    : IRequestHandler<ListPendingWorkflowLinksQuery, Result<PagedResult<PendingWorkflowLinkListItemDto>>>
{
    public async Task<Result<PagedResult<PendingWorkflowLinkListItemDto>>> Handle(
        ListPendingWorkflowLinksQuery request,
        CancellationToken cancellationToken)
    {
        var (page, pageSize) = PaginationDefaults.Normalize(request.Page, request.PageSize, PaginationDefaults.AdminMax);

        var where = new List<string> { "a.TenantId = @TenantId" };
        var p = new DynamicParameters();
        p.Add("TenantId", request.TenantId);

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            where.Add("a.Status = @Status");
            p.Add("Status", request.Status);
        }

        var whereClause = string.Join(" AND ", where);
        p.Add("Offset", (page - 1) * pageSize);
        p.Add("PageSize", pageSize);

        // JOIN FormTemplates to project t.PublicId AS FormTemplateId (Guid T2 AK) — mirrors
        // ListFormSubmissionsQueryHandler's Phase-07 O-2 retiering so the FE never sees a's internal
        // long FormTemplateId FK.
        const string fromJoin = """
            FROM forms.FormSubmissionWorkflowLinkPendingAttempts a
            INNER JOIN forms.FormTemplates t ON t.Id = a.FormTemplateId
            """;
        var countSql = $"SELECT COUNT(*) {fromJoin} WHERE {whereClause}";
        var dataSql = $"""
            SELECT a.SubmissionId AS Id, t.PublicId AS FormTemplateId,
                   COALESCE(NULLIF(t.NameVi, ''), t.Name) AS TemplateName,
                   a.ApprovalWorkflowDefinitionId,
                   a.AttemptCount, a.LastFailureCode, a.LastError, a.NextRetryAt, a.Status,
                   a.ResolvedAt, a.ResolvedBy, a.CreatedAt
            {fromJoin}
            WHERE {whereClause}
            ORDER BY a.CreatedAt DESC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
            """;

        var total = await db.QuerySingleAsync<int>(countSql, p, cancellationToken);
        var items = (await db.QueryAsync<PendingWorkflowLinkListItemDto>(dataSql, p, cancellationToken)).ToList();

        var totalPages = (int)Math.Ceiling(total / (double)pageSize);
        var meta = new PaginationMeta(
            page, pageSize, totalPages,
            NextCursor: null, PrevCursor: null,
            HasNextPage: page < totalPages);

        return Result.Ok(new PagedResult<PendingWorkflowLinkListItemDto>(items, total, meta));
    }
}
