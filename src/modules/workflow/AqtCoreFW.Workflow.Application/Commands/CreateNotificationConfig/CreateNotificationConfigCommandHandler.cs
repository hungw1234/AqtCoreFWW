using AqtCoreFW.SharedKernel.Domain.Repositories;
using AqtCoreFW.SharedKernel.Errors;
using AqtCoreFW.Workflow.Application.DTOs;
using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Domain.Entities;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Commands.CreateNotificationConfig;

/// <summary>
/// Creates a WorkflowNotificationConfig entity and persists via generic repository.
/// Returns a DTO representing the newly created config.
/// Phase-4b B6a: added IWorkflowDefinitionRepository to resolve T2 PublicId → internal long FK.
/// </summary>
public sealed class CreateNotificationConfigCommandHandler(
    IRepository<WorkflowNotificationConfig, long> repository,
    IWorkflowDefinitionRepository definitionRepository)
    : IRequestHandler<CreateNotificationConfigCommand, Result<WorkflowNotificationConfigDto>>
{
    public async Task<Result<WorkflowNotificationConfigDto>> Handle(
        CreateNotificationConfigCommand request,
        CancellationToken cancellationToken)
    {
        // Resolve T2 WorkflowDefinition PublicId (Guid) → internal long Id for FK storage
        var defResult = await definitionRepository.GetByIdAsync(
            request.DefinitionId, cancellationToken);
        if (defResult.IsFailed)
            return Result.Fail(new NotFoundError(
                $"WorkflowDefinition '{request.DefinitionId}' not found."));

        var config = WorkflowNotificationConfig.Create(
            defResult.Value.Id,
            request.ActionName,
            request.Channel,
            request.RecipientType,
            request.RecipientValue,
            request.Subject,
            request.BodyTemplate,
            request.TenantId,
            request.CreatedBy);

        await repository.AddAsync(config, cancellationToken);

        return Result.Ok(MapToDto(config));
    }

    internal static WorkflowNotificationConfigDto MapToDto(WorkflowNotificationConfig c) => new(
        c.Id, c.DefinitionId, c.ActionName, c.Channel,
        c.RecipientType, c.RecipientValue,
        c.Subject, c.BodyTemplate, c.IsActive);
}
