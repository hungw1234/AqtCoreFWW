using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using AqtCoreFW.Tests.SharedFixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AqtCoreFW.Identity.Integration.Tests;

/// <summary>
/// Integration tests for ApplicationUser.SigningNationalId (CCCD) AES-256-GCM encryption at rest.
///
/// CCCD is Confidential PII (QĐ742) — MUST be encrypted at rest via EncryptedStringConverter.
/// Mirrors PhoneNumberEncryptionRoundtripTests:
///   1. A saved CCCD decrypts correctly on read (EF roundtrip)
///   2. The raw storage column carries a v{N}: prefix (ciphertext, not plaintext)
///   3. NULL CCCD round-trips as NULL (no encryption of null)
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class SigningNationalIdEncryptionRoundtripTests : IClassFixture<WebAppFixture>
{
    private const string SampleCccd = "017091004878";

    private readonly WebAppFixture _app;

    public SigningNationalIdEncryptionRoundtripTests(WebAppFixture app)
    {
        _app = app;
    }

    [Fact]
    public async Task SigningNationalId_SaveAndReload_DecryptsCorrectly()
    {
        var userId = await SeedUserWithCccdAsync(SampleCccd);

        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var loaded = await db.Users.AsNoTracking().FirstAsync(u => u.Id == userId);

        loaded.SigningNationalId.Should().Be(SampleCccd,
            because: "EF EncryptedStringConverter must decrypt SigningNationalId transparently on read");
    }

    [Fact]
    public async Task SigningNationalId_StorageColumn_CarriesVersionPrefixAndNoPlaintext()
    {
        var userId = await SeedUserWithCccdAsync(SampleCccd);

        // Read the raw column bypassing the EF value converter — proves the at-rest value is ciphertext.
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var connStr = db.Database.GetConnectionString()!;

        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(connStr);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT SigningNationalId FROM [identity].[AspNetUsers] WHERE Id = @id";
        cmd.Parameters.AddWithValue("@id", userId);
        var rawValue = (string?)await cmd.ExecuteScalarAsync();

        rawValue.Should().NotBeNull(because: "SigningNationalId was explicitly set");
        rawValue.Should().MatchRegex(@"^v\d+:",
            because: "encrypted column MUST carry the v{N}: prefix per EncryptedStringConverter format");
        rawValue.Should().NotContain(SampleCccd,
            because: "the plaintext CCCD must NOT appear in the storage column");
    }

    [Fact]
    public async Task SigningNationalId_Null_RoundTripsAsNull()
    {
        var userId = await SeedUserWithCccdAsync(null);

        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var loaded = await db.Users.AsNoTracking().FirstAsync(u => u.Id == userId);

        loaded.SigningNationalId.Should().BeNull(
            because: "null CCCD must persist as null (EF skips the converter for null values)");
    }

    private async Task<Guid> SeedUserWithCccdAsync(string? cccd)
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var tag = Guid.NewGuid().ToString("N");
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = $"cccd.test.{tag}@example.vn",
            Email = $"cccd.test.{tag}@example.vn",
            NormalizedEmail = $"CCCD.TEST.{tag}@EXAMPLE.VN",
            NormalizedUserName = $"CCCD.TEST.{tag}@EXAMPLE.VN",
            SecurityStamp = Guid.NewGuid().ToString(),
            ConcurrencyStamp = Guid.NewGuid().ToString(),
            FullName = "CCCD Test User",
            IsActive = true,
            SigningNationalId = cccd,
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();

        return user.Id;
    }
}
