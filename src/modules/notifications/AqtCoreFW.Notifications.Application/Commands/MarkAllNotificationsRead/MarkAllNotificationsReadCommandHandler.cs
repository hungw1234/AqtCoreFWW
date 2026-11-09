using FluentResults;
using MediatR;
using AqtCoreFW.Notifications.Domain.Repositories;

namespace AqtCoreFW.Notifications.Application.Commands.MarkAllNotificationsRead;

public sealed class MarkAllNotificationsReadCommandHandler(INotificationRepository repository)
    : IRequestHandler<MarkAllNotificationsReadCommand, Result>
{
    public async Task<Result> Handle(MarkAllNotificationsReadCommand request, CancellationToken cancellationToken)
    {
        var unread = await repository.GetUnreadByUserAsync(request.UserId, request.TenantId, cancellationToken);
        if (unread.Count == 0)
            return Result.Ok();

        foreach (var notification in unread)
            notification.MarkAsRead();

        // Single SaveChanges for the whole batch (was N round-trips — one per notification).
        await repository.UpdateRangeAsync(unread, cancellationToken);
        return Result.Ok();
    }
}
