using FluentResults;
using MediatR;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Errors;
using AqtCoreFW.Notifications.Domain.Repositories;

namespace AqtCoreFW.Notifications.Application.Commands.DeleteNotificationTemplate;

public sealed class DeleteNotificationTemplateCommandHandler(
    INotificationTemplateRepository repository,
    ICurrentUser currentUser) : IRequestHandler<DeleteNotificationTemplateCommand, Result>
{
    public async Task<Result> Handle(
        DeleteNotificationTemplateCommand request,
        CancellationToken cancellationToken)
    {
        // T2: look up by Guid PublicId with cross-tenant guard via currentUser.TenantId
        var template = await repository.GetByPublicIdAsync(request.Id, currentUser.TenantId ?? Guid.Empty, cancellationToken);
        if (template is null)
            return Result.Fail(new NotFoundError($"Notification template {request.Id} not found."));

        // Soft-delete via internal Id — AuditableEntity.Delete() is called by repo
        await repository.DeleteAsync(template.Id, cancellationToken);
        return Result.Ok();
    }
}
