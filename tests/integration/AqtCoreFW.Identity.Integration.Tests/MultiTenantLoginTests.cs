using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using AqtCoreFW.Identity.Infrastructure.Services;
using AqtCoreFW.Tests.SharedFixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AqtCoreFW.Identity.Integration.Tests;

/// <summary>
/// Integration tests proving that tenanted users can log in after Fixup P0-1 (two-column hash split).
///
/// Regression guard for the P0-1 bug:
///   OLD interceptor used user.TenantId as pepper → FindByEmailAsync (Guid.Empty pepper) mismatch → 401.
///   FIXED: interceptor always writes EmailHashGlobal with Guid.Empty; FindByEmailAsync uses Guid.Empty.
///
/// TestPepperResolver perturbs bytes for non-empty tenantId so global ≠ tenant hash.
/// This means: if the interceptor accidentally wrote tenant-pepper bytes into the Global column,
/// FindByEmailAsync would return null → test fails → regression caught.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class MultiTenantLoginTests : IClassFixture<WebAppFixture>
{
    private static readonly Guid TestTenantId = Guid.Parse("a1b2c3d4-0000-0000-0000-000000b7c100");

    private readonly WebAppFixture _app;

    public MultiTenantLoginTests(WebAppFixture app)
    {
        _app = app;
    }

    // ── Test 1: Tenanted user can be found via FindByEmailAsync (global pepper path) ─────────

    [Fact]
    public async Task TenantedUser_CanBeFoundByEmail_ViaGlobalHash()
    {
        // Arrange: create a tenanted user via EF (triggers EmailHashInterceptor on SaveChanges)
        var email = $"mt.login.{Guid.NewGuid():N}@example.vn";
        var userId = await SeedTenantedUserAsync(email, TestTenantId);

        // Act: call FindByEmailAsync — this is the login lookup path
        using var scope = _app.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<IdentityDbContext>();
        var emailHashService = sp.GetRequiredService<EmailHashService>();
        var store = sp.GetRequiredService<Microsoft.AspNetCore.Identity.IUserStore<ApplicationUser>>();

        // IUserStore.FindByNameAsync is what UserManager calls internally after normalization.
        // Cast to the EmailHashedUserStore directly to call FindByEmailAsync.
        var emailStore = (EmailHashedUserStore)store;
        var normalizedEmail = email.ToUpperInvariant();
        var found = await emailStore.FindByEmailAsync(normalizedEmail, CancellationToken.None);

        // Assert: user IS found (not null) — proves global hash write matches global hash read
        found.Should().NotBeNull(
            because: "tenanted user written with Guid.Empty pepper must be findable by FindByEmailAsync (Guid.Empty pepper)");
        found!.Id.Should().Be(userId);
    }

    // ── Test 2: Both global and tenant hash columns are populated for tenanted user ──────────

    [Fact]
    public async Task TenantedUser_HasBothGlobalAndTenantHashColumns_Populated()
    {
        // Arrange
        var email = $"mt.both.{Guid.NewGuid():N}@example.vn";
        var userId = await SeedTenantedUserAsync(email, TestTenantId);

        // Assert: load raw columns and verify both are non-null
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var user = await db.Users.AsNoTracking().FirstAsync(u => u.Id == userId);

        user.NormalizedEmailHashGlobal.Should().NotBeNull(
            because: "global hash must be populated by EmailHashInterceptor");
        user.EmailHashGlobal.Should().NotBeNull(
            because: "global email hash must be populated");
        user.NormalizedEmailHashTenant.Should().NotBeNull(
            because: "tenant hash must be populated for user with non-null TenantId");
        user.EmailHashTenant.Should().NotBeNull(
            because: "tenant email hash must be populated");
    }

    // ── Test 3: Global hash differs from tenant hash (pepper perturbation proof) ────────────

    [Fact]
    public async Task TenantedUser_GlobalHashDiffersFromTenantHash()
    {
        // This test proves that TestPepperResolver correctly perturbs tenant pepper bytes.
        // If global == tenant, the perturbation isn't working and the suite would be blind
        // to a P0-1 re-regression where both use the same pepper accidentally.
        var email = $"mt.diff.{Guid.NewGuid():N}@example.vn";
        var userId = await SeedTenantedUserAsync(email, TestTenantId);

        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var user = await db.Users.AsNoTracking().FirstAsync(u => u.Id == userId);

        user.NormalizedEmailHashGlobal.Should().NotBe(user.NormalizedEmailHashTenant,
            because: "TestPepperResolver perturbs tenant pepper bytes; global (Guid.Empty) and " +
                     "tenant peppers must produce different hashes for the same email");
    }

    // ── Test 4: Non-tenanted user (TenantId = null) has null tenant hash columns ─────────────

    [Fact]
    public async Task NonTenantedUser_TenantHashColumns_AreNull()
    {
        // Non-tenanted users (TenantId = null) should have null tenant columns — not applicable.
        var email = $"mt.notenaut.{Guid.NewGuid():N}@example.vn";
        var userId = await SeedTenantedUserAsync(email, tenantId: null);

        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var user = await db.Users.AsNoTracking().FirstAsync(u => u.Id == userId);

        user.NormalizedEmailHashGlobal.Should().NotBeNull(because: "global hash always populated");
        user.NormalizedEmailHashTenant.Should().BeNull(
            because: "TenantId is null — no per-tenant hash applicable");
        user.EmailHashTenant.Should().BeNull(
            because: "TenantId is null — no per-tenant hash applicable");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Seeds a user via EF DbContext (triggers EmailHashInterceptor on SaveChanges).
    /// This is the real interceptor path — proves the write path uses correct pepper.
    /// </summary>
    private async Task<Guid> SeedTenantedUserAsync(string email, Guid? tenantId)
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            NormalizedUserName = email.ToUpperInvariant(),
            SecurityStamp = Guid.NewGuid().ToString(),
            ConcurrencyStamp = Guid.NewGuid().ToString(),
            FullName = "Multi-Tenant Test User",
            IsActive = true,
            TenantId = tenantId,
        };

        db.Users.Add(user);
        await db.SaveChangesAsync(); // triggers EmailHashInterceptor

        return user.Id;
    }
}
