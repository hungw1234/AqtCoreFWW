using AqtCoreFW.Identity.Infrastructure.Persistence;
using AqtCoreFW.Identity.Infrastructure.Seeding;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.Tests.SharedFixtures;
using Dapper;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AqtCoreFW.Identity.Integration.Tests.Seeding;

/// <summary>
/// Integration tests for TenantBackfillSeeder.
/// WebAppFixture removes IHostedService instances (memory: feedback-webappfixture-removes-hostedservices.md),
/// so this test calls the static BackfillTenantsAsync method directly with a real DB connection
/// rather than relying on the hosted service to fire on app boot.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class TenantBackfillSeederTests : IClassFixture<WebAppFixture>, IAsyncLifetime
{
    private readonly WebAppFixture _app;
    private static readonly Guid TestTenantId = Guid.NewGuid();

    public TenantBackfillSeederTests(WebAppFixture app)
    {
        _app = app;
    }

    public async Task InitializeAsync()
    {
        // Pre-seed an OrgUnit with TestTenantId so the backfill has a tenant to discover.
        // Schema lookups must use raw SQL because EF DbContexts for Organization aren't
        // referenced from this Identity test project — keep payload minimal.
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IReadDbConnection>();

        // Phase-04a: OrgUnit is T2 — Id is bigint IDENTITY (DB-assigned), PublicId is Guid AK.
        // Drop @Id from INSERT; let IDENTITY assign. PublicId required (Guid AK).
        const string ensureRootOrgUnit = """
            IF NOT EXISTS (SELECT 1 FROM [organization].[OrgUnits] WHERE TenantId = @TenantId)
            BEGIN
                INSERT INTO [organization].[OrgUnits]
                    (PublicId, TenantId, Name, NameEn, Code, ParentId, Level, IsActive, IsDeleted,
                     CreatedAt, CreatedBy, ClassificationLevel)
                VALUES
                    (@PublicId, @TenantId, N'Tenant Backfill Test Root', 'Tenant Backfill Test Root',
                     @Code, NULL, 1, 1, 0, SYSUTCDATETIME(), @TenantId, 0)
            END
            """;

        await db.ExecuteAsync(ensureRootOrgUnit, new
        {
            PublicId = Guid.NewGuid(),
            TenantId = TestTenantId,
            Code = $"TBS-{TestTenantId.ToString("N")[..8].ToUpperInvariant()}",
        });

        // Pre-cleanup: remove any prior TenantBackfillSeederTests row in [identity].[Tenants].
        // Tenant is T2: long PK (Id/bigint identity) + Guid AK (PublicId).
        // Backfiller seeds PublicId from OrgUnit.TenantId — clean up by PublicId.
        await db.ExecuteAsync(
            "DELETE FROM [identity].[Tenants] WHERE PublicId = @PublicId",
            new { PublicId = TestTenantId });
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task BackfillTenantsAsync_CreatesRowForExistingTenantIdInOrgUnit()
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IReadDbConnection>();

        var (insertedCount, skippedCount) = await TenantBackfillSeeder.BackfillTenantsAsync(
            db, NullLogger.Instance, CancellationToken.None);

        // Backfill is global — assert >= 1 only if our specific test tenant is missing.
        insertedCount.Should().BeGreaterThanOrEqualTo(1);
        skippedCount.Should().Be(0, "happy path must not skip any tenants");

        // Verify the row exists and has the expected shape.
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        // Tenant is T2: long PK (Id, DB-assigned) + Guid AK (PublicId).
        // Backfiller seeds PublicId from the OrgUnit TenantId Guid.
        var tenant = await dbContext.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(t => t.PublicId == TestTenantId);

        tenant.Should().NotBeNull("backfill must seed a Tenant row for OrgUnit-only TenantIds");
        tenant!.Code.Should().StartWith("TENANT-");
        tenant.Name.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task BackfillTenantsAsync_IsIdempotent_RunningTwiceInsertsZeroOnSecondPass()
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IReadDbConnection>();

        await TenantBackfillSeeder.BackfillTenantsAsync(db, NullLogger.Instance, CancellationToken.None);
        var (secondInserted, secondSkipped) = await TenantBackfillSeeder.BackfillTenantsAsync(
            db, NullLogger.Instance, CancellationToken.None);

        secondInserted.Should().Be(0, "subsequent runs must be no-ops once the table is up-to-date");
        secondSkipped.Should().Be(0);
    }
}
