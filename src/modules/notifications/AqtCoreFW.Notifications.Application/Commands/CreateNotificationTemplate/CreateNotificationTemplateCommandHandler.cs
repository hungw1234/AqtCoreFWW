using FluentResults;
using MediatR;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.Notifications.Domain.Entities;
using AqtCoreFW.Notifications.Domain.Repositories;
using AqtCoreFW.Notifications.Domain.ValueObjects;

namespace AqtCoreFW.Notifications.Application.Commands.CreateNotificationTemplate;

public sealed class CreateNotificationTemplateCommandHandler(
    IGuidGenerator guidGen,
    INotificationTemplateRepository repository,
    ICurrentUser currentUser) : IRequestHandler<CreateNotificationTemplateCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(
        CreateNotificationTemplateCommand request,
        CancellationToken cancellationToken)
    {
        var channel = NotificationChannel.From(request.Channel);

        // Check key uniqueness per tenant+channel
        var existing = await repository.FindByKeyAsync(
            request.TemplateKey, request.Channel, request.TenantId, cancellationToken);
        if (existing is not null)
            return Result.Fail($"Template key '{request.TemplateKey}' already exists for channel '{request.Channel}'.");

        var template = NotificationTemplate.Create(
            guidGen,
            request.TenantId,
            request.TemplateKey,
            request.SubjectTemplate,
            request.BodyTemplate,
            channel,
            currentUser.UserId);

        await repository.AddAsync(template, cancellationToken);

        // T2: return PublicId (Guid AK) — not internal long Id
        return Result.Ok(template.PublicId);
    }
}
