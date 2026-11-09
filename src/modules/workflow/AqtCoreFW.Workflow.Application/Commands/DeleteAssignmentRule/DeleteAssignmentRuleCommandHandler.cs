using AqtCoreFW.SharedKernel.Application.Caching;
using AqtCoreFW.SharedKernel.Domain.Repositories;
using AqtCoreFW.SharedKernel.Errors;
using AqtCoreFW.Workflow.Domain.Entities;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Commands.DeleteAssignmentRule;

public sealed class DeleteAssignmentRuleCommandHandler(
    IRepository<WorkflowAssignmentRule, long> repository,
    ICacheService cache)
    : IRequestHandler<DeleteAssignmentRuleCommand, Result>
{
    public async Task<Result> Handle(
        DeleteAssignmentRuleCommand request,
        CancellationToken cancellationToken)
    {
        var result = await repository.GetByIdAsync(request.Id, cancellationToken);
        if (result.IsFailed)
            return Result.Fail(new NotFoundError($"WorkflowAssignmentRule '{request.Id}' not found."));

        var rule = result.Value;

        // Guard: tenant isolation — reject cross-tenant deactivation attempts
        if (rule.TenantId != request.TenantId)
            return Result.Fail(new NotFoundError($"WorkflowAssignmentRule '{request.Id}' not found."));

        rule.Deactivate(request.DeletedBy);
        await repository.UpdateAsync(rule, cancellationToken);

        // Invalidate all cached resolution entries for this tenant
        await cache.RemoveByPrefixAsync($"wf:assignment:{request.TenantId}:", cancellationToken);

        return Result.Ok();
    }
}
