using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.Notifications.Application.Providers;
using AqtCoreFW.Tests.SharedFixtures;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using System.Security.Claims;

namespace AqtCoreFW.Startup.SmokeTests;

/// <summary>
/// Dedicated WebApplicationFactory for the startup smoke test.
///
/// Cannot inherit WebAppFixture (it is sealed). Mirrors its setup and adds:
///   1. Environment = Development (seeders guarded by IsDevelopment() must run).
///   2. Env vars that enable config-gated seeders — set before host build because
///      InfrastructureRegistration reads IOptions at registration time (ConfigurationManager
///      phase), before ConfigureWebHost's AddInMemoryCollection takes effect.
///
/// Config-key inventory (true keys from source, not the placeholder keys in the phase doc):
/// ┌────────────────────────────────┬───────────────────────────────────────────┬─────────────────────┐
/// │ Seeder                         │ Enable mechanism                          │ Smoke override      │
/// ├────────────────────────────────┼───────────────────────────────────────────┼─────────────────────┤
/// │ E2ETestDataSeeder              │ env.IsDevelopment() only                  │ ASPNETCORE_ENV=Dev  │
/// │ TenantSeeder                   │ Identity:TenantSeed:Enabled               │ Enabled=true        │
/// │ TenantRoleSeeder               │ Identity:TenantRoleSeed:Enabled           │ Enabled=true        │
/// │ TenantUserSeeder               │ IsDevelopment() + Identity:TenantUserSeed │ Enabled=true        │
/// │ TenantBackfillSeeder           │ always runs (no gate)                     │ —                   │
/// │ OrgTreeSeeder                  │ Organization:OrgTreeSeed:Enabled          │ Enabled=true L2     │
/// │ TenantAssignmentSeeder         │ IsDevelopment() + Org:TenantAssignmentSeed│ Enabled=true        │
/// │ MasterDataSeeder               │ always runs (no gate)                     │ —                   │
/// │ SystemParamSeeder              │ always runs (no gate)                     │ —                   │
/// │ NotificationTemplateSeeder     │ always runs (no gate)                     │ —                   │
/// │ ApiKeyTestSeeder               │ Env.GetEnvironmentVariable("SEED_TEST_APIKEY") │ "true"         │
/// │ AlertRuleDevSeeder             │ Alerting:DevSeed:Enabled                   │ Enabled=true        │
/// └────────────────────────────────┴───────────────────────────────────────────┴─────────────────────┘
/// </summary>
public sealed class SeederSmokeFixture : WebApplicationFactory<AqtCoreFW.Api.Program>, IAsyncLifetime
{
    private readonly SqlServerFixture _sqlServer;

    public SeederSmokeFixture(SqlServerFixture sqlServer)
    {
        _sqlServer = sqlServer;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Development environment: seeders guarded by IsDevelopment() execute.
        // Note: env vars (set in InitializeAsync) take precedence over UseEnvironment —
        // both are set for belt-and-suspenders coverage.
        builder.UseEnvironment("Development");

        // Connection strings via host settings (ConfigurationManager-phase visible) — see
        // UseTestConnectionStrings for why AddInMemoryCollection is too late for DbContext/Hangfire
        // registration reads. Points at the shared SqlServerFixture container.
        builder.UseTestConnectionStrings(_sqlServer.ConnectionString, includeHangfire: true);

        // Skip boot-path recurring-job registration — the static Hangfire facades freeze
        // JobStorage.Current onto the first booted host's container for the whole testhost process,
        // so a later boot in the multi-host smoke suite registers against a disposed container →
        // connection refused. This host validates seeder boot, not job scheduling.
        builder.UseSetting("Hangfire:RegisterRecurringJobs", "false");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Infrastructure noise disabled (same as WebAppFixture)
                ["Vault:Enabled"]              = "false",
                ["MessageBus:Transport"]       = "InMemory",
                ["Redis:ConnectionString"]     = string.Empty,
                ["AI:Ollama:Enabled"]          = "false",
                ["Gateway:Mode"]               = "Disabled",

                // Tenant-agnostic identity chain: tenant rows → roles → users.
                // This is a BOOT smoke (asserts /health/live 200 = no seeder threw), not a count check.
                // Since the host registers the seeder cluster in explicit dependency order (Program.cs,
                // pinned by SeederStartupOrderGuardTests), OrgTreeSeeder now runs BEFORE
                // TenantUserSeeder — so this single boot seeds the full MaxLevel=2 user set and
                // TenantAssignmentSeeder does real assignment work. That makes this smoke STRONGER
                // (single-boot convergence is exercised end-to-end); the password hash is computed
                // once and shared, so the added cost is EF inserts, still inside the ≤90s NF1 budget.
                ["Identity:TenantSeed:Enabled"]       = "true",
                ["Identity:TenantRoleSeed:Enabled"]   = "true",
                ["Identity:TenantUserSeed:Enabled"]   = "true",

                // OrgTreeSeeder — section: Organization:OrgTreeSeed (needs TenantSeed for tenant rows).
                // MaxLevel=2 seeds L1+L2 only (~36 BTC + 23 BNV vs ~1,200 at L4); treasury (L4) skipped.
                ["Organization:OrgTreeSeed:Enabled"]  = "true",
                ["Organization:OrgTreeSeed:MaxLevel"] = "2",

                // TenantAssignmentSeeder — section: Organization:TenantAssignmentSeed. Runs last in
                // the cluster; with the ordered registration it finds this boot's users and seeds
                // real assignments (previously it skipped — no users existed yet in a single boot).
                ["Organization:TenantAssignmentSeed:Enabled"] = "true",

                // AlertRuleDevSeeder — seeds the disabled signing-allowlist deny AlertRule at boot.
                ["Alerting:DevSeed:Enabled"] = "true",
            });
        });

        builder.ConfigureTestServices(services =>
        {
            // ── Auth: same test-auth bypass as WebAppFixture ──────────────────
            services.AddAuthentication()
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                    TestAuthHandler.SchemeName, _ => { });

            services.PostConfigure<AuthenticationOptions>(options =>
            {
                options.DefaultScheme                = TestAuthHandler.SchemeName;
                options.DefaultAuthenticateScheme    = TestAuthHandler.SchemeName;
                options.DefaultChallengeScheme       = TestAuthHandler.SchemeName;
                options.DefaultForbidScheme          = TestAuthHandler.SchemeName;
            });

            // ── Outbound messaging: fakes (no SMTP/webhook calls) ─────────────
            services.RemoveAll<IEmailSender>();
            services.AddScoped<IEmailSender, FakeEmailSender>();
            services.RemoveAll<ISmsProvider>();
            services.AddScoped<ISmsProvider, FakeSmsProvider>();

            // ── IHostedService filter: keep ONLY IIntegrationTestSeeder ───────
            // Same predicate as WebAppFixture (DRY-extracted to HostedServiceStripFilter) —
            // strips Hangfire/MassTransit/outbox/polling but retains all seeder services
            // (IIntegrationTestSeeder-marked) so startup bugs surface here. No retain-list
            // opt-in needed for this fixture — seeders are the only thing it wants running.
            for (var i = services.Count - 1; i >= 0; i--)
            {
                var d = services[i];
                if (d.ServiceType != typeof(IHostedService)) continue;
                if (!HostedServiceStripFilter.ShouldRetain(d, Array.Empty<Type>()))
                    services.RemoveAt(i);
            }

            // ── Auth bypass: SystemAdmin role satisfies all PermissionRequirements ──
            services.AddSingleton<IAuthorizationHandler, TestSystemAdminPermissionBypassHandler>();

            // ── Vault stubs ── QĐ742 §4.4 Wave-3 resolvers reach Vault unconditionally
            // (Vault:Enabled=false only gates AddVaultConfiguration, not AddVault{Key,Pepper}Resolver).
            // Without these stubs, dev-env Vault permission-denied blocks E2ETestDataSeeder boot
            // → smoke suite runs only in CI. Mirrors WebAppFixture parity.
            services.RemoveAll<IKeyResolver>();
            services.AddSingleton<IKeyResolver>(new TestKeyResolver());

            services.RemoveAll<IPepperResolver>();
            services.AddSingleton<IPepperResolver>(new TestPepperResolver());
        });
    }

    // Connection strings reach the ConfigurationManager phase via builder.UseSetting
    // (UseTestConnectionStrings in ConfigureWebHost) — no process-global env-var mutation.
    // This removes the shared-slot race between overlapping smoke fixture lifecycles AND the
    // cross-assembly bleed that review-M2 flagged (a global ConnectionStrings__Default would
    // leak into a concurrently-running test assembly). Seeder Enabled flags stay in
    // ConfigureAppConfiguration above — they're config-bound IOptions reads that DO see it.
    //
    // IAsyncLifetime is retained (no-op Initialize) so the ApiStartupSmokeTests consumer, which
    // constructs this fixture manually and drives its lifecycle, keeps compiling unchanged. The
    // shared SqlServerFixture owns the container; Dispose tears down the WebApplicationFactory host.
    Task IAsyncLifetime.InitializeAsync() => Task.CompletedTask;

    async Task IAsyncLifetime.DisposeAsync()
    {
        Dispose();
        await Task.CompletedTask;
    }

    /// <summary>
    /// Additive auth handler for tests: succeeds any PermissionRequirement when the
    /// principal has the "SystemAdmin" role claim (mirrors WebAppFixture inner class).
    /// </summary>
    private sealed class TestSystemAdminPermissionBypassHandler
        : AuthorizationHandler<PermissionRequirement>
    {
        protected override Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            PermissionRequirement requirement)
        {
            if (context.User.IsInRole("SystemAdmin"))
                context.Succeed(requirement);
            return Task.CompletedTask;
        }
    }
}
