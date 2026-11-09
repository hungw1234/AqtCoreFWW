using AqtCoreFW.SharedKernel.Domain.Repositories;
using AqtCoreFW.SharedKernel.Errors;
using AqtCoreFW.Workflow.Domain.Entities;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Commands.DeleteNotificationConfig;

public sealed class DeleteNotificationConfigCommandHandler(
    IRepository<WorkflowNotificationConfig, long> repository)
    : IRequestHandler<DeleteNotificationConfigCommand, Result>
{
    public async Task<Result> Handle(
        DeleteNotificationConfigCommand request,
        CancellationToken cancellationToken)
    {
        var result = await repository.GetByIdAsync(request.ConfigId, cancellationToken);
        if (result.IsFailed)
            return Result.Fail(new NotFoundError($"NotificationConfig '{request.ConfigId}' not found."));

        // Tenant isolation — ensure config belongs to the requesting tenant
        if (result.Value.TenantId != request.TenantId)
            return Result.Fail(new NotFoundError($"NotificationConfig '{request.ConfigId}' not found."));

        await repository.DeleteAsync(request.ConfigId, cancellationToken);
        return Result.Ok();
    }
}
