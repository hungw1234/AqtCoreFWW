using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.Identity.Application.Authorization;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Identity.Application.Commands.ManageAbacRule;

public sealed class CreateAbacRuleCommandHandler(
    IAttributeRuleRepository repository,
    IAbacCacheInvalidator cacheInvalidator)
    : IRequestHandler<CreateAbacRuleCommand, Result<long>>
{
    public async Task<Result<long>> Handle(CreateAbacRuleCommand cmd, CancellationToken ct)
    {
        if (!Enum.TryParse<AttributeEffect>(cmd.Effect, true, out var effect))
            return Result.Fail(new ValidationError($"Invalid effect: {cmd.Effect}. Use 'Allow' or 'Deny'."));

        var rule = AttributeRule.Create(
            cmd.Resource, cmd.Action, cmd.AttributeKey, cmd.AttributeValue, effect, cmd.TenantId);

        await repository.AddAsync(rule, ct);

        // Invalidate ABAC cache (this pod L1+L2 + publish cross-pod L1 eviction).
        await cacheInvalidator.InvalidateAsync(rule.TenantId, rule.AttributeValue, ct);

        // T1: return long Id (DB-assigned IDENTITY after SaveChanges in AddAsync)
        return Result.Ok(rule.Id);
    }
}
