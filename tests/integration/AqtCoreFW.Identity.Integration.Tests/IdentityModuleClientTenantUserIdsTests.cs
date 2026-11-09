using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.Tests.SharedFixtures;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace AqtCoreFW.Identity.Integration.Tests;

/// <summary>
/// Real-DB integration tests for <see cref="IIdentityModuleClient.GetUserIdsInTenantAsync"/>.
///
/// This is the cross-module seam the Audit module uses to tenant-bind audit.LoginAttempts without
/// a cross-schema subquery. The contract is deliberately MEMBERSHIP-ONLY (no IsActive / soft-delete
/// filter) so the returned set equals a raw <c>WHERE AspNetUsers.TenantId = @tenant</c> — the exact
/// rows the former <c>EXISTS([identity].AspNetUsers …)</c> matched. The IsActive test below locks
/// that invariant: a future "helpful" <c>IsActive=1</c> filter here would silently change which
/// login-audit rows a tenant admin sees.
///
/// Each test uses a unique tenant id to stay isolated under the shared Testcontainers SQL Server.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class IdentityModuleClientTenantUserIdsTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;

    public IdentityModuleClientTenantUserIdsTests(WebAppFixture app)
    {
        _app = app;
    }

    [Fact]
    public async Task GetUserIdsInTenantAsync_ReturnsOnlyTenantMembers_ExcludesOtherTenants()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var userA1 = Guid.NewGuid();
        var userA2 = Guid.NewGuid();
        var userB = Guid.NewGuid();
        await SeedUsersAsync((userA1, tenantA), (userA2, tenantA), (userB, tenantB));

        var ids = await ExecAsync(c => c.GetUserIdsInTenantAsync(tenantA));

        ids.Should().Contain(new[] { userA1, userA2 }, "both TenantA users belong to the set");
        ids.Should().NotContain(userB, "a TenantB user must never appear in a TenantA query");
    }

    [Fact]
    public async Task GetUserIdsInTenantAsync_TenantWithNoUsers_ReturnsEmpty()
    {
        var emptyTenant = Guid.NewGuid();

        var ids = await ExecAsync(c => c.GetUserIdsInTenantAsync(emptyTenant));

        ids.Should().BeEmpty("a tenant with no users resolves to an empty set (caller sees no rows)");
    }

    [Fact]
    public async Task GetUserIdsInTenantAsync_InactiveUser_StillReturned()
    {
        // Membership-only invariant: the set must match a raw TenantId filter regardless of
        // IsActive, so the LoginAudit tenant-bind stays equivalent to the former cross-schema EXISTS.
        var tenant = Guid.NewGuid();
        var activeUser = Guid.NewGuid();
        var inactiveUser = Guid.NewGuid();
        await SeedUsersAsync((activeUser, tenant, true), (inactiveUser, tenant, false));

        var ids = await ExecAsync(c => c.GetUserIdsInTenantAsync(tenant));

        ids.Should().Contain(inactiveUser,
            "membership-only: an inactive user is still a tenant member (no IsActive filter)");
        ids.Should().Contain(activeUser);
    }

    // --- Helpers ---

    private Task<TResult> ExecAsync<TResult>(Func<IIdentityModuleClient, Task<TResult>> action)
    {
        return WithScopeAsync(async sp =>
        {
            var client = sp.GetRequiredService<IIdentityModuleClient>();
            return await action(client);
        });
    }

    private async Task<TResult> WithScopeAsync<TResult>(Func<IServiceProvider, Task<TResult>> action)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    private Task SeedUsersAsync(params (Guid id, Guid tenantId, bool isActive)[] users)
    {
        return WithScopeAsync<bool>(async sp =>
        {
            var db = sp.GetRequiredService<IdentityDbContext>();
            db.Users.AddRange(users.Select(u => BuildUser(u.id, u.tenantId, u.isActive)));
            await db.SaveChangesAsync();
            return true;
        });
    }

    private Task SeedUsersAsync(params (Guid id, Guid tenantId)[] users) =>
        SeedUsersAsync(users.Select(u => (u.id, u.tenantId, true)).ToArray());

    private static ApplicationUser BuildUser(Guid userId, Guid tenantId, bool isActive)
    {
        var email = $"tenant-userids-test.{userId:N}@example.vn";
        return new ApplicationUser
        {
            Id = userId,
            UserName = email,
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            NormalizedUserName = email.ToUpperInvariant(),
            SecurityStamp = Guid.NewGuid().ToString(),
            ConcurrencyStamp = Guid.NewGuid().ToString(),
            FullName = "Tenant-userids test user",
            IsActive = isActive,
            TenantId = tenantId,
        };
    }
}
