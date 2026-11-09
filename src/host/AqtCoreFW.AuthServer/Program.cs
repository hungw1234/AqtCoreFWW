using AqtCoreFW.Audit.Infrastructure;
using AqtCoreFW.AuthServer.Models;
using Hangfire;
using Hangfire.Dashboard;
using AqtCoreFW.Infrastructure.BackgroundJobs;
using AqtCoreFW.AuthServer.Services;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Infrastructure.Messaging;
using AqtCoreFW.Infrastructure.Security;
using MassTransit;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.Identity.Infrastructure.Authentication;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using AqtCoreFW.Infrastructure.Caching;
using AqtCoreFW.SharedKernel.Application.Caching;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using Serilog;
using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.Infrastructure.Persistence;
using AqtCoreFW.Infrastructure.Persistence.Outbox;
using AqtCoreFW.SharedKernel.Application.Data;

namespace AqtCoreFW.AuthServer;

public partial class Program
{
    public static async Task Main(string[] args)
    {
        // Detect (and strip) `--migrate-only` before CreateBuilder — a bare token with no value breaks
        // CommandLineConfigurationProvider ("Unrecognized argument format"). See StartupMigrationMode.
        var hasMigrateOnlyArg = StartupMigrationMode.HasMigrateOnlyArg(args);
        var builder = WebApplication.CreateBuilder(StartupMigrationMode.StripKnownArgs(args));

        // === Serilog structured logging ===
        // Phase 06 S4a: destructure policy masks secret-named properties in structured logs.
        // Inline (not via shared AddSerilogLogging) to preserve AuthServer's console-only log
        // format without adding rolling-file output that Api host uses.
        builder.Host.UseSerilog((ctx, cfg) =>
            cfg.ReadFrom.Configuration(ctx.Configuration)
               .Destructure.With<AqtCoreFW.Infrastructure.Logging.SecretMaskingDestructuringPolicy>());

        // === Vault secrets — D10.1: mirrors Api/Program.cs:74-87 exactly ===
        // Must be before GetConnectionString so Vault-sourced connstring is available.
        builder.Configuration.AddVaultConfiguration(builder.Configuration);

        var connectionString = builder.Configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' not found.");

        // === EF Core + OpenIddict stores ===
        // D10.1 dual-host parity: inject IKeyResolver so IdentityDbContext encrypts Email/TotpSecret
        // identically in AuthServer as in the Api host. IKeyResolver registered by AddVaultKeyResolver
        // below; AddDbContext factory executes lazily so registration order is safe.
        builder.Services.AddDbContext<IdentityDbContext>((sp, options) =>
        {
            options.UseSqlServer(
                connectionString,
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "identity"));
            options.UseOpenIddict();
            // Suppress pending-model-changes warning: OpenIddict manages its own schema
            // via its own migration system; EF warning is a false positive here
            options.ConfigureWarnings(w =>
                w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));

            // QĐ742 §4.4 Wave-3: populate EmailHash/NormalizedEmailHash on every user save.
            options.AddInterceptors(
                sp.GetRequiredService<AqtCoreFW.Identity.Infrastructure.Persistence.EmailHashInterceptor>());
        });

        // === ASP.NET Core Identity ===
        builder.Services.AddIdentity<ApplicationUser, ApplicationRole>(opts =>
            {
                opts.Password.RequiredLength = 12;
                opts.Password.RequireNonAlphanumeric = true;
                opts.Password.RequireDigit = true;
                opts.Password.RequireUppercase = true;
                // Fallback only — TenantAwareUserManager overrides AccessFailedAsync to read
                // per-tenant CredentialPolicy.MaxFailedAttempts/LockoutMinutes (QĐ 742 1.4 / FU2).
                // These values apply when the user has no tenant or no policy is configured.
                opts.Lockout.MaxFailedAccessAttempts = 5;
                // Dev/Test: short lockout for E2E test resilience; Production: 15min per QĐ742 §5.2
                opts.Lockout.DefaultLockoutTimeSpan = builder.Environment.IsProduction()
                    ? TimeSpan.FromMinutes(15)
                    : TimeSpan.FromSeconds(30);
                opts.User.RequireUniqueEmail = true;
            })
            .AddEntityFrameworkStores<IdentityDbContext>()
            .AddDefaultTokenProviders()
            .AddUserManager<TenantAwareUserManager>()
            // QĐ742 §4.4 Wave-3: override UserStore so FindByEmailAsync uses NormalizedEmailHash.
            // Must come AFTER AddEntityFrameworkStores — last registration wins in DI.
            .AddUserStore<AqtCoreFW.Identity.Infrastructure.Persistence.EmailHashedUserStore>();

        // === Cookie security per architecture decision ===
        builder.Services.ConfigureApplicationCookie(opts =>
        {
            opts.Cookie.Name = builder.Environment.IsDevelopment() ? "aqt-auth" : "__Host-aqt-auth";
            opts.Cookie.HttpOnly = true;
            opts.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
                ? CookieSecurePolicy.SameAsRequest
                : CookieSecurePolicy.Always;
            opts.Cookie.SameSite = SameSiteMode.Strict;
            opts.ExpireTimeSpan = TimeSpan.FromHours(8);
            opts.SlidingExpiration = false;
        });

        // === External identity providers (SSO/OIDC federation) ===
        var externalProviders = builder.Configuration
            .GetSection("ExternalProviders")
            .Get<ExternalProviderConfig[]>() ?? [];

        foreach (var provider in externalProviders)
        {
            builder.Services.AddAuthentication()
                .AddOpenIdConnect(provider.Scheme, provider.DisplayName, opts =>
                {
                    opts.Authority = provider.Authority;
                    opts.ClientId = provider.ClientId;
                    opts.ClientSecret = provider.ClientSecret;
                    opts.ResponseType = "code"; // Authorization Code flow
                    opts.SaveTokens = false;    // We don't need IdP tokens
                    opts.GetClaimsFromUserInfoEndpoint = true;
                    opts.CallbackPath = $"/signin-{provider.Scheme.ToLowerInvariant()}";
                    opts.SignInScheme = IdentityConstants.ExternalScheme;
                    // Map standard claims
                    opts.Scope.Clear();
                    opts.Scope.Add("openid");
                    opts.Scope.Add("profile");
                    opts.Scope.Add("email");
                });
        }

        // === OpenIddict authorization server ===
        builder.Services.AddOpenIddict()
            .AddCore(opts => opts.UseEntityFrameworkCore().UseDbContext<IdentityDbContext>())
            .AddServer(opts =>
            {
                // Only Authorization Code + PKCE — no implicit flow (OIDC hardening)
                opts.AllowAuthorizationCodeFlow()
                    .RequireProofKeyForCodeExchange();

                opts.AllowRefreshTokenFlow();

                // ROPC dev flow — for .http file testing only
                if (builder.Environment.IsDevelopment())
                    opts.AllowPasswordFlow();

                // Register custom scopes — must match FE oidc-client-ts scope config
                opts.RegisterScopes("openid", "profile", "email", "roles", "api", "offline_access");

                // Disable all other flows
                opts.DisableAccessTokenEncryption(); // reference tokens used instead

                // Endpoints
                opts.SetAuthorizationEndpointUris("/connect/authorize")
                    .SetTokenEndpointUris("/connect/token")
                    .SetUserInfoEndpointUris("/connect/userinfo")
                    .SetIntrospectionEndpointUris("/connect/introspect")
                    .SetRevocationEndpointUris("/connect/revoke")
                    .SetEndSessionEndpointUris("/connect/logout");

                // Token settings
                opts.SetAccessTokenLifetime(TimeSpan.FromMinutes(15));
                opts.SetRefreshTokenLifetime(TimeSpan.FromDays(7));

                // Refresh token rotation — zero reuse leeway (security hardening)
                opts.SetRefreshTokenReuseLeeway(TimeSpan.Zero);

                // Use reference tokens (stored in DB, revocable)
                opts.UseReferenceAccessTokens();
                opts.UseReferenceRefreshTokens();

                // Signing/encryption — dev uses ephemeral dev certs; non-Development loads PFX
                // files from config paths (mounted from a k8s Secret; passwords come via Vault/env,
                // never appsettings). Missing paths outside Development throw at startup — an OIDC
                // server silently minting tokens with an ephemeral key would invalidate every token
                // on each restart AND hide a broken cert mount.
                if (builder.Environment.IsDevelopment())
                {
                    opts.AddDevelopmentEncryptionCertificate();
                    opts.AddDevelopmentSigningCertificate();
                }
                else
                {
                    opts.AddSigningCertificate(LoadRequiredCertificate(
                        builder.Configuration, "Auth:SigningCertificate"));
                    opts.AddEncryptionCertificate(LoadRequiredCertificate(
                        builder.Configuration, "Auth:EncryptionCertificate"));
                }

                // Allow HTTP behind local ingress / set issuer explicitly to avoid host auto-detection.
                {
                    var issuerUri = builder.Configuration["Auth:IssuerUri"];
                    if (!string.IsNullOrEmpty(issuerUri))
                        opts.SetIssuer(new Uri(issuerUri));
                }

                var aspNetCore = opts.UseAspNetCore()
                    .EnableAuthorizationEndpointPassthrough()
                    .EnableTokenEndpointPassthrough()
                    .EnableUserInfoEndpointPassthrough()
                    .EnableEndSessionEndpointPassthrough()
                    .EnableStatusCodePagesIntegration();

                // "Auth:AllowHttp" (default false) is the rehearsal-only counterpart of
                // VneId:AllowStub: a local Production-mode cluster fronts this pod with a plain
                // HTTP ingress, and OpenIddict would otherwise reject every OIDC request.
                // Real production keeps HTTPS enforcement — never set this outside
                // infra/helm/*/values-local.yaml.
                if (builder.Environment.IsDevelopment()
                    || builder.Configuration.GetValue<bool>("Auth:AllowHttp"))
                    aspNetCore.DisableTransportSecurityRequirement();
            })
            .AddValidation(opts =>
            {
                opts.UseLocalServer();
                opts.UseAspNetCore();
            });

        builder.Services.AddControllersWithViews();

        // NullCacheService — AuthServer only handles consent/token flows; no caching needed
        builder.Services.AddSingleton<ICacheService, NullCacheService>();

        // SharedKernel GUID generator — required by AuditService for log entry ID generation
        builder.Services.AddSharedKernelGuidGenerator();

        // Audit infrastructure — login attempt logging (shared DB with API).
        // Register manually (not via AddAuditInfrastructure) because AuthServer doesn't load
        // the full Infrastructure stack (interceptors, repositories not needed by login flow).
        builder.Services.AddDbContext<AqtCoreFW.Audit.Infrastructure.Persistence.AuditDbContext>(options =>
            options.UseSqlServer(connectionString,
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "audit")));
        builder.Services.AddScoped<AqtCoreFW.Audit.Domain.Repositories.IAuditLogRepository,
            AqtCoreFW.Audit.Infrastructure.Persistence.AuditLogRepository>();
        // AuditEntryPersister is the shared row-persist path AuditService now depends on (atomic
        // row + outbox + HMAC enqueue). It is registered by AddAuditInfrastructure on the Api host;
        // AuthServer wires audit manually, so it must be added here too or AuditService fails to
        // activate on first audited login. Same missing-transitive-dep class as the notes below.
        builder.Services.AddScoped<AqtCoreFW.Audit.Infrastructure.Services.AuditEntryPersister>();
        builder.Services.AddScoped<AqtCoreFW.Audit.Domain.Services.IAuditService,
            AqtCoreFW.Audit.Infrastructure.Services.AuditService>();
        // IAuditLogger required if any MediatR AuditBehavior runs in AuthServer flows.
        builder.Services.AddScoped<AqtCoreFW.SharedKernel.Application.IAuditLogger,
            AqtCoreFW.Audit.Infrastructure.Services.AuditService>();
        builder.Services.AddScoped<AqtCoreFW.Audit.Application.Services.IBackgroundAuditJobEnqueuer,
            AqtCoreFW.Audit.Infrastructure.Services.HangfireAuditJobEnqueuer>();

        // ICrossModuleUnitOfWork + IOutboxWriter required by AuditService (Slice 1B-2) — it now
        // wraps the audit-row write + AuditLogAppendedIntegrationEvent outbox enqueue in an atomic
        // span instead of a direct messageBus.PublishAsync. AuthServer skips AddInfrastructure()
        // (see the "manual, minimal registration" note above), so DatabaseOptions is never
        // configured either — CrossModuleUnitOfWork's ctor needs it to open its own connection.
        // Same pattern-recurrence risk documented below for IMessageBus/IGuidGenerator: a missing
        // transitive dep here throws InvalidOperationException on first audited login.
        builder.Services.Configure<DatabaseOptions>(opts => opts.ConnectionString = connectionString);
        builder.Services.AddScoped<ICrossModuleUnitOfWork, CrossModuleUnitOfWork>();
        builder.Services.AddScoped<IOutboxWriter, OutboxWriter>();
        builder.Services.AddOutboxSchema("audit");

        // IMessageBus required by LockoutEscalationService (below) for publishing
        // AccountAutoDisabledIntegrationEvent directly. AuditService itself no longer needs it
        // (Slice 1B-2 moved AuditLogAppendedIntegrationEvent off direct messageBus.PublishAsync
        // onto the outbox — see ICrossModuleUnitOfWork/IOutboxWriter registration above), but the
        // AuditLogAppendedConsumer registration stays here too so each host's bus endpoint set is
        // identical, matching the Api host, ahead of future microservice extraction.
        builder.Services.AddMessageBus(builder.Configuration, x =>
        {
            x.AddConsumers(
                typeof(AqtCoreFW.Audit.Application.EventHandlers.AuditLogAppendedConsumer).Assembly,
                typeof(AqtCoreFW.Notifications.Application.EventHandlers.AccountAutoDisabledNotificationConsumer).Assembly);
        });
        // ICurrentUser required by AuditService — resolved from HttpContext claims
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ICurrentUser, HttpContextCurrentUser>();

        // JIT SSO provisioning — auto-create users on first external login
        builder.Services.AddScoped<AqtCoreFW.Identity.Domain.Repositories.IExternalIdentityRepository,
            AqtCoreFW.Identity.Infrastructure.Persistence.ExternalIdentityRepository>();
        builder.Services.AddScoped<AqtCoreFW.Identity.Domain.Repositories.IJitProviderConfigRepository,
            AqtCoreFW.Identity.Infrastructure.Persistence.JitProviderConfigRepository>();
        builder.Services.AddScoped<JitProvisioningService>();

        // QĐ 742 auth lifecycle: lockout escalation (1.4.d) + credential policy lookup for RemainingAttempts (1.4.b)
        // Dependencies of LockoutEscalationService: UserManager + IMessageBus + ILogger already registered above.
        builder.Services.AddScoped<AqtCoreFW.Identity.Domain.Repositories.ICredentialPolicyRepository,
            AqtCoreFW.Identity.Infrastructure.Persistence.CredentialPolicyRepository>();
        builder.Services.AddScoped<AqtCoreFW.Identity.Application.Services.ILockoutEscalationService,
            AqtCoreFW.Identity.Infrastructure.Authentication.LockoutEscalationService>();

        // Hangfire CLIENT ONLY (no server) — required by HangfireAuditJobEnqueuer (static
        // BackgroundJob.Enqueue reads JobStorage.Current). API host runs the worker; jobs flow
        // through the shared SQL store ([hangfire] schema). This also auto-registers
        // IBackgroundJobClient (used by MfaService) — supersedes the prior NoOp stub.
        // PrepareSchemaIfNecessary=false: API host owns the schema migration.
        builder.Services.AddHangfire(cfg => cfg
            .SetDataCompatibilityLevel(Hangfire.CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UseSqlServerStorage(connectionString, new Hangfire.SqlServer.SqlServerStorageOptions
            {
                SchemaName = "hangfire",
                QueuePollInterval = TimeSpan.Zero,
                UseRecommendedIsolationLevel = true,
                DisableGlobalLocks = true,
                PrepareSchemaIfNecessary = false
            }));
        // Intentionally NO AddHangfireServer — AuthServer enqueues only.

        // QĐ 742 1.6.b — MFA challenge temp_token + policy resolution for ROPC mfa_required path.
        // IMemoryCache: used by MfaTempTokenService for single-use token enforcement.
        // IMfaPolicyRepository + IMfaPolicyResolver: needed to determine grace expiry in ROPC path.
        // IMfaService + IRecoveryCodeService: needed by MfaChallengeController (TOTP/recovery verify).
        builder.Services.AddMemoryCache();
        builder.Services.AddSingleton<AqtCoreFW.AuthServer.Services.MfaTempTokenService>();
        // OWASP A07 / QĐ742 timing-side-channel mitigation — see PasswordVerifyTimingDecoy.cs
        // Scoped to match IPasswordHasher<ApplicationUser> lifetime (avoids captive dependency).
        builder.Services.AddScoped<AqtCoreFW.AuthServer.Services.IPasswordVerifyTimingDecoy,
            AqtCoreFW.AuthServer.Services.PasswordVerifyTimingDecoy>();
        builder.Services.AddScoped<AqtCoreFW.Identity.Domain.Repositories.IMfaPolicyRepository,
            AqtCoreFW.Identity.Infrastructure.Persistence.MfaPolicyRepository>();
        builder.Services.AddScoped<AqtCoreFW.Identity.Application.Services.IMfaPolicyResolver,
            AqtCoreFW.Identity.Application.Services.MfaPolicyResolver>();
        // QĐ 742 1.6.b — MFA enrolment-required evaluator + feature flag for FE-redirect claim emission.
        builder.Services.AddScoped<AqtCoreFW.AuthServer.Services.IMfaEnrolmentEvaluator,
            AqtCoreFW.AuthServer.Services.MfaEnrolmentEvaluator>();

        // SystemParams thin DbContext + runtime kill-switch for MFA enrolment enforcement.
        // We deliberately skip AddSystemParamsInfrastructure() here — the full registration pulls Redis
        // (IConnectionMultiplexer) which AuthServer does not host. MfaEnrolmentSettings reads
        // the SystemParameters table directly with no caching (sub-ms query, called once per
        // token issuance). NullTenantContext satisfies SystemParamsDbContext's ctor — we never
        // write through it, only read SystemParameters which does not gate by tenant filters.
        builder.Services.AddScoped<AqtCoreFW.SharedKernel.Domain.ITenantContext,
            AqtCoreFW.Infrastructure.Security.NullTenantContext>();
        builder.Services.AddDbContext<AqtCoreFW.SystemParams.Infrastructure.Persistence.SystemParamsDbContext>(opts =>
            opts.UseSqlServer(connectionString,
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "config")));
        builder.Services.AddScoped<AqtCoreFW.AuthServer.Services.IMfaEnrolmentSettings,
            AqtCoreFW.AuthServer.Services.MfaEnrolmentSettings>();
        builder.Services.Configure<AqtCoreFW.AuthServer.Configuration.MfaOptions>(
            builder.Configuration.GetSection(AqtCoreFW.AuthServer.Configuration.MfaOptions.SectionName));
        builder.Services.AddScoped<AqtCoreFW.Identity.Application.Services.IMfaService,
            AqtCoreFW.Identity.Infrastructure.Services.MfaService>();
        builder.Services.AddScoped<AqtCoreFW.Identity.Infrastructure.Services.EmailOtpJob>();
        builder.Services.AddScoped<AqtCoreFW.Identity.Application.Services.IRecoveryCodeService,
            AqtCoreFW.Identity.Infrastructure.Services.RecoveryCodeService>();

        // MediatR — only consent handlers needed for authorization consent page
        // Disable ValidateOnBuild: other Identity.Application handlers require full
        // infrastructure (repositories, Dapper) registered only in AqtCoreFW.Api
        builder.Services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(
                typeof(AqtCoreFW.Identity.Application.ModuleRegistration).Assembly));
        builder.Host.UseDefaultServiceProvider(options =>
        {
            options.ValidateScopes = builder.Environment.IsDevelopment();
            options.ValidateOnBuild = false; // AuthServer only needs consent handlers at runtime
        });

        // Health checks — sqlserver + vault readiness
        builder.Services.AddVaultKeyResolver(builder.Configuration);
        // QĐ742 §4.4 Wave-3: pepper resolver for HMAC email hashing.
        // Requires IVaultClient (registered by AddVaultKeyResolver) + IMemoryCache (line 272).
        builder.Services.AddVaultPepperResolver();
        // EmailHashService + interceptor: Scoped — one per request, fresh per login flow.
        builder.Services.AddScoped<AqtCoreFW.Identity.Infrastructure.Services.EmailHashService>();
        builder.Services.AddScoped<AqtCoreFW.Identity.Infrastructure.Persistence.EmailHashInterceptor>();
        builder.Services.AddHealthChecks()
            .AddSqlServer(connectionString, name: "sqlserver", tags: ["ready"])
            .AddVaultHealthCheck();

        // === CORS — allow FE origins for OIDC discovery + token endpoints ===
        // Origins configured via Cors:AllowedOrigins in appsettings; fallback to local dev defaults.
        builder.Services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
            {
                var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                    ?? ["http://localhost:5173", "http://localhost:3000", "http://localhost:5001"];
                policy.WithOrigins(corsOrigins)
                    .WithHeaders("Content-Type", "Authorization", "X-Correlation-Id", "X-Tenant-Id", "Accept", "Accept-Language")
                    .WithMethods("GET", "POST", "PUT", "DELETE", "OPTIONS", "PATCH")
                    .AllowCredentials();
            });
        });

        // Trust forwarded headers from any proxy (Cloudflare Tunnel, Docker, nginx)
        // In production, restrict KnownProxies/KnownNetworks to actual proxy IPs
        builder.Services.Configure<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>(opts =>
        {
            opts.ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
                                  | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto;
            opts.KnownIPNetworks.Clear();
            opts.KnownProxies.Clear();
        });

        var app = builder.Build();

        // === Phase 06 S4: Fail-fast on missing/DEV-prefix secrets in Production ===
        // Runs before MigrateAsync so a missing ConnectionString yields a clear error,
        // not a SQL connection exception buried in migration stack traces.
        app.ValidateProductionSecrets();

        // Force Hangfire IGlobalConfiguration eager resolution so JobStorage.Current is set
        // for static BackgroundJob.Enqueue paths. AddHangfire registers config lazily; without
        // AddHangfireServer (which we deliberately omit on this enqueue-only host), nothing
        // else resolves JobStorage automatically. Without this line, HangfireAuditJobEnqueuer
        // (audit pipeline on /connect/authorize) throws "JobStorage instance has not been initialized"
        // on the first request.
        _ = app.Services.GetRequiredService<Hangfire.JobStorage>();

        // Warm the timing-decoy hash so the first user-not-found login after process start
        // doesn't pay an extra HashPassword cost (~100-300ms enumeration window per cold start).
        // See PasswordVerifyTimingDecoy + BE-LOGIN-TIMING-SIDE-CHANNEL.
        using (var warmupScope = app.Services.CreateScope())
        {
            warmupScope.ServiceProvider
                .GetRequiredService<AqtCoreFW.AuthServer.Services.IPasswordVerifyTimingDecoy>()
                .Consume(string.Empty);
        }

        // === RLS-PRINCIPAL-SKIP-STARTUP-MIGRATIONS: resolve the startup migration gate ===
        // --migrate-only / RUN_MIGRATIONS_ONLY → migrate, then exit. SKIP_STARTUP_MIGRATIONS → runtime
        // boot skips MigrateAsync so the runtime DB login can drop db_ddladmin. See deployment-guide §4.
        var migrationMode = StartupMigrationMode.Resolve(hasMigrateOnlyArg, app.Configuration);

        // === Run EF migrations on startup (idempotent) ===
        // These two contexts are a strict subset of the Api host's migrate set (Api Program.cs migrates
        // both IdentityDbContext and MessagingDbContext), so the Helm `db-migrate` Job (which runs on the
        // Api image) covers AuthServer's schema in K8s. This in-app block exists for dev / single-container
        // topologies, or when AuthServer is deployed as a standalone release without that Job.
        // MessagingDbContext: AuthServer publishes AuditLogAppendedIntegrationEvent which can
        // dead-letter on retry exhaustion; the dead_letters table must exist on this host's DB.
        if (migrationMode.RunMigrations)
        {
            using var scope = app.Services.CreateScope();
            var sp = scope.ServiceProvider;
            // Migrate the declared subset in order. StartupMigrationSets.Contexts is the single
            // source of truth the arch test reads to enforce the ADR-010 subset invariant.
            foreach (var contextType in StartupMigrationSets.Contexts)
            {
                await ((DbContext)sp.GetRequiredService(contextType)).Database.MigrateAsync();
            }
        }
        else
        {
            app.Logger.LogInformation(
                "Startup migrations skipped (SKIP_STARTUP_MIGRATIONS) — DB schema must be migrated out-of-band " +
                "(Helm db-migrate Job / init-container with an elevated login). See docs/deployment-guide.md §4.");
        }

        if (migrationMode.ExitAfterMigrations)
        {
            app.Logger.LogInformation("--migrate-only / RUN_MIGRATIONS_ONLY: schema migrations complete; exiting without starting the host.");
            return;
        }

        // === PHASE6-RLS-PROD-PRINCIPAL-VERIFY: warn if the prod DB login defeats RLS ===
        // Runs AFTER migrations so the DB/schema exist on first deploy. Advisory only — logs Critical,
        // never throws. Skipped in --migrate-only mode (that invocation runs under the elevated migration
        // login, which is db_owner by design). See docs/deployment-guide.md §4 'Application DB Principal'.
        app.WarnIfDbOwnerInProduction();

        // === QĐ742 §4.4 Wave-4: data-plane PII upgrade sequence (Step 1→2→3, order mandatory) ===
        // AuthServer owns the login path — columns must be encrypted and NormalizedEmailHash populated
        // before app.Run() accepts logins. The shared runner owns the step ordering and fail-soft
        // (KeyNotFound) handling; AuthServer's column set is identity-only.
        await AqtCoreFW.Identity.Infrastructure.Startup.StartupEncryptionUpgradeRunner.RunAsync(
            app.Services,
            app.Configuration.GetConnectionString("Default")!,
            plaintextColumns:
            [
                new("identity", "AspNetUsers", "Email"),
                new("identity", "AspNetUsers", "NormalizedEmail"),
                new("identity", "AspNetUsers", "PhoneNumber"),
            ],
            legacyColumns:
            [
                new("identity", "AspNetUsers", "TotpSecret"),
            ],
            app.Services.GetRequiredService<ILogger<AqtCoreFW.AuthServer.Program>>());

        // === Seed dev client applications ===
        await OpenIddictDataSeeder.SeedApplicationsAsync(app.Services, app.Environment);

        // === Seed dev users for local/E2E testing ===
        if (app.Environment.IsDevelopment())
            await OpenIddictDataSeeder.SeedDevUsersAsync(app.Services);

        if (app.Environment.IsDevelopment())
            app.UseDeveloperExceptionPage();

        // Forwarded Headers — required when behind reverse proxy (Cloudflare Tunnel, nginx, Docker)
        // Ensures OpenIddict generates https:// endpoint URLs when tunnel terminates TLS
        app.UseForwardedHeaders(new ForwardedHeadersOptions
        {
            ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
                             | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto
        });

        // Auth:AllowHttp — same rehearsal-only escape hatch as the OpenIddict transport
        // requirement above (plain-HTTP local ingress); real production keeps the redirect.
        if (!app.Environment.IsDevelopment()
            && !app.Configuration.GetValue<bool>("Auth:AllowHttp"))
            app.UseHttpsRedirection();
        app.UseStaticFiles();
        app.UseRouting();
        app.UseCors(); // Must be after UseRouting, before UseAuthentication
        app.UseCookiePolicy();
        app.UseAuthentication();
        app.UseAuthorization();

        // Hangfire dashboard — hosted here (cookie-native ASP.NET Identity) so the SystemAdmin filter
        // resolves from HttpContext.User. MUST be after UseAuthentication/UseAuthorization so the Identity
        // cookie principal is populated. This host is enqueue-only (NO UseHangfireServer); the dashboard
        // reads the shared SQL store, so the API host's job-processing server appears here too.
        app.UseHangfireDashboard("/hangfire", new DashboardOptions
        {
            Authorization = [new HangfireAuthorizationFilter()],
            DisplayStorageConnectionString = false
        });

        app.MapControllers();
        // /health — liveness probe: no dependency checks (pod is alive)
        app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
        {
            Predicate = _ => false
        });
        // /health/ready — readiness probe: sqlserver + vault (tagged "ready")
        app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
        {
            Predicate = hc => hc.Tags.Contains("ready")
        });

        await app.RunAsync();
    }

    /// <summary>
    /// Loads a PFX certificate for OpenIddict from config: "{section}:Path" (required outside
    /// Development) + "{section}:Password" (optional — sourced from Vault/env, never appsettings).
    /// Throws with the offending config key when the path is unset or the file is missing, so a
    /// broken Secret mount fails the pod at startup instead of minting ephemeral-key tokens.
    /// </summary>
    public static System.Security.Cryptography.X509Certificates.X509Certificate2 LoadRequiredCertificate(
        IConfiguration configuration, string sectionKey)
    {
        var path = configuration[$"{sectionKey}:Path"];
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException(
                $"OpenIddict certificate not configured: set {sectionKey}:Path to a PFX file " +
                "(mounted from the certificate Secret) for non-Development environments.");

        if (!File.Exists(path))
            throw new InvalidOperationException(
                $"OpenIddict certificate file not found: {sectionKey}:Path = '{path}'. " +
                "Check the certificate Secret mount.");

        var password = configuration[$"{sectionKey}:Password"];
        return System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12FromFile(
            path, string.IsNullOrEmpty(password) ? null : password);
    }
}
