using AqtCoreFW.Identity.Application.Commands.CreateExternalIdentity;
using AqtCoreFW.Identity.Application.Commands.DeleteExternalIdentity;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Errors;
using AqtCoreFW.Tests.SharedFixtures;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AqtCoreFW.Identity.Integration.Tests;

/// <summary>
/// Integration tests for ExternalIdentity re-link-after-unlink. The two unique indexes
/// (UserId,Provider) and (Provider,ExternalId) are partial (<c>WHERE [IsDeleted] = 0</c>), so a
/// soft-deleted (unlinked) link no longer occupies the unique slot — re-linking the same provider /
/// external account after an unlink must succeed instead of throwing a duplicate-key error. Uniqueness
/// among ACTIVE rows is still enforced (no two live links for the same key).
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class ExternalIdentityRelinkAfterUnlinkTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;

    public ExternalIdentityRelinkAfterUnlinkTests(WebAppFixture app) => _app = app;

    [Fact]
    public async Task Relink_SameProviderAndExternalId_AfterUnlink_Succeeds()
    {
        // Arrange: user with one OAuth link, then unlink (soft-delete) it.
        var tenant = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await SeedUserAsync(userId, tenant);
        var externalId = $"oauth.{Guid.NewGuid():N}";
        var mediator = _app.Services.GetRequiredService<IMediator>();

        var first = await mediator.Send(CreateCmd(userId, externalId, tenant));
        first.IsSuccess.Should().BeTrue();
        var firstLinkId = first.Value;

        var unlink = await mediator.Send(new DeleteExternalIdentityCommand(
            Id: firstLinkId, ActorId: Guid.NewGuid(), CallerTenantId: tenant, IsPrivileged: false));
        unlink.IsSuccess.Should().BeTrue();

        // Act: re-link the SAME provider + external id (hits both partial unique indexes).
        var relink = await mediator.Send(CreateCmd(userId, externalId, tenant));

        // Assert: re-link succeeds (was a duplicate-key 500 before the partial filter); exactly one
        // ACTIVE link remains, and the original is soft-deleted.
        relink.IsSuccess.Should().BeTrue("a soft-deleted link must not block re-linking the same provider");
        relink.Value.Should().NotBe(firstLinkId, "re-link is a new row, not a revival of the deleted one");
        (await CountActiveLinksAsync(userId)).Should().Be(1);
        (await IsDeletedAsync(firstLinkId)).Should().BeTrue();
    }

    [Fact]
    public async Task SecondActiveLink_SameProvider_WithoutUnlink_IsBlocked()
    {
        // Two live links for the same (user, provider) must still be rejected (dup-check).
        var tenant = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await SeedUserAsync(userId, tenant);
        var mediator = _app.Services.GetRequiredService<IMediator>();

        var first = await mediator.Send(CreateCmd(userId, $"oauth.{Guid.NewGuid():N}", tenant));
        first.IsSuccess.Should().BeTrue();

        var second = await mediator.Send(CreateCmd(userId, $"oauth.{Guid.NewGuid():N}", tenant));

        second.IsSuccess.Should().BeFalse("a second active link for the same provider is not allowed");
        second.Errors[0].Should().BeOfType<ConflictError>();
        (await CountActiveLinksAsync(userId)).Should().Be(1);
    }

    [Fact]
    public async Task PartialUniqueIndex_AllowsActiveAlongsideSoftDeleted_RejectsTwoActive()
    {
        // DB-level proof of the partial unique index, independent of the handler dup-check.
        var tenant = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await SeedUserAsync(userId, tenant);
        var externalId = $"ldap.{Guid.NewGuid():N}";

        // Seed one link for (user, LDAP, externalId) and soft-delete it.
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var seeded = ExternalIdentity.Create(userId, ExternalIdentityProvider.LDAP, externalId, "n", "e@x.vn", userId);
            db.ExternalIdentities.Add(seeded);
            await db.SaveChangesAsync();
            seeded.Delete();
            await db.SaveChangesAsync();
        }

        // An ACTIVE row with the same key inserts fine (partial index excludes the soft-deleted slot).
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            db.ExternalIdentities.Add(
                ExternalIdentity.Create(userId, ExternalIdentityProvider.LDAP, externalId, "n", "e@x.vn", userId));
            var act = async () => await db.SaveChangesAsync();
            await act.Should().NotThrowAsync("an active row may coexist with a soft-deleted one for the same key");
        }

        // A SECOND active row with the same key violates the partial unique index.
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            db.ExternalIdentities.Add(
                ExternalIdentity.Create(userId, ExternalIdentityProvider.LDAP, externalId, "n", "e@x.vn", userId));
            var act = async () => await db.SaveChangesAsync();
            await act.Should().ThrowAsync<DbUpdateException>("two active rows for the same key must be rejected");
        }
    }

    // --- Helpers ---

    private static CreateExternalIdentityCommand CreateCmd(Guid userId, string externalId, Guid tenant) =>
        new(UserId: userId, Provider: ExternalIdentityProvider.OAuth, ExternalId: externalId,
            DisplayName: "Test", Email: "test@example.vn",
            ActorId: Guid.NewGuid(), CallerTenantId: tenant, IsPrivileged: false);

    private async Task SeedUserAsync(Guid userId, Guid tenantId)
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var email = $"extid-relink-test.{userId:N}@example.vn";
        db.Users.Add(new ApplicationUser
        {
            Id = userId,
            UserName = email,
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            NormalizedUserName = email.ToUpperInvariant(),
            SecurityStamp = Guid.NewGuid().ToString(),
            ConcurrencyStamp = Guid.NewGuid().ToString(),
            FullName = "External-ID relink test user",
            IsActive = true,
            TenantId = tenantId,
        });
        await db.SaveChangesAsync();
    }

    private async Task<int> CountActiveLinksAsync(Guid userId)
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        return await db.ExternalIdentities.CountAsync(e => e.UserId == userId && !e.IsDeleted);
    }

    private async Task<bool> IsDeletedAsync(long id)
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var entity = await db.ExternalIdentities.IgnoreQueryFilters().AsNoTracking().FirstAsync(e => e.Id == id);
        return entity.IsDeleted;
    }
}
