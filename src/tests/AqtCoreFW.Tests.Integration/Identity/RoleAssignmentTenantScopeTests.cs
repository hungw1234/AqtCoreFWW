using AqtCoreFW.Identity.Application.Commands.AssignRole;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using AqtCoreFW.Tests.Integration.Infrastructure;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AqtCoreFW.Tests.Integration.Identity;

/// <summary>
/// Real-SQL coverage for tenant-scoped role assignment (AssignRoleCommandHandler).
///
/// Once role display names are unique PER TENANT rather than globally (ScopeRoleNameToTenant), a
/// bare name lookup is ambiguous: two tenants can both hold "Bộ trưởng". The pre-fix handler resolved
/// via UserManager/RoleManager name APIs, whose base store uses SingleOrDefaultAsync(NormalizedName)
/// — so a duplicated business-role name threw (500), and at best could bind the wrong tenant's role.
/// The fix resolves by (NormalizedName, TenantId) and assigns by RoleId. This test is non-vacuous:
/// without the fix the first case throws instead of returning the tenant-A role.
/// </summary>
[Collection("Integration")]
public class RoleAssignmentTenantScopeTests(DatabaseFixture db) : IntegrationTestBase(db)
{
    [Fact]
    public async Task AssignRole_DuplicateBusinessRoleName_ResolvesToUsersTenant_NotCrossTenant()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        await using var scope = Factory.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var dbCtx = sp.GetRequiredService<IdentityDbContext>();
        var normalizer = sp.GetRequiredService<ILookupNormalizer>();
        var mediator = sp.GetRequiredService<IMediator>();

        // Same display name, two tenants, distinct code prefixes — the exact ambiguity the schema permits.
        var roleA = NewRole("ZZ Bộ trưởng", "ZZA_MIN", tenantA, normalizer);
        var roleB = NewRole("ZZ Bộ trưởng", "ZZB_MIN", tenantB, normalizer);
        var user = NewUser(tenantA, normalizer);
        dbCtx.Roles.AddRange(roleA, roleB);
        dbCtx.Users.Add(user);
        await dbCtx.SaveChangesAsync();

        try
        {
            var result = await mediator.Send(new AssignRoleCommand(user.Id, "ZZ Bộ trưởng", user.Id));

            result.IsSuccess.Should().BeTrue(
                "a duplicated business-role name must resolve within the user's tenant, not throw on ambiguity");

            var assignedRoleIds = await dbCtx.UserRoles
                .Where(ur => ur.UserId == user.Id).Select(ur => ur.RoleId).ToListAsync();

            assignedRoleIds.Should().ContainSingle()
                .Which.Should().Be(roleA.Id, "the tenant-A role must be chosen — never tenant-B's");
            assignedRoleIds.Should().NotContain(roleB.Id);
        }
        finally
        {
            await dbCtx.UserRoles.Where(ur => ur.UserId == user.Id).ExecuteDeleteAsync();
            await dbCtx.Users.Where(u => u.Id == user.Id).ExecuteDeleteAsync();
            await dbCtx.Roles.Where(r => r.TenantId == tenantA || r.TenantId == tenantB).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task AssignRole_SystemRoleByName_StillResolves_ViaNullTenantFallback()
    {
        var userTenant = Guid.NewGuid();
        await using var scope = Factory.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var dbCtx = sp.GetRequiredService<IdentityDbContext>();
        var normalizer = sp.GetRequiredService<ILookupNormalizer>();
        var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var mediator = sp.GetRequiredService<IMediator>();

        // System-wide role: TenantId == null (the shape the FE's 4 assignable roles use).
        var systemRole = NewRole("ZZ System Role", "ZZ_SYS", tenantId: null, normalizer);
        var user = NewUser(userTenant, normalizer);

        // C2 guard: granting System role requires actor to be SystemAdmin. Create a SystemAdmin user.
        var systemAdminRole = NewRole("SystemAdmin", "SYS_ADMIN", tenantId: null, normalizer);
        var systemAdminUser = NewUser(userTenant, normalizer);

        dbCtx.Roles.AddRange(systemRole, systemAdminRole);
        dbCtx.Users.AddRange(user, systemAdminUser);
        await dbCtx.SaveChangesAsync();

        // Assign SystemAdmin role to the actor
        await userManager.AddToRoleAsync(systemAdminUser, "SystemAdmin");

        try
        {
            // Actor is the SystemAdmin user, not the target user — resolution still exercises null-tenant fallback
            var result = await mediator.Send(new AssignRoleCommand(user.Id, "ZZ System Role", systemAdminUser.Id));

            result.IsSuccess.Should().BeTrue("system roles resolve via the null-tenant fallback regardless of user tenant");
            (await dbCtx.UserRoles.AnyAsync(ur => ur.UserId == user.Id && ur.RoleId == systemRole.Id))
                .Should().BeTrue();
        }
        finally
        {
            await dbCtx.UserRoles.Where(ur => ur.UserId == user.Id || ur.UserId == systemAdminUser.Id).ExecuteDeleteAsync();
            await dbCtx.Users.Where(u => u.Id == user.Id || u.Id == systemAdminUser.Id).ExecuteDeleteAsync();
            await dbCtx.Roles.Where(r => r.Id == systemRole.Id || r.Id == systemAdminRole.Id).ExecuteDeleteAsync();
        }
    }

    private static ApplicationRole NewRole(string name, string code, Guid? tenantId, ILookupNormalizer normalizer)
        => new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            NormalizedName = normalizer.NormalizeName(name),
            Code = code,
            RoleType = tenantId is null ? RoleType.System : RoleType.Business,
            IsActive = true,
            TenantId = tenantId,
            ConcurrencyStamp = Guid.NewGuid().ToString(),
        };

    private static ApplicationUser NewUser(Guid tenantId, ILookupNormalizer normalizer)
    {
        var email = $"role-scope-{Guid.NewGuid():N}@test.vn";
        return new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            NormalizedUserName = normalizer.NormalizeName(email),
            Email = email,
            NormalizedEmail = normalizer.NormalizeEmail(email),
            FullName = "Role Scope Test User",
            TenantId = tenantId,
            SecurityStamp = Guid.NewGuid().ToString(),
            ConcurrencyStamp = Guid.NewGuid().ToString(),
        };
    }
}
