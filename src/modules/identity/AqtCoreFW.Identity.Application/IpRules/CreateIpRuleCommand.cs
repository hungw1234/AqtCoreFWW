using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Application.Caching;
using AqtCoreFW.SharedKernel.Compliance;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using FluentValidation;
using MediatR;

namespace AqtCoreFW.Identity.Application.IpRules;

/// <summary>Creates a new IP filter rule and invalidates the middleware cache.</summary>
[AuditCategory(AuditCategory.ConfigChange)]
public sealed record CreateIpRuleCommand(
    Guid? TenantId,
    string Cidr,
    string Description,
    bool IsEnabled,
    bool IsBlock,
    Guid ActorId) : ICommand<long>;

public sealed class CreateIpRuleCommandValidator : AbstractValidator<CreateIpRuleCommand>
{
    public CreateIpRuleCommandValidator()
    {
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

public sealed class CreateIpRuleCommandHandler(
    IIpFilterRuleRepository repository,
    ICacheService cache)
    : IRequestHandler<CreateIpRuleCommand, Result<long>>
{
    private const string RulesCacheKey = "system:ip-rules";

    public async Task<Result<long>> Handle(CreateIpRuleCommand cmd, CancellationToken ct)
    {
        // Application-level uniqueness check before DB unique index fires (better UX error message).
        if (await repository.ExistsByCidrAsync(cmd.TenantId, cmd.Cidr, excludeId: null, ct))
            return Result.Fail(new ConflictError($"An active rule for CIDR '{cmd.Cidr}' already exists for this tenant."));

        var rule = IpFilterRule.Create(
            cmd.TenantId, cmd.Cidr, cmd.Description,
            cmd.IsEnabled, cmd.IsBlock, cmd.ActorId);

        await repository.AddAsync(rule, ct);

        // Invalidate middleware cache so new rule takes effect within next request cycle.
        await cache.RemoveAsync(RulesCacheKey, ct);

        return Result.Ok(rule.Id);
    }
}
