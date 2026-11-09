using AqtCoreFW.Tests.SharedFixtures;
using Xunit;

namespace AqtCoreFW.Startup.SmokeTests;

/// <summary>
/// Startup smoke test: boots the full Program host against a fresh DB with all 9 seeders
/// enabled, then asserts the /health endpoint returns 200.
///
/// Purpose: catches the class of bug exemplified by a past seeder Guid↔long FK drift that was
/// missed by regular integration tests because WebAppFixture stripped all IHostedService
/// registrations at the time. With the marker-based filter, seeders now run — this test verifies
/// they complete StartAsync without throwing.
///
/// Runtime expectation: ≤90s p95 (NF1). The org tree is capped at MaxLevel=2 (~36 BTC + 23 BNV
/// units vs ~1,200 at L4). The host registers the seeder cluster in dependency order (pinned by
/// SeederStartupOrderGuardTests), so this single boot seeds the org tree, then the full L2 user
/// set (shared password hash — cost is EF inserts), then real assignments: single-boot
/// convergence is exercised end-to-end.
///
/// Other Api smoke tests (e.g. controller DI resolution) use <see cref="ApiHostFixture"/> with its
/// own private Testcontainer.
///
/// Tag: [Trait("Category","Smoke")] — excluded from fast-feedback runs but mandatory in
/// CI finalize gate. Run with:
///   dotnet test --filter "Category=Smoke"
/// </summary>
[Collection(SmokeTestCollection.CollectionName)]
public sealed class ApiStartupSmokeTests : IAsyncLifetime
{
    private readonly SqlServerFixture _sqlServer;
    private SeederSmokeFixture? _fixture;

    public ApiStartupSmokeTests(SqlServerFixture sqlServer)
    {
        _sqlServer = sqlServer;
    }

    public async Task InitializeAsync()
    {
        _fixture = new SeederSmokeFixture(_sqlServer);
        await ((IAsyncLifetime)_fixture).InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        if (_fixture is not null)
            await ((IAsyncLifetime)_fixture).DisposeAsync();
    }

    /// <summary>
    /// Boots the host (which runs all 9 seeders synchronously before serving requests),
    /// then confirms /health returns 200. A seeder exception causes host start failure,
    /// which surfaces here as an exception on CreateClient() or GetAsync().
    ///
    /// This test would have caught Phase-04a-RT: BtcIdentitySeeder would have thrown a
    /// Guid↔long FK type mismatch during EF SaveChanges, crashing host startup.
    /// </summary>
    [Fact, Trait("Category", "Smoke")]
    public async Task Api_StartsAndAllSeedersComplete_WithoutError()
    {
        // CreateClient() forces the host to build and start — all IHostedService.StartAsync
        // methods run synchronously before the first request is served.
        // If any seeder throws, this line throws and the test fails with the seeder's exception.
        var client = _fixture!.CreateClient();

        // /health/live is the liveness probe (predicate _ => false → no checks run).
        // 200 here = host built, seeders completed StartAsync without throwing, DI graph resolved.
        // We deliberately avoid /health/ready because tests strip non-seeder hosted services
        // (MassTransit, etc.) but their health checks remain registered → ready returns 503
        // for irrelevant reasons. Liveness is the correct gate for "did the host boot cleanly".
        var response = await client.GetAsync("/health/live");

        Assert.True(
            response.IsSuccessStatusCode,
            $"Expected /health/live 200 but got {(int)response.StatusCode} {response.ReasonPhrase}. " +
            "Non-200 means the host failed to start — usually a seeder threw during StartAsync.");
    }
}
