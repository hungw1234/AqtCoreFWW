using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Application.Caching;
using AqtCoreFW.SharedKernel.Compliance;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using FluentValidation;
using MediatR;

namespace AqtCoreFW.Identity.Application.IpRules;

/// <summary>Updates an existing IP filter rule and invalidates the middleware cache.</summary>
[AuditCategory(AuditCategory.ConfigChange)]
public sealed record UpdateIpRuleCommand(
    long Id,
    string Cidr,
    string Description,
    bool IsEnabled,
    bool IsBlock,
    Guid ActorId) : ICommand;

public sealed class UpdateIpRuleCommandValidator : AbstractValidator<UpdateIpRuleCommand>
{
    public UpdateIpRuleCommandValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.Cidr)
            .NotEmpty()
            .MaximumLength(50)
            .Must(CidrValidator.IsValidCidr)
            .WithMessage("Cidr must be valid IPv4 or IPv6 CIDR notation (e.g. 10.0.0.0/24).")
            .Must(cidr => !CidrValidator.IsLocalhostCidr(cidr))
            .WithMessage("Localhost/loopback CIDRs cannot be blocked — they are always allowed.")
            .Must(cidr => !CidrValidator.IsAllNetworkCidr(cidr))
            .WithMessage("All-network CIDRs (0.0.0.0/0 or ::/0) are not allowed — they would override all other rules.");

        RuleFor(x => x.Description).MaximumLength(500);
        RuleFor(x => x.ActorId).NotEmpty();
    }
}

public sealed class UpdateIpRuleCommandHandler(
    IIpFilterRuleRepository repository,
    ICacheService cache)
    : IRequestHandler<UpdateIpRuleCommand, Result>
{
    private const string RulesCacheKey = "system:ip-rules";

    public async Task<Result> Handle(UpdateIpRuleCommand cmd, CancellationToken ct)
    {
        var rule = await repository.GetByIdAsync(cmd.Id, ct);
        if (rule is null)
            return Result.Fail(new NotFoundError($"IpFilterRule {cmd.Id} not found."));

        // Application-level uniqueness check: same CIDR + same tenant, excluding the current rule.
        if (await repository.ExistsByCidrAsync(rule.TenantId, cmd.Cidr, excludeId: cmd.Id, ct))
            return Result.Fail(new ConflictError($"An active rule for CIDR '{cmd.Cidr}' already exists for this tenant."));

        rule.Update(cmd.Cidr, cmd.Description, cmd.IsEnabled, cmd.IsBlock, cmd.ActorId);

        await repository.UpdateAsync(rule, ct);

        // Invalidate middleware cache so updated rule takes effect on next request cycle.
        await cache.RemoveAsync(RulesCacheKey, ct);

        return Result.Ok();
    }
}
