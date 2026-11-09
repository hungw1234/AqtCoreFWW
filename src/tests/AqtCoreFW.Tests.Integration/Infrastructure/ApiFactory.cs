using AqtCoreFW.Api;
using AqtCoreFW.Ai.Infrastructure.Persistence;
using AqtCoreFW.Audit.Infrastructure.Persistence;
using AqtCoreFW.Files.Infrastructure.Persistence;
using AqtCoreFW.Forms.Infrastructure.Persistence;
using AqtCoreFW.Identity.Domain.Services;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using AqtCoreFW.Infrastructure.ApiKeys;
using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.Infrastructure.Messaging;
using AqtCoreFW.Infrastructure.Webhooks;
using AqtCoreFW.MasterData.Infrastructure;
using AqtCoreFW.MasterData.Infrastructure.Persistence;
using AqtCoreFW.MasterData.Infrastructure.Services;
using AqtCoreFW.SystemParams.Infrastructure.Services;
using AqtCoreFW.Notifications.Infrastructure.Persistence;
using AqtCoreFW.Organization.Infrastructure.Persistence;
using AqtCoreFW.Reporting.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SystemParams.Infrastructure;
using AqtCoreFW.SystemParams.Infrastructure.Persistence;
using AqtCoreFW.Workflow.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;


namespace AqtCoreFW.Tests.Integration.Infrastructure;

/// <summary>
/// WebApplicationFactory wired to TestContainers SQL Server + Redis.
/// Replaces OpenIddict auth with TestAuthHandler so no OIDC discovery occurs.
/// ConfigureAppConfiguration runs before module registrations so all
/// GetConnectionString("Default") calls see the container connection string.
/// </summary>
public class ApiFactory(DatabaseFixture db) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Must be "Testing" — Program.cs skips DI validation for non-Dev/Staging envs
        builder.UseEnvironment("Testing");

        // Override config BEFORE services are registered so connection strings propagate
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // All modules use GetConnectionString("Default") — point to TestContainers
                ["ConnectionStrings:Default"] = db.SqlConnectionString,
                // Redis backplane + cache
                ["Redis:ConnectionString"] = db.RedisConnectionString,
                // OpenIddict SetIssuer — irrelevant in tests but must be valid-looking URI
                ["Auth:AuthorityUrl"] = "https://test-not-used.local",
                // Disable AI Ollama health check probes (no Ollama container in tests)
                ["AI:Ollama:Enabled"] = "false",
                // Disable YARP reverse proxy — controllers serve requests directly in tests.
                // YARP mode "InProcess" proxies /api/** to localhost:5001 which is not running.
                ["Gateway:Mode"] = "Disabled",
                // Disable rate limiter — TestServer has constant remote IP so all parallel test
                // requests share the same partition and trigger 429 false-positives.
                ["RateLimiting:Enabled"] = "false",
            });
        });

        builder.ConfigureTestServices(services =>
        {
            // --- Replace all auth with test bypass scheme ---
            // RemoveAll clears DefaultAuthenticateScheme set by OpenIddict/Identity
            services.RemoveAll<IAuthenticationSchemeProvider>();
            services.AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                    TestAuthHandler.SchemeName, _ => { });

            // Production AddAuthentication sets DefaultAuthenticateScheme = "MultiAuth" and
            // DefaultChallengeScheme = "MultiAuth". AddAuthentication(SchemeName) above only
            // sets DefaultScheme. PostConfigureAll runs after all Configure<T> calls and wins,
            // ensuring TestScheme handles authentication/challenge/forbid in tests.
            services.PostConfigureAll<AuthenticationOptions>(o =>
            {
                o.DefaultScheme = TestAuthHandler.SchemeName;
                o.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                o.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                o.DefaultForbidScheme = TestAuthHandler.SchemeName;
            });

            // --- ICurrentUser: not registered in production DI (resolved per-request) ---
            services.AddHttpContextAccessor();
            services.RemoveAll<ICurrentUser>();
            services.AddScoped<ICurrentUser, TestCurrentUser>();

            // --- IEffectivePermissionService stub (D9): bridge legacy role-based test headers
            // (X-Test-Roles) to the permission-code model enforced post-D5/D6/D7. Admin/SystemAdmin
            // roles grant full perms (loaded once from seeded DB); other roles get empty set → 403.
            // Singleton: lazy one-time DB load cached for process lifetime; uses IServiceScopeFactory
            // to resolve scoped IdentityDbContext. See TestEffectivePermissionService.cs for rationale.
            services.RemoveAll<IEffectivePermissionService>();
            services.AddSingleton<IEffectivePermissionService, TestEffectivePermissionService>();

            // --- IPepperResolver stub: production VaultPepperResolver reads email-hash peppers from
            // Vault, but the test harness has no Vault policy for those paths. The write path
            // (EmailHashInterceptor) swallows the resulting KeyNotFoundException, but the read path
            // (EmailHashedUserStore.FindByEmailAsync) does not — surfacing as a 500 on user creation.
            // A deterministic stub keeps write/read hashes consistent so lookups resolve.
            services.RemoveAll<IPepperResolver>();
            services.AddSingleton<IPepperResolver, FakePepperResolver>();

            // --- IKeyResolver stub: EncryptedStringConverter (Email / TotpSecret columns) resolves the
            // AES key at model-build time from this service; the real VaultKeyResolver throws when the
            // test token can't read the Vault key path, failing SaveChanges with a DbUpdateException.
            // The deterministic stub round-trips writes and reads with a stable per-version key.
            services.RemoveAll<IKeyResolver>();
            services.AddSingleton<IKeyResolver, FakeKeyResolver>();

            // --- Suppress all IHostedService instances during host startup ---
            // WebApplicationFactory.get_Services triggers host.StartAsync() which runs all
            // IHostedService.StartAsync() — including seeders that need a migrated DB.
            // We remove them here and call seeders explicitly AFTER MigrateAsync in
            // InitializeDatabaseAsync, which preserves the correct migration → seed order.
            services.RemoveAll<IHostedService>();

            // --- Override Redis connection to use TestContainers Redis ---
            // IConnectionMultiplexer is registered in AddInfrastructure with a connection string
            // captured at registration time (from appsettings.json). ConfigureTestServices runs
            // after all production services are registered, so we can replace it with the
            // TestContainers address here.
            services.RemoveAll<IConnectionMultiplexer>();
            services.AddSingleton<IConnectionMultiplexer>(_ =>
                ConnectionMultiplexer.Connect(db.RedisConnectionString));

            // Re-register seeders as transient (not IHostedService) so InitializeDatabaseAsync
            // can resolve them directly for explicit post-migration invocation.
            services.AddTransient<MasterDataSeeder>();
            services.AddTransient<SystemParamSeeder>();

            // Override all DbContexts to use TestContainers SQL Server and suppress
            // PendingModelChangesWarning. EF Core appends options — last UseSqlServer wins,
            // so this overrides the connection string captured at production registration time
            // (from appsettings.json) while preserving other options (UseOpenIddict, etc.).
            var testSql = db.SqlConnectionString;
            OverrideDbContextForTest<IdentityDbContext>(services, testSql, "identity");
            OverrideDbContextForTest<MasterDataDbContext>(services, testSql, "masterdata");
            OverrideDbContextForTest<AuditDbContext>(services, testSql, "audit");
            OverrideDbContextForTest<FilesDbContext>(services, testSql, "files");
            OverrideDbContextForTest<FormsDbContext>(services, testSql, "forms");
            OverrideDbContextForTest<WorkflowDbContext>(services, testSql, "workflow");
            OverrideDbContextForTest<NotificationsDbContext>(services, testSql, "notifications");
            OverrideDbContextForTest<OrgDbContext>(services, testSql, "organization");
            OverrideDbContextForTest<SystemParamsDbContext>(services, testSql, "config");
            OverrideDbContextForTest<AiDbContext>(services, testSql, "ai");
            OverrideDbContextForTest<ReportingDbContext>(services, testSql, "reporting");
            OverrideDbContextForTest<WebhookDbContext>(services, testSql, "webhooks");
            OverrideDbContextForTest<MessagingDbContext>(services, testSql);
            OverrideDbContextForTest<ApiKeyDbContext>(services, testSql, "gateway");
        });
    }

    /// <summary>
    /// Applies all EF Core migrations by explicitly resolving each known DbContext type.
    /// Uses GetService (not GetRequiredService) to gracefully skip any context not registered.
    /// IdentityDbContext migrated first — OpenIddict tables must exist before other seeders run.
    /// Runs once during DatabaseFixture.InitializeAsync.
    /// </summary>
    public async Task InitializeDatabaseAsync()
    {
        // Force the host to build so all module DbContexts are registered
        _ = Services;

        using var scope = Services.CreateScope();
        var sp = scope.ServiceProvider;

        // Ordered list — identity first (OpenIddict deps), then all others.
        // GetService returns null if a context is not registered; those are skipped.
        var contextsToMigrate = new List<DbContext?>
        {
            sp.GetService<IdentityDbContext>(),           // identity schema — must be first (OpenIddict)
            sp.GetService<MasterDataDbContext>(),         // masterdata schema
            sp.GetService<AuditDbContext>(),              // audit schema
            sp.GetService<FilesDbContext>(),              // files schema
            sp.GetService<FormsDbContext>(),              // forms schema
            sp.GetService<WorkflowDbContext>(),           // workflow schema
            sp.GetService<NotificationsDbContext>(),      // notifications schema
            sp.GetService<OrgDbContext>(),                // organization schema
            sp.GetService<SystemParamsDbContext>(),       // config schema — before SystemParamSeeder
            sp.GetService<AiDbContext>(),                 // ai schema
            sp.GetService<ReportingDbContext>(),          // reporting schema
            sp.GetService<WebhookDbContext>(),            // shared: webhooks
            sp.GetService<MessagingDbContext>(),          // shared: outbox / messaging
            sp.GetService<ApiKeyDbContext>(),             // shared: api keys
        };

        foreach (var ctx in contextsToMigrate.Where(c => c != null))
        {
            await ctx!.Database.MigrateAsync();
            await ctx.DisposeAsync();
        }

        // Run production seeders explicitly after migrations are applied.
        // All IHostedService registrations were suppressed in ConfigureTestServices to prevent
        // seeders from firing during host startup against an unmigrated DB.
        // Seeders are re-registered as transient so they can be resolved directly here.
        var masterDataSeeder = sp.GetService<MasterDataSeeder>();
        if (masterDataSeeder != null)
            await masterDataSeeder.StartAsync(CancellationToken.None);

        var systemParamSeeder = sp.GetService<SystemParamSeeder>();
        if (systemParamSeeder != null)
            await systemParamSeeder.StartAsync(CancellationToken.None);
    }

    /// <summary>
    /// Overrides the DbContext SQL Server connection string to use TestContainers and
    /// suppresses PendingModelChangesWarning. EF Core stacks IDbContextOptionsConfiguration
    /// registrations — the last UseSqlServer call wins, overriding the connection string
    /// captured at production registration time while preserving other options (UseOpenIddict, etc.).
    /// </summary>
    private static void OverrideDbContextForTest<TContext>(
        IServiceCollection services,
        string connectionString,
        string? migrationHistorySchema = null)
        where TContext : DbContext
    {
        services.AddDbContext<TContext>(options =>
        {
            if (migrationHistorySchema != null)
                options.UseSqlServer(connectionString, sql =>
                    sql.MigrationsHistoryTable("__EFMigrationsHistory", migrationHistorySchema));
            else
                options.UseSqlServer(connectionString);

            options.ConfigureWarnings(w =>
                w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
        });
    }

    /// <summary>
    /// Creates an HttpClient with test identity headers pre-populated.
    /// The TestAuthHandler reads these headers to build ClaimsPrincipal.
    /// </summary>
    public HttpClient CreateAuthenticatedClient(
        string? userId = null,
        string[]? roles = null,
        string? tenantId = null)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        client.DefaultRequestHeaders.Add(
            TestAuthHandler.UserIdHeader,
            userId ?? Guid.NewGuid().ToString());

        if (roles?.Length > 0)
            client.DefaultRequestHeaders.Add(
                TestAuthHandler.RolesHeader,
                string.Join(",", roles));

        if (tenantId != null)
            client.DefaultRequestHeaders.Add(
                TestAuthHandler.TenantIdHeader,
                tenantId);

        return client;
    }

    /// <summary>
    /// Creates an HttpClient with explicit permission codes for fine-grained RBAC tests.
    /// Use this when testing specific permission combinations (e.g., View but not Manage).
    /// </summary>
    public HttpClient CreateClientWithPermissions(
        string[] permissions,
        string? userId = null,
        string? tenantId = null)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        client.DefaultRequestHeaders.Add(
            TestAuthHandler.UserIdHeader,
            userId ?? Guid.NewGuid().ToString());

        if (permissions.Length > 0)
            client.DefaultRequestHeaders.Add(
                TestAuthHandler.PermissionsHeader,
                string.Join(",", permissions));

        if (tenantId != null)
            client.DefaultRequestHeaders.Add(
                TestAuthHandler.TenantIdHeader,
                tenantId);

        return client;
    }
}
