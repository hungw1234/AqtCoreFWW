using AqtCoreFW.Audit.Application.Services;
using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.Notifications.Application.Providers;
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

namespace AqtCoreFW.Tests.SharedFixtures;

/// <summary>
/// WebApplicationFactory wired to SQL Server (native-first, Testcontainers fallback via SqlServerFixture).
/// Overrides:
///   - DB connection strings â†’ SqlServerFixture instance (local or containerised)
///   - Auth â†’ TestAuthHandler (bypasses JWT validation in tests)
///   - Outbound messaging (IEmailSender, ISmsProvider) â†’ in-memory fakes (no SMTP/webhook traffic)
///   - Background services â†’ disabled to avoid Hangfire/MassTransit noise
/// </summary>
public sealed class WebAppFixture : WebApplicationFactory<AqtCoreFW.Api.Program>, IAsyncLifetime
{
    private readonly SqlServerFixture _sqlServer;

    public WebAppFixture(SqlServerFixture sqlServer)
    {
        _sqlServer = sqlServer;
    }

    /// <summary>
    /// Test-only opt-in: hosted-service implementation types to retain despite the seeder-only
    /// strip filter below (see <see cref="HostedServiceStripFilter"/>). Populate BEFORE the host
    /// is first built — i.e. before any <c>CreateClient()</c>/<c>Server</c>/<c>Services</c>
    /// access on this instance or on a <c>WithWebHostBuilder</c> clone of it, since
    /// <see cref="ConfigureWebHost"/> runs lazily on first access. Empty by default: with no opt-in
    /// the predicate strips every concrete non-seeder (Hangfire, MassTransit, outbox, pub/sub,
    /// polling) exactly as the pre-existing strip-only-seeders loop did — it adds only this opt-in
    /// plus a fail-safe retain for the narrow interface-pinned factory-seeder shape (no production
    /// registration uses that shape today — see <see cref="HostedServiceStripFilter"/>).
    ///
    /// <see cref="WebAppFixture"/> is <c>sealed</c> (mirrored by <c>SeederSmokeFixture</c>'s own
    /// doc comment — "Cannot inherit WebAppFixture, it is sealed"), so this is a plain mutable
    /// instance property rather than a <c>protected virtual</c> override point. Each test class
    /// gets its own <see cref="WebAppFixture"/> instance via <c>IClassFixture&lt;WebAppFixture&gt;</c>,
    /// so a per-class opt-in does not leak into other test classes.
    /// </summary>
    public HashSet<Type> HostedServicesToRetain { get; } = [];

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            // Override all connection strings to use Testcontainers SQL Server
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = _sqlServer.ConnectionString,
                ["ConnectionStrings:Hangfire"] = _sqlServer.ConnectionString,
                ["Vault:Enabled"] = "false",
                // Disable RabbitMQ â€” use InMemory transport in tests
                ["MessageBus:Transport"] = "InMemory",
                // Use in-process cache for tests
                ["Redis:ConnectionString"] = string.Empty,
                ["AI:Ollama:Enabled"] = "false",
                ["Gateway:Mode"] = "Disabled", // prevents YARP + rate-limiter policy conflict in tests
                // W3 — DocFlow JWT signing key (HS256, ≥32 bytes) for integration tests.
                // Tests issue + validate JWT via IDocFlowJwtService; appsettings.Development.json
                // is NOT picked up in Testing environment, so seed via in-memory config here.
                ["Signing:DocFlowJwt:SigningKey"] = "DEV_ONLY_test_signing_key_for_HS256_min_32_bytes_W3_docflow_2026_05_11",
                ["Signing:DocFlowJwt:IssuerName"] = "AqtCoreFW.Test",
                ["Signing:DocFlowJwt:TtlSeconds"] = "300",
                ["Signing:DocFlow:BaseUrl"] = "http://localhost",
                // W6 — Mysign async-callback inbound credentials for integration tests.
                // Both fields must be non-empty to pass the 503-guard in MysignCallbackAuthController.
                ["Signing:Providers:ViettelMysign:CallbackInboundClientId"] = "test-callback-client",
                ["Signing:Providers:ViettelMysign:CallbackInboundClientSecret"] = "test-callback-secret-min-16-chars-W6",
                ["Signing:Providers:ViettelMysign:CallbackTokenTtlSeconds"] = "3600",
                ["Signing:Providers:ViettelMysign:PendingTxTtlHours"] = "24",
                ["Signing:Providers:ViettelMysign:PendingTxCleanupGraceMinutes"] = "60",
            });
        });

        builder.ConfigureTestServices(services =>
        {
            // Register test auth handler and force it as the default scheme.
            // PostConfigure runs AFTER all Configure registrations (including the app's
            // MultiAuth PolicyScheme), ensuring TestAuth wins as the authentication default.
            services.AddAuthentication()
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                    TestAuthHandler.SchemeName, _ => { });

            services.PostConfigure<AuthenticationOptions>(options =>
            {
                options.DefaultScheme = TestAuthHandler.SchemeName;
                options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                options.DefaultForbidScheme = TestAuthHandler.SchemeName;
            });

            // Replace outbound messaging providers with test doubles â€” prevents
            // real SMTP/webhook calls during integration tests (avoids 51s SMTP timeouts).
            services.RemoveAll<IEmailSender>();
            services.AddScoped<IEmailSender, FakeEmailSender>();
            services.RemoveAll<ISmsProvider>();
            services.AddScoped<ISmsProvider, FakeSmsProvider>();

            // Strip non-seeder hosted services (Hangfire, MassTransit, OutboxProcessor,
            // pub/sub, polling) — keep IIntegrationTestSeeder (plus any test opt-in via
            // HostedServicesToRetain) so seeder regressions surface in tests instead of
            // slipping to prod startup (false-green pattern). See HostedServiceStripFilter for
            // the retain predicate, including the factory-lambda fail-safe.
            for (var i = services.Count - 1; i >= 0; i--)
            {
                var d = services[i];
                if (d.ServiceType != typeof(IHostedService)) continue;
                if (!HostedServiceStripFilter.ShouldRetain(d, HostedServicesToRetain))
                {
                    services.RemoveAt(i);
                }
            }

            // Test-only authorization shortcut: any user holding "SystemAdmin" role claim
            // satisfies all PermissionRequirement gates without DB lookup. Production code
            // (PermissionAuthorizationHandler) still runs alongside; this is additive.
            // Avoids needing to seed full User+Role+Permission graph per test.
            services.AddSingleton<IAuthorizationHandler, TestSystemAdminPermissionBypassHandler>();

            // QĐ742 Wave-3: stub Vault-backed resolvers so integration tests run without Vault.
            // IKeyResolver — EncryptedStringConverter calls GetKey() when a Confidential property
            //   is read/written. Returning a fixed 32-byte key lets EF roundtrip encrypt/decrypt
            //   correctly within a single test run (each test uses a fresh DB schema via Testcontainers).
            // IPepperResolver — EmailHashedUserStore + EmailHashService call GetPepper() on every
            //   FindByEmail / save with an email change. Fixed pepper produces deterministic hashes
            //   across all operations within the test session.
            // Both are idempotent (same input → same output) so determinism is preserved.
            services.RemoveAll<IKeyResolver>();
            services.AddSingleton<IKeyResolver>(new TestKeyResolver());

            services.RemoveAll<IPepperResolver>();
            services.AddSingleton<IPepperResolver>(new TestPepperResolver());

            // IAuditHmacKeyResolver — HmacChainService calls GetKey() to compute HMAC signatures.
            // Without this stub, VaultAuditHmacKeyResolver throws KeyNotFoundException when Vault
            // is unavailable (Vault:Enabled=false), causing HTTP 500 on any chain signing/backfill
            // request routed through the DI-registered service (i.e., via the HTTP endpoint path).
            // Direct-service tests (NewService() stubs in HmacChainBackfillTests) are unaffected
            // by this registration since they construct HmacChainService with their own resolver.
            services.RemoveAll<IAuditHmacKeyResolver>();
            services.AddSingleton<IAuditHmacKeyResolver>(new TestAuditHmacKeyResolver());
        });
    }

    /// <summary>
    /// Additive auth handler for tests â€” succeeds any PermissionRequirement when the
    /// principal has the "SystemAdmin" role claim. Bypasses IEffectivePermissionService
    /// DB lookup which would fail for ephemeral test users not present in the AspNetUsers table.
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

    /// <summary>Creates an HttpClient pre-authenticated for the given tenant and roles.</summary>
    public HttpClient CreateAuthenticatedClient(Guid userId, Guid tenantId, params string[] roles)
        => CreateAuthenticatedClient(userId, tenantId, includeMfaClaim: false, roles);

    /// <summary>Creates an HttpClient pre-authenticated for the given tenant and roles, optionally including mfa_at claim.</summary>
    public HttpClient CreateAuthenticatedClient(Guid userId, Guid tenantId, bool includeMfaClaim, params string[] roles)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantIdHeader, tenantId.ToString());
        if (roles.Length > 0)
            client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, string.Join(",", roles));
        if (includeMfaClaim)
            client.DefaultRequestHeaders.Add(TestAuthHandler.MfaAtHeader, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString());
        return client;
    }

    public Task InitializeAsync()
    {
        // Environment variables have highest priority in .NET config (override appsettings.json).
        // ConfigureAppConfiguration on IWebHostBuilder only reaches the web host config layer,
        // not builder.Configuration (ConfigurationManager) used by AddDbContext lambdas.
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Testing");
        Environment.SetEnvironmentVariable("ConnectionStrings__Default", _sqlServer.ConnectionString);
        Environment.SetEnvironmentVariable("ConnectionStrings__Hangfire", _sqlServer.ConnectionString);
        Environment.SetEnvironmentVariable("Vault__Enabled", "false");
        Environment.SetEnvironmentVariable("MessageBus__Transport", "InMemory");
        return Task.CompletedTask;
    }

    public new Task DisposeAsync()
    {
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", null);
        Environment.SetEnvironmentVariable("ConnectionStrings__Default", null);
        Environment.SetEnvironmentVariable("ConnectionStrings__Hangfire", null);
        Environment.SetEnvironmentVariable("Vault__Enabled", null);
        Environment.SetEnvironmentVariable("MessageBus__Transport", null);
        base.Dispose();
        return Task.CompletedTask;
    }
}

/// <summary>
/// Test-only auth handler â€” reads claims from separate headers, no JWT needed.
/// Uses X-Test-UserId, X-Test-Roles, X-Test-TenantId, X-Test-MfaAt headers.
/// </summary>
public sealed class TestAuthHandler(
    Microsoft.Extensions.Options.IOptionsMonitor<AuthenticationSchemeOptions> options,
    Microsoft.Extensions.Logging.ILoggerFactory logger,
    System.Text.Encodings.Web.UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "TestAuth";
    public const string UserIdHeader = "X-Test-UserId";
    public const string RolesHeader = "X-Test-Roles";
    public const string TenantIdHeader = "X-Test-TenantId";
    public const string MfaAtHeader = "X-Test-MfaAt";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.ContainsKey(UserIdHeader))
            return Task.FromResult(AuthenticateResult.NoResult());

        var userId = Request.Headers[UserIdHeader].FirstOrDefault()!;
        var roles = Request.Headers[RolesHeader].FirstOrDefault()?.Split(',') ?? [];
        var tenantId = Request.Headers[TenantIdHeader].FirstOrDefault();
        var mfaAt = Request.Headers[MfaAtHeader].FirstOrDefault();

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId),
            new(ClaimTypes.Name, "Test User"),
            new(ClaimTypes.Email, "test@test.vn"),
        };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        if (tenantId != null) claims.Add(new("tenant_id", tenantId));
        if (mfaAt != null) claims.Add(new("mfa_at", mfaAt));

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(principal, SchemeName)));
    }
}
