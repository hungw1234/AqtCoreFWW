using FluentResults;
using MediatR;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.Notifications.Domain.Entities;
using AqtCoreFW.Notifications.Domain.Repositories;
using AqtCoreFW.Notifications.Domain.ValueObjects;

namespace AqtCoreFW.Notifications.Application.Commands.CreateNotification;

public sealed class CreateNotificationCommandHandler(
    IGuidGenerator guidGen,
    INotificationRepository repository,
    ICurrentUser currentUser) : IRequestHandler<CreateNotificationCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(
        CreateNotificationCommand request,
        CancellationToken cancellationToken)
    {
        var channel = NotificationChannel.From(request.Channel);

        var notification = Notification.Create(
            guidGen,
            request.TenantId,
            request.RecipientUserId,
            request.Subject,
            request.Body,
            channel,
            currentUser.UserId);

        await repository.AddAsync(notification, cancellationToken);

        // T2: return PublicId (Guid AK) — not internal long Id
        return Result.Ok(notification.PublicId);
    }
}
