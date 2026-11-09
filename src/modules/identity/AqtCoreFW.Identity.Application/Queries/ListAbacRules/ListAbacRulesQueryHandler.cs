using AqtCoreFW.Identity.Domain.Repositories;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Identity.Application.Queries.ListAbacRules;

public sealed class ListAbacRulesQueryHandler(IAttributeRuleRepository repository)
    : IRequestHandler<ListAbacRulesQuery, Result<IReadOnlyList<AbacRuleDto>>>
{
    public async Task<Result<IReadOnlyList<AbacRuleDto>>> Handle(
        ListAbacRulesQuery request, CancellationToken ct)
    {
        var rules = await repository.GetAllAsync(request.TenantId, ct);
        var dtos = rules.Select(r => new AbacRuleDto(
            r.Id, r.Resource, r.Action,
            r.AttributeKey, r.AttributeValue,
            r.Effect.ToString(), r.TenantId)).ToList();
        return Result.Ok<IReadOnlyList<AbacRuleDto>>(dtos);
    }
}
