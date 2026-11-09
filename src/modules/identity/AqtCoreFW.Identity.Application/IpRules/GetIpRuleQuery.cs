using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using FluentValidation;
using MediatR;

namespace AqtCoreFW.Identity.Application.IpRules;

/// <summary>Returns a single IP filter rule by its long Id (T1 addressing).</summary>
public sealed record GetIpRuleQuery(long Id) : IRequest<Result<IpRuleDto>>;

public sealed class GetIpRuleQueryValidator : AbstractValidator<GetIpRuleQuery>
{
    public GetIpRuleQueryValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
    }
}

public sealed class GetIpRuleQueryHandler(IIpFilterRuleRepository repository)
    : IRequestHandler<GetIpRuleQuery, Result<IpRuleDto>>
{
    public async Task<Result<IpRuleDto>> Handle(GetIpRuleQuery request, CancellationToken ct)
    {
        var rule = await repository.GetByIdAsync(request.Id, ct);
        if (rule is null)
            return Result.Fail(new NotFoundError($"IpFilterRule {request.Id} not found."));

        return Result.Ok(new IpRuleDto(
            rule.Id, rule.TenantId, rule.Cidr, rule.Description,
            rule.IsEnabled, rule.IsBlock, rule.CreatedAt, rule.UpdatedAt));
    }
}
