using AqtCoreFW.Identity.Application.Commands.CreateExternalIdentity;
using AqtCoreFW.Identity.Application.Commands.DeleteExternalIdentity;
using AqtCoreFW.Identity.Application.Commands.UpdateExternalIdentity;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Errors;
using AqtCoreFW.Tests.SharedFixtures;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AqtCoreFW.Identity.Integration.Tests;

/// <summary>
/// Integration tests for ExternalIdentities cross-tenant WRITE isolation (OWASP A01 — tampering).
///
/// The three write handlers (Create/Update/Delete) operate by Guid UserId (Create) or sequential
/// bigint Id (Update/Delete) on a table with NO TenantId column / RLS and NO EF global filter, so a
/// non-SystemAdmin caller could otherwise mutate identities belonging to other tenants' users. Each
/// handler binds to the owning user's tenant and returns NotFound (never Forbidden) on a cross-tenant
/// hit so record/user existence is not leaked; SystemAdmin (IsPrivileged) bypasses the bind.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class ExternalIdentitiesWriteTenantIsolationTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;

    public ExternalIdentitiesWriteTenantIsolationTests(WebAppFixture app) => _app = app;

    // --- Create ---

    [Fact]
    public async Task Create_NonPrivileged_CrossTenantUser_ReturnsNotFound_NoLink()
    {
        // Arrange: target user lives in TenantB; caller is a non-privileged actor in TenantA.
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var userInB = Guid.NewGuid();
        await SeedUsersAsync((userInB, tenantB));

        var mediator = _app.Services.GetRequiredService<IMediator>();
        var result = await mediator.Send(new CreateExternalIdentityCommand(
            UserId: userInB, Provider: ExternalIdentityProvider.OAuth,
            ExternalId: $"oauth.{Guid.NewGuid():N}", DisplayName: "x", Email: "x@example.vn",
            ActorId: Guid.NewGuid(), CallerTenantId: tenantA, IsPrivileged: false));

        // Assert: NotFound, and no link was created for the cross-tenant user.
        result.IsSuccess.Should().BeFalse("non-privileged caller must not link across tenants");
        result.Errors[0].Should().BeOfType<NotFoundError>("must not leak cross-tenant user existence");
        (await CountLinksAsync(userInB)).Should().Be(0);
    }

    [Fact]
    public async Task Create_NonPrivileged_SameTenant_Succeeds()
    {
        var tenantA = Guid.NewGuid();
        var userInA = Guid.NewGuid();
        await SeedUsersAsync((userInA, tenantA));

        var mediator = _app.Services.GetRequiredService<IMediator>();
        var result = await mediator.Send(new CreateExternalIdentityCommand(
            UserId: userInA, Provider: ExternalIdentityProvider.OAuth,
            ExternalId: $"oauth.{Guid.NewGuid():N}", DisplayName: "x", Email: "x@example.vn",
            ActorId: Guid.NewGuid(), CallerTenantId: tenantA, IsPrivileged: false));

        result.IsSuccess.Should().BeTrue("same-tenant link is allowed");
        (await CountLinksAsync(userInA)).Should().Be(1);
    }

    [Fact]
    public async Task Create_Privileged_CrossTenantUser_Succeeds()
    {
        var tenantB = Guid.NewGuid();
        var userInB = Guid.NewGuid();
        await SeedUsersAsync((userInB, tenantB));

        var mediator = _app.Services.GetRequiredService<IMediator>();
        var result = await mediator.Send(new CreateExternalIdentityCommand(
            UserId: userInB, Provider: ExternalIdentityProvider.LDAP,
            ExternalId: $"ldap.{Guid.NewGuid():N}", DisplayName: "x", Email: "x@example.vn",
            ActorId: Guid.NewGuid(), CallerTenantId: Guid.NewGuid(), IsPrivileged: true));

        result.IsSuccess.Should().BeTrue("SystemAdmin may link across tenants");
        (await CountLinksAsync(userInB)).Should().Be(1);
    }

    // --- Update ---

    [Fact]
    public async Task Update_NonPrivileged_CrossTenant_ReturnsNotFound_NoMutation()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var userInB = Guid.NewGuid();
        await SeedUsersAsync((userInB, tenantB));
        var linkId = await SeedExternalIdentityAsync(userInB, "google", displayName: "Original");

        var mediator = _app.Services.GetRequiredService<IMediator>();
        var result = await mediator.Send(new UpdateExternalIdentityCommand(
            Id: linkId, DisplayName: "Tampered", Email: "evil@example.vn", Metadata: null,
            ActorId: Guid.NewGuid(), CallerTenantId: tenantA, IsPrivileged: false));

        result.IsSuccess.Should().BeFalse("cross-tenant update must fail");
        result.Errors[0].Should().BeOfType<NotFoundError>();
        (await GetDisplayNameAsync(linkId)).Should().Be("Original", "no mutation should be persisted");
    }

    [Fact]
    public async Task Update_NonPrivileged_SameTenant_Succeeds()
    {
        var tenantA = Guid.NewGuid();
        var userInA = Guid.NewGuid();
        await SeedUsersAsync((userInA, tenantA));
        var linkId = await SeedExternalIdentityAsync(userInA, "google", displayName: "Original");

        var mediator = _app.Services.GetRequiredService<IMediator>();
        var result = await mediator.Send(new UpdateExternalIdentityCommand(
            Id: linkId, DisplayName: "Renamed", Email: "ok@example.vn", Metadata: null,
            ActorId: Guid.NewGuid(), CallerTenantId: tenantA, IsPrivileged: false));

        result.IsSuccess.Should().BeTrue();
        (await GetDisplayNameAsync(linkId)).Should().Be("Renamed");
    }

    [Fact]
    public async Task Update_Privileged_CrossTenant_Succeeds()
    {
        var tenantB = Guid.NewGuid();
        var userInB = Guid.NewGuid();
        await SeedUsersAsync((userInB, tenantB));
        var linkId = await SeedExternalIdentityAsync(userInB, "google", displayName: "Original");

        var mediator = _app.Services.GetRequiredService<IMediator>();
        var result = await mediator.Send(new UpdateExternalIdentityCommand(
            Id: linkId, DisplayName: "AdminRenamed", Email: "ok@example.vn", Metadata: null,
            ActorId: Guid.NewGuid(), CallerTenantId: Guid.NewGuid(), IsPrivileged: true));

        result.IsSuccess.Should().BeTrue("SystemAdmin bypasses the tenant bind");
        (await GetDisplayNameAsync(linkId)).Should().Be("AdminRenamed");
    }

    // --- Delete ---

    [Fact]
    public async Task Delete_NonPrivileged_CrossTenant_ReturnsNotFound_NoDeletion()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var userInB = Guid.NewGuid();
        await SeedUsersAsync((userInB, tenantB));
        var linkId = await SeedExternalIdentityAsync(userInB, "google");

        var mediator = _app.Services.GetRequiredService<IMediator>();
        var result = await mediator.Send(new DeleteExternalIdentityCommand(
            Id: linkId, ActorId: Guid.NewGuid(), CallerTenantId: tenantA, IsPrivileged: false));

        result.IsSuccess.Should().BeFalse("cross-tenant delete must fail");
        result.Errors[0].Should().BeOfType<NotFoundError>();
        (await IsDeletedAsync(linkId)).Should().BeFalse("the victim's link must remain intact");
    }

    [Fact]
    public async Task Delete_NonPrivileged_SameTenant_Succeeds()
    {
        var tenantA = Guid.NewGuid();
        var userInA = Guid.NewGuid();
        await SeedUsersAsync((userInA, tenantA));
        var linkId = await SeedExternalIdentityAsync(userInA, "google");

        var mediator = _app.Services.GetRequiredService<IMediator>();
        var result = await mediator.Send(new DeleteExternalIdentityCommand(
            Id: linkId, ActorId: Guid.NewGuid(), CallerTenantId: tenantA, IsPrivileged: false));

        result.IsSuccess.Should().BeTrue();
        (await IsDeletedAsync(linkId)).Should().BeTrue();
    }

    [Fact]
    public async Task Delete_Privileged_CrossTenant_Succeeds()
    {
        var tenantB = Guid.NewGuid();
        var userInB = Guid.NewGuid();
        await SeedUsersAsync((userInB, tenantB));
        var linkId = await SeedExternalIdentityAsync(userInB, "google");

        var mediator = _app.Services.GetRequiredService<IMediator>();
        var result = await mediator.Send(new DeleteExternalIdentityCommand(
            Id: linkId, ActorId: Guid.NewGuid(), CallerTenantId: Guid.NewGuid(), IsPrivileged: true));

        result.IsSuccess.Should().BeTrue("SystemAdmin bypasses the tenant bind");
        (await IsDeletedAsync(linkId)).Should().BeTrue();
    }

    // --- Helpers ---

    private async Task SeedUsersAsync(params (Guid id, Guid? tenantId)[] users)
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        db.Users.AddRange(users.Select(u => BuildUser(u.id, u.tenantId)));
        await db.SaveChangesAsync();
    }

    private async Task<long> SeedExternalIdentityAsync(
        Guid userId, string provider, string? displayName = null)
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var entity = ExternalIdentity.Create(
            userId, ParseProviderEnum(provider),
            externalId: $"{provider.ToLowerInvariant()}.{Guid.NewGuid():N}",
            displayName: displayName ?? $"Test {provider} User",
            email: $"test.{provider}@example.vn",
            createdBy: Guid.NewGuid());
        db.ExternalIdentities.Add(entity);
        await db.SaveChangesAsync();
        return entity.Id;
    }

    private async Task<int> CountLinksAsync(Guid userId)
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        return await db.ExternalIdentities.CountAsync(e => e.UserId == userId && !e.IsDeleted);
    }

    private async Task<string?> GetDisplayNameAsync(long id)
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var entity = await db.ExternalIdentities.AsNoTracking().FirstAsync(e => e.Id == id);
        return entity.DisplayName;
    }

    private async Task<bool> IsDeletedAsync(long id)
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var entity = await db.ExternalIdentities.IgnoreQueryFilters().AsNoTracking().FirstAsync(e => e.Id == id);
        return entity.IsDeleted;
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
        var email = $"extid-write-test.{userId:N}@example.vn";
        return new ApplicationUser
        {
            Id = userId,
            UserName = email,
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            NormalizedUserName = email.ToUpperInvariant(),
            SecurityStamp = Guid.NewGuid().ToString(),
            ConcurrencyStamp = Guid.NewGuid().ToString(),
            FullName = "External-ID write test user",
            IsActive = true,
            TenantId = tenantId,
        };
    }
}
