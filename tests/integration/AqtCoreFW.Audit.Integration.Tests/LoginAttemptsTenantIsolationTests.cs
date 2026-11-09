using AqtCoreFW.Audit.Application.DTOs;
using AqtCoreFW.Audit.Application.Queries.GetLoginAudit;
using AqtCoreFW.Audit.Domain.Entities;
using AqtCoreFW.Audit.Infrastructure.Persistence;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.Tests.SharedFixtures;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AqtCoreFW.Audit.Integration.Tests;

/// <summary>
/// Integration tests for LoginAttempts cross-tenant isolation (OWASP A01 fix).
///
/// Handler tested:
///   GetLoginAuditQueryHandler — queries audit.LoginAttempts. The table has NO TenantId column
///   and NO RLS policy, so non-privileged callers are scoped by resolving the tenant's user-id set
///   via IIdentityModuleClient.GetUserIdsInTenantAsync and filtering la.UserId against it (the
///   module-client boundary replaces the former cross-schema subquery into identity.AspNetUsers).
///
/// Special case: LoginAttempt.UserId is nullable (unknown email / pre-resolution probes).
///   - Non-privileged: NULL UserId rows excluded (not tenant-attributable).
///   - Privileged (SystemAdmin): NULL UserId rows included (sees all audit).
///
/// Invariant: LoginAttempts has NO TenantId column and NO RLS policy, so queries must bind
/// explicitly via the user→tenant mapping owned by the Identity module.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class LoginAttemptsTenantIsolationTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;

    public LoginAttemptsTenantIsolationTests(WebAppFixture app)
    {
        _app = app;
    }

    [Fact]
    public async Task GetLoginAudit_NonPrivilegedActor_SameTenant_ReturnsAttempts()
    {
        // Arrange: create two users in TenantA, seed login attempts for both.
        var tenantA = Guid.NewGuid();
        var userA1 = Guid.NewGuid();
        var userA2 = Guid.NewGuid();
        await SeedUsersAsync((userA1, tenantA), (userA2, tenantA));
        await SeedLoginAttemptAsync(userA1, "user1@example.vn", success: true);
        await SeedLoginAttemptAsync(userA2, "user2@example.vn", success: false);

        var mediator = _app.Services.GetRequiredService<IMediator>();
        var query = new GetLoginAuditQuery(
            UserId: null,
            From: null,
            To: null,
            Success: null,
            CallerTenantId: tenantA,
            IsPrivileged: false);

        // Act
        var result = await mediator.Send(query);

        // Assert
        result.IsSuccess.Should().BeTrue("Query should succeed");
        var dtos = result.Value.Items;
        dtos.Should().HaveCount(2, "both users' attempts visible to same-tenant caller");
        dtos.Should().AllSatisfy(d =>
            (d.UserId == userA1 || d.UserId == userA2).Should().BeTrue(),
            "all returned attempts belong to tenant users");
        result.Value.TotalCount.Should().Be(2);
    }

    [Fact]
    public async Task GetLoginAudit_NonPrivilegedActor_CrossTenant_ReturnsEmpty()
    {
        // Arrange: create user + login attempts in TenantB; query as non-privileged TenantA actor.
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var userInB = Guid.NewGuid();
        await SeedUsersAsync((userInB, tenantB));
        await SeedLoginAttemptAsync(userInB, "userb@example.vn", success: true);
        await SeedLoginAttemptAsync(userInB, "userb@example.vn", success: false);

        var mediator = _app.Services.GetRequiredService<IMediator>();
        var query = new GetLoginAuditQuery(
            UserId: null,
            From: null,
            To: null,
            Success: null,
            CallerTenantId: tenantA, // Cross-tenant
            IsPrivileged: false);

        // Act
        var result = await mediator.Send(query);

        // Assert
        result.IsSuccess.Should().BeTrue("Query should execute");
        var dtos = result.Value.Items;
        dtos.Should().BeEmpty("non-privileged cross-tenant caller must not see rows");
        result.Value.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task GetLoginAudit_NonPrivilegedActor_ExcludesNullUserId()
    {
        // Arrange: create user + two attempts (one with UserId, one with NULL UserId for pre-resolution probe).
        var tenantA = Guid.NewGuid();
        var userA = Guid.NewGuid();
        await SeedUsersAsync((userA, tenantA));
        await SeedLoginAttemptAsync(userA, "usera@example.vn", success: true);
        await SeedLoginAttemptAsync(null, "unknown@example.vn", success: false); // NULL UserId

        var mediator = _app.Services.GetRequiredService<IMediator>();
        var query = new GetLoginAuditQuery(
            UserId: null,
            From: null,
            To: null,
            Success: null,
            CallerTenantId: tenantA,
            IsPrivileged: false);

        // Act
        var result = await mediator.Send(query);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var dtos = result.Value.Items;
        dtos.Should().HaveCount(1, "NULL UserId excluded for non-privileged (not tenant-attributable)");
        dtos[0].UserId.Should().Be(userA, "only known user appears");
        result.Value.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task GetLoginAudit_PrivilegedActor_SeesAllTenants()
    {
        // Arrange: create users in TenantA and TenantB; seed attempts for both.
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        await SeedUsersAsync((userA, tenantA), (userB, tenantB));
        await SeedLoginAttemptAsync(userA, "usera@example.vn", success: true);
        await SeedLoginAttemptAsync(userB, "userb@example.vn", success: true);

        var mediator = _app.Services.GetRequiredService<IMediator>();
        var query = new GetLoginAuditQuery(
            UserId: null,
            From: null,
            To: null,
            Success: null,
            CallerTenantId: tenantA, // Doesn't matter for privileged
            IsPrivileged: true,
            PageSize: 100);

        // Act
        var result = await mediator.Send(query);

        // Assert
        // Privileged + no filter spans the whole (shared-container) table — other tests in the
        // collection also seed attempts — so assert the seeded cross-tenant rows are PRESENT
        // rather than an exact global count.
        result.IsSuccess.Should().BeTrue();
        var dtos = result.Value.Items;
        dtos.Should().Contain(d => d.UserId == userA, "privileged sees TenantA rows")
            .And.Contain(d => d.UserId == userB, "privileged sees TenantB rows");
    }

    [Fact]
    public async Task GetLoginAudit_PrivilegedActor_IncludesNullUserId()
    {
        // Arrange: create user + attempts including one with NULL UserId.
        var tenantA = Guid.NewGuid();
        var userA = Guid.NewGuid();
        await SeedUsersAsync((userA, tenantA));
        await SeedLoginAttemptAsync(userA, "usera@example.vn", success: true);
        await SeedLoginAttemptAsync(null, "unknown@example.vn", success: false);

        var mediator = _app.Services.GetRequiredService<IMediator>();
        var query = new GetLoginAuditQuery(
            UserId: null,
            From: null,
            To: null,
            Success: null,
            CallerTenantId: tenantA,
            IsPrivileged: true,
            PageSize: 100);

        // Act
        var result = await mediator.Send(query);

        // Assert
        // Privileged + no filter spans the shared container; assert the seeded user's row and a
        // NULL-UserId (non-tenant-attributable) row are PRESENT, not an exact global count.
        result.IsSuccess.Should().BeTrue();
        var dtos = result.Value.Items;
        dtos.Should().Contain(d => d.UserId == userA, "privileged sees the user's attempt")
            .And.Contain(d => d.UserId == null, "privileged includes NULL-UserId rows");
    }

    [Fact]
    public async Task GetLoginAudit_FilterByUserId_NonPrivilegedSameTenant_ReturnsMatching()
    {
        // Arrange: create two users in TenantA; filter for one.
        var tenantA = Guid.NewGuid();
        var userA1 = Guid.NewGuid();
        var userA2 = Guid.NewGuid();
        await SeedUsersAsync((userA1, tenantA), (userA2, tenantA));
        await SeedLoginAttemptAsync(userA1, "user1@example.vn", success: true);
        await SeedLoginAttemptAsync(userA2, "user2@example.vn", success: false);

        var mediator = _app.Services.GetRequiredService<IMediator>();
        var query = new GetLoginAuditQuery(
            UserId: userA1, // Filter for specific user
            From: null,
            To: null,
            Success: null,
            CallerTenantId: tenantA,
            IsPrivileged: false);

        // Act
        var result = await mediator.Send(query);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var dtos = result.Value.Items;
        dtos.Should().HaveCount(1, "only userA1's attempt matches filter");
        dtos[0].UserId.Should().Be(userA1);
        result.Value.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task GetLoginAudit_FilterByUserId_NonPrivilegedCrossTenant_ReturnsEmpty()
    {
        // Arrange: create user in TenantB; non-privileged TenantA actor filters for that user.
        // Even with explicit UserId filter, cross-tenant binding blocks the row.
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var userInB = Guid.NewGuid();
        await SeedUsersAsync((userInB, tenantB));
        await SeedLoginAttemptAsync(userInB, "userb@example.vn", success: true);

        var mediator = _app.Services.GetRequiredService<IMediator>();
        var query = new GetLoginAuditQuery(
            UserId: userInB, // Explicit filter on cross-tenant user
            From: null,
            To: null,
            Success: null,
            CallerTenantId: tenantA,
            IsPrivileged: false);

        // Act
        var result = await mediator.Send(query);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var dtos = result.Value.Items;
        dtos.Should().BeEmpty("tenant-binding prevents cross-tenant leak even with explicit UserId filter");
        result.Value.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task GetLoginAudit_FilterBySuccess_NonPrivilegedSameTenant_ReturnsMatching()
    {
        // Arrange: create user in TenantA with success=true and success=false attempts.
        var tenantA = Guid.NewGuid();
        var userA = Guid.NewGuid();
        await SeedUsersAsync((userA, tenantA));
        await SeedLoginAttemptAsync(userA, "usera@example.vn", success: true);
        await SeedLoginAttemptAsync(userA, "usera@example.vn", success: false);

        var mediator = _app.Services.GetRequiredService<IMediator>();
        var query = new GetLoginAuditQuery(
            UserId: null,
            From: null,
            To: null,
            Success: true, // Filter for successful attempts
            CallerTenantId: tenantA,
            IsPrivileged: false);

        // Act
        var result = await mediator.Send(query);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var dtos = result.Value.Items;
        dtos.Should().HaveCount(1, "only successful attempt matches");
        dtos[0].Success.Should().BeTrue();
        result.Value.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task GetLoginAudit_Pagination_NonPrivilegedSameTenant_PaginatesCorrectly()
    {
        // Arrange: create user in TenantA with multiple attempts; page with size=2.
        var tenantA = Guid.NewGuid();
        var userA = Guid.NewGuid();
        await SeedUsersAsync((userA, tenantA));
        var attempt1 = await SeedLoginAttemptAsync(userA, "usera@example.vn", success: true);
        await Task.Delay(10); // Ensure AttemptedAt differs for stable ORDER BY
        var attempt2 = await SeedLoginAttemptAsync(userA, "usera@example.vn", success: false);
        await Task.Delay(10);
        var attempt3 = await SeedLoginAttemptAsync(userA, "usera@example.vn", success: true);

        var mediator = _app.Services.GetRequiredService<IMediator>();
        var query = new GetLoginAuditQuery(
            UserId: null,
            From: null,
            To: null,
            Success: null,
            CallerTenantId: tenantA,
            IsPrivileged: false,
            Page: 1,
            PageSize: 2);

        // Act
        var result = await mediator.Send(query);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var dtos = result.Value.Items;
        dtos.Should().HaveCount(2, "PageSize=2");
        result.Value.TotalCount.Should().Be(3, "3 total attempts seeded");
        result.Value.Meta.HasNextPage.Should().BeTrue("page 1 has next page");
    }

    [Fact]
    public async Task GetLoginAudit_AdversarialQuery_NonPrivilegedActor_BlocksCrossTenant()
    {
        // Arrange: create users in TenantA and TenantB; non-privileged TenantA actor
        // tries multiple filter combinations to extract TenantB data.
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        await SeedUsersAsync((userA, tenantA), (userB, tenantB));
        await SeedLoginAttemptAsync(userA, "usera@example.vn", success: true);
        await SeedLoginAttemptAsync(userB, "userb@example.vn", success: true);

        var mediator = _app.Services.GetRequiredService<IMediator>();

        // Attempt 1: no filter (should see only TenantA)
        var result1 = await mediator.Send(new GetLoginAuditQuery(
            UserId: null, From: null, To: null, Success: null,
            CallerTenantId: tenantA, IsPrivileged: false));
        result1.Value.Items.Should().HaveCount(1);
        result1.Value.Items[0].UserId.Should().Be(userA);

        // Attempt 2: explicit filter on TenantB user (should return empty)
        var result2 = await mediator.Send(new GetLoginAuditQuery(
            UserId: userB, From: null, To: null, Success: null,
            CallerTenantId: tenantA, IsPrivileged: false));
        result2.Value.Items.Should().BeEmpty("tenant-binding blocks explicit cross-tenant filter");

        // Attempt 3: filter by email (search term) from TenantB user
        // Handler implements server-side LIKE on Email; still bound by tenant
        var result3 = await mediator.Send(new GetLoginAuditQuery(
            UserId: null, From: null, To: null, Success: null,
            CallerTenantId: tenantA, IsPrivileged: false, SearchTerm: "userb@example.vn"));
        result3.Value.Items.Should().BeEmpty("tenant-binding blocks search-term filter crossing tenant");
    }

    // --- Helpers ---

    private async Task SeedUsersAsync(params (Guid id, Guid? tenantId)[] users)
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        db.Users.AddRange(users.Select(u => BuildUser(u.id, u.tenantId)));
        await db.SaveChangesAsync();
    }

    /// <summary>Seed a LoginAttempt and return its MessageId (Guid AK).</summary>
    private async Task<Guid> SeedLoginAttemptAsync(
        Guid? userId, string email, bool success, string? failureReason = null)
    {
        using var scope = _app.Services.CreateScope();
        var guidGen = scope.ServiceProvider.GetRequiredService<IGuidGenerator>();
        var auditDb = scope.ServiceProvider.GetRequiredService<AuditDbContext>();

        var attempt = LoginAttempt.Record(
            guidGen, userId, email, "192.168.1.1", userAgent: null, success, failureReason);

        auditDb.LoginAttempts.Add(attempt);
        await auditDb.SaveChangesAsync();

        return attempt.MessageId;
    }

    private static ApplicationUser BuildUser(Guid userId, Guid? tenantId)
    {
        var email = $"login-audit-test.{userId:N}@example.vn";
        return new ApplicationUser
        {
            Id = userId,
            UserName = email,
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            NormalizedUserName = email.ToUpperInvariant(),
            SecurityStamp = Guid.NewGuid().ToString(),
            ConcurrencyStamp = Guid.NewGuid().ToString(),
            FullName = "Login-Audit test user",
            IsActive = true,
            TenantId = tenantId,
        };
    }
}
