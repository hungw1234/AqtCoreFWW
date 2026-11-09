using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.SharedKernel.Application.Caching;
using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.SharedKernel.Domain;
using Microsoft.AspNetCore.Authorization;

namespace AqtCoreFW.Identity.Application.Authorization;

/// <summary>
/// ABAC handler — check AttributeRules từ DB với 5-min Two-Tier cache (L1 IMemoryCache + L2 Redis).
/// Cross-pod L1 invalidation via <see cref="IAbacCacheInvalidator"/> + AbacCacheSubscriber.
/// Check order: (1) Admin fast path → Succeed, (2) ABAC rules from cache, (3) DepartmentCode match.
///
/// Key: <see cref="AbacCacheKeys.ForDepartment"/> — tenant-scoped (sys: hoặc t:{tid}:) để không leak chéo tenant.
/// Cache value type <see cref="List{AttributeRule}"/> (không phải IReadOnlyList) — System.Text.Json round-trip safe.
/// </summary>
public sealed class AbacAuthorizationHandler
    : AuthorizationHandler<DepartmentAccessRequirement>
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private readonly IAttributeRuleRepository _rules;
    private readonly ICacheService _cache;
    private readonly ITenantContext _tenantContext;

    public AbacAuthorizationHandler(
        IAttributeRuleRepository rules,
        ICacheService cache,
        ITenantContext tenantContext)
    {
        _rules = rules;
        _cache = cache;
        _tenantContext = tenantContext;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext ctx,
        DepartmentAccessRequirement requirement)
    {
        // (1) RBAC fast path — Admin/SystemAdmin bypasses all ABAC checks.
        if (ctx.User.IsInRole(Roles.Admin) || ctx.User.IsInRole(Roles.SystemAdmin))
        {
            ctx.Succeed(requirement);
            return;
        }

        var department = ctx.User.FindFirst("department")?.Value;
        if (string.IsNullOrEmpty(department))
            return; // Deny implicitly — no department claim.

        // (2) Load ABAC rules from Two-Tier cache (L1 per-pod → L2 Redis shared → DB).
        // Base AuthorizationHandler không expose CancellationToken → dùng None (matches behavior cũ).
        var cacheKey = AbacCacheKeys.ForDepartment(_tenantContext.TenantId, department);
        var rules = await _cache.GetAsync<List<AttributeRule>>(cacheKey, CancellationToken.None);
        if (rules is null)
        {
            var fetched = await _rules.GetByAttributeAsync("department", department);
            rules = fetched.ToList();
            await _cache.SetAsync(cacheKey, rules, CacheTtl, CancellationToken.None);
        }

        // Explicit Deny wins over Allow.
        if (rules.Any(r => r.Effect == AttributeEffect.Deny))
            return;

        // (3) At least one Allow rule — or user's DepartmentCode matches (fallback).
        if (rules.Any(r => r.Effect == AttributeEffect.Allow))
        {
            ctx.Succeed(requirement);
            return;
        }

        // Fallback: check DepartmentCode claim directly.
        var deptCode = ctx.User.FindFirst("department_code")?.Value;
        if (!string.IsNullOrEmpty(deptCode) && deptCode == department)
            ctx.Succeed(requirement);
    }
}
