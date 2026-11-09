using FluentResults;
using MediatR;
using AqtCoreFW.SharedKernel.Errors;
using AqtCoreFW.Notifications.Domain.Repositories;

namespace AqtCoreFW.Notifications.Application.Commands.MarkNotificationRead;

public sealed class MarkNotificationReadCommandHandler(INotificationRepository repository)
    : IRequestHandler<MarkNotificationReadCommand, Result>
{
    public async Task<Result> Handle(MarkNotificationReadCommand request, CancellationToken cancellationToken)
    {
        // T2: external addressing via PublicId (Guid AK)
        var notification = await repository.GetByPublicIdAsync(request.NotificationId, cancellationToken);
        if (notification is null)
            return Result.Fail(new NotFoundError($"Notification {request.NotificationId} not found."));

        if (notification.RecipientUserId != request.UserId)
            return Result.Fail(new ForbiddenError("Cannot mark another user's notification as read."));

        notification.MarkAsRead();
        await repository.UpdateAsync(notification, cancellationToken);
        return Result.Ok();
    }
}
