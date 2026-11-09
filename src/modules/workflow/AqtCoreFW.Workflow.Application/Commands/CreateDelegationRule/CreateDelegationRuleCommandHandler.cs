using AqtCoreFW.SharedKernel.Errors;
using AqtCoreFW.Workflow.Application.DTOs;
using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Domain.Entities;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Commands.CreateDelegationRule;

/// <summary>
/// Creates a delegation rule after verifying no active overlapping rule exists for the delegator.
/// Enforces max-1-active-rule constraint via HasOverlappingRuleAsync.
/// </summary>
public sealed class CreateDelegationRuleCommandHandler(IDelegationRuleRepository repository)
    : IRequestHandler<CreateDelegationRuleCommand, Result<DelegationRuleDto>>
{
    public async Task<Result<DelegationRuleDto>> Handle(
        CreateDelegationRuleCommand request,
        CancellationToken cancellationToken)
    {
        var hasOverlap = await repository.HasOverlappingRuleAsync(
            request.DelegatorUserId,
            request.ValidFrom,
            request.ValidUntil,
            request.TenantId,
            excludeId: null,
            cancellationToken);

        if (hasOverlap)
            return Result.Fail(new ConflictError(
                $"An active delegation rule already exists for user '{request.DelegatorUserId}' in this time window."));

        var rule = DelegationRule.Create(
            request.DelegatorUserId,
            request.DelegateUserId,
            request.ValidFrom,
            request.ValidUntil,
            request.TenantId,
            request.CreatedBy);

        await repository.AddAsync(rule, cancellationToken);

        return Result.Ok(MapToDto(rule));
    }

    internal static DelegationRuleDto MapToDto(DelegationRule r) => new(
        r.Id, r.DelegatorUserId, r.DelegateUserId,
        r.ValidFrom, r.ValidUntil, r.IsActive,
        r.TenantId, r.CreatedAt, r.CreatedBy);
}
