using AqtCoreFW.Identity.Application.Queries.ListPolicyRules;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using FluentResults;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Identity.Infrastructure.Queries.PolicyRule;

/// <summary>Handles ListPolicyRulesQuery — reads policy rules from IdentityDbContext.</summary>
public sealed class ListPolicyRulesQueryHandler(IdentityDbContext db)
    : IRequestHandler<ListPolicyRulesQuery, Result<IReadOnlyList<PolicyRuleDto>>>
{
    public async Task<Result<IReadOnlyList<PolicyRuleDto>>> Handle(
        ListPolicyRulesQuery request, CancellationToken ct)
    {
        var rules = await db.PolicyRules
            .Where(r =>
                (request.TenantId == null || r.TenantId == request.TenantId) &&
                (request.PermissionCode == null || r.PermissionCode == request.PermissionCode))
            .OrderByDescending(r => r.Priority)
            .Select(r => new PolicyRuleDto(
                r.Id, r.Code, r.PermissionCode, r.ConditionExpression,
                r.Effect.ToString(), r.Priority, r.IsActive, r.LogOnDeny,
                r.Description, r.TenantId))
            .ToListAsync(ct);

        return Result.Ok<IReadOnlyList<PolicyRuleDto>>(rules);
    }
}
