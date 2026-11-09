using AqtCoreFW.Workflow.Application.Commands.CreateWorkflowDefinition;
using AqtCoreFW.Workflow.Application.DTOs;
using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Domain.Errors;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Commands.UpdateWorkflowDefinition;

public sealed class UpdateWorkflowDefinitionCommandHandler(
    IWorkflowDefinitionRepository repository)
    : IRequestHandler<UpdateWorkflowDefinitionCommand, Result<WorkflowDefinitionDto>>
{
    public async Task<Result<WorkflowDefinitionDto>> Handle(
        UpdateWorkflowDefinitionCommand request,
        CancellationToken cancellationToken)
    {
        var result = await repository.GetWithStatesAndTransitionsAsync(request.Id, cancellationToken);
        if (result.IsFailed)
            return Result.Fail(WorkflowErrors.DefinitionNotFound(request.Id));

        var definition = result.Value;

        definition.Update(request.Name, request.Description, request.ModifiedBy);

        await repository.UpdateAsync(definition, cancellationToken);

        return Result.Ok(CreateWorkflowDefinitionCommandHandler.MapToDto(definition));
    }
}
