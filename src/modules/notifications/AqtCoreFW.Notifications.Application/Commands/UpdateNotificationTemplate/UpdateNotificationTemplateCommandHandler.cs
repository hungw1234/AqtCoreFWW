using FluentResults;
using MediatR;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Errors;
using AqtCoreFW.Notifications.Domain.Repositories;

namespace AqtCoreFW.Notifications.Application.Commands.UpdateNotificationTemplate;

public sealed class UpdateNotificationTemplateCommandHandler(
    INotificationTemplateRepository repository,
    ICurrentUser currentUser) : IRequestHandler<UpdateNotificationTemplateCommand, Result>
{
    public async Task<Result> Handle(
        UpdateNotificationTemplateCommand request,
        CancellationToken cancellationToken)
    {
        // T2: look up by Guid PublicId with cross-tenant guard via currentUser.TenantId
        var template = await repository.GetByPublicIdAsync(request.Id, currentUser.TenantId ?? Guid.Empty, cancellationToken);
        if (template is null)
            return Result.Fail(new NotFoundError($"Notification template {request.Id} not found."));

        template.Update(request.SubjectTemplate, request.BodyTemplate, currentUser.UserId);
        await repository.UpdateAsync(template, cancellationToken);
        return Result.Ok();
    }
}
