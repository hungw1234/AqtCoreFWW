using AqtCoreFW.SharedKernel.Errors;
using AqtCoreFW.Workflow.Application.Commands.CreateDelegationRule;
using AqtCoreFW.Workflow.Application.DTOs;
using AqtCoreFW.Workflow.Application.Repositories;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Commands.UpdateDelegationRule;

/// <summary>
/// Updates an existing delegation rule's window and delegate user.
/// Enforces max-1-active-rule: checks for overlapping rules excluding the rule being updated.
/// Tenant isolation: GetByIdAndTenantAsync enforces tenant boundary (C3).
/// </summary>
public sealed class UpdateDelegationRuleCommandHandler(IDelegationRuleRepository repository)
    : IRequestHandler<UpdateDelegationRuleCommand, Result<DelegationRuleDto>>
{
    public async Task<Result<DelegationRuleDto>> Handle(
        UpdateDelegationRuleCommand request,
        CancellationToken cancellationToken)
    {
        // C3: tenant-scoped lookup prevents cross-tenant access
        var findResult = await repository.GetByIdAndTenantAsync(request.RuleId, request.TenantId, cancellationToken);
        if (findResult.IsFailed)
            return Result.Fail(new NotFoundError($"DelegationRule '{request.RuleId}' not found."));

        var rule = findResult.Value;

        var hasOverlap = await repository.HasOverlappingRuleAsync(
            rule.DelegatorUserId,
            request.ValidFrom,
            request.ValidUntil,
            request.TenantId,
            excludeId: request.RuleId,
            cancellationToken);

        if (hasOverlap)
            return Result.Fail(new ConflictError(
                $"Another active delegation rule exists for user '{rule.DelegatorUserId}' in this time window."));

        rule.UpdateWindow(request.DelegateUserId, request.ValidFrom, request.ValidUntil, request.ModifiedBy);

        await repository.UpdateAsync(rule, cancellationToken);

        return Result.Ok(CreateDelegationRuleCommandHandler.MapToDto(rule));
    }
}
