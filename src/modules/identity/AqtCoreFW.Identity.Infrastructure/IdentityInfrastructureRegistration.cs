using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.Identity.Application.Authorization;
using AqtCoreFW.Identity.Application.Services;
using AqtCoreFW.Identity.Application.Signing;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.Identity.Domain.Services;
using AqtCoreFW.Identity.Infrastructure.Authentication;
using AqtCoreFW.Identity.Infrastructure.Authorization;
using AqtCoreFW.Identity.Infrastructure.Jobs;
using AqtCoreFW.Identity.Infrastructure.Services.Signing;
using AqtCoreFW.Identity.Infrastructure.BackgroundJobs;
using AqtCoreFW.Identity.Infrastructure.Services.Signing.ViettelMysign;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using Polly.Retry;
using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Contracts.Rtbf;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using AqtCoreFW.Identity.Infrastructure.Services;
using AqtCoreFW.Identity.Infrastructure.VNeID;
using Hangfire;
using MediatR;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AqtCoreFW.Identity.Infrastructure;

/// <summary>
/// Registers all Identity Infrastructure services.
/// Call from Api host: services.AddIdentityInfrastructure(configuration, env)
/// </summary>
public static class IdentityInfrastructureRegistration
{
    public static IServiceCollection AddIdentityInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment env)
    {
        // --- MediatR handlers in Infrastructure assembly (DbContext-dependent handlers) ---
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(IdentityInfrastructureRegistration).Assembly));

        // --- EF Core ---
        // Register IConfiguration as a service so IdentityDbContext can receive it via DI.
        // IdentityDbContext has a (DbContextOptions, IConfiguration) ctor â€” DI resolves both.
        services.AddDbContext<IdentityDbContext>((sp, options) =>
        {
            options.UseSqlServer(
                NonAuditConnectionString.Get(configuration),
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "identity"));

            // OpenIddict EF Core stores registered on IdentityDbContext
            options.UseOpenIddict();

            options.AddInterceptors(
                sp.GetRequiredService<SlowQueryInterceptor>(),
                sp.GetRequiredService<SoftDeleteInterceptor>(),
                sp.GetRequiredService<OutboxInterceptor>(),
                sp.GetRequiredService<AuditableEntityInterceptor>(),
                sp.GetRequiredService<TenantSessionContextInterceptor>(),
                // QĐ742 §4.4 Wave-3: populate EmailHash/NormalizedEmailHash on every user save.
                sp.GetRequiredService<EmailHashInterceptor>());

            // Suppress PendingModelChangesWarning during startup migrations.
            // Model drift during development is expected until all pending migrations are applied.
            options.ConfigureWarnings(w =>
                w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
        });

        // --- ASP.NET Core Identity ---
        services.AddIdentity<ApplicationUser, ApplicationRole>(opts =>
            {
                // Password policy per QÄ742 â€” teams tighten via configuration
                opts.Password.RequiredLength = 12;
                opts.Password.RequireNonAlphanumeric = true;
                opts.Password.RequireDigit = true;
                opts.Password.RequireUppercase = true;
                // Fallback only — TenantAwareUserManager overrides AccessFailedAsync to read
                // per-tenant CredentialPolicy.MaxFailedAttempts/LockoutMinutes (QĐ 742 1.4 / FU2).
                // These values apply when the user has no tenant or no policy is configured.
                opts.Lockout.MaxFailedAccessAttempts = 5;
                opts.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                opts.User.RequireUniqueEmail = true;
            })
            .AddEntityFrameworkStores<IdentityDbContext>()
            .AddDefaultTokenProviders()
            .AddUserManager<TenantAwareUserManager>()
            // QĐ742 §4.4 Wave-3: override default UserStore so FindByEmailAsync uses
            // NormalizedEmailHash column instead of the encrypted NormalizedEmail column.
            // Must come AFTER AddEntityFrameworkStores — last registration wins in DI.
            .AddUserStore<EmailHashedUserStore>();

        // --- OpenIddict (resource server validation â€” remote discovery via AuthServer) ---
        services.AddOpenIddict()
            .AddCore(opts => opts.UseEntityFrameworkCore().UseDbContext<IdentityDbContext>())
            .AddValidation(opts =>
            {
                // AuthorityUrl must match the issuer AuthServer advertises in discovery doc.
                // In Docker: use host.docker.internal to reach the host-mapped AuthServer port.
                // Local dev: use http://localhost:5002 (AuthServer local port).
                var authority = configuration["Auth:AuthorityUrl"]
                    ?? throw new InvalidOperationException("Auth:AuthorityUrl is required.");
                opts.SetIssuer(authority);

                // Use introspection for reference token validation â€” API sends token
                // to AuthServer's /connect/introspect endpoint for verification
                var clientId = AuthResourceClient.ResolveClientId(configuration);
                var clientSecret = AuthResourceClient.ResolveSecret(configuration, env.EnvironmentName);
                opts.UseIntrospection()
                    .SetClientId(clientId)
                    .SetClientSecret(clientSecret);

                opts.UseSystemNetHttp();
                opts.UseAspNetCore();
            });

        // --- QĐ742 §4.4 Wave-3: Email hashing (HMAC-SHA-256 per-tenant pepper) ---
        // EmailHashService: Scoped — IPepperResolver is Singleton, safe to scope downward.
        services.AddScoped<EmailHashService>();
        // EmailHashInterceptor: Scoped — resolved fresh per DbContext lifetime (also Scoped).
        services.AddScoped<EmailHashInterceptor>();

        // --- Repositories ---
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IAccessReviewRepository, AccessReviewRepository>();
        services.AddScoped<ISessionRepository, SessionRepository>();
        services.AddScoped<IConsentRepository, ConsentRepository>();
        services.AddScoped<IDelegationRepository, DelegationRepository>();
        services.AddScoped<IAttributeRuleRepository, AttributeRuleRepository>();
        services.AddScoped<IExternalIdentityRepository, ExternalIdentityRepository>();
        services.AddScoped<ICredentialPolicyRepository, CredentialPolicyRepository>();
        services.AddScoped<IJitProviderConfigRepository, JitProviderConfigRepository>();
        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<ITenantRepository, TenantRepository>();
        services.AddScoped<IIpFilterRuleRepository, IpFilterRuleRepository>();
        services.AddScoped<AqtCoreFW.Infrastructure.Middleware.IIpRuleSetLoader, Services.IpRuleSetLoader>();
        services.AddScoped<ITenantSignatureConfigRepository, TenantSignatureConfigRepository>();
        services.AddScoped<ISignerCertEnrollmentRepository, SignerCertEnrollmentRepository>();
        services.AddScoped<ISignerEnrollmentNonceRepository, SignerEnrollmentNonceRepository>();
        services.AddScoped<IUserSignatureProfileRepository, UserSignatureProfileRepository>();
        // Anti-impersonation: cert==assigned-signer binding (enrolled key first, CCCD-subject fallback).
        services.AddScoped<ISignerBindingGuard, Services.Signing.SignerBindingGuard>();

        // --- Digital Signature (QĐ 742 §5.1, phase-05b W6) ---
        // Bind Signing:TrustStore config section (RootCertPaths + IntermediateCertPaths).
        services.Configure<VnCaTrustStoreOptions>(
            configuration.GetSection(VnCaTrustStoreOptions.SectionName));

        // VnCaRootStore: Singleton — trust anchor store loaded from config at startup.
        // When RootCertPaths is empty, falls back to empty ctor (PRODUCTION_SEED_DEFERRED).
        services.AddSingleton<VnCaRootStore>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<VnCaTrustStoreOptions>>().Value;
            var logger = sp.GetRequiredService<ILogger<VnCaRootStore>>();
            var ders = TrustRootLoader.LoadDer(opts.RootCertPaths, logger);

            if (ders.Count == 0)
                return new VnCaRootStore(); // deferred fallback — health-check will warn

            // Internal ctor accessible because IdentityInfrastructureRegistration is in the same assembly
            var store = new VnCaRootStore(ders);
            var parser = new Org.BouncyCastle.X509.X509CertificateParser();
            foreach (var der in ders)
            {
                var cert = parser.ReadCertificate(der);
                logger.LogInformation(
                    "Loaded root CA certificate. Subject={Subject} Thumbprint={Thumbprint}",
                    cert.SubjectDN,
                    VnCaRootStore.ComputeThumbprint(cert.GetEncoded()));
            }
            return store;
        });

        // VnCaIntermediateStore: Singleton — path-building candidates loaded from config.
        // Intermediates are added to allCerts for PKIX bridging but never to trustAnchors or pin-set.
        services.AddSingleton<VnCaIntermediateStore>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<VnCaTrustStoreOptions>>().Value;
            var logger = sp.GetRequiredService<ILogger<VnCaIntermediateStore>>();
            var ders = TrustRootLoader.LoadDer(opts.IntermediateCertPaths, logger);

            var store = new VnCaIntermediateStore(ders);

            var parser = new Org.BouncyCastle.X509.X509CertificateParser();
            foreach (var der in ders)
            {
                var cert = parser.ReadCertificate(der);
                logger.LogInformation(
                    "Loaded intermediate CA certificate (path-building candidate). Subject={Subject} Thumbprint={Thumbprint}",
                    cert.SubjectDN,
                    VnCaRootStore.ComputeThumbprint(cert.GetEncoded()));
            }
            return store;
        });

        // ICertificateChainValidator: Scoped — BouncyCastle PKIX chain builder, delegates to VnCaRootStore + VnCaIntermediateStore.
        services.AddScoped<ICertificateChainValidator, CertificateChainValidator>();
        // IStructuredSignatureService: Scoped — Tier-2 XMLDSig + JWS compact verification (W2).
        services.AddScoped<IStructuredSignatureService, StructuredSignatureService>();
        // IDigitalSignatureProvider keyed registrations — resolved by SignatureProviderResolver.
        // Also registered unkeyed for IEnumerable<> discovery in GetAvailableProviders() (D6 extensibility).
        services.AddKeyedScoped<IDigitalSignatureProvider, UsbTokenSignatureProvider>("usb-vgca");
        services.AddScoped<IDigitalSignatureProvider, UsbTokenSignatureProvider>();
        services.AddKeyedScoped<IDigitalSignatureProvider, ViettelMysignProvider>("remote-viettel-mysign");
        services.AddScoped<IDigitalSignatureProvider, ViettelMysignProvider>();
        // ISignatureProviderResolver: Scoped — wraps IServiceProvider keyed lookups.
        services.AddScoped<ISignatureProviderResolver, SignatureProviderResolver>();

        // Signing orchestration facade (Phase 1 foundation). Strategies own one cell of the
        // (artifact kind × capabilities) matrix each; the router resolves exactly one per request.
        services.AddScoped<ISigningStrategy, Services.Signing.Strategies.DocFlowSigningStrategy>();
        services.AddScoped<ISigningStrategy, Services.Signing.Strategies.DetachedDataSigningStrategy>();
        services.AddScoped<ISigningStrategy, Services.Signing.Strategies.PadesAssemblerSigningStrategy>();
        // PAdES async-poll initiator backing the server-side strategy (login → cert-info → signHash async=2).
        services.AddScoped<Services.Signing.Pades.IPadesTransactionInitiator, Services.Signing.Pades.PadesTransactionInitiator>();
        // PAdES async-poll completion (status poll → splice CMS → store signed PDF); driven by PadesCompletionPollJob.
        services.AddScoped<Services.Signing.Pades.IPadesCompletionService, Services.Signing.Pades.PadesCompletionService>();
        services.AddScoped<SigningStrategyRouter>();
        // providerKey → EnrollmentKind source of truth (stateless). Lives in Infrastructure/Services/Signing so
        // the vendor key literals stay out of Domain/Application (D6); the guard consumes the abstraction.
        services.AddSingleton<IProviderEnrollmentKindMap, Services.Signing.ProviderEnrollmentKindMap>();
        // Fail-closed provider allowlist guard — called first by the orchestrator (shared chokepoint) to deny a
        // request whose assigned signer holds no Approved enrollment of the providerKey's EnrollmentKind.
        services.AddScoped<ISigningProviderAllowlistGuard, SigningProviderAllowlistGuard>();
        services.AddScoped<ISignatureOrchestrator, SignatureOrchestrator>();
        // Visible-stamp text composer (sanitize + assemble display lines); the P5 PDF writer draws the result.
        services.AddScoped<ISignatureStampComposer, SignatureStampComposer>();
        // Visible-stamp rasterizer (QuestPDF → baseline JPEG); consumed by PadesTransactionInitiator at initiate.
        services.AddScoped<ISignatureStampRasterizer, Services.Signing.SignatureStampRasterizer>();
        // IMemoryCache: required by Viettel Mysign token + cert-info caches.
        services.AddMemoryCache();
        // Viettel Mysign typed HTTP clients:
        //   Login + CertInfo: Polly retry pipeline (3 attempts, exponential 200/600/1800 ms).
        //   SignHash: NO retry — each call triggers a mobile confirmation prompt.
        services.AddHttpClient<ViettelMysignLoginClient>()
            .AddResilienceHandler("mysign-login-retry", pipeline =>
            {
                pipeline.AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = 3,
                    Delay = TimeSpan.FromMilliseconds(200),
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = false,
                    ShouldHandle = static args =>
                        ValueTask.FromResult(
                            args.Outcome.Exception is HttpRequestException ||
                            (args.Outcome.Result?.IsSuccessStatusCode == false &&
                             (int)args.Outcome.Result.StatusCode >= 500)),
                });
            });
        services.AddHttpClient<ViettelMysignCertInfoClient>()
            .AddResilienceHandler("mysign-certinfo-retry", pipeline =>
            {
                pipeline.AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = 3,
                    Delay = TimeSpan.FromMilliseconds(200),
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = false,
                    ShouldHandle = static args =>
                        ValueTask.FromResult(
                            args.Outcome.Exception is HttpRequestException ||
                            (args.Outcome.Result?.IsSuccessStatusCode == false &&
                             (int)args.Outcome.Result.StatusCode >= 500)),
                });
            });
        // SignHash: plain HttpClient, no retry pipeline.
        services.AddHttpClient<ViettelMysignSignHashClient>();
        // StatusPoll: Polly retry (idempotent read — retry is safe unlike SignHash).
        services.AddHttpClient<ViettelMysignStatusPollClient>()
            .AddResilienceHandler("mysign-statuspoll-retry", pipeline =>
            {
                pipeline.AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = 3,
                    Delay = TimeSpan.FromMilliseconds(200),
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = false,
                    ShouldHandle = static args =>
                        ValueTask.FromResult(
                            args.Outcome.Exception is HttpRequestException ||
                            (args.Outcome.Result?.IsSuccessStatusCode == false &&
                             (int)args.Outcome.Result.StatusCode >= 500)),
                });
            });
        services.Configure<ViettelMysignOptions>(
            configuration.GetSection(ViettelMysignOptions.SectionName));
        // M-1: startup health check — warns when VnCaRootStore is empty or tenants are enabled without roots.
        services.AddHostedService<SigningHealthCheckService>();
        // W6: Inbound token service for Mysign callback auth endpoint.
        services.AddScoped<IMysignInboundTokenService, MysignInboundTokenService>();
        // W6: Cleanup job — marks Initiated transactions past ExpiresAt + grace as Expired.
        // NOT registered in Testing environment — WebAppFixture strips non-seeder hosted services.
        if (!env.IsEnvironment("Testing"))
        {
            services.AddHostedService<PendingTransactionCleanupJob>();
            // Drives PAdES async-poll completion: poll Mysign for Initiated PAdES rows → splice CMS → store signed PDF.
            services.AddHostedService<PadesCompletionPollJob>();
            // Hourly sweep — hard-deletes consumed/expired SignerEnrollmentNonce rows so the table stays bounded.
            services.AddHostedService<EnrollmentNonceCleanupJob>();
        }

        // --- Doc-flow JWT + workflow (QĐ 742 §5.1, phase-05b W3) ---
        // DocFlowJwtOptions: sourced from appsettings Signing:DocFlowJwt; in production override via Vault.
        services.Configure<DocFlowJwtOptions>(
            configuration.GetSection("Signing:DocFlowJwt"));
        // IDocFlowJwtService: Singleton — stateless HS256 token issuer/validator.
        services.AddSingleton<IDocFlowJwtService, DocFlowJwtService>();
        // IDocumentSigningWorkflow: Scoped — uses IdentityDbContext (scoped) + IFileStorageService.
        services.AddScoped<IDocumentSigningWorkflow, DocumentSigningWorkflow>();

        // --- Seeder options (bindings only — hosted-service registration lives at the host) ---
        // TenantSeeder / TenantBackfillSeeder / TenantRoleSeeder / TenantUserSeeder are registered
        // by the API host in an explicit dependency order (TenantSeeder → TenantBackfillSeeder →
        // TenantRoleSeeder → OrgTreeSeeder → TenantUserSeeder → TenantAssignmentSeeder):
        // TenantUserSeeder reads organization.OrgUnits so the Organization module's seeders must
        // interleave with these, and the canonical TenantSeeder must beat the backfill's derived
        // "TENANT-xxxx" fallback rows. Module-local registration cannot express that order.
        services.Configure<Seeding.TenantSeederOptions>(
            configuration.GetSection(Seeding.TenantSeederOptions.SectionName));
        services.Configure<Seeding.TenantRoleSeederOptions>(
            configuration.GetSection(Seeding.TenantRoleSeederOptions.SectionName));
        services.Configure<Seeding.TenantUserSeederOptions>(
            configuration.GetSection(Seeding.TenantUserSeederOptions.SectionName));

        // --- Claims enrichment (roles + delegation â€” cached in Redis) ---
        services.AddTransient<IClaimsTransformation, ClaimsEnrichmentTransformer>();

        // --- MFA (TOTP + Email OTP via Hangfire + enrolment flow) ---
        services.AddScoped<IMfaService, MfaService>();
        services.AddScoped<EmailOtpJob>();

        // --- MFA Policy (QÄ742 1.6.a) ---
        services.AddScoped<IMfaPolicyRepository, MfaPolicyRepository>();
        services.AddScoped<IMfaPolicyResolver, MfaPolicyResolver>();
        services.AddScoped<IMfaEmergencyDisableRequestRepository, MfaEmergencyDisableRequestRepository>();

        // --- Recovery codes (BCrypt work-factor 11, 16 one-time codes) ---
        services.AddScoped<IRecoveryCodeService, RecoveryCodeService>();

        // --- VNeID connector (stub for Development/Testing/Benchmark; production requires real VNeIdHttpConnector) ---
        // "VneId:AllowStub" (default false) is a REHEARSAL-ONLY escape hatch: a local/staging
        // Production-mode cluster can boot with the stub while VNeIdHttpConnector does not exist yet.
        // Real production deploys must NOT set it — the fail-fast throw below stays the default so a
        // prod rollout without a real connector still refuses to boot instead of silently stubbing
        // citizen identity. Set only in infra/helm/*/values-local.yaml.
        if (IsVneIdStubAllowed(env, configuration))
            services.AddSingleton<IVneIdConnector, VNeIdStubConnector>();
        else
            throw new InvalidOperationException(
                "VNeIdHttpConnector is not implemented. Register a production IVneIdConnector before deploying, " +
                "or set VneId:AllowStub=true (rehearsal environments ONLY).");

        // --- Password expiry (QÄ742 policy) ---
        services.AddScoped<PasswordExpiryService>();

        // --- Bulk role cache invalidation (Hangfire job) ---
        services.AddScoped<BulkRoleChangeService>();
        services.AddScoped<IBulkRoleChangeService>(sp => sp.GetRequiredService<BulkRoleChangeService>());

        // --- Permission version counter (Redis atomic increment for cache invalidation) ---
        services.AddScoped<IPermissionVersionService, PermissionVersionService>();

        // --- Delegation expiry job (Hangfire recurring â€” marks overdue Active delegations as Expired) ---
        services.AddScoped<DelegationExpiryJob>();

        // --- SoD audit log purge job (Hangfire recurring â€” deletes logs older than 30d) ---
        services.AddScoped<SodAuditLogPurgeJob>();

        // --- Access review scheduler (Hangfire recurring â€” every 6 months) ---
        services.AddScoped<AccessReviewSchedulerJob>();

        // --- ABAC authorization handler (Two-Tier cache L1 + L2 Redis + cross-pod pub/sub) ---
        // IMemoryCache: registered transitively via shared Infrastructure (TwoTierCacheService dep).
        services.AddScoped<IAuthorizationHandler, AbacAuthorizationHandler>();
        services.AddScoped<IAttributeRuleReader, AttributeRuleReaderAdapter>();
        // Cache invalidator â€” inject into any AttributeRule write command to bust stale ABAC cache (F-17).
        // Singleton: ICacheService + IConnectionMultiplexer Ä‘á»u Singleton â†’ lifetime-compatible.
        services.AddSingleton<IAbacCacheInvalidator, AbacCacheInvalidator>();
        // Cross-pod L1 invalidation subscriber â€” listens Redis channel "abac-invalidate".
        services.AddHostedService<AbacCacheSubscriber>();
        // T4-05-FU: periodic safety net (15-min sweep) for residual subscriber-fail risk
        // after R2 retry exhaustion. Mirrors FeatureFlagRefreshService precedent.
        services.AddHostedService<AbacCacheRefreshService>();

        // --- Data scope resolution (role + user-override merge, Redis-cached) ---
        services.AddScoped<IDataScopeService, DataScopeService>();

        // --- Policy rule evaluation (Redis-cached, TTL 5 min) ---
        services.AddScoped<IPolicyRuleEvaluator, PolicyRuleEvaluator>();

        // --- Effective permission resolution (direct + group + delegation, Redis-cached TTL 10 min) ---
        services.AddScoped<IEffectivePermissionService, EffectivePermissionService>();

        // --- SoD conflict detection (compares user's effective perms vs candidate role perms) ---
        services.AddScoped<ISodConflictChecker, SodConflictChecker>();

        // --- Menu authorization (permission-filtered tree, Redis-cached TTL 30 min per tenant) ---
        services.AddScoped<IMenuService, MenuService>();

        // --- Permission-code authorization handler ---
        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();

        // --- QÄ 742 auth lifecycle: lockout escalation + MFA enforcement ---
        // LockoutEscalationService: tracks consecutive lockouts, auto-disables account on threshold (1.4.d)
        services.AddScoped<ILockoutEscalationService, LockoutEscalationService>();
        // MFA enforcement: dynamic policy provider + handler for [RequiresMfa] attribute (1.6.b)
        // Must be registered AFTER AddIdentity/AddAuthorization â€” overrides the default policy provider.
        services.AddSingleton<IAuthorizationPolicyProvider, MfaPolicyProvider>();
        services.AddScoped<IAuthorizationHandler, MfaRequirementHandler>();
        // Tags 403 from MFA requirement with X-Mfa-Required so FE can redirect to enrolment / step-up.
        services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationMiddlewareResultHandler,
            Authorization.MfaForbidResultHandler>();

        // RTBF PII anonymizer
        services.AddScoped<IModulePiiAnonymizer, IdentityPiiAnonymizer>();

        // Cross-module identity lookup (used by Audit RTBF, etc.)
        services.AddScoped<SharedKernel.Contracts.Clients.IIdentityModuleClient, InProcessIdentityModuleClient>();

        // Cross-module access-review aggregator (used by Reporting.ComplianceEvidence)
        services.AddScoped<SharedKernel.Contracts.Clients.IIdentityAuditClient, InProcessIdentityAuditModuleClient>();

        // Cross-module PDPL consent recording (used by Forms module)
        services.AddScoped<SharedKernel.Contracts.Clients.IConsentModuleClient, InProcessConsentModuleClient>();

        // Cross-module digital-signature verify (used by Forms verify-at-ingest)
        services.AddScoped<SharedKernel.Contracts.Clients.IIdentitySigningClient, Services.Signing.InProcessIdentitySigningClient>();

        // Cross-module multi-signer session gate (used by BusinessProcess sign-gate step)
        services.AddScoped<SharedKernel.Contracts.Clients.IDocumentSignatureSessionClient,
            Services.Signing.InProcessDocumentSignatureSessionClient>();

        // Signing step query service — thin EF read surface for Files + Forms step listing/download
        services.AddScoped<Services.Signing.SigningStepQueryService>();

        // Cross-module signing-request initiation + step reads (used by Forms and Files)
        services.AddScoped<SharedKernel.Contracts.Clients.ISigningRequestClient,
            Services.Signing.InProcessSigningRequestClient>();

        return services;
    }

    /// <summary>
    /// Whether the VNeID stub connector may be registered: always in Development/Testing/Benchmark,
    /// and in any other environment ONLY when "VneId:AllowStub" is explicitly true (rehearsal
    /// clusters). Public so the fail-fast branch is unit-testable without the full DI graph —
    /// the false branch throws in <see cref="AddIdentityInfrastructure"/>.
    /// </summary>
    public static bool IsVneIdStubAllowed(IWebHostEnvironment env, IConfiguration configuration) =>
        env.IsDevelopment() || env.IsEnvironment("Testing") || env.IsEnvironment("Benchmark")
        || configuration.GetValue<bool>("VneId:AllowStub");

    /// <summary>
    /// Registers Hangfire recurring jobs for the Identity module.
    /// Called from the API host startup after Hangfire server is configured.
    /// </summary>
    public static void AddIdentityRecurringJobs()
    {
        // Every 6 months on the 1st day at midnight
        RecurringJob.AddOrUpdate<AccessReviewSchedulerJob>(
            recurringJobId: "identity.access-review-scheduler",
            methodCall: j => j.ExecuteAsync(),
            cronExpression: "0 0 1 */6 *");

        // Daily at 02:00 UTC â€” purge SoD audit logs older than 30 days
        RecurringJob.AddOrUpdate<SodAuditLogPurgeJob>(
            recurringJobId: "sod-audit-log-purge",
            methodCall: j => j.ExecuteAsync(),
            cronExpression: Cron.Daily(2, 0),
            options: new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });
    }
}
