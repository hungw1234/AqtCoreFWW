using AqtCoreFW.Identity.Domain.Repositories;
using FluentResults;
using FluentValidation;
using MediatR;

namespace AqtCoreFW.Identity.Application.IpRules;

/// <summary>Returns all IP filter rules (no pagination — expected &lt;1000 rules total).</summary>
public sealed record ListIpRulesQuery : IRequest<Result<IReadOnlyList<IpRuleDto>>>;

public sealed class ListIpRulesQueryValidator : AbstractValidator<ListIpRulesQuery>
{
    public ListIpRulesQueryValidator() { /* no parameters to validate */ }
}

public sealed class ListIpRulesQueryHandler(IIpFilterRuleRepository repository)
    : IRequestHandler<ListIpRulesQuery, Result<IReadOnlyList<IpRuleDto>>>
{
    public async Task<Result<IReadOnlyList<IpRuleDto>>> Handle(
        ListIpRulesQuery request, CancellationToken ct)
    {
        var rules = await repository.ListAllAsync(ct);

        var dtos = rules
            .Select(r => new IpRuleDto(
                r.Id, r.TenantId, r.Cidr, r.Description,
                r.IsEnabled, r.IsBlock, r.CreatedAt, r.UpdatedAt))
            .ToList();

        return Result.Ok<IReadOnlyList<IpRuleDto>>(dtos);
    }
}
