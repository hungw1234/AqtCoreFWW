using AqtCoreFW.Audit.Domain.Entities;
using AqtCoreFW.Audit.Infrastructure.Persistence.Configurations;
using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.Infrastructure.Extensions;
using AqtCoreFW.Infrastructure.Persistence;
using AqtCoreFW.Infrastructure.Persistence.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AqtCoreFW.Audit.Infrastructure.Persistence;

/// <summary>
/// Audit module DB context — append-only enforcement.
/// Overrides SaveChanges to throw on Modified/Deleted entries (tamper prevention).
/// Does NOT inherit ModuleDbContext soft-delete filter — audit logs are never soft-deleted.
/// Schema: "audit".
///
/// IKeyResolver is injected so RtbfRequestConfiguration can wire EncryptedStringConverter
/// for SubjectEmail and CitizenNationalId (QĐ 742 §4.4 Wave-4).
/// Parameterless ctor (for design-time migration tool) omits encryption.
/// </summary>
public sealed class AuditDbContext : DbContext
{
    private readonly IKeyResolver? _keyResolver;

    /// <summary>Design-time migration scaffolding ctor — no encryption key.</summary>
    public AuditDbContext(DbContextOptions<AuditDbContext> options) : base(options) { }

    /// <summary>Runtime ctor — wires versioned encryption for Rtbf PII columns.</summary>
    public AuditDbContext(
        DbContextOptions<AuditDbContext> options,
        IKeyResolver keyResolver)
        : base(options)
    {
        _keyResolver = keyResolver;
    }

    public DbSet<AuditLogEntry> AuditLogEntries { get; set; } = default!;
    public DbSet<AuditRetentionPolicy> AuditRetentionPolicies { get; set; } = default!;

    /// <summary>Per-tenant HMAC chain tail-attestation (schema/migration only — signer/verifier read+write via raw SQL).</summary>
    public DbSet<ChainWatermark> ChainWatermarks { get; set; } = default!;
    public DbSet<LoginAttempt> LoginAttempts { get; set; } = default!;
    public DbSet<PersonalDataProcessingLog> PersonalDataProcessingLogs { get; set; } = default!;
    public DbSet<SecurityEventLog> SecurityEventLogs { get; set; } = default!;
    public DbSet<SecurityIncident> SecurityIncidents { get; set; } = default!;
    public DbSet<RtbfRequest> RtbfRequests { get; set; } = default!;

    // M15 AI Governance
    public DbSet<AiPromptTrace> AiPromptTraces { get; set; } = default!;
    public DbSet<AiOutputReview> AiOutputReviews { get; set; } = default!;
    public DbSet<CompliancePolicy> CompliancePolicies { get; set; } = default!;
    public DbSet<CompliancePolicyEvaluation> CompliancePolicyEvaluations { get; set; } = default!;

    // QĐ 742 cấp 4 §4.4 — data classification handling rules (Wave 2)
    public DbSet<ClassificationRule> ClassificationRules { get; set; } = default!;

    // Slice 1B-2: module-agnostic Outbox escape hatch for AuditLogAppendedIntegrationEvent
    // (AuditLogEntry is not an IAggregateRoot, so OutboxInterceptor never captures it —
    // AuditService enqueues explicitly via IOutboxWriter instead). AuditDbContext does NOT
    // inherit ModuleDbContext (no soft-delete/tenant global filters — append-only, nullable
    // tenant), so it wires the OutboxMessages table directly here, mirroring exactly what
    // ModuleDbContext.OnModelCreating does for every other module's DbContext.
    public DbSet<OutboxMessage> OutboxMessages { get; set; } = default!;

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        // Suppress PendingModelChangesWarning during startup migrations.
        // Model drift during development is expected until all pending migrations are applied.
        optionsBuilder.ConfigureWarnings(w =>
            w.Ignore(RelationalEventId.PendingModelChangesWarning));
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema("audit");

        // Assembly scan picks up all IEntityTypeConfiguration with parameterless ctors.
        // RtbfRequestConfiguration has a parameterless ctor (design-time path, no encryption).
        // We override the scanned registration by applying the resolver-injected version afterwards.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AuditDbContext).Assembly);

        // Override RtbfRequestConfiguration with the IKeyResolver-injected variant
        // so SubjectEmail and CitizenNationalId use versioned encryption keys from Vault at runtime.
        modelBuilder.ApplyConfiguration(new RtbfRequestConfiguration(_keyResolver));

        // Slice 1B-2: audit.OutboxMessages table — same shape ModuleDbContext gives every other
        // module's schema, applied directly since AuditDbContext doesn't inherit ModuleDbContext.
        modelBuilder.ConfigureOutbox("audit");
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ThrowIfMutating();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        ThrowIfMutating();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void ThrowIfMutating()
    {
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted
                && IsImmutableAuditEntity(entry))
            {
                throw new InvalidOperationException(
                    $"Audit entity '{entry.Metadata.Name}' is append-only. " +
                    $"Update and Delete are prohibited (tamper prevention).");
            }
        }
    }

    private static bool IsImmutableAuditEntity(EntityEntry entry) =>
        entry.Entity is AuditLogEntry or LoginAttempt
            or PersonalDataProcessingLog or SecurityEventLog
            or AiPromptTrace or CompliancePolicyEvaluation;
}
