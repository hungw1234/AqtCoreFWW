using FluentResults;
using MediatR;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Application.Pagination;
using AqtCoreFW.Notifications.Application.DTOs;

namespace AqtCoreFW.Notifications.Application.Queries.GetUserNotifications;

public sealed class GetUserNotificationsQueryHandler(IReadDbConnection db)
    : IRequestHandler<GetUserNotificationsQuery, Result<PagedResult<NotificationDto>>>
{
    public async Task<Result<PagedResult<NotificationDto>>> Handle(
        GetUserNotificationsQuery request, CancellationToken cancellationToken)
    {
        var (page, pageSize) = PaginationDefaults.Normalize(request.Page, request.PageSize, PaginationDefaults.AdminMax);
        var offset = (page - 1) * pageSize;

        // Build dynamic filter conditions
        var channelFilter = request.Channel is not null ? "AND Channel = @Channel" : "";
        var readFilter = request.IsRead.HasValue ? "AND IsRead = @IsRead" : "";

        var countSql = $"""
            SELECT COUNT(*)
            FROM notifications.Notifications
            WHERE RecipientUserId = @UserId AND TenantId = @TenantId AND IsDeleted = 0
            {channelFilter} {readFilter}
            """;

        // Order: unread first (IsRead=0 < IsRead=1 with ASC), then newest first.
        // Ensures top-N on first page surfaces unread items even when many read items exist.
        // T2: project PublicId AS Id — DTO.Id exposes external Guid AK
        var dataSql = $"""
            SELECT PublicId AS Id, TenantId, RecipientUserId, Subject, Body, Channel,
                   IsRead, ReadAt, CreatedAt
            FROM notifications.Notifications
            WHERE RecipientUserId = @UserId AND TenantId = @TenantId AND IsDeleted = 0
            {channelFilter} {readFilter}
            ORDER BY IsRead ASC, CreatedAt DESC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
            """;

        var param = new
        {
            request.UserId,
            request.TenantId,
            request.Channel,
            request.IsRead,
            Offset = offset,
            PageSize = pageSize
        };

        var total = await db.QueryFirstOrDefaultAsync<int>(countSql, param, cancellationToken);
        var data = await db.QueryAsync<NotificationDto>(dataSql, param, cancellationToken);

        var totalPages = (int)Math.Ceiling(total / (double)pageSize);
        var meta = new PaginationMeta(page, pageSize, totalPages, null, null, page < totalPages);

        return Result.Ok(new PagedResult<NotificationDto>(data.ToList(), total, meta));
    }
}
