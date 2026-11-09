using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.SharedKernel.Application.Caching;
using AqtCoreFW.SharedKernel.Authorization;
using Microsoft.AspNetCore.Authorization;

namespace AqtCoreFW.Identity.Application.Authorization;

/// <summary>
/// ABAC handler: checks AttributeRules (cached 5 min via ICacheService).
/// Admin role bypasses all ABAC checks (fast path).
/// </summary>
public sealed class DepartmentAccessHandler
    : AuthorizationHandler<DepartmentAccessRequirement>
{
    private readonly ICacheService _cache;
    private readonly IAttributeRuleReader _ruleReader;

    public DepartmentAccessHandler(ICacheService cache, IAttributeRuleReader ruleReader)
    {
        _cache = cache;
        _ruleReader = ruleReader;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext ctx,
        DepartmentAccessRequirement requirement)
    {
        if (ctx.User.IsInRole(Roles.Admin) || ctx.User.IsInRole(Roles.SystemAdmin))
        {
            ctx.Succeed(requirement);
            return;
        }

        var department = ctx.User.FindFirst("department")?.Value;
        if (string.IsNullOrEmpty(department))
            return;

        var cacheKey = $"abac:dept-rules:{department}";
        var rules = await _cache.GetAsync<List<AttributeRule>>(cacheKey)
            ?? await LoadAndCacheRulesAsync(cacheKey, department);

        if (rules.Any(r => r.Effect == AttributeEffect.Deny))
            return;

        if (rules.Any(r => r.Effect == AttributeEffect.Allow))
            ctx.Succeed(requirement);
    }

    private async Task<List<AttributeRule>> LoadAndCacheRulesAsync(string cacheKey, string department)
    {
        var rules = await _ruleReader.GetRulesByAttributeAsync("department", department);
        await _cache.SetAsync(cacheKey, rules, TimeSpan.FromMinutes(5));
        return rules;
    }
}

/// <summary>Read-only accessor for AttributeRules — implemented in Infrastructure.</summary>
public interface IAttributeRuleReader
{
    Task<List<AttributeRule>> GetRulesByAttributeAsync(
        string attributeKey, string attributeValue, CancellationToken ct = default);
}
