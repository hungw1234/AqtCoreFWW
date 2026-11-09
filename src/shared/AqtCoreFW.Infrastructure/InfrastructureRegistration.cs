using AqtCoreFW.Infrastructure.BackgroundJobs;
using AqtCoreFW.Infrastructure.Caching;
using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.Infrastructure.Events;
using AqtCoreFW.Infrastructure.Export;
using AqtCoreFW.Infrastructure.Extensions;
using AqtCoreFW.Infrastructure.HealthChecks;
using AqtCoreFW.Infrastructure.Persistence;
using AqtCoreFW.Infrastructure.Persistence.Outbox;
using AqtCoreFW.Infrastructure.RateLimiting;
using AqtCoreFW.Infrastructure.Resilience;
using AqtCoreFW.Infrastructure.Search;
using AqtCoreFW.Infrastructure.Security;
using Microsoft.AspNetCore.Http;
using AqtCoreFW.Infrastructure.Telemetry;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Application.Caching;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Domain.Events;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace AqtCoreFW.Infrastructure;

/// <summary>
/// Cross-cutting infrastructure DI registration — called once in Program.cs.
/// Registers: logging, telemetry, caching, rate limiting, health checks, CORS,
/// response compression, EF interceptors, outbox processor, circuit breaker registry.
/// Module-specific infra registered in each module's own InfrastructureRegistration.
/// </summary>
public static class InfrastructureRegistration
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Register global Dapper type handlers — fixes DateTimeOffset materialization from datetime2.
        // Idempotent; safe across host restarts and test fixtures.
        DapperTypeHandlers.Register();

        // --- Options ---
        services.Configure<DatabaseOptions>(configuration.GetSection(DatabaseOptions.SectionName));
        services.PostConfigure<DatabaseOptions>(opts =>
        {
            if (string.IsNullOrEmpty(opts.ConnectionString))
                opts.ConnectionString = configuration.GetConnectionString("Default") ?? string.Empty;
        });
        services.Configure<OutboxOptions>(configuration.GetSection(OutboxOptions.SectionName));
        services.Configure<CacheOptions>(configuration.GetSection(CacheOptions.SectionName));

        // --- Core plumbing ---
        services.AddHttpContextAccessor();
        // ICurrentUser / ITenantContext — resolved from JWT claims in active HTTP request
        services.AddScoped<ICurrentUser, HttpContextCurrentUser>();
        // Async-local tenant override for scopes with no HttpContext (MassTransit consume — Slice 1D).
        services.AddSingleton<AmbientTenantScope>();
        // ITenantContext resolution order:
        //   1. HttpContext present        → tenant from JWT claims (web requests)
        //   2. AmbientTenantScope pushed   → tenant from the message payload (consume scope, Slice 1D)
        //   3. otherwise                   → cross-tenant background-job context (Hangfire)
        // Branch 2 is additive: the HttpContext and Hangfire paths are unchanged.
        services.AddScoped<ITenantContext>(sp =>
        {
            var httpAccessor = sp.GetRequiredService<IHttpContextAccessor>();
            if (httpAccessor.HttpContext is not null)
                return new HttpContextTenantContext(httpAccessor);

            var ambient = sp.GetRequiredService<AmbientTenantScope>();
            return ambient.Current is { } state
                ? new ScopedTenantContext(state.TenantId)
                : new BackgroundJobs.BackgroundJobTenantContext();
        });
        services.AddScoped<IDomainEventPublisher, MediatRDomainEventPublisher>();
        // One shared connection + transaction per request scope — enlisting multiple DbContexts
        // onto it avoids DTC promotion when two modules write inside the same logical operation.
        // Dormant until a handler calls BeginAsync; zero behavior change for all other paths.
        services.AddScoped<ICrossModuleUnitOfWork, CrossModuleUnitOfWork>();
        // Module-agnostic Outbox escape hatch for entities that aren't (yet) an IAggregateRoot
        // captured by OutboxInterceptor — writes ride the ICrossModuleUnitOfWork transaction above.
        services.AddScoped<IOutboxWriter, OutboxWriter>();
        // Read-path login isolation (ISO-DAPPER): the DEFAULT (unkeyed) reader runs on the
        // least-privilege NonAudit login (denied [audit]); audit-capable query handlers resolve the
        // keyed "audit" reader on the broad Default (audit-owner) login via
        // [FromKeyedServices(ReadConnectionKeys.Audit)]. Opt-in — NonAuditConnectionString.Get falls
        // back to Default when the NonAudit login is unprovisioned, so dev/test/CI boot unchanged.
        // Both registrations keep the same tenant/UoW dependencies; only the login differs.
        services.AddScoped<IReadDbConnection>(sp => new DapperReadDbConnection(
            sp.GetRequiredService<IOptionsMonitor<DatabaseOptions>>(),
            sp.GetRequiredService<ITenantContext>(),
            sp.GetRequiredService<ICrossModuleUnitOfWork>(),
            NonAuditConnectionString.Get(configuration)));
        services.AddKeyedScoped<IReadDbConnection>(ReadConnectionKeys.Audit, (sp, _) => new DapperReadDbConnection(
            sp.GetRequiredService<IOptionsMonitor<DatabaseOptions>>(),
            sp.GetRequiredService<ITenantContext>(),
            sp.GetRequiredService<ICrossModuleUnitOfWork>(),
            configuration.GetConnectionString("Default")));

        // IGuidGenerator (V7GuidGenerator singleton) — required by OutboxInterceptor + middleware.
        // Test fixtures override with FakeGuidGenerator post-AddInfrastructure.
        services.AddSharedKernelGuidGenerator();

        // --- Export (IExcelExporter, IPdfExporter) ---
        services.AddExport();

        // --- EF interceptors ---
        services.AddSingleton<SlowQueryInterceptor>();
        services.AddSingleton<SoftDeleteInterceptor>();
        services.AddSingleton<OutboxInterceptor>();
        services.AddScoped<AuditableEntityInterceptor>();
        services.AddScoped<TenantSessionContextInterceptor>();

        // Outbox dispatcher (Phase 02c, v2.133) — polling loop publishes IExternalDomainEvent
        // rows captured by OutboxInterceptor through MassTransit at-least-once.
        services.AddSingleton<IOutboxStore, OutboxStore>();
        // Stable-token (Type.FullName) event-type resolver — built once from a scan of
        // loaded AqtCoreFW.* assemblies, with legacy AssemblyQualifiedName fallback.
        services.AddSingleton<IOutboxEventTypeResolver, OutboxEventTypeResolver>();
        services.AddScoped<OutboxDispatcher>();
        services.AddHostedService<OutboxProcessor>();

        // Default schema allowlist — 12 module DbContexts that inherit ModuleDbContext.
        // Modules CAN re-register via services.AddOutboxSchema(...) — registration is idempotent.
        services.AddOutboxSchema("ai");
        services.AddOutboxSchema("assetmanagement");
        services.AddOutboxSchema("businessprocess");
        services.AddOutboxSchema("collab");
        services.AddOutboxSchema("config");
        services.AddOutboxSchema("contracts");
        services.AddOutboxSchema("files");
        services.AddOutboxSchema("forms");
        services.AddOutboxSchema("integration");
        services.AddOutboxSchema("masterdata");
        services.AddOutboxSchema("notifications");
        services.AddOutboxSchema("organization");
        services.AddOutboxSchema("reporting");
        services.AddOutboxSchema("rules");
        services.AddOutboxSchema("workflow");

        // --- Redis + two-tier cache ---
        var redisConnStr = configuration["Redis:ConnectionString"];
        if (string.IsNullOrEmpty(redisConnStr)) redisConnStr = "localhost:6379";
        services.AddSingleton<IConnectionMultiplexer>(_ =>
        {
            var config = ConfigurationOptions.Parse(redisConnStr);
            config.AbortOnConnectFail = false; // C-05: app starts even if Redis is down
            config.ConnectRetry = 3;
            config.ConnectTimeout = 5000;
            return ConnectionMultiplexer.Connect(config);
        });

        services.AddMemoryCache(opt =>
            opt.SizeLimit = configuration.GetValue<int>("Cache:L1:MaxEntries", 5000));

        services.AddSingleton<ICacheService, TwoTierCacheService>();

        // --- IDistributedCache backed by Redis (used by Forms ExternalDataSourceResolver, AI CachedEmbedding) ---
        services.AddStackExchangeRedisCache(opts => opts.Configuration = redisConnStr);

        // --- Resilience ---
        services.AddSingleton<ICircuitBreakerRegistry, InMemoryCircuitBreakerRegistry>();

        // --- Outbound HttpClient propagation ---
        services.AddTransient<TenantPropagationHandler>();

        // --- Rate limiter (app-level; YARP adds gateway-level in Phase 06) ---
        // Pass configuration to support RateLimiting:Enabled flag (Benchmark env = false)
        services.AddGovRateLimiter(configuration);

        // --- Health checks ---
        services.AddGovHealthChecks(configuration);

        // --- OpenTelemetry (traces + metrics via OTLP) ---
        services.AddOpenTelemetryObservability(configuration);

        // --- i18n ---
        services.AddLocalization();
        services.Configure<RequestLocalizationOptions>(opts =>
        {
            var supported = new[] { "vi", "en" };
            opts.DefaultRequestCulture = new Microsoft.AspNetCore.Localization.RequestCulture("vi");
            opts.SupportedCultures = supported
                .Select(c => new System.Globalization.CultureInfo(c)).ToList();
            opts.SupportedUICultures = opts.SupportedCultures;
        });

        // --- CORS --- (allow-list isolated in GovApiCorsExtensions + locked by GovApiCorsPolicyTests)
        services.AddGovApiCors(configuration);

        // --- Antiforgery (X-XSRF-TOKEN header) ---
        services.AddAntiforgery(opts => opts.HeaderName = "X-XSRF-TOKEN");

        // --- Response compression (Brotli + Gzip) ---
        services.AddResponseCompression(opts =>
        {
            opts.EnableForHttps = true;
            opts.MimeTypes = Microsoft.AspNetCore.ResponseCompression.ResponseCompressionDefaults
                .MimeTypes
                .Concat(["application/json", "application/problem+json"]);
        });

        // --- Background jobs (Hangfire + NĐ53 archive) ---
        services.AddHangfireJobs(configuration);

        // --- Search (SQL FTS default; switch via Search:Provider = "Elasticsearch") ---
        services.AddSearchInfrastructure(configuration);

        // --- Extension Framework (M17) ---
        services.AddExtensionFramework();

        // --- Graceful shutdown ---
        services.Configure<HostOptions>(opts =>
            opts.ShutdownTimeout = TimeSpan.FromSeconds(30));

        return services;
    }
}
