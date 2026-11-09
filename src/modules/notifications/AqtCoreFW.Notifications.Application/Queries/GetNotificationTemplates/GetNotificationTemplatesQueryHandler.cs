using FluentResults;
using MediatR;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Application.Pagination;
using AqtCoreFW.Notifications.Application.DTOs;

namespace AqtCoreFW.Notifications.Application.Queries.GetNotificationTemplates;

public sealed class GetNotificationTemplatesQueryHandler(IReadDbConnection db)
    : IRequestHandler<GetNotificationTemplatesQuery, Result<PagedResult<NotificationTemplateDto>>>
{
    public async Task<Result<PagedResult<NotificationTemplateDto>>> Handle(
        GetNotificationTemplatesQuery request, CancellationToken cancellationToken)
    {
        var (page, pageSize) = PaginationDefaults.Normalize(request.Page, request.PageSize, PaginationDefaults.AdminMax);
        var offset = (page - 1) * pageSize;
        var channelFilter = request.Channel is not null ? "AND Channel = @Channel" : "";

        var countSql = $"""
            SELECT COUNT(*)
            FROM [notifications].[NotificationTemplates]
            WHERE TenantId = @TenantId AND IsDeleted = 0
            {channelFilter}
            """;

        // T2: project PublicId as Id — NotificationTemplateDto.Id is Guid AK
        var dataSql = $"""
            SELECT PublicId AS Id, TenantId, TemplateKey, SubjectTemplate, BodyTemplate,
                   Channel, IsDefault, CreatedAt, UpdatedAt
            FROM notifications.NotificationTemplates
            WHERE TenantId = @TenantId AND IsDeleted = 0
            {channelFilter}
            ORDER BY TemplateKey ASC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
            """;

        var param = new { request.TenantId, request.Channel, Offset = offset, PageSize = pageSize };

        var total = await db.QueryFirstOrDefaultAsync<int>(countSql, param, cancellationToken);
        // CreatedAt/UpdatedAt are datetime2 columns → DateTimeOffset via the global DapperTypeHandlers
        // handler, so the DTO materializes directly (no intermediate Row record needed).
        var rows = await db.QueryAsync<NotificationTemplateDto>(dataSql, param, cancellationToken);

        var totalPages = (int)Math.Ceiling(total / (double)pageSize);
        var meta = new PaginationMeta(page, pageSize, totalPages, null, null, page < totalPages);

        return Result.Ok(new PagedResult<NotificationTemplateDto>(rows.ToList(), total, meta));
    }
}
