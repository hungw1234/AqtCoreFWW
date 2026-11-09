using AqtCoreFW.Audit.Application.Queries.GetClassificationInventory;
using AqtCoreFW.Audit.Application.Services;
using AqtCoreFW.Audit.Domain.Repositories;
using AqtCoreFW.Audit.Domain.Services;
using AqtCoreFW.Audit.Infrastructure.Compliance;
using AqtCoreFW.Audit.Infrastructure.HealthChecks;
using AqtCoreFW.Audit.Infrastructure.Jobs;
using AqtCoreFW.Audit.Infrastructure.Persistence;
using AqtCoreFW.Audit.Infrastructure.Services;
using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.Infrastructure.Persistence;
using AqtCoreFW.Infrastructure.Persistence.Outbox;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.AI;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.SharedKernel.Contracts.Rtbf;
using Hangfire;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AqtCoreFW.Audit.Infrastructure;

/// <summary>Registers Audit Infrastructure services: DbContext, repositories, services.</summary>
public static class AuditInfrastructureRegistration
{
    public static IServiceCollection AddAuditInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // MediatR handlers in Infrastructure assembly (AuditDbContext-dependent handlers)
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(AuditInfrastructureRegistration).Assembly));

        // QĐ 742 §4.4 Wave-4: AuditDbContext receives IKeyResolver via DI (optional ctor param).
        // When IKeyResolver is registered (runtime), EF selects the (options, keyResolver) ctor
        // so RtbfRequestConfiguration wires EncryptedStringConverter for SubjectEmail + CitizenNationalId.
        // When IKeyResolver is absent (design-time / test stubs), EF falls back to (options) ctor.
        services.AddDbContext<AuditDbContext>((sp, options) =>
        {
            options.UseSqlServer(
                configuration.GetConnectionString("Default"),
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "audit"));

            options.AddInterceptors(
                sp.GetRequiredService<SlowQueryInterceptor>(),
                sp.GetRequiredService<SoftDeleteInterceptor>(),
                sp.GetRequiredService<OutboxInterceptor>(),
                sp.GetRequiredService<AuditableEntityInterceptor>(),
                sp.GetRequiredService<TenantSessionContextInterceptor>());
        });

        // Slice 1B-2: audit's own OutboxMessages table lives outside the shared "12 ModuleDbContext"
        // allowlist registered centrally in AqtCoreFW.Infrastructure's InfrastructureRegistration —
        // AuditDbContext does not inherit ModuleDbContext, so it self-registers here. Idempotent.
        services.AddOutboxSchema("audit");

        services.AddScoped<IAuditLogRepository, AuditLogRepository>();
        services.AddScoped<IAuditRetentionPolicyRepository, AuditRetentionPolicyRepository>();
        services.AddScoped<ISecurityIncidentRepository, SecurityIncidentRepository>();
        services.AddScoped<IRtbfRequestRepository, RtbfRequestRepository>();

        // M15 AI Governance repositories
        services.AddScoped<IAiPromptTraceRepository, AiPromptTraceRepository>();
        services.AddScoped<IAiOutputReviewRepository, AiOutputReviewRepository>();
        services.AddScoped<ICompliancePolicyRepository, CompliancePolicyRepository>();

        // QĐ 742 cấp 4 §4.4 — data classification inventory (Wave 2)
        services.AddSingleton<IDataClassificationService, DataClassificationService>();

        // Shared audit-row persist path (atomic row + outbox + HMAC enqueue), reused by AuditService
        // (sync request path) and AuditIngestionRetryJob (durable retry path). Scoped: enlists the
        // request/job-scoped ICrossModuleUnitOfWork + repository.
        services.AddScoped<AuditEntryPersister>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IAuditLogger, AuditService>();
        // HMAC chain key resolver — Vault-backed (aqt/audit/hmac-v{N}) with legacy Audit:HmacKey fallback.
        // Singleton: cache + IVaultClient are thread-safe; mirrors VaultKeyResolver / VaultPepperResolver pattern.
        // Required by HmacChainService below; only register on hosts that execute HMAC Hangfire jobs (Api host).
        services.AddSingleton<IAuditHmacKeyResolver, VaultAuditHmacKeyResolver>();
        // Register HmacChainService for both direct use (Hangfire job) and IHmacChainVerifier (MediatR handler).
        // Connection string is passed explicitly so SignTenantChainAsync opens a dedicated raw SqlConnection
        // independent of the EF AuditDbContext. The EF connection carries TenantSessionContextInterceptor
        // which independently manages SESSION_CONTEXT — sharing it with the batch signer would cause the
        // interceptor to clear the RLS bypass flag between the read and write steps of the batch.
        //
        // adminConnectionString (ConnectionStrings:AuditAdmin) is used ONLY for the cross-tenant tenant
        // enumeration query in BackfillAllTenantsAsync. It must be a db_owner-capable connection so the
        // RLS FILTER predicate is bypassed, returning all distinct TenantIds in one query. Falls back to
        // the runtime Default when AuditAdmin is not configured (dev: sa Default already has db_owner).
        services.AddSingleton<IBackgroundAuditJobEnqueuer, HangfireAuditJobEnqueuer>();
        services.AddScoped<HmacChainService>(sp =>
        {
            var repo = sp.GetRequiredService<IAuditLogRepository>();
            var keyResolver = sp.GetRequiredService<IAuditHmacKeyResolver>();
            var logger = sp.GetRequiredService<ILogger<HmacChainService>>();
            var guidGenerator = sp.GetRequiredService<IGuidGenerator>();
            var jobEnqueuer = sp.GetRequiredService<IBackgroundAuditJobEnqueuer>();
            var connStr = configuration.GetConnectionString("Default")
                ?? throw new InvalidOperationException(
                    "HmacChainService requires ConnectionStrings:Default — not configured.");
            var adminConnStr = configuration.GetConnectionString("AuditAdmin"); // null = fallback to Default
            return new HmacChainService(repo, keyResolver, logger, connStr, adminConnStr, guidGenerator, jobEnqueuer);
        });
        services.AddScoped<IHmacChainVerifier>(sp => sp.GetRequiredService<HmacChainService>());
        services.AddScoped<IHmacChainBackfiller>(sp => sp.GetRequiredService<HmacChainService>());

        // RTBF PII anonymizer (collected by ProcessRtbfRequestCommandHandler via IEnumerable<IModulePiiAnonymizer>)
        services.AddScoped<IModulePiiAnonymizer, AuditPiiAnonymizer>();

        // Cross-module client (v2.132p10): expose compliance evidence to Reporting via owned-schema queries
        services.AddScoped<IAuditEvidenceClient, InProcessAuditEvidenceClient>();

        // M15 AI Governance services
        services.AddScoped<IPiiDetectionService, PiiDetectionService>();
        services.AddScoped<IAiPromptTracer, AiPromptTraceDecorator>();

        // SLA breach checker — registered as scoped; Hangfire resolves from DI
        services.AddScoped<RtbfSlaBreachCheckerJob>();
        // QĐ 742 §3.2.b — retention-driven purge job (phase-01 slice-D2). Scoped: each
        // Hangfire run gets a fresh AuditDbContext + connection (clean SESSION_CONTEXT scope).
        services.AddScoped<AuditLogPurgeJob>();
        // Chain-signing safety net — re-signs dormant chains the append-time enqueue never re-fires.
        // Scoped: resolves HmacChainService (scoped) per run.
        services.AddScoped<AuditChainSweepJob>();
        // Durable audit-ingestion retry — recovers an audit row whose synchronous write failed
        // (cell #7 L1). Scoped: reuses AuditEntryPersister + repository per Hangfire run.
        services.AddScoped<AuditIngestionRetryJob>();

        // Unsigned-backlog health check — monitors entries pending HMAC signing across ALL tenants.
        // Uses the AuditAdmin (db_owner-capable) connection so the RLS FILTER predicate is bypassed
        // and the count reflects the true cross-tenant backlog (not just null-tenant rows visible under
        // audit_admin_bypass on a non-db_owner login). Falls back to Default when AuditAdmin is unset.
        services.Configure<UnsignedAuditBacklogHealthCheckOptions>(
            configuration.GetSection("Audit:HealthChecks:UnsignedBacklog"));

        var adminConnStrForHc = configuration.GetConnectionString("AuditAdmin")
            ?? configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException(
                "UnsignedAuditBacklogHealthCheck requires ConnectionStrings:Default — not configured.");

        // Factory registration: adminConnectionString is a plain string that cannot be resolved
        // from DI, so we capture it in the closure (same pattern as HmacChainService registration).
        // IOptions<> and ILogger<> are resolved from the container so they receive real implementations.
        services.AddHealthChecks()
            .Add(new HealthCheckRegistration(
                name: "audit-unsigned-backlog",
                factory: sp => new UnsignedAuditBacklogHealthCheck(
                    sp.GetRequiredService<IOptions<UnsignedAuditBacklogHealthCheckOptions>>(),
                    adminConnStrForHc,
                    sp.GetRequiredService<ILogger<UnsignedAuditBacklogHealthCheck>>()),
                failureStatus: HealthStatus.Unhealthy,
                tags: ["audit", "ready"]));

        return services;
    }

    /// <summary>
    /// Registers Hangfire recurring jobs for the Audit module.
    /// Called from the API host startup after Hangfire server is configured.
    /// </summary>
    public static void AddAuditRecurringJobs()
    {
        // Daily at midnight — scan for overdue RTBF requests (Law 91/2025 SLA: 30 days)
        RecurringJob.AddOrUpdate<RtbfSlaBreachCheckerJob>(
            recurringJobId: "audit.rtbf-sla-breach-checker",
            methodCall: j => j.ExecuteAsync(),
            cronExpression: Cron.Daily());

        // 03:00 UTC daily — purge AuditLogEntries past per-(tenant, category) retention.
        // Off-peak; 1h after typical SodAuditLogPurgeJob slot to avoid audit DB contention.
        RecurringJob.AddOrUpdate<AuditLogPurgeJob>(
            recurringJobId: "audit.log-purge",
            methodCall: j => j.ExecuteAsync(CancellationToken.None),
            cronExpression: "0 3 * * *");

        // Every 5 minutes — re-sign dormant chains. Append-time signing leaves a gap: a chain that
        // stops receiving new entries (e.g. the low-traffic system/NULL chain) never re-fires the
        // per-append enqueue, so rows left at SequenceId=0 sit unsigned and trip the unsigned-backlog
        // health check. The 5-minute interval matches that check's 10-minute oldest-unsigned threshold
        // (two intervals). The sweep only enqueues — no inline sign, no audit entry — so it cannot fork
        // the chain or loop.
        RecurringJob.AddOrUpdate<AuditChainSweepJob>(
            recurringJobId: "audit.chain-sweep",
            methodCall: j => j.ExecuteAsync(CancellationToken.None),
            cronExpression: "*/5 * * * *");

        // Run one sweep immediately at startup so a backlog already present at deploy time is healed
        // without waiting for the first 5-minute tick (RecurringJob.AddOrUpdate does not fire on
        // registration). Idempotent: a no-op when no chain is backlogged. Unlike the recurring
        // registration above, this one-shot is not deduped across hosts — N API replicas would each
        // enqueue one immediate sweep at startup. Harmless: the sweep only reads + enqueues, and the
        // resulting per-tenant signs serialize on the per-tenant applock and collapse to no-ops once
        // the rows are signed.
        BackgroundJob.Enqueue<AuditChainSweepJob>(j => j.ExecuteAsync(CancellationToken.None));
    }
}
