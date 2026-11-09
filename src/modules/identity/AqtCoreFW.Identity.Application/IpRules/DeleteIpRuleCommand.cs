using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Application.Caching;
using AqtCoreFW.SharedKernel.Compliance;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using FluentValidation;
using MediatR;

namespace AqtCoreFW.Identity.Application.IpRules;

/// <summary>Soft-deletes an IP filter rule and invalidates the middleware cache.</summary>
[AuditCategory(AuditCategory.ConfigChange)]
public sealed record DeleteIpRuleCommand(long Id) : ICommand;

public sealed class DeleteIpRuleCommandValidator : AbstractValidator<DeleteIpRuleCommand>
{
    public DeleteIpRuleCommandValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
    }
}

public sealed class DeleteIpRuleCommandHandler(
    IIpFilterRuleRepository repository,
    ICacheService cache)
    : IRequestHandler<DeleteIpRuleCommand, Result>
{
    private const string RulesCacheKey = "system:ip-rules";

    public async Task<Result> Handle(DeleteIpRuleCommand cmd, CancellationToken ct)
    {
        var rule = await repository.GetByIdAsync(cmd.Id, ct);
        if (rule is null)
            return Result.Fail(new NotFoundError($"IpFilterRule {cmd.Id} not found."));

        await repository.DeleteAsync(rule, ct);

        // Invalidate middleware cache so deleted rule is no longer applied.
        await cache.RemoveAsync(RulesCacheKey, ct);

        return Result.Ok();
    }
}
