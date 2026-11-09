using AqtCoreFW.SharedKernel.Errors;
using AqtCoreFW.Workflow.Application.Repositories;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Commands.DeleteDelegationRule;

/// <summary>
/// Soft-deletes a delegation rule by calling Deactivate() (sets IsActive = false).
/// Does not remove the row — preserves audit history.
/// Tenant isolation: GetByIdAndTenantAsync enforces tenant boundary (C3).
/// Cascade return of in-flight delegations is handled by DelegationExpiryJob (KISS decision).
/// </summary>
public sealed class DeleteDelegationRuleCommandHandler(IDelegationRuleRepository repository)
    : IRequestHandler<DeleteDelegationRuleCommand, Result>
{
    public async Task<Result> Handle(
        DeleteDelegationRuleCommand request,
        CancellationToken cancellationToken)
    {
        // C3: tenant-scoped lookup prevents cross-tenant access
        var findResult = await repository.GetByIdAndTenantAsync(request.RuleId, request.TenantId, cancellationToken);
        if (findResult.IsFailed)
            return Result.Fail(new NotFoundError($"DelegationRule '{request.RuleId}' not found."));

        var rule = findResult.Value;
        rule.Deactivate(request.ModifiedBy);

        await repository.UpdateAsync(rule, cancellationToken);

        return Result.Ok();
    }
}
