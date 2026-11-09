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
/// Boots the Api host (Program.cs) with ALL FIVE deferred-module flags OFF
/// (<c>Features:{Ai|Collaboration|BusinessProcess|Reporting|Rules}:Enabled=false</c>) —
/// the launch posture the flags exist for. One boot covers every module's OFF branch.
///
/// Mirrors <see cref="ApiHostFixture"/> (own ephemeral Testcontainer, hosted services
/// stripped, external infra quieted); differs only in the flag settings. The flags are
/// injected via <c>builder.UseSetting</c>, NOT <c>AddInMemoryCollection</c> — Program.cs
/// reads them from <c>builder.Configuration</c> during service REGISTRATION, the same
/// ConfigurationManager-phase constraint the connection strings have (see
/// <c>UseTestConnectionStrings</c>).
///
/// The startup migration loop runs against <c>StartupMigrationSets.EnabledContexts</c>,
/// so this fixture also empirically proves a fresh database boots WITHOUT the five
/// deferred schemas — resolving a disabled DbContext during migration would throw.
/// </summary>
public sealed class DeferredModulesOffHostFixture
    : WebApplicationFactory<AqtCoreFW.Api.Program>, IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
        .WithPassword("ModulesOff@Test1234!")
        .Build();

    private string _connectionString = string.Empty;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        var csb = new SqlConnectionStringBuilder(_container.GetConnectionString())
        {
            TrustServerCertificate = true,
            Encrypt = SqlConnectionEncryptOption.Optional,
        };
        _connectionString = csb.ConnectionString;
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _container.DisposeAsync();
        Dispose();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Same environment rationale as ApiHostFixture: dev stub for VNeId, ValidateOnBuild on.
        builder.UseEnvironment("Development");

        builder.UseTestConnectionStrings(_connectionString, includeHangfire: true);
        builder.UseSetting("Hangfire:RegisterRecurringJobs", "false");

        // The subject of this fixture: every deferred module OFF. UseSetting, not
        // AddInMemoryCollection — registration-time reads (see class remarks).
        foreach (var moduleName in AqtCoreFW.Api.DeferredModuleFlags.ModuleNames)
            builder.UseSetting($"Features:{moduleName}:Enabled", "false");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Vault:Enabled"]          = "false",
                ["MessageBus:Transport"]   = "InMemory",
                ["Redis:ConnectionString"] = string.Empty,
                ["AI:Ollama:Enabled"]      = "false",
                ["Gateway:Mode"]           = "Disabled",
            });
        });

        builder.ConfigureTestServices(services =>
        {
            // Strip IHostedService — same rationale as ApiHostFixture (this fixture asserts
            // registration/discovery/migration behavior, not hosted-service runtime; the
            // absence of disabled modules' hosted services is asserted at the
            // ServiceCollection level in the test class, BEFORE this strip would matter,
            // via the captured pre-strip inspection below).
            for (var i = services.Count - 1; i >= 0; i--)
            {
                if (services[i].ServiceType == typeof(IHostedService))
                {
                    // Capture what Program.cs registered before stripping, so tests can
                    // assert disabled modules contributed no hosted service (the strip
                    // itself would otherwise make that assertion vacuously true).
                    RegisteredHostedServiceImplementations.Add(
                        services[i].ImplementationType?.FullName
                        ?? services[i].ImplementationFactory?.Method.DeclaringType?.FullName
                        ?? services[i].ServiceType.FullName!);
                    services.RemoveAt(i);
                }
            }
        });
    }

    /// <summary>
    /// Implementation type names of every IHostedService Program.cs registered under the
    /// all-off flag posture, captured before the test-fixture strip. Lets tests assert the
    /// disabled modules registered NO hosted service without the strip masking the check
    /// (lesson: a "host boots" smoke is blind to over-retention once services are stripped).
    /// </summary>
    public List<string> RegisteredHostedServiceImplementations { get; } = [];
}
