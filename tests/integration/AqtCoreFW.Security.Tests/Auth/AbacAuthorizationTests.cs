using System.Security.Claims;
using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.Identity.Application.Authorization;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.SharedKernel.Application.Caching;
using AqtCoreFW.SharedKernel.Domain;
using Microsoft.AspNetCore.Authorization;
using NSubstitute;

namespace AqtCoreFW.Security.Tests.Auth;

/// <summary>
/// ABAC authorization tests â€” TC-SEC-AUTHZ-004, TC-SEC-AUTHZ-005.
/// Covers: department-based ABAC policy, admin bypass, deny-wins-over-allow,
/// Two-Tier cache integration, and tenant-scoped cache keys (T4-05 Phase 01).
/// </summary>
public sealed class AbacAuthorizationTests
{
    // â”€â”€ Helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static ClaimsPrincipal CreateUser(string? department = null, params string[] roles)
    {
        var claims = new List<Claim>();
        if (department != null)
            claims.Add(new Claim("department", department));
        foreach (var role in roles)
            claims.Add(new Claim(ClaimTypes.Role, role));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
    }

    private static AuthorizationHandlerContext CreateContext(ClaimsPrincipal user)
    {
        var requirement = new DepartmentAccessRequirement();
        return new AuthorizationHandlerContext([requirement], user, null);
    }

    /// <summary>Build handler vá»›i cache-miss default (forces DB path) + null tenant.</summary>
    private static (AbacAuthorizationHandler Handler, IAttributeRuleRepository Rules, ICacheService Cache, ITenantContext TenantCtx)
        BuildHandler(Guid? tenantId = null)
    {
        var rules = Substitute.For<IAttributeRuleRepository>();
        var cache = Substitute.For<ICacheService>();
        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.TenantId.Returns(tenantId);

        // Default: cache miss â€” force DB lookup path.
        cache.GetAsync<List<AttributeRule>>(Arg.Any<string>(), Arg.Any<CancellationToken>())
             .Returns((List<AttributeRule>?)null);

        return (new AbacAuthorizationHandler(rules, cache, tenantCtx), rules, cache, tenantCtx);
    }

    // â”€â”€ 7 existing tests (refactored) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    [Trait("Category", "Security")]
    public async Task AdminRole_BypassesAbacChecks()
    {
        var (handler, rules, _, _) = BuildHandler();

        var user = CreateUser("CNTT", Roles.Admin);
        var ctx = CreateContext(user);

        await handler.HandleAsync(ctx);

        ctx.HasSucceeded.Should().BeTrue();
        // DB should never be hit for admin
        await rules.DidNotReceive().GetByAttributeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task SystemAdminRole_BypassesAbacChecks()
    {
        var (handler, _, _, _) = BuildHandler();

        var user = CreateUser(null, Roles.SystemAdmin);
        var ctx = CreateContext(user);

        await handler.HandleAsync(ctx);

        ctx.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task NoDepartmentClaim_DeniedImplicitly()
    {
        var (handler, _, _, _) = BuildHandler();

        var user = CreateUser(department: null); // no department
        var ctx = CreateContext(user);

        await handler.HandleAsync(ctx);

        ctx.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task AllowRule_GrantsAccess()
    {
        var (handler, rules, _, _) = BuildHandler();
        rules.GetByAttributeAsync("department", "CNTT", Arg.Any<CancellationToken>())
            .Returns(new List<AttributeRule> { new() { Effect = AttributeEffect.Allow } });

        var user = CreateUser("CNTT");
        var ctx = CreateContext(user);

        await handler.HandleAsync(ctx);

        ctx.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task DenyRule_OverridesAllowRule()
    {
        var (handler, rules, _, _) = BuildHandler();
        rules.GetByAttributeAsync("department", "RESTRICTED", Arg.Any<CancellationToken>())
            .Returns(new List<AttributeRule>
            {
                new() { Effect = AttributeEffect.Allow },
                new() { Effect = AttributeEffect.Deny }
            });

        var user = CreateUser("RESTRICTED");
        var ctx = CreateContext(user);

        await handler.HandleAsync(ctx);

        ctx.HasSucceeded.Should().BeFalse("Deny must override Allow");
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task NoRules_FallbackToDepartmentCodeClaim()
    {
        var (handler, rules, _, _) = BuildHandler();
        rules.GetByAttributeAsync("department", "TCCB", Arg.Any<CancellationToken>())
            .Returns(new List<AttributeRule>());

        // User has matching department_code claim
        var claims = new List<Claim>
        {
            new("department", "TCCB"),
            new("department_code", "TCCB")
        };
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        var ctx = CreateContext(user);

        await handler.HandleAsync(ctx);

        ctx.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task NoRules_MismatchedDeptCode_Denied()
    {
        var (handler, rules, _, _) = BuildHandler();
        rules.GetByAttributeAsync("department", "CNTT", Arg.Any<CancellationToken>())
            .Returns(new List<AttributeRule>());

        var claims = new List<Claim>
        {
            new("department", "CNTT"),
            new("department_code", "TCCB") // mismatch
        };
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        var ctx = CreateContext(user);

        await handler.HandleAsync(ctx);

        ctx.HasSucceeded.Should().BeFalse();
    }

    // â”€â”€ 3 NEW tests (Two-Tier cache + tenant-scoped keys, T4-05 Phase 01) â”€â”€â”€

    [Fact]
    [Trait("Category", "Security")]
    public async Task CacheKey_SystemScope_WhenTenantIdNull()
    {
        // Null tenant â†’ key MUST start with "sys:" â€” matches AbacCacheKeys.ForDepartment.
        var (handler, rules, cache, _) = BuildHandler(tenantId: null);
        rules.GetByAttributeAsync("department", "CNTT", Arg.Any<CancellationToken>())
            .Returns(new List<AttributeRule> { new() { Effect = AttributeEffect.Allow } });

        var user = CreateUser("CNTT");
        await handler.HandleAsync(CreateContext(user));

        await cache.Received(1).GetAsync<List<AttributeRule>>(
            Arg.Is<string>(k => k == "sys:abac:rules:department:CNTT"),
            Arg.Any<CancellationToken>());
        await cache.Received(1).SetAsync(
            Arg.Is<string>(k => k == "sys:abac:rules:department:CNTT"),
            Arg.Any<List<AttributeRule>>(),
            Arg.Any<TimeSpan?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task CacheKey_TenantScope_WhenTenantIdPresent()
    {
        // Non-null tenant â†’ key MUST be "t:{guid}:..." â€” tenant isolation preserved.
        var tenantId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var (handler, rules, cache, _) = BuildHandler(tenantId: tenantId);
        rules.GetByAttributeAsync("department", "TCCB", Arg.Any<CancellationToken>())
            .Returns(new List<AttributeRule> { new() { Effect = AttributeEffect.Allow } });

        var user = CreateUser("TCCB");
        await handler.HandleAsync(CreateContext(user));

        var expectedKey = $"t:{tenantId}:abac:rules:department:TCCB";
        await cache.Received(1).GetAsync<List<AttributeRule>>(
            Arg.Is<string>(k => k == expectedKey),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task CacheHit_SkipsDbCall()
    {
        // Pre-seeded cache â†’ DB MUST NOT be queried (Two-Tier hit path).
        var rules = Substitute.For<IAttributeRuleRepository>();
        var cache = Substitute.For<ICacheService>();
        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.TenantId.Returns((Guid?)null);

        // Cache hit â€” return pre-seeded Allow rule.
        cache.GetAsync<List<AttributeRule>>(Arg.Any<string>(), Arg.Any<CancellationToken>())
             .Returns(new List<AttributeRule> { new() { Effect = AttributeEffect.Allow } });

        var handler = new AbacAuthorizationHandler(rules, cache, tenantCtx);
        var user = CreateUser("CNTT");
        var ctx = CreateContext(user);

        await handler.HandleAsync(ctx);

        ctx.HasSucceeded.Should().BeTrue();
        // DB bypassed â€” repo method must not be called.
        await rules.DidNotReceive().GetByAttributeAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        // No SetAsync either (cache already hot).
        await cache.DidNotReceive().SetAsync(
            Arg.Any<string>(), Arg.Any<List<AttributeRule>>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>());
    }
}
