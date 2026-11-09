using MassTransit;
using Microsoft.AspNetCore.HttpOverrides;
using AqtCoreFW.Ai.Application;
using AqtCoreFW.Ai.Infrastructure;
using AqtCoreFW.Ai.Presentation;
using AqtCoreFW.Rules.Application;
using AqtCoreFW.Rules.Infrastructure;
using AqtCoreFW.Collaboration.Application;
using AqtCoreFW.Collaboration.Infrastructure;
using AqtCoreFW.BusinessProcess.Application;
using AqtCoreFW.BusinessProcess.Infrastructure;
using AqtCoreFW.Integration.Application;
using AqtCoreFW.Integration.Infrastructure;
using AqtCoreFW.Integration.Infrastructure.Persistence;
using AqtCoreFW.AssetManagement.Application;
using AqtCoreFW.AssetManagement.Infrastructure;
using AqtCoreFW.Contracts.Application;
using AqtCoreFW.Contracts.Infrastructure;
using AqtCoreFW.Api.Gateway;
using AqtCoreFW.Audit.Application;
using AqtCoreFW.Audit.Infrastructure;
using AqtCoreFW.Identity.Application;
using AqtCoreFW.Identity.Infrastructure;
using AqtCoreFW.Infrastructure;
using AqtCoreFW.Infrastructure.Alerting;
using AqtCoreFW.Infrastructure.ApiKeys;
using AqtCoreFW.Infrastructure.BackgroundJobs;
using AqtCoreFW.Infrastructure.Events;
using AqtCoreFW.Infrastructure.HealthChecks;
using AqtCoreFW.Infrastructure.Messaging;
using AqtCoreFW.Infrastructure.Middleware;
using AqtCoreFW.Infrastructure.Backup;
using AqtCoreFW.Infrastructure.Webhooks;
using Hangfire;
using AqtCoreFW.Infrastructure.Logging;
using AqtCoreFW.Infrastructure.Telemetry;
using AqtCoreFW.Forms.Application;
using AqtCoreFW.Forms.Infrastructure;
using AqtCoreFW.Forms.Infrastructure.Jobs;
using AqtCoreFW.Workflow.Application;
using AqtCoreFW.Workflow.Infrastructure.Jobs;
using AqtCoreFW.Workflow.Infrastructure;
using AqtCoreFW.MasterData.Infrastructure;
using AqtCoreFW.Organization.Infrastructure;
using AqtCoreFW.SystemParams.Infrastructure;
using AqtCoreFW.Notifications.Application;
using AqtCoreFW.Notifications.Infrastructure;
using AqtCoreFW.Notifications.Infrastructure.Realtime;
using AqtCoreFW.Files.Application;
using AqtCoreFW.Files.Infrastructure;
using AqtCoreFW.Reporting.Application;
using AqtCoreFW.Reporting.Infrastructure;
using AqtCoreFW.SharedKernel.Application.Behaviors;
using AqtCoreFW.SharedKernel.Contracts;
using AqtCoreFW.Ai.Infrastructure.Persistence;
using AqtCoreFW.Audit.Infrastructure.Persistence;
using AqtCoreFW.Files.Infrastructure.Persistence;
using AqtCoreFW.Reporting.Infrastructure.Persistence;
using AqtCoreFW.Rules.Infrastructure.Persistence;
using AqtCoreFW.Collaboration.Infrastructure.Persistence;
using AqtCoreFW.Forms.Infrastructure.Persistence;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using AqtCoreFW.MasterData.Infrastructure.Persistence;
using AqtCoreFW.Notifications.Infrastructure.Persistence;
using AqtCoreFW.Organization.Infrastructure.Persistence;
using AqtCoreFW.SystemParams.Infrastructure.Persistence;
using AqtCoreFW.Workflow.Infrastructure.Persistence;
using AqtCoreFW.BusinessProcess.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using AqtCoreFW.Infrastructure.Configuration;
using VaultSharp;
using Prometheus;

namespace AqtCoreFW.Api;

public partial class Program
{
    public static async Task Main(string[] args)
    {
        // Detect (and strip) the `--migrate-only` flag before CreateBuilder — a bare token with
        // no value breaks CommandLineConfigurationProvider ("Unrecognized argument format").
        var hasMigrateOnlyArg = StartupMigrationMode.HasMigrateOnlyArg(args);
        var builder = WebApplication.CreateBuilder(StartupMigrationMode.StripKnownArgs(args));

        // === Vault secrets (Production only — loads from HashiCorp Vault KV v2) ===
        builder.Configuration.AddVaultConfiguration(builder.Configuration);

        // === Vault key resolver (singleton, 5-min cache) ===
        // IVaultClient registered as singleton, shared by IKeyResolver + VaultHealthCheck.
        // IMemoryCache provided by AddInfrastructure → AddGovHealthChecks path; DI factory is lazy.
        builder.Services.AddVaultKeyResolver(builder.Configuration);
        // QĐ742 §4.4 Wave-3: pepper resolver for HMAC email hashing (singleton, 5-min cache).
        // Requires IVaultClient (AddVaultKeyResolver above) + IMemoryCache (AddInfrastructure below).
        builder.Services.AddVaultPepperResolver();

        // L-02: Global request body size limit — prevents DoS via oversized uploads on any endpoint
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Limits.MaxRequestBodySize = 104_857_600; // 100 MB
        });
        builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
        {
            options.MultipartBodyLengthLimit = 104_857_600; // 100 MB
        });

        // === Logging (Serilog structured JSON — must be first) ===
        builder.AddSerilogLogging();

        // === Core Services ===
        // Module controllers live in *.Presentation assemblies ProjectReference'd by this host (see
        // .csproj). ASP.NET Core auto-discovers them as application parts via DependencyContext
        // (deps.json) for project-referenced assemblies, and host-local admin controllers live in this
        // entry assembly (always the primary part) — so explicit AddApplicationPart(typeof(X).Assembly)
        // calls were redundant and removed. Empirically proven: Integration + BusinessProcess route 200
        // despite never having had an explicit part; guarded by
        // ApiControllerDiResolutionSmokeTests.Api_EveryModulePresentation_ContributesAtLeastOneController
        // (fails loudly if any module's controllers stop being discovered).
        // NOTE: if trim / single-file / AOT publishing is enabled later, DependencyContext discovery
        // changes — re-introduce explicit parts (the presence guard will go red first).
        builder.Services.AddControllers()
            .AddJsonOptions(opts =>
            {
                // Serialize enums as strings for REST API readability and FE compatibility
                opts.JsonSerializerOptions.Converters.Add(
                    new System.Text.Json.Serialization.JsonStringEnumConverter());
            })
            .ConfigureApplicationPartManager(pm =>
            {
                // Deferred-module gate, controller-discovery half: DependencyContext auto-discovery
                // (comment above) pulls in EVERY project-referenced Presentation assembly regardless
                // of DI registration, so a module whose Features:{Name}:Enabled flag is off must have
                // its Presentation part REMOVED here — otherwise its routes still map and requests
                // die with a 500 on handler resolution instead of the intended 404.
                // Match by assembly-name prefix ("AqtCoreFW.{Name}.Presentation") rather than a type
                // reference so this block compiles even if a module's Presentation assembly is ever
                // dropped from the host. See DeferredModuleFlags for the flag semantics.
                foreach (var moduleName in DeferredModuleFlags.ModuleNames)
                {
                    if (DeferredModuleFlags.IsEnabled(builder.Configuration, moduleName))
                        continue;
                    var presentationPrefix = $"AqtCoreFW.{moduleName}.Presentation";
                    var toRemove = pm.ApplicationParts
                        .Where(p => p.Name.StartsWith(presentationPrefix, StringComparison.Ordinal))
                        .ToList();
                    foreach (var part in toRemove)
                        pm.ApplicationParts.Remove(part);
                }
            });

        // === Gateway (YARP + API Versioning + OpenAPI/Scalar) ===
        builder.Services.AddGateway(builder.Configuration);

        // === Presentation-layer validators (non-CQRS controllers) ===
        FluentValidation.ServiceCollectionExtensions.AddValidatorsFromAssemblyContaining(
            builder.Services,
            typeof(AqtCoreFW.SystemParams.Presentation.Controllers.AnnouncementsController),
            ServiceLifetime.Scoped);

        // === API Key Auth (M2M — SHA-256 + Redis cache) ===
        builder.Services.AddApiKeyAuth(builder.Configuration);

        // === MediatR Pipeline (order: TenantAware first, then Validation) ===
        builder.Services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(typeof(Program).Assembly);
            cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(TenantAwareBehavior<,>));
            cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
            cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(AuditBehavior<,>));
        });

        // === SharedKernel.Contracts defaults (monolith mode) ===
        builder.Services.AddSingleton<IServiceIdentityProvider, NoOpServiceIdentityProvider>();

        // === Infrastructure (cross-cutting: cache, rate limit, OTEL, health checks, CORS, etc.) ===
        builder.Services.AddInfrastructure(builder.Configuration);

        // Vault readiness probe — added after AddInfrastructure so the "ready" tag endpoint is wired.
        builder.Services.AddHealthChecks().AddVaultHealthCheck();

        // === Event Catalog (M07 — in-memory registry of domain events) ===
        builder.Services.AddSingleton<IEventCatalogService, EventCatalogService>();

        // === Webhook Engine (outgoing HTTP dispatcher + subscription admin) ===
        builder.Services.AddWebhookEngine(builder.Configuration);

        // === Backup/Restore infrastructure (NĐ53 compliance) ===
        builder.Services.AddBackupInfrastructure(builder.Configuration);

        // === Alerting infrastructure (M14 — AlertRules, Runbooks, evaluation job) ===
        builder.Services.AddAlertingInfrastructure(builder.Configuration);

        // === Message Bus (MassTransit — InMemory dev / RabbitMQ prod) ===
        // Phase-04b: register module consumers via assembly scan. Without this, IConsumer<T>
        // implementations declared in modules are never wired to the bus; a published message
        // lands on an endpoint with zero consumers and is silently dropped (or dead-lettered).
        builder.Services.AddMessageBus(builder.Configuration, x =>
        {
            x.AddConsumers(
                // Notifications: FormSubmissionAcknowledgedConsumer (260427-1943 Phase-03)
                typeof(AqtCoreFW.Notifications.Application.EventHandlers.FormSubmissionAcknowledgedConsumer).Assembly,
                // Audit: AuditLogAppendedConsumer noop (260427-1943 Phase-03 — extraction prep)
                typeof(AqtCoreFW.Audit.Application.EventHandlers.AuditLogAppendedConsumer).Assembly,
                // Forms: WorkflowFinalizedFormSubmissionConsumer — outbox-routed replacement for the
                // former in-process WorkflowTransitionedFormSubmissionHandler MediatR handler.
                typeof(AqtCoreFW.Forms.Application.EventHandlers.WorkflowFinalizedFormSubmissionConsumer).Assembly);
        });

        // === Dead Letter admin service ===
        builder.Services.AddScoped<DeadLetterService>();

        // === Signing doc-flow orphan-blob sweep (cross-module: Identity records + Files storage) ===
        builder.Services.AddScoped<AqtCoreFW.Api.Jobs.SigningDocFlowOrphanBlobSweepJob>();

        // === Module Registration ===
        builder.Services
            .AddIdentityModule(builder.Configuration)
            .AddIdentityInfrastructure(builder.Configuration, builder.Environment);

        builder.Services
            .AddNotificationsModule(builder.Configuration)
            .AddNotificationsInfrastructure(builder.Configuration);

        // === SignalR Redis backplane (must follow AddNotificationsInfrastructure which calls AddSignalR) ===
        var redisConn = builder.Configuration["Redis:ConnectionString"];
        if (!string.IsNullOrEmpty(redisConn))
            builder.Services.AddSignalR().AddStackExchangeRedis(redisConn);

        builder.Services
            .AddAuditModule(builder.Configuration)
            .AddAuditInfrastructure(builder.Configuration);

        builder.Services
            .AddFormsModule()
            .AddFormsInfrastructure(builder.Configuration);

        builder.Services.AddWorkflowModule();
        builder.Services.AddWorkflowInfrastructure(builder.Configuration);

        builder.Services.AddMasterDataInfrastructure(builder.Configuration);
        builder.Services.AddSystemParamsInfrastructure(builder.Configuration);
        builder.Services.AddOrganizationInfrastructure(builder.Configuration);

        // === Cross-module tenant/org/user seeder cluster — EXPLICIT ORDER (host-owned) ===
        // IHostedService.StartAsync runs sequentially in registration order, and these five seeders
        // form a cross-module dependency chain that module-local registration cannot express:
        //   TenantSeeder (canonical tenant rows — must beat backfill's derived "TENANT-xxxx" rows)
        //   → TenantBackfillSeeder (unconditional reactive fallback, fills tenants canonical missed)
        //   → TenantRoleSeeder (business roles, needs tenants)
        //   → OrgTreeSeeder (org tree, needs tenants)
        //   → TenantUserSeeder (users, reads organization.OrgUnits + binds roles)
        //   → TenantAssignmentSeeder (assignments, reads identity.AspNetUsers)
        // Before this block existed, TenantUserSeeder (Identity registration) ran BEFORE
        // OrgTreeSeeder (Organization registration) and graceful-skipped on first boot — the
        // "dev 2-restart converge" behavior. Options sections stay module-bound; only the
        // enable-check + AddHostedService live here. Order pinned by an Architecture test.
        if (builder.Configuration.GetValue<bool>(
                $"{AqtCoreFW.Identity.Infrastructure.Seeding.TenantSeederOptions.SectionName}:Enabled"))
            builder.Services.AddHostedService<AqtCoreFW.Identity.Infrastructure.Seeding.TenantSeeder>();
        // Unconditional: the reactive fallback that guarantees Tenant rows exist for everything
        // downstream — after TenantSeeder so canonical rows (proper code + diacritics) win.
        builder.Services.AddHostedService<AqtCoreFW.Identity.Infrastructure.Seeding.TenantBackfillSeeder>();
        if (builder.Configuration.GetValue<bool>(
                $"{AqtCoreFW.Identity.Infrastructure.Seeding.TenantRoleSeederOptions.SectionName}:Enabled"))
            builder.Services.AddHostedService<AqtCoreFW.Identity.Infrastructure.Seeding.TenantRoleSeeder>();
        if (builder.Configuration.GetValue<bool>(
                $"{AqtCoreFW.Organization.Infrastructure.Seeding.OrgTreeSeederOptions.SectionName}:Enabled"))
            builder.Services.AddHostedService<AqtCoreFW.Organization.Infrastructure.Seeding.OrgTreeSeeder>();
        if (builder.Configuration.GetValue<bool>(
                $"{AqtCoreFW.Identity.Infrastructure.Seeding.TenantUserSeederOptions.SectionName}:Enabled"))
            builder.Services.AddHostedService<AqtCoreFW.Identity.Infrastructure.Seeding.TenantUserSeeder>();
        if (builder.Configuration.GetValue<bool>(
                $"{AqtCoreFW.Organization.Infrastructure.Seeding.TenantAssignmentSeederOptions.SectionName}:Enabled"))
            builder.Services.AddHostedService<AqtCoreFW.Organization.Infrastructure.Seeding.TenantAssignmentSeeder>();

        // === E2E test data seeder (Development only) ===
        if (builder.Environment.IsDevelopment())
            builder.Services.AddHostedService<AqtCoreFW.Api.E2ETestDataSeeder>();

        // === Deferred-module flags (v1.0 launch scope) ===
        // AI / Collaboration / BusinessProcess / Reporting / Rules are deferred to v1.1 and can be
        // turned off per environment via Features:{Name}:Enabled (missing key = enabled, so dev/test
        // keep everything-on behavior). A disabled module contributes NOTHING: its registration block
        // below is skipped, its Presentation assembly is removed from controller discovery (see the
        // ConfigureApplicationPartManager block), its DbContext is filtered out of the startup
        // migration set, and (Collaboration) its SignalR hub is not mapped. See DeferredModuleFlags.
        var aiEnabled = DeferredModuleFlags.IsEnabled(builder.Configuration, DeferredModuleFlags.Ai);
        var collaborationEnabled = DeferredModuleFlags.IsEnabled(builder.Configuration, DeferredModuleFlags.Collaboration);
        var businessProcessEnabled = DeferredModuleFlags.IsEnabled(builder.Configuration, DeferredModuleFlags.BusinessProcess);
        var reportingEnabled = DeferredModuleFlags.IsEnabled(builder.Configuration, DeferredModuleFlags.Reporting);
        var rulesEnabled = DeferredModuleFlags.IsEnabled(builder.Configuration, DeferredModuleFlags.Rules);

        // === AI module ===
        if (aiEnabled)
        {
            builder.Services
                .AddAiModule()
                .AddAiInfrastructure(builder.Configuration)
                .AddAiPresentation();
        }

        builder.Services
            .AddFilesModule(builder.Configuration)
            .AddFilesInfrastructure(builder.Configuration);

        // === Cross-module adapters: Forms ↔ Files storage interfaces ===
        // Both adapters live in the host — the only composition root permitted to hold
        // compile-time references to both modules simultaneously. This keeps Forms.Application
        // free of any Files.Domain reference, satisfying the module-isolation arch tests.
        builder.Services.AddScoped<
            AqtCoreFW.Forms.Application.Interfaces.ISigningOriginalBucketUploader,
            AqtCoreFW.Api.CrossModule.SigningOriginalBucketUploaderAdapter>();
        builder.Services.AddScoped<
            AqtCoreFW.Forms.Application.Interfaces.ISignedDocumentDownloader,
            AqtCoreFW.Api.CrossModule.SignedDocumentDownloaderAdapter>();
        // Read-side port for the server-side PAdES assembler (Identity) — reads the staged original PDF from the
        // signing-original bucket without an Identity→Files compile-time dependency (bound here at the host).
        builder.Services.AddScoped<
            AqtCoreFW.Identity.Application.Signing.ISigningOriginalPdfReader,
            AqtCoreFW.Api.CrossModule.SigningOriginalPdfReaderAdapter>();
        // Write-side port for the server-side PAdES assembler — stages the placeholder PDF into the signing-pades
        // bucket so the async-poll completion path can splice the CMS without re-issuing signHash (F1 idempotency).
        builder.Services.AddScoped<
            AqtCoreFW.Identity.Application.Signing.ISigningPlaceholderPdfWriter,
            AqtCoreFW.Api.CrossModule.SigningPlaceholderPdfWriterAdapter>();
        // Read-side twin — the completion poll job reads the staged placeholder back to splice the polled CMS.
        builder.Services.AddScoped<
            AqtCoreFW.Identity.Application.Signing.ISigningPlaceholderPdfReader,
            AqtCoreFW.Api.CrossModule.SigningPlaceholderPdfReaderAdapter>();
        // Streaming blob port for the VGCA Tier-3 doc-flow handler — moves the PDF to be signed + the
        // uploaded signed PDF through the signing-original / signing-docflow buckets, bound at the host
        // so the doc-flow command handler has no Identity→Files compile-time dependency.
        builder.Services.AddScoped<
            AqtCoreFW.Identity.Application.Signing.ISigningDocFlowBlobStore,
            AqtCoreFW.Api.CrossModule.SigningDocFlowBlobStoreAdapter>();
        // Validates (Files security pipeline → PNG/JPEG narrowing, content-type ignored) and stores a user's
        // visual signature image in the user-signatures bucket — bound at the host so the Identity profile
        // image-upload handler has no Identity→Files compile-time dependency.
        builder.Services.AddScoped<
            AqtCoreFW.Identity.Application.Signing.IUserSignatureImageStore,
            AqtCoreFW.Api.CrossModule.UserSignatureImageStoreAdapter>();

        if (reportingEnabled)
        {
            builder.Services
                .AddReportingModule()
                .AddReportingInfrastructure(builder.Configuration);
        }

        // === Phase 2: Critical New Modules ===
        if (rulesEnabled)
        {
            builder.Services
                .AddRulesModule()
                .AddRulesInfrastructure(builder.Configuration);
        }

        if (collaborationEnabled)
        {
            builder.Services
                .AddCollaborationModule()
                .AddCollaborationInfrastructure(builder.Configuration);
        }

        builder.Services
            .AddIntegrationModule()
            .AddIntegrationInfrastructure(builder.Configuration);

        builder.Services
            .AddAssetManagementModule(builder.Configuration)
            .AddAssetManagementInfrastructure(builder.Configuration);

        builder.Services
            .AddContractsModule(builder.Configuration)
            .AddContractsInfrastructure(builder.Configuration);

        // === BP MVP P3: BusinessProcess module — pairs WorkflowDefinition with per-state/per-transition Forms. ===
        if (businessProcessEnabled)
        {
            builder.Services
                .AddBusinessProcessModule()
                .AddBusinessProcessInfrastructure(builder.Configuration);
        }

        // === Demo BusinessProcess seeder (host-resident, cross-module) — default OFF, opt-in per env. ===
        // Registered after the Forms + Workflow module registrations so its IHostedService starts after
        // their demo seeders: it resolves the demo Workflow definition + Form template they produce.
        builder.Services.Configure<DemoBusinessProcessSeederOptions>(
            builder.Configuration.GetSection(DemoBusinessProcessSeederOptions.SectionName));
        var demoBpOpts = builder.Configuration
            .GetSection(DemoBusinessProcessSeederOptions.SectionName)
            .Get<DemoBusinessProcessSeederOptions>();
        // The demo seeder resolves BusinessProcess services — it cannot run with the module off.
        if (demoBpOpts?.Enabled == true && businessProcessEnabled)
        {
            builder.Services.AddHostedService<DemoBusinessProcessSeeder>();
        }

        // === Multi-scheme auth policy (ApiKey for M2M, Bearer/OpenIddict for user tokens) ===
        // After all modules are registered — schemes must exist before the policy references them.
        builder.Services.AddAuthentication(options =>
        {
            options.DefaultScheme = "MultiAuth";
            options.DefaultAuthenticateScheme = "MultiAuth";
            options.DefaultChallengeScheme = "MultiAuth";
        })
        .AddPolicyScheme("MultiAuth", "MultiAuth", options =>
        {
            options.ForwardDefaultSelector = ctx =>
                ctx.Request.Headers.ContainsKey("X-Api-Key") || ctx.Request.Query.ContainsKey("api_key")
                    ? "ApiKey"
                    : "OpenIddict.Validation.AspNetCore";
        });

        // === DI Validation (dev + staging — catches misconfigured services at startup) ===
        if (builder.Environment.IsDevelopment() || builder.Environment.IsStaging())
        {
            builder.Host.UseDefaultServiceProvider(opts =>
            {
                opts.ValidateScopes = true;
                opts.ValidateOnBuild = true;
            });
        }

        // === Output caching for read endpoints (p95 optimization) ===
        builder.Services.AddOutputCache(options =>
        {
            options.AddBasePolicy(policy => policy.NoCache());
            options.AddPolicy("MasterData", policy =>
                policy.Expire(TimeSpan.FromMinutes(5))
                      .SetVaryByQuery("type"));
            options.AddPolicy("ReportDashboard", policy =>
                policy.Expire(TimeSpan.FromMinutes(5))
                      .SetVaryByHeader("Authorization"));
        });

        var app = builder.Build();

        // === Phase 06 S4: Fail-fast on missing/DEV-prefix secrets in Production ===
        // Runs before MigrateAsync so a missing ConnectionString yields a clear error,
        // not a SQL connection exception buried in migration stack traces.
        app.ValidateProductionSecrets();

        // === ISO audit read-isolation: warn (do NOT fail-fast) when the boundary is dormant in Production ===
        // The NonAudit login is opt-in — NonAuditConnectionString.Get falls back to Default when unset, so a
        // prod deploy that never provisioned the least-privilege login boots silently with isolation OFF. Log
        // once at startup so operators see it before the first /health/ready probe (which also reports Degraded
        // via NonAuditIsolationHealthCheck). Warning-only preserves the opt-in fallback. Backlog:
        // ISO-NONAUDIT-PROD-FAILFAST-HEALTHCHECK.
        if (app.Environment.IsProduction() && !NonAuditConnectionString.IsExplicitlyConfigured(app.Configuration))
        {
            app.Logger.LogWarning(
                "Audit read-isolation DORMANT: ConnectionStrings:NonAudit is unset in Production — non-audit " +
                "module reads run on the broad Default login (can reach [audit]). Provision the NonAudit login " +
                "(scripts/260714-nonaudit-login-provisioning.sql) and set ConnectionStrings:NonAudit to activate isolation.");
        }

        // === M07: Register known domain events in EventCatalogService ===
        // Module-specific event registrations can be added here as needed.

        // === RLS-PRINCIPAL-SKIP-STARTUP-MIGRATIONS: resolve the startup migration gate ===
        // --migrate-only / RUN_MIGRATIONS_ONLY → migrate, then exit (init-container / Helm db-migrate Job).
        // SKIP_STARTUP_MIGRATIONS → runtime boot skips MigrateAsync so the runtime DB login can drop
        // db_ddladmin (a SECURITY POLICY can only be ALTERed by a DDL-capable principal — see
        // docs/deployment-guide.md §4). Default off: dev / test / single-container still migrate on boot.
        var migrationMode = StartupMigrationMode.Resolve(hasMigrateOnlyArg, app.Configuration);

        // === Run EF migrations on startup (idempotent — safe to run every boot) ===
        // Runs in all environments including Testing — Testcontainers starts a fresh SQL Server per run.
        if (migrationMode.RunMigrations)
        {
            using var scope = app.Services.CreateScope();
            var sp = scope.ServiceProvider;
            // Migrate the declared set in order, minus disabled deferred modules — their DbContexts
            // are not registered (registration block skipped) so resolving them would throw, and a
            // fresh database must not receive schemas for modules that are off.
            // StartupMigrationSets.Contexts stays the complete declared set the arch test reads to
            // enforce the ADR-010 subset invariant.
            foreach (var contextType in StartupMigrationSets.EnabledContexts(app.Configuration))
            {
                await ((DbContext)sp.GetRequiredService(contextType)).Database.MigrateAsync();
            }
        }
        else
        {
            app.Logger.LogInformation(
                "Startup migrations skipped (SKIP_STARTUP_MIGRATIONS) — DB schema must be migrated out-of-band " +
                "(init-container / Helm db-migrate Job with an elevated login). See docs/deployment-guide.md §4.");
        }

        if (migrationMode.ExitAfterMigrations)
        {
            app.Logger.LogInformation("--migrate-only / RUN_MIGRATIONS_ONLY: schema migrations complete; exiting without starting the host.");
            return;
        }

        // === PHASE6-RLS-PROD-PRINCIPAL-VERIFY: warn if the prod DB login defeats RLS ===
        // Runs AFTER migrations so the DB/schema exist on first deploy. Advisory only — logs Critical,
        // never throws. Skipped in --migrate-only mode (that invocation uses the elevated migration
        // login, which is db_owner by design). See docs/deployment-guide.md §4 'Application DB Principal'.
        app.WarnIfDbOwnerInProduction();

        // === Connection pool warming (p95 optimization) ===
        // Opens+returns connections so first real request doesn't pay cold-start penalty
        try
        {
            using var warmupScope = app.Services.CreateScope();
            await warmupScope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.CanConnectAsync();
            await warmupScope.ServiceProvider.GetRequiredService<FilesDbContext>().Database.CanConnectAsync();
        }
        catch { /* Non-fatal — pool warms on first request instead */ }

        // === Runtime startup tasks: Hangfire wiring, recurring jobs, data-plane PII upgraders ===
        // (Not reached in --migrate-only mode; runs even when SKIP_STARTUP_MIGRATIONS=true — the
        //  encryption upgraders below are data-plane only and login depends on NormalizedEmailHash.)
        {
            // Force-resolve Hangfire storage singleton from DI so JobStorage.Current is set.
            // Previously UseHangfireDashboard() did this implicitly, but it was moved after
            // UseAuthentication() for security — so we need explicit resolution here.
            _ = app.Services.GetRequiredService<Hangfire.JobStorage>();

            // Recurring-job registration acquires a Hangfire SQL distributed lock during boot.
            // Skip it under the Testing environment: integration-test databases (ephemeral
            // Testcontainers or freshly-truncated local) can't satisfy that lock, and scheduled
            // jobs are never exercised in tests. JobStorage above stays resolved so static
            // BackgroundJob.Enqueue paths still work.
            //
            // The Hangfire:RegisterRecurringJobs flag (default true) lets a Development-environment
            // test host opt out of this block. The static RecurringJob/BackgroundJob facades bind
            // JobStorage.Current once per process on first use; a multi-host test process (e.g. the
            // startup smoke suite, which boots ~7 hosts under Development for ValidateOnBuild/seeders)
            // would freeze that static onto the first booted host's now-disposed Testcontainer, so
            // every later boot's registration hits a dead connection. Flag-gating is safer than
            // widening the Testing check because those hosts must stay Development. Scheduled jobs
            // are not what the smoke suite asserts (it validates DI graph, seeder boot, route probes).
            if (!app.Environment.IsEnvironment("Testing") &&
                app.Configuration.GetValue("Hangfire:RegisterRecurringJobs", true))
            {
                // Register through the boot-lock retry wrapper: the calls below drive Hangfire's static
                // RecurringJob facade, which acquires a SQL distributed lock. A transient lock timeout on
                // a contended boot must not hard-fail the host — retry, then fail-soft (jobs re-register on
                // the next boot / another replica). All registrations are idempotent AddOrUpdate upserts,
                // so re-running the whole block on retry is safe.
                RecurringJobRegistrationRetry.RunWithRetry(
                    register: () =>
                    {
                        // Register NĐ53 recurring archive jobs (idempotent — safe on every startup)
                        HangfireRegistration.RegisterRecurringJobs();

                        // Identity: access review scheduler + SoD audit log purge
                        AqtCoreFW.Identity.Infrastructure.IdentityInfrastructureRegistration.AddIdentityRecurringJobs();

                        // Audit: RTBF SLA breach checker + AuditLogPurgeJob (QĐ 742 §3.2.b retention enforcement)
                        AqtCoreFW.Audit.Infrastructure.AuditInfrastructureRegistration.AddAuditRecurringJobs();

                        // Reporting: nightly report file cleanup (E-06). Deferred-module gate: this is a
                        // STATIC Hangfire facade call, not part of AddReportingInfrastructure — with the
                        // module off it would upsert a job whose type cannot be activated (nightly
                        // failure loop). RemoveIfExists covers the toggle case: a schedule persisted by
                        // a previously-ON deployment must not survive the flag turning off.
                        if (reportingEnabled)
                            AqtCoreFW.Reporting.Infrastructure.ReportingInfrastructureRegistration.AddReportingRecurringJobs();
                        else
                            RecurringJob.RemoveIfExists("reporting.report-cleanup");

                        // Workflow: SLA breach checker — every 5 minutes
                        RecurringJob.AddOrUpdate<SlaBreachCheckerJob>(
                            "workflow-sla-breach-checker",
                            job => job.ExecuteAsync(CancellationToken.None),
                            "*/5 * * * *",
                            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

                        // M05: Escalation checker — every 15 minutes
                        RecurringJob.AddOrUpdate<AqtCoreFW.Workflow.Infrastructure.Jobs.EscalationCheckJob>(
                            "workflow-escalation-checker",
                            job => job.ExecuteAsync(CancellationToken.None),
                            "*/15 * * * *",
                            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

                        // Parallel branch timeout checker — every 5 minutes
                        RecurringJob.AddOrUpdate<AqtCoreFW.Workflow.Infrastructure.Jobs.BranchTimeoutCheckerJob>(
                            "workflow-branch-timeout-checker",
                            job => job.ExecuteAsync(CancellationToken.None),
                            "*/5 * * * *",
                            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

                        // Parallel branch join reconciler — every 5 minutes. Backstop that finishes joins whose
                        // condition is met but whose instance never advanced (crash-after-resolve / transient
                        // conflict). Dormant until a parallel definition goes live.
                        RecurringJob.AddOrUpdate<AqtCoreFW.Workflow.Infrastructure.Jobs.BranchJoinReconcilerJob>(
                            "workflow-branch-join-reconciler",
                            job => job.ExecuteAsync(CancellationToken.None),
                            "*/5 * * * *",
                            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

                        // T5-08: Delegation expiry — every 15 minutes, returns tasks whose delegation rule expired
                        RecurringJob.AddOrUpdate<AqtCoreFW.Workflow.Infrastructure.Jobs.DelegationExpiryJob>(
                            "workflow-delegation-expiry",
                            job => job.ExecuteAsync(CancellationToken.None),
                            "*/15 * * * *",
                            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

                        // M08: Retention policy enforcement — daily at 2 AM UTC
                        RecurringJob.AddOrUpdate<AqtCoreFW.Files.Infrastructure.Jobs.RetentionPolicyEnforcementJob>(
                            "files-retention-policy-enforcement",
                            job => job.ExecuteAsync(CancellationToken.None),
                            "0 2 * * *",
                            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

                        // Signing: reclaim orphaned doc-flow signed blobs — daily at 3 AM UTC
                        RecurringJob.AddOrUpdate<AqtCoreFW.Api.Jobs.SigningDocFlowOrphanBlobSweepJob>(
                            "signing-docflow-orphan-blob-sweep",
                            job => job.ExecuteAsync(CancellationToken.None),
                            "0 3 * * *",
                            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

                        // Forms: orphan-workflow-link retry — durable recovery for FormSubmission→
                        // WorkflowInstance links that failed to establish (5b fail-CLOSED graceful-skip
                        // + M1 throw-after-create orphans). Cron read from bound Forms:WorkflowLinkRetry
                        // config (falls back to the LOCKED default "*/5 * * * *" — see
                        // FormWorkflowLinkRetryOptions) so an ops override doesn't require a redeploy.
                        RecurringJob.AddOrUpdate<FormWorkflowLinkRetryJob>(
                            "forms-workflow-link-retry",
                            job => job.ExecuteAsync(CancellationToken.None),
                            app.Configuration[$"{FormWorkflowLinkRetryOptions.SectionName}:Cron"]
                                ?? new FormWorkflowLinkRetryOptions().Cron,
                            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

                        // M14: Alert evaluation — every 1 minute
                        AlertingRegistration.RegisterRecurringJobs();
                    },
                    logger: app.Services.GetRequiredService<ILogger<AqtCoreFW.Api.Program>>());
            }

            // === QĐ742 §4.4 Wave-4: data-plane PII upgrade sequence (Step 1→2→3, order mandatory) ===
            // The shared runner owns the step ordering and fail-soft (KeyNotFound) handling; the
            // column lists are host-specific. Api owns the identity + audit (Rtbf) + files schemas.
            await AqtCoreFW.Identity.Infrastructure.Startup.StartupEncryptionUpgradeRunner.RunAsync(
                app.Services,
                app.Configuration.GetConnectionString("Default")!,
                plaintextColumns:
                [
                    new("identity", "AspNetUsers", "Email"),
                    new("identity", "AspNetUsers", "NormalizedEmail"),
                    new("identity", "AspNetUsers", "PhoneNumber"),
                    // RtbfRequests PK is InternalId (bigint identity), not Id.
                    new("audit", "RtbfRequests", "SubjectEmail", PkColumn: "InternalId"),
                    new("audit", "RtbfRequests", "CitizenNationalId", PkColumn: "InternalId"),
                ],
                legacyColumns:
                [
                    new("files", "FileRecords", "UploaderDisplayName"),
                ],
                app.Services.GetRequiredService<ILogger<AqtCoreFW.Api.Program>>());
        }

        // === Middleware Pipeline (ORDER IS CRITICAL) ===

        // 0. Forwarded Headers — required behind reverse proxy (Cloudflare Tunnel, nginx)
        app.UseForwardedHeaders(new ForwardedHeadersOptions
        {
            ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
                             | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto
        });

        // 1. Response compression — outermost so all responses are compressed
        app.UseResponseCompression();

        // 2. Security headers — before any content is written
        app.UseSecurityHeaders();

        // 3. Correlation ID — enrich all subsequent logs
        app.UseCorrelationId();

        // 4. Global exception handler — catch all unhandled exceptions
        app.UseGlobalExceptionHandler();

        // 5. IP filter — block before expensive processing
        app.UseIpFilter();

        // 6. Prometheus HTTP metrics middleware
        app.UsePrometheusMetrics();

        // 7. Rate limiting
        app.UseRateLimiter();

        // 8. CORS
        app.UseCors("GovApiPolicy");

        // 9. Cookie policy
        app.UseCookiePolicy(new CookiePolicyOptions
        {
            Secure = CookieSecurePolicy.Always,
            HttpOnly = Microsoft.AspNetCore.CookiePolicy.HttpOnlyPolicy.Always,
            MinimumSameSitePolicy = SameSiteMode.Strict
        });

        // 10. Request localization (vi default)
        app.UseRequestLocalization();

        // HTTPS always enforced — disable only via explicit config flag for Docker dev
        if (builder.Configuration.GetValue("ForceHttps", true))
            app.UseHttpsRedirection();
        app.UseAuthentication();

        // QĐ742 1.3c/d/đ: Password expiry enforcement — returns 403 + X-Password-Expired header
        // MUST run after UseAuthentication (needs User.Identity) but BEFORE UseAuthorization so the
        // compliance gate fires for ALL authenticated requests regardless of permission level.
        app.UseMiddleware<AqtCoreFW.Identity.Infrastructure.Middleware.PasswordExpiryMiddleware>();

        // QĐ742 1.3a: First-login force password change — returns 403 + X-Must-Change-Password header
        // Same ordering rationale: enforce before authorization short-circuits the pipeline.
        app.UseMiddleware<AqtCoreFW.Identity.Infrastructure.Middleware.FirstLoginPasswordChangeMiddleware>();

        app.UseAuthorization();

        // Hangfire dashboard is hosted on the AuthServer (cookie-native), not here — this API host
        // authenticates via Bearer/MultiAuth, which a browser nav to /hangfire can't carry.

        // Gateway: OpenAPI + Scalar UI + YARP reverse proxy
        app.UseGateway();

        // 11. Output caching — after auth so cached responses respect authorization
        app.UseOutputCache();

        // 12. Idempotency — after auth so tenantId claim is available
        app.UseIdempotency();

        // 13. Anti double-submit (X-Request-Token, 5s TTL) — complements idempotency for UI race conditions
        app.UseAntiDoubleSubmit();

        app.MapControllers();

        // SignalR hubs — authenticated connections only
        app.MapHub<NotificationsHub>("/hubs/notifications");
        // ChatHub belongs to the deferred Collaboration module — with the module off its services
        // are not registered, so mapping the hub would fail at the first client connection.
        if (collaborationEnabled)
            app.MapHub<AqtCoreFW.Collaboration.Infrastructure.Realtime.ChatHub>("/hubs/chat");

        // Health check endpoints
        app.MapGovHealthChecks();

        // Prometheus scraping endpoints — prometheus-net serves the HTTP request metrics, the OTel
        // exporter serves the module Meters. Separate routes; a collector must scrape both.
        app.MapMetrics("/metrics");
        app.MapOtelMetricsEndpoint();

        await app.RunAsync();
    }
}
