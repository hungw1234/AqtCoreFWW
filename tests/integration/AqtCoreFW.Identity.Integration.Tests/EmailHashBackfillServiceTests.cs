using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using AqtCoreFW.Identity.Infrastructure.Services;
using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.Tests.SharedFixtures;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AqtCoreFW.Identity.Integration.Tests;

/// <summary>
/// Integration tests for <see cref="EmailHashBackfillService"/> (QĐ 742 §4.4 Wave-4, Fixup P0-1).
///
/// Verifies the complete cycle for a startup backfill:
///   1. Users with NULL NormalizedEmailHashGlobal are found via EF (Email is decrypted by converter)
///   2. EmailHashService computes global hashes (Guid.Empty pepper — cross-tenant login path)
///   3. SaveChanges persists all 4 hash columns to DB
///   4. Second run touches 0 rows (idempotent)
///
/// Critically: EF must decrypt Email/NormalizedEmail transparently when loading users.
/// TestKeyResolver (WebAppFixture) provides a fixed encryption key.
/// TestPepperResolver provides deterministic peppers — perturbed by tenantId so global ≠ tenant.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class EmailHashBackfillServiceTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;

    public EmailHashBackfillServiceTests(WebAppFixture app)
    {
        _app = app;
    }

    // ── Test 1: BackfillAsync populates hashes for users with NULL NormalizedEmailHash ──

    [Fact]
    public async Task BackfillAsync_UsersWithNullHashes_PopulatesNormalizedEmailHashAndEmailHash()
    {
        // Arrange: seed 2 users with NULL NormalizedEmailHash (simulating pre-backfill state).
        // EF encodes Email/NormalizedEmail via EncryptedStringConverter on insert.
        var user1Email = $"backfill.test.{Guid.NewGuid():N}@example.vn";
        var user2Email = $"backfill.test.{Guid.NewGuid():N}@gov.vn";

        var (user1Id, user2Id) = await SeedUsersWithNullHashesAsync(user1Email, user2Email);

        // Act: run backfill in a fresh scope (as Program.cs startup does it)
        var backfillResult = await RunBackfillAsync();

        // Assert: at least the 2 seeded users were backfilled
        backfillResult.Backfilled.Should().BeGreaterThanOrEqualTo(2,
            because: "at least the 2 seeded users with NULL NormalizedEmailHashGlobal must be backfilled");

        // Verify hashes were written correctly
        using var scope = _app.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<IdentityDbContext>();
        var emailHashService = sp.GetRequiredService<EmailHashService>();

        var loadedUser1 = await db.Users.AsNoTracking().FirstAsync(u => u.Id == user1Id);
        var loadedUser2 = await db.Users.AsNoTracking().FirstAsync(u => u.Id == user2Id);

        // NormalizedEmailHashGlobal = HMAC(NormalizedEmail, Guid.Empty — login path sentinel)
        var expectedHash1 = emailHashService.ComputeHash(
            loadedUser1.NormalizedEmail!, Guid.Empty);
        var expectedHash2 = emailHashService.ComputeHash(
            loadedUser2.NormalizedEmail!, Guid.Empty);

        loadedUser1.NormalizedEmailHashGlobal.Should().Be(expectedHash1,
            because: "NormalizedEmailHashGlobal must match EmailHashService.ComputeHash(NormalizedEmail, Guid.Empty)");
        loadedUser2.NormalizedEmailHashGlobal.Should().Be(expectedHash2,
            because: "NormalizedEmailHashGlobal must match EmailHashService.ComputeHash(NormalizedEmail, Guid.Empty)");

        // EmailHashGlobal = HMAC(Email canonical, Guid.Empty)
        var expectedEmailHash1 = emailHashService.ComputeHash(loadedUser1.Email!, Guid.Empty);
        var expectedEmailHash2 = emailHashService.ComputeHash(loadedUser2.Email!, Guid.Empty);

        loadedUser1.EmailHashGlobal.Should().Be(expectedEmailHash1,
            because: "EmailHashGlobal must match EmailHashService.ComputeHash(Email, Guid.Empty)");
        loadedUser2.EmailHashGlobal.Should().Be(expectedEmailHash2,
            because: "EmailHashGlobal must match EmailHashService.ComputeHash(Email, Guid.Empty)");
    }

    // ── Test 2: Idempotency — second run touches 0 rows ──────────────────────

    [Fact]
    public async Task BackfillAsync_SecondRun_BackfillsZeroRows()
    {
        // Arrange: seed users with NULL hash (force fresh ones per test via unique email)
        var email = $"idempotent.test.{Guid.NewGuid():N}@example.vn";
        await SeedUsersWithNullHashesAsync(email);

        // First run
        var firstRun = await RunBackfillAsync();
        firstRun.Backfilled.Should().BeGreaterThanOrEqualTo(1,
            because: "first run must backfill at least the seeded user");

        // Second run — WHERE NormalizedEmailHashGlobal IS NULL returns 0 rows
        var secondRun = await RunBackfillAsync();
        secondRun.Backfilled.Should().Be(0,
            because: "second run must touch 0 rows — NormalizedEmailHashGlobal IS NULL matches nobody");
    }

    // ── Test 3: User with non-NULL NormalizedEmailHashGlobal is not modified ─────

    [Fact]
    public async Task BackfillAsync_UserWithExistingHash_IsNotModified()
    {
        // Arrange: seed a user WITH an existing NormalizedEmailHashGlobal
        var email = $"already.hashed.{Guid.NewGuid():N}@example.vn";
        var userId = await SeedUserWithExistingHashAsync(email);

        // Record hash before backfill
        using var scopeBefore = _app.Services.CreateScope();
        var dbBefore = scopeBefore.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var userBefore = await dbBefore.Users.AsNoTracking().FirstAsync(u => u.Id == userId);
        var hashBefore = userBefore.NormalizedEmailHashGlobal;
        hashBefore.Should().NotBeNull(because: "user was seeded with a non-null NormalizedEmailHashGlobal");

        // Act: run backfill
        await RunBackfillAsync();

        // Assert: global hash unchanged
        using var scopeAfter = _app.Services.CreateScope();
        var dbAfter = scopeAfter.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var userAfter = await dbAfter.Users.AsNoTracking().FirstAsync(u => u.Id == userId);

        userAfter.NormalizedEmailHashGlobal.Should().Be(hashBefore,
            because: "user with existing NormalizedEmailHashGlobal must not be modified by backfill");
    }

    // ── Test 4: Guid.Empty sentinel matches FindByEmailAsync path ─────────────

    [Fact]
    public async Task BackfillAsync_NormalizedEmailHashGlobal_UsesGuidEmptySentinel()
    {
        // Verifies that the backfill produces the SAME hash that EmailHashedUserStore
        // would compute for a cross-tenant login lookup (Guid.Empty sentinel).
        // If sentinel mismatches, login fails even after backfill.

        var email = $"sentinel.test.{Guid.NewGuid():N}@example.vn";
        var (userId, _) = await SeedUsersWithNullHashesAsync(email);

        await RunBackfillAsync();

        using var scope = _app.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<IdentityDbContext>();
        var emailHashService = sp.GetRequiredService<EmailHashService>();

        var user = await db.Users.AsNoTracking().FirstAsync(u => u.Id == userId);

        // Compute what FindByEmailAsync would use: Guid.Empty
        var loginPathHash = emailHashService.ComputeHash(user.NormalizedEmail!, Guid.Empty);

        user.NormalizedEmailHashGlobal.Should().Be(loginPathHash,
            because: "backfilled global hash must match EmailHashService.ComputeHash(NormalizedEmail, Guid.Empty)");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Seeds N users with NULL NormalizedEmailHash (simulating pre-backfill state).
    /// Uses direct DbContext insert without EmailHashInterceptor to ensure NULL state.
    /// Returns the Ids of the seeded users.
    /// </summary>
    private async Task<(Guid First, Guid Second)> SeedUsersWithNullHashesAsync(
        string firstEmail, string? secondEmail = null)
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var user1 = BuildUser(firstEmail);
        var user2 = secondEmail is not null ? BuildUser(secondEmail) : BuildUser($"auto.{Guid.NewGuid():N}@test.vn");

        // Insert directly to ensure NormalizedEmailHash = NULL (bypassing interceptor).
        db.Users.Add(user1);
        db.Users.Add(user2);
        await db.SaveChangesAsync();

        // Force NULL for all hash columns via raw UPDATE (interceptor may set them on insert)
        var connStr = db.Database.GetConnectionString()!;
        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(connStr);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE [identity].[AspNetUsers] SET " +
            "NormalizedEmailHashGlobal = NULL, EmailHashGlobal = NULL, " +
            "NormalizedEmailHashTenant = NULL, EmailHashTenant = NULL " +
            "WHERE Id IN (@id1, @id2)";
        cmd.Parameters.AddWithValue("@id1", user1.Id);
        cmd.Parameters.AddWithValue("@id2", user2.Id);
        await cmd.ExecuteNonQueryAsync();

        return (user1.Id, user2.Id);
    }

    private async Task<Guid> SeedUserWithExistingHashAsync(string email)
    {
        using var scope = _app.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<IdentityDbContext>();
        var emailHashService = sp.GetRequiredService<EmailHashService>();

        var user = BuildUser(email);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        // Set a non-null global hash directly
        var normalized = email.ToUpperInvariant();
        var hash = emailHashService.ComputeHash(normalized, Guid.Empty);

        var connStr = db.Database.GetConnectionString()!;
        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(connStr);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE [identity].[AspNetUsers] SET NormalizedEmailHashGlobal = @hash WHERE Id = @id";
        cmd.Parameters.AddWithValue("@hash", hash);
        cmd.Parameters.AddWithValue("@id", user.Id);
        await cmd.ExecuteNonQueryAsync();

        return user.Id;
    }

    private static ApplicationUser BuildUser(string email) => new()
    {
        Id = Guid.NewGuid(),
        UserName = email,
        Email = email,
        NormalizedEmail = email.ToUpperInvariant(),
        NormalizedUserName = email.ToUpperInvariant(),
        SecurityStamp = Guid.NewGuid().ToString(),
        ConcurrencyStamp = Guid.NewGuid().ToString(),
        FullName = "Backfill Test User",
        IsActive = true,
        EmailHashGlobal = null,
        NormalizedEmailHashGlobal = null,
        EmailHashTenant = null,
        NormalizedEmailHashTenant = null,
    };

    private async Task<EmailHashBackfillService.BackfillResult> RunBackfillAsync()
    {
        using var scope = _app.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<IdentityDbContext>();
        var emailHashService = sp.GetRequiredService<EmailHashService>();

        return await EmailHashBackfillService.BackfillAsync(db, emailHashService);
    }
}
