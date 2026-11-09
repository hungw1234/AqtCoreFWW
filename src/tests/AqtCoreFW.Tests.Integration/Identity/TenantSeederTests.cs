using AqtCoreFW.Identity.Infrastructure.Seeding;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Application.Seeding;
using AqtCoreFW.Tests.Integration.Infrastructure;
using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AqtCoreFW.Tests.Integration.Identity;

/// <summary>
/// Real-SQL coverage for <see cref="TenantSeeder"/> — the proactive Tenant-row seeder.
///
/// Two concerns are exercised against the migrated test DB via <see cref="IReadDbConnection"/>:
///   - insert + diacritic fidelity (Vietnamese names round-trip through the nvarchar column + Dapper)
///   - idempotency / backfill-safety (skip-by-PublicId so a second pass — or TenantBackfillSeeder —
///     never double-inserts)
///
/// Synthetic profiles with fresh GUIDs are used for the DB facts so shared-DB rows seeded by other
/// tests / fixtures cannot pollute the exact-count assertions. The canonical profile data shape is
/// asserted separately as a pure unit fact (no DB).
/// </summary>
[Collection("Integration")]
public class TenantSeederTests(DatabaseFixture db) : IntegrationTestBase(db)
{
    private static TenantSeedProfile SyntheticProfile(string nameWithDiacritics, string code) => new()
    {
        TenantId = Guid.NewGuid(),
        Code = code,
        Name = nameWithDiacritics,
        ContactEmail = "admin@synthetic.test",
        RolePrefix = "SYN_",
        UserCodePrefix = "SYN-SEED-",
        LeadEmailPrefix = "syn.lead.",
        DevEmailPrefix = "syn.dev.",
        FillEmailPrefix = "syn",
        EmailDomain = "@synthetic.test",
        UserCount = 10,
        MaxLevel = 2,
        SeedVersion = "test",
    };

    [Fact]
    public void CanonicalProfiles_Have_Diacritic_Names_And_Distinct_Deterministic_Guids()
    {
        TenantSeedProfiles.BoTaiChinh.Name.Should().Be("Bộ Tài chính");
        TenantSeedProfiles.BoNoiVu.Name.Should().Be("Bộ Nội Vụ");

        TenantSeedProfiles.BoTaiChinh.Code.Should().Be("BoTaiChinh");
        TenantSeedProfiles.BoNoiVu.Code.Should().Be("BoNoiVu");

        // GUIDs must be distinct from each other and from the dev tenant ("…0001").
        var devTenant = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var guids = new[] { TenantSeedProfiles.BoTaiChinh.TenantId, TenantSeedProfiles.BoNoiVu.TenantId };
        guids.Should().OnlyHaveUniqueItems();
        guids.Should().NotContain(devTenant);
    }

    [Fact]
    public async Task SeedTenantsAsync_Inserts_Missing_Rows_With_Diacritics_Intact()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var conn = scope.ServiceProvider.GetRequiredService<IReadDbConnection>();

        var profiles = new[]
        {
            SyntheticProfile("Bộ Tài chính", $"SYN-{Guid.NewGuid():N}"),
            SyntheticProfile("Bộ Nội Vụ", $"SYN-{Guid.NewGuid():N}"),
        };

        var (inserted, skipped) = await TenantSeeder.SeedTenantsAsync(
            conn, profiles, NullLogger.Instance, CancellationToken.None);

        inserted.Should().Be(2);
        skipped.Should().Be(0);

        // Diacritics must survive the nvarchar column + Dapper parameter round-trip.
        foreach (var p in profiles)
        {
            var name = (await conn.QueryAsync<string>(
                "SELECT Name FROM [identity].[Tenants] WHERE PublicId = @Id",
                new { Id = p.TenantId }, CancellationToken.None)).Single();
            name.Should().Be(p.Name);
        }
    }

    [Fact]
    public async Task SeedTenantsAsync_Is_Idempotent_And_Backfill_Safe()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var conn = scope.ServiceProvider.GetRequiredService<IReadDbConnection>();

        var profiles = new[] { SyntheticProfile("Bộ Tài chính", $"SYN-{Guid.NewGuid():N}") };

        var first = await TenantSeeder.SeedTenantsAsync(
            conn, profiles, NullLogger.Instance, CancellationToken.None);
        first.Inserted.Should().Be(1);

        // Second pass (mimics a restart, or TenantBackfillSeeder running afterwards): the row is
        // already present by PublicId → skipped, never double-inserted.
        var second = await TenantSeeder.SeedTenantsAsync(
            conn, profiles, NullLogger.Instance, CancellationToken.None);
        second.Inserted.Should().Be(0);
        second.Skipped.Should().Be(1);

        var rowCount = (await conn.QueryAsync<int>(
            "SELECT COUNT(*) FROM [identity].[Tenants] WHERE PublicId = @Id",
            new { Id = profiles[0].TenantId }, CancellationToken.None)).Single();
        rowCount.Should().Be(1);
    }
}
