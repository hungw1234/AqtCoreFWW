using AqtCoreFW.Rules.Application.DTOs;
using AqtCoreFW.Rules.Domain.Repositories;
using AqtCoreFW.Rules.Domain.Services;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Rules.Application.Commands.UpdateRule;

public sealed class UpdateRuleCommandHandler(
    IRuleSetRepository repository,
    ICurrentUser currentUser,
    IRuleEvaluator ruleEvaluator,
    ITenantContext tenantContext,
    ILogger<UpdateRuleCommandHandler> logger)
    : IRequestHandler<UpdateRuleCommand, Result<RuleDto>>
{
    public async Task<Result<RuleDto>> Handle(
        UpdateRuleCommand request,
        CancellationToken cancellationToken)
    {
        // request.RuleSetId is the external Guid PublicId (T2 addressing).
        var ruleSet = await repository.GetByPublicIdAsync(request.RuleSetId, cancellationToken);
        if (ruleSet is null)
            return Result.Fail(new NotFoundError($"RuleSet {request.RuleSetId} not found."));

        if (ruleSet.TenantId != tenantContext.TenantId && !tenantContext.IsSystemAdmin)
        {
            logger.LogWarning(
                "Cross-tenant RuleSet update rejected: currentTenantId={CurrentTenantId}, attemptedTenantId={AttemptedTenantId}, ruleSetPublicId={RuleSetPublicId}",
                tenantContext.TenantId, ruleSet.TenantId, ruleSet.PublicId);
            return Result.Fail(new ValidationError("Cross-tenant RuleSet update rejected"));
        }

        // request.RuleId is Rule.PublicId (T2 addressing).
        var rule = ruleSet.Rules.FirstOrDefault(r => r.PublicId == request.RuleId);
        if (rule is null)
            return Result.Fail(new NotFoundError($"Rule {request.RuleId} not found in RuleSet {request.RuleSetId}."));

        rule.Update(
            request.Name,
            request.Expression,
            request.Priority,
            request.Enabled,
            request.FailureAction,
            currentUser.UserId);

        await repository.UpdateAsync(ruleSet, cancellationToken);
        ruleEvaluator.InvalidateCache(ruleSet.PublicId);

        return Result.Ok(new RuleDto(
            rule.PublicId, ruleSet.PublicId, rule.Name, rule.Expression,
            rule.Priority, rule.Enabled, rule.ErrorMessage, rule.FailureAction.ToString()));
    }
}
