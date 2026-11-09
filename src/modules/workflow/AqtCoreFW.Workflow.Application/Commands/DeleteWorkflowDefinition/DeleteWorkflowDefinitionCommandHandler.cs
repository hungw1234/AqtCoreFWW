using System.Transactions;
using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Domain.Errors;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Commands.DeleteWorkflowDefinition;

public sealed class DeleteWorkflowDefinitionCommandHandler(
    IWorkflowDefinitionRepository repository)
    : IRequestHandler<DeleteWorkflowDefinitionCommand, Result>
{
    public async Task<Result> Handle(
        DeleteWorkflowDefinitionCommand request,
        CancellationToken cancellationToken)
    {
        var result = await repository.GetByIdAsync(request.Id, cancellationToken);
        if (result.IsFailed)
            return Result.Fail(WorkflowErrors.DefinitionNotFound(request.Id));

        var hasActive = await repository.HasActiveInstancesAsync(request.Id, cancellationToken);
        if (hasActive)
            return Result.Fail(WorkflowErrors.DefinitionHasInstances(request.Id));

        var target = result.Value;
        // Capture latest-flag + key before delete: deleting the current latest version must hand the flag
        // to the highest surviving version, or the key is left with zero latest rows (the seeded
        // demo-parallel-process hit exactly this — deleted v2 kept IsLatest, v1 stuck not-latest).
        var wasLatest = target.IsLatest;
        var definitionKey = target.DefinitionKey;
        var tenantId = target.TenantId;

        // Soft-delete and the IsLatest hand-off are one unit: a crash between them must not strand the key
        // with no latest row. ReplaceGraphAsync precedent — repo SaveChanges enlists in the ambient scope.
        using var scope = new TransactionScope(
            TransactionScopeOption.Required,
            new TransactionOptions { IsolationLevel = IsolationLevel.ReadCommitted },
            TransactionScopeAsyncFlowOption.Enabled);

        // GenericRepository.DeleteAsync triggers SoftDeleteInterceptor — sets IsDeleted=true
        await repository.DeleteAsync(request.Id, cancellationToken);

        if (wasLatest)
        {
            // Excludes the just-deleted row (default soft-delete filter). Null = that was the last version.
            var survivor = await repository.GetHighestSurvivingByKeyAsync(definitionKey, tenantId, cancellationToken);
            if (survivor is not null)
            {
                survivor.MarkLatest(request.DeletedBy);
                await repository.UpdateAsync(survivor, cancellationToken);
                await repository.SaveChangesAsync(cancellationToken);
            }
        }

        scope.Complete();

        return Result.Ok();
    }
}
