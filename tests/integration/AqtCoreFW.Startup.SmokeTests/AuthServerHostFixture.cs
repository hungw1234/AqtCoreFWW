using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.Tests.SharedFixtures;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Testcontainers.MsSql;

namespace AqtCoreFW.Startup.SmokeTests;

/// <summary>
/// Boots the AuthServer host (Program.cs) against an ephemeral Testcontainers SQL Server
/// for DI-graph validation only.
///
/// Purpose: AUTHSERVER-DI-ARCH guard. AuthServer Program.cs disables ValidateOnBuild
/// because Identity.Application MediatR handlers require dependencies the AuthServer
/// doesn't host. That choice masks transitive DI gaps until first request:
///   - v2.133p3 (2026-04-28): IMessageBus missing → /connect/authorize 500
///   - 2026-05-08:           IGuidGenerator missing → PHASE7-TIER-BC-VALIDATION miss
///
/// Strategy: enumerate every ControllerBase in the AuthServer assembly and resolve each
/// via ActivatorUtilities.CreateInstance (delegated to <see cref="HostDiArchGuard"/>).
/// Captures real HTTP-surface DI gaps without false-positive noise from the unused
/// MediatR scan that ValidateOnBuild=true would emit.
///
/// CreateClient also exercises Program.cs post-Build paths (Hangfire JobStorage,
/// IPasswordVerifyTimingDecoy warmup, IdentityDbContext + MessagingDbContext migrate),
/// so those four explicit GetRequiredService calls are validated transitively.
///
/// Relocated 2026-05-09 from <c>tests/unit/AqtCoreFW.AuthServer.Tests/Startup/</c> per
/// AUTHSERVER-DI-ARCH-FU2: this is conceptually a smoke (not unit) test, owns its own
/// Testcontainer, and aligns with <c>ApiStartupSmokeTests</c> placement.
/// </summary>
public sealed class AuthServerHostFixture : WebApplicationFactory<AqtCoreFW.AuthServer.Program>, IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
        .WithPassword("AuthSrv@Test1234!")
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
        builder.UseEnvironment("Development");

        // Connection string via host settings (ConfigurationManager-phase visible) — see
        // UseTestConnectionStrings for why AddInMemoryCollection is too late for DbContext
        // registration reads.
        builder.UseTestConnectionStrings(_connectionString);

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Quiet external infra — none of these are required for DI validation.
                ["Vault:Enabled"]              = "false",
                ["MessageBus:Transport"]      = "InMemory",
                ["Redis:ConnectionString"]    = string.Empty,
                ["AI:Ollama:Enabled"]          = "false",

                // Issuer must be set explicitly in Development to avoid OpenIddict
                // HTTPS auto-detection in the test harness.
                ["Auth:IssuerUri"] = "http://localhost:5002/",
            });
        });

        builder.ConfigureTestServices(services =>
        {
            // Strip every IHostedService — defensive against future background-worker
            // registrations. AuthServer does not register Hangfire server today, and
            // OpenIddict data seeders run via direct `await` inside Main (not as
            // IHostedService), so they still execute against the Testcontainer.
            for (var i = services.Count - 1; i >= 0; i--)
            {
                if (services[i].ServiceType == typeof(IHostedService))
                    services.RemoveAt(i);
            }

            // ── Vault stubs ── SeedDevUsersAsync (Program.cs:530) runs UserManager.CreateAsync
            // unconditionally in Development → EmailHashedUserStore → IPepperResolver → real Vault.
            // Without these stubs, dev-env Vault permission-denied fails host build. Mirrors
            // WebAppFixture / SeederSmokeFixture parity.
            services.RemoveAll<IKeyResolver>();
            services.AddSingleton<IKeyResolver>(new TestKeyResolver());

            services.RemoveAll<IPepperResolver>();
            services.AddSingleton<IPepperResolver>(new TestPepperResolver());
        });
    }
}
