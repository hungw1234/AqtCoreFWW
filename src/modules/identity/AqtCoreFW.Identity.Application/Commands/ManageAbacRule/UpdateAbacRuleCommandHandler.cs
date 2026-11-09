using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.Identity.Application.Authorization;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Identity.Application.Commands.ManageAbacRule;

public sealed class UpdateAbacRuleCommandHandler(
    IAttributeRuleRepository repository,
    IAbacCacheInvalidator cacheInvalidator,
    ILogger<UpdateAbacRuleCommandHandler> logger)
    : IRequestHandler<UpdateAbacRuleCommand, Result>
{
    public async Task<Result> Handle(UpdateAbacRuleCommand cmd, CancellationToken ct)
    {
        var rule = await repository.GetByIdAsync(cmd.RuleId, ct);
        if (rule is null)
            return Result.Fail(new NotFoundError($"ABAC rule {cmd.RuleId} not found."));

        // Defence-in-depth: reject cross-tenant ABAC rule update.
        // TenantId is immutable after Create() â€” cannot change tenant ownership.
        if (cmd.TenantId != rule.TenantId)
        {
            logger.LogWarning(
                "Cross-tenant ABAC rule update rejected: currentTenantId={CurrentTenantId}, attemptedTenantId={AttemptedTenantId}, ruleId={RuleId}",
                rule.TenantId, cmd.TenantId, cmd.RuleId);
            return Result.Fail(new ValidationError("Cross-tenant ABAC rule update rejected"));
        }

        // Capture old cache-key inputs BEFORE mutation â€” invalidation needs old + new
        var oldAttributeValue = rule.AttributeValue;

        rule.Resource = cmd.Resource;
        rule.Action = cmd.Action;
        rule.AttributeKey = cmd.AttributeKey;
        rule.AttributeValue = cmd.AttributeValue;
        rule.Effect = Enum.Parse<AttributeEffect>(cmd.Effect);
        // TenantId not mutated â€” immutable after Create()

        await repository.UpdateAsync(rule, ct);

        // Invalidate old key (this pod + cross-pod); if key changed â†’ invalidate new key too.
        await cacheInvalidator.InvalidateAsync(rule.TenantId, oldAttributeValue, ct);
        if (oldAttributeValue != rule.AttributeValue)
        {
            await cacheInvalidator.InvalidateAsync(rule.TenantId, rule.AttributeValue, ct);
        }

        return Result.Ok();
    }
}
