using AqtCoreFW.Identity.Application.DTOs;
using AqtCoreFW.Identity.Application.Queries.GetExternalIdentityById;
using AqtCoreFW.Identity.Application.Queries.ListExternalIdentities;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Errors;
using AqtCoreFW.Tests.SharedFixtures;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AqtCoreFW.Identity.Integration.Tests;

/// <summary>
/// Integration tests for ExternalIdentities cross-tenant isolation (OWASP A01 fix).
///
/// Two handlers tested:
///   1. ListExternalIdentitiesQueryHandler — lists a user's linked external IDs; Dapper query adds
///      tenant scoping via EXISTS (SELECT 1 FROM AspNetUsers WHERE Id=UserId AND TenantId=@CallerTenantId).
///   2. GetExternalIdentityByIdQueryHandler — loads by sequential bigint Id; non-privileged callers
///      validated to confirm the UserId's tenant matches CallerTenantId; cross-tenant hits return
///      NotFound (never Forbidden, to avoid leaking record existence).
///
/// Invariant: tables ExternalIdentities and LoginAttempts have NO TenantId column and NO RLS policy,
/// so queries must explicitly bind via AspNetUsers FK + session tenant context.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class ExternalIdentitiesTenantIsolationTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;

    public ExternalIdentitiesTenantIsolationTests(WebAppFixture app)
    {
        _app = app;
    }

    // --- ListExternalIdentitiesQueryHandler tests ---

    [Fact]
    public async Task ListExternalIdentities_NonPrivilegedActor_SameTenanAsUserOwner_ReturnsRows()
    {
        // Arrange: create user + externalId in TenantA; list as non-privileged actor in TenantA.
        var tenantA = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await SeedUsersAsync((userId, tenantA));
        var linkId = await SeedExternalIdentityAsync(userId, "google");

        var mediator = _app.Services.GetRequiredService<IMediator>();
        var query = new ListExternalIdentitiesQuery(
            UserId: userId,
            CallerTenantId: tenantA,
            IsPrivileged: false,
            Page: 1,
            PageSize: 20);

        // Act
        var result = await mediator.Send(query);

        // Assert
        result.IsSuccess.Should().BeTrue("Query should succeed");
        var dtos = result.Value.Items;
        dtos.Should().HaveCount(1, "exactly one external ID seeded");
        dtos[0].Id.Should().Be(linkId);
        dtos[0].UserId.Should().Be(userId);
        dtos[0].Provider.Should().Be(ExternalIdentityProvider.OAuth);
        result.Value.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task ListExternalIdentities_NonPrivilegedActor_CrossTenant_ReturnsEmpty()
    {
        // Arrange: create user + externalId in TenantB; list as non-privileged actor in TenantA.
        // The query should return 0 rows because the user's tenant doesn't match CallerTenantId.
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var userInB = Guid.NewGuid();
        await SeedUsersAsync((userInB, tenantB));
        var linkId = await SeedExternalIdentityAsync(userInB, "google");

        var mediator = _app.Services.GetRequiredService<IMediator>();
        var query = new ListExternalIdentitiesQuery(
            UserId: userInB,
            CallerTenantId: tenantA, // Cross-tenant caller
            IsPrivileged: false,
            Page: 1,
            PageSize: 20);

        // Act
        var result = await mediator.Send(query);

        // Assert
        result.IsSuccess.Should().BeTrue("Query should succeed");
        var dtos = result.Value.Items;
        dtos.Should().BeEmpty("non-privileged cross-tenant caller must not see rows");
        result.Value.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task ListExternalIdentities_PrivilegedActor_SeesAllTenants()
    {
        // Arrange: create users in TenantA and TenantB, seed externalIds for both.
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var userInA = Guid.NewGuid();
        var userInB = Guid.NewGuid();
        await SeedUsersAsync((userInA, tenantA), (userInB, tenantB));
        var linkA = await SeedExternalIdentityAsync(userInA, "google");
        var linkB = await SeedExternalIdentityAsync(userInB, "ldap");

        var mediator = _app.Services.GetRequiredService<IMediator>();
        // Privileged caller querying userInB from anywhere (CallerTenantId is ignored for privileged).
        var query = new ListExternalIdentitiesQuery(
            UserId: userInB,
            CallerTenantId: tenantA, // Doesn't matter for privileged
            IsPrivileged: true,
            Page: 1,
            PageSize: 20);

        // Act
        var result = await mediator.Send(query);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var dtos = result.Value.Items;
        dtos.Should().HaveCount(1, "query filters on UserId, so only userInB's links appear");
        dtos[0].Id.Should().Be(linkB);
        result.Value.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task ListExternalIdentities_ExcludesDeletedRows()
    {
        // Arrange: create user in TenantA, seed two externalIds, soft-delete one.
        var tenantA = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await SeedUsersAsync((userId, tenantA));
        var liveLink = await SeedExternalIdentityAsync(userId, "google");
        var deletedLink = await SeedExternalIdentityAsync(userId, "ldap", isDeleted: true);

        var mediator = _app.Services.GetRequiredService<IMediator>();
        var query = new ListExternalIdentitiesQuery(
            UserId: userId,
            CallerTenantId: tenantA,
            IsPrivileged: false,
            Page: 1,
            PageSize: 20);

        // Act
        var result = await mediator.Send(query);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var dtos = result.Value.Items;
        dtos.Should().HaveCount(1, "soft-deleted row excluded");
        dtos[0].Id.Should().Be(liveLink);
        result.Value.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task ListExternalIdentities_Pagination_CorrectMeta()
    {
        // Arrange: the query filters on UserId (cannot paginate across different users).
        // Since ExternalIdentity has unique constraint on (UserId, Provider),
        // max 5 external identities per user. Test pagination META is correct.
        var tenantA = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await SeedUsersAsync((userId, tenantA));
        // Create links with distinct providers (unique constraint allows 5 max)
        var link1 = await SeedExternalIdentityAsync(userId, "google");
        await Task.Delay(10);
        var link2 = await SeedExternalIdentityAsync(userId, "ldap");
        await Task.Delay(10);
        var link3 = await SeedExternalIdentityAsync(userId, "vneid");

        var mediator = _app.Services.GetRequiredService<IMediator>();
        var query = new ListExternalIdentitiesQuery(
            UserId: userId,
            CallerTenantId: tenantA,
            IsPrivileged: false,
            Page: 1,
            PageSize: 2);

        // Act
        var result = await mediator.Send(query);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var dtos = result.Value.Items;
        dtos.Should().HaveCount(2, "PageSize=2, 3 total items");
        result.Value.TotalCount.Should().Be(3, "3 total links for this user");
        result.Value.Meta.HasNextPage.Should().BeTrue("page 1 of 2");
        result.Value.Meta.Page.Should().Be(1);
        result.Value.Meta.PageSize.Should().Be(2);
    }

    // --- GetExternalIdentityByIdQueryHandler tests ---

    [Fact]
    public async Task GetExternalIdentityById_NonPrivilegedActor_SameTenant_ReturnsRecord()
    {
        // Arrange: create user + externalId in TenantA; retrieve as non-privileged TenantA actor.
        var tenantA = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await SeedUsersAsync((userId, tenantA));
        var linkId = await SeedExternalIdentityAsync(userId, "google");

        var mediator = _app.Services.GetRequiredService<IMediator>();
        var query = new GetExternalIdentityByIdQuery(
            Id: linkId,
            CallerTenantId: tenantA,
            IsPrivileged: false);

        // Act
        var result = await mediator.Send(query);

        // Assert
        result.IsSuccess.Should().BeTrue("same-tenant access should succeed");
        var dto = result.Value;
        dto.Id.Should().Be(linkId);
        dto.UserId.Should().Be(userId);
        dto.Provider.Should().Be(ExternalIdentityProvider.OAuth);
    }

    [Fact]
    public async Task GetExternalIdentityById_NonPrivilegedActor_CrossTenant_ReturnsNotFound()
    {
        // Arrange: create user + externalId in TenantB; try to retrieve as non-privileged TenantA actor.
        // Handler must return NotFound (not Forbidden) to avoid leaking existence.
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var userInB = Guid.NewGuid();
        await SeedUsersAsync((userInB, tenantB));
        var linkId = await SeedExternalIdentityAsync(userInB, "google");

        var mediator = _app.Services.GetRequiredService<IMediator>();
        var query = new GetExternalIdentityByIdQuery(
            Id: linkId,
            CallerTenantId: tenantA, // Cross-tenant
            IsPrivileged: false);

        // Act
        var result = await mediator.Send(query);

        // Assert
        result.IsSuccess.Should().BeFalse("cross-tenant access should fail");
        result.Errors.Should().HaveCount(1);
        result.Errors[0].Should().BeOfType<NotFoundError>("must not leak record existence");
        result.Errors[0].Message.Should().Contain("not found");
    }

    [Fact]
    public async Task GetExternalIdentityById_PrivilegedActor_SeesAllTenants()
    {
        // Arrange: create user in TenantB, seed externalId; retrieve as privileged actor.
        var tenantB = Guid.NewGuid();
        var userInB = Guid.NewGuid();
        await SeedUsersAsync((userInB, tenantB));
        var linkId = await SeedExternalIdentityAsync(userInB, "ldap");

        var mediator = _app.Services.GetRequiredService<IMediator>();
        var query = new GetExternalIdentityByIdQuery(
            Id: linkId,
            CallerTenantId: Guid.NewGuid(), // Arbitrary; privileged bypasses scoping
            IsPrivileged: true);

        // Act
        var result = await mediator.Send(query);

        // Assert
        result.IsSuccess.Should().BeTrue("privileged access bypasses tenant scoping");
        var dto = result.Value;
        dto.Id.Should().Be(linkId);
        dto.UserId.Should().Be(userInB);
    }

    [Fact]
    public async Task GetExternalIdentityById_AdversarialEnumeration_NonPrivilegedCrossTenant_ReturnsNotFound()
    {
        // Arrange: sequentially create externalIds in TenantB; non-privileged TenantA actor
        // tries to enumerate by incrementing Id. All should return NotFound (never leaking
        // the record exists in a different tenant).
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var userInB = Guid.NewGuid();
        await SeedUsersAsync((userInB, tenantB));
        var link1 = await SeedExternalIdentityAsync(userInB, "google");
        var link2 = await SeedExternalIdentityAsync(userInB, "ldap");

        var mediator = _app.Services.GetRequiredService<IMediator>();

        // Act & Assert: both link1 and link2 should return NotFound for cross-tenant caller
        var result1 = await mediator.Send(new GetExternalIdentityByIdQuery(
            Id: link1,
            CallerTenantId: tenantA,
            IsPrivileged: false));
        result1.IsSuccess.Should().BeFalse("cross-tenant enumeration blocked");
        result1.Errors[0].Should().BeOfType<NotFoundError>();

        var result2 = await mediator.Send(new GetExternalIdentityByIdQuery(
            Id: link2,
            CallerTenantId: tenantA,
            IsPrivileged: false));
        result2.IsSuccess.Should().BeFalse("enumeration of subsequent Ids also blocked");
        result2.Errors[0].Should().BeOfType<NotFoundError>();
    }

    [Fact]
    public async Task GetExternalIdentityById_NonExistentId_ReturnsNotFound()
    {
        // Arrange: no externalId seeded; try to retrieve a non-existent Id.
        var tenantA = Guid.NewGuid();
        var mediator = _app.Services.GetRequiredService<IMediator>();
        var query = new GetExternalIdentityByIdQuery(
            Id: 99999L, // Non-existent
            CallerTenantId: tenantA,
            IsPrivileged: false);

        // Act
        var result = await mediator.Send(query);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Errors[0].Should().BeOfType<NotFoundError>();
    }

    // --- Helpers ---

    private async Task SeedUsersAsync(params (Guid id, Guid? tenantId)[] users)
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        db.Users.AddRange(users.Select(u => BuildUser(u.id, u.tenantId)));
        await db.SaveChangesAsync();
    }

    /// <summary>Seed an ExternalIdentity and return its database Id (bigint IDENTITY).</summary>
    private async Task<long> SeedExternalIdentityAsync(
        Guid userId, string provider, bool isDeleted = false)
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var guidGen = scope.ServiceProvider.GetRequiredService<IGuidGenerator>();

        var entity = ExternalIdentity.Create(
            userId,
            ParseProviderEnum(provider),
            externalId: $"{provider.ToLower()}.{Guid.NewGuid():N}",
            displayName: $"Test {provider} User",
            email: $"test.{provider}@example.vn",
            createdBy: Guid.NewGuid());

        if (isDeleted)
        {
            entity.MarkDeleted();
        }

        db.ExternalIdentities.Add(entity);
        await db.SaveChangesAsync();

        // Return the auto-generated bigint Id
        return entity.Id;
    }

    private static ExternalIdentityProvider ParseProviderEnum(string provider) =>
        provider.ToLowerInvariant() switch
        {
            "google" or "oauth" => ExternalIdentityProvider.OAuth,
            "ldap" => ExternalIdentityProvider.LDAP,
            "vneid" => ExternalIdentityProvider.VNeID,
            "saml" => ExternalIdentityProvider.SAML,
            _ => ExternalIdentityProvider.SSO
        };

    private static ApplicationUser BuildUser(Guid userId, Guid? tenantId)
    {
        var email = $"external-id-test.{userId:N}@example.vn";
        return new ApplicationUser
        {
            Id = userId,
            UserName = email,
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            NormalizedUserName = email.ToUpperInvariant(),
            SecurityStamp = Guid.NewGuid().ToString(),
            ConcurrencyStamp = Guid.NewGuid().ToString(),
            FullName = "External-ID test user",
            IsActive = true,
            TenantId = tenantId,
        };
    }
}
