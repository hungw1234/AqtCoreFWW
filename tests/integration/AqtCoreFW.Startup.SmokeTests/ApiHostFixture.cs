using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Testcontainers.MsSql;
using AqtCoreFW.Tests.SharedFixtures;

namespace AqtCoreFW.Startup.SmokeTests;

/// <summary>
/// Boots the Api host (Program.cs) against an ephemeral Testcontainers SQL Server
/// for DI-graph validation only.
///
/// Purpose: AUTHSERVER-DI-ARCH-FU1 mirror for Api host. Api Program.cs has
/// <c>ValidateOnBuild=true</c> in dev/staging, but controllers are NOT registered in
/// the DI container — they construct per-request via <see cref="ActivatorUtilities"/>,
/// so DI failures only surface on first HTTP call. This fixture's strategy parallels
/// <see cref="AuthServerHostFixture"/>:
///
///   1. Own ephemeral Testcontainer (isolated from <see cref="SeederSmokeFixture"/>
///      which shares a SQL DB across tests for amortization).
///   2. Strip every <see cref="IHostedService"/> — bypasses seeder runtime
///      (BtcIdentitySeeder, BtcOrgUnitSeeder, etc.) which would either cost ~40s
///      or fail config validation against a non-empty shared DB.
///   3. Quiet external infra (Vault/Redis/Hangfire/AI) via in-memory config.
///
/// Why not reuse <see cref="SeederSmokeFixture"/>: that fixture shares
/// <see cref="Tests.SharedFixtures.SqlServerFixture"/> across the smoke collection.
/// When ApiStartupSeederSmokeTest runs first and seeds 36 OrgUnits, a subsequent
/// controller test in the same collection re-runs BtcIdentitySeeder which fails
/// config validation (config UserCount=50 &lt; required 165 = 36 lead + 129 reserve).
/// Per-test private Testcontainer eliminates this cross-test contamination.
/// </summary>
public sealed class ApiHostFixture : WebApplicationFactory<AqtCoreFW.Api.Program>, IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
        .WithPassword("ApiHost@Test1234!")
        .Build();

    private string _connectionString = string.Empty;

    public Task InitializeAsync() => StartContainerAsync();

    private async Task StartContainerAsync()
    {
        await _container.StartAsync();
        var csb = new SqlConnectionStringBuilder(_container.GetConnectionString())
        {
            TrustServerCertificate = true,
            Encrypt = SqlConnectionEncryptOption.Optional,
        };
        _connectionString = csb.ConnectionString;

        // The connection string is injected into the ConfigurationManager phase via
        // builder.UseSetting in ConfigureWebHost (see UseTestConnectionStrings) — no
        // process-global env-var mutation, so overlapping fixture lifecycles can't race a
        // shared slot.
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _container.DisposeAsync();
        Dispose();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Use Development environment because:
        //   * Production env trips the IVneIdConnector "register real impl before deploy"
        //     guard inside VNeIdHttpConnector ctor — we need the dev stub.
        //   * Dev keeps ValidateOnBuild=true (Program.cs:268-275), giving extra DI coverage
        //     during host build.
        //   * E2ETestDataSeeder + Btc* seeders are IHostedService — stripped below regardless.
        builder.UseEnvironment("Development");

        // Connection strings via host settings (ConfigurationManager-phase visible) — see
        // UseTestConnectionStrings for why AddInMemoryCollection is too late for DbContext/Hangfire
        // registration reads.
        builder.UseTestConnectionStrings(_connectionString, includeHangfire: true);

        // Skip boot-path recurring-job registration: the static Hangfire RecurringJob/BackgroundJob
        // facades freeze JobStorage.Current onto the first booted host's container for the whole
        // testhost process, so a later host boot in a multi-host suite registers against a disposed
        // container → connection refused. This host validates the DI graph, not job scheduling.
        builder.UseSetting("Hangfire:RegisterRecurringJobs", "false");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Quiet external infra — none of these are required for DI validation.
                ["Vault:Enabled"]              = "false",
                ["MessageBus:Transport"]       = "InMemory",
                ["Redis:ConnectionString"]     = string.Empty,
                ["AI:Ollama:Enabled"]          = "false",
                ["Gateway:Mode"]               = "Disabled",
            });
        });

        builder.ConfigureTestServices(services =>
        {
            // Strip every IHostedService — bypasses Hangfire server, MassTransit, outbox
            // dispatcher, and all *Seeder hosted services. The Api host's DI graph still
            // builds and EF migrations still run (those execute inline in Main, not as
            // IHostedService), so controllers can resolve their dependencies.
            for (var i = services.Count - 1; i >= 0; i--)
            {
                if (services[i].ServiceType == typeof(IHostedService))
                    services.RemoveAt(i);
            }

        });
    }
}
