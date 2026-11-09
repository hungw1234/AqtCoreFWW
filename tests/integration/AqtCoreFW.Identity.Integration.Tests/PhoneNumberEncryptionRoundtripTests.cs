using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using AqtCoreFW.Tests.SharedFixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AqtCoreFW.Identity.Integration.Tests;

/// <summary>
/// Integration tests for PhoneNumber AES-256-GCM encryption at rest (Fixup P1-1).
///
/// Plan §66: PhoneNumber is Confidential — MUST be encrypted at rest via EncryptedStringConverter.
/// These tests verify:
///   1. A saved PhoneNumber is decrypted correctly on read (EF roundtrip)
///   2. The raw storage column carries a v{N}: prefix (ciphertext, not plaintext)
///   3. NULL PhoneNumber round-trips as NULL (no encryption of null)
///   4. PhoneNumber update is encrypted on next save
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class PhoneNumberEncryptionRoundtripTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;

    public PhoneNumberEncryptionRoundtripTests(WebAppFixture app)
    {
        _app = app;
    }

    // ── Test 1: EF roundtrip — save phone, reload, assert decrypted correctly ─────────────

    [Fact]
    public async Task PhoneNumber_SaveAndReload_DecryptsCorrectly()
    {
        const string plainPhone = "+84-912-345-678";

        var userId = await SeedUserWithPhoneAsync(plainPhone);

        // Reload via EF — EncryptedStringConverter decrypts transparently
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var loaded = await db.Users.AsNoTracking().FirstAsync(u => u.Id == userId);

        loaded.PhoneNumber.Should().Be(plainPhone,
            because: "EF EncryptedStringConverter must decrypt PhoneNumber transparently on read");
    }

    // ── Test 2: Raw storage column carries v{N}: prefix (ciphertext proof) ───────────────

    [Fact]
    public async Task PhoneNumber_StorageColumn_CarriesVersionPrefix()
    {
        const string plainPhone = "+84-912-999-001";

        var userId = await SeedUserWithPhoneAsync(plainPhone);

        // Read raw value from DB bypassing EF value converter
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var connStr = db.Database.GetConnectionString()!;

        string? rawValue = null;
        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(connStr);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT PhoneNumber FROM [identity].[AspNetUsers] WHERE Id = @id";
        cmd.Parameters.AddWithValue("@id", userId);
        rawValue = (string?)await cmd.ExecuteScalarAsync();

        rawValue.Should().NotBeNull(because: "PhoneNumber was explicitly set");
        rawValue.Should().MatchRegex(@"^v\d+:",
            because: "encrypted column MUST carry v{N}: prefix per EncryptedStringConverter format (D10.2)");
        rawValue.Should().NotContain(plainPhone,
            because: "plaintext phone number must NOT appear in the storage column");
    }

    // ── Test 3: NULL PhoneNumber round-trips as NULL ──────────────────────────────────────

    [Fact]
    public async Task PhoneNumber_Null_RoundTripsAsNull()
    {
        var userId = await SeedUserWithPhoneAsync(null);

        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var loaded = await db.Users.AsNoTracking().FirstAsync(u => u.Id == userId);

        loaded.PhoneNumber.Should().BeNull(
            because: "null PhoneNumber must persist as null (EF skips converter for null values)");
    }

    // ── Test 4: PhoneNumber update is re-encrypted on next save ──────────────────────────

    [Fact]
    public async Task PhoneNumber_Update_ReEncryptsNewValue()
    {
        const string originalPhone = "+84-900-111-222";
        const string updatedPhone = "+84-900-333-444";

        var userId = await SeedUserWithPhoneAsync(originalPhone);

        // Update via EF
        using var updateScope = _app.Services.CreateScope();
        var dbUpdate = updateScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var userToUpdate = await dbUpdate.Users.FirstAsync(u => u.Id == userId);
        userToUpdate.PhoneNumber = updatedPhone;
        await dbUpdate.SaveChangesAsync();

        // Reload and verify
        using var readScope = _app.Services.CreateScope();
        var dbRead = readScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var loaded = await dbRead.Users.AsNoTracking().FirstAsync(u => u.Id == userId);

        loaded.PhoneNumber.Should().Be(updatedPhone,
            because: "updated PhoneNumber must decrypt to new value after re-encryption");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────────

    private async Task<Guid> SeedUserWithPhoneAsync(string? phoneNumber)
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = $"phone.test.{Guid.NewGuid():N}@example.vn",
            Email = $"phone.test.{Guid.NewGuid():N}@example.vn",
            NormalizedEmail = $"PHONE.TEST.{Guid.NewGuid():N}@EXAMPLE.VN",
            NormalizedUserName = $"PHONE.TEST.{Guid.NewGuid():N}@EXAMPLE.VN",
            SecurityStamp = Guid.NewGuid().ToString(),
            ConcurrencyStamp = Guid.NewGuid().ToString(),
            FullName = "Phone Test User",
            IsActive = true,
            PhoneNumber = phoneNumber,
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();

        return user.Id;
    }
}
