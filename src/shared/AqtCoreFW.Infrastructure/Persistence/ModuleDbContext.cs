using AqtCoreFW.Infrastructure.Extensions;
using AqtCoreFW.Infrastructure.Persistence.Outbox;
using AqtCoreFW.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace AqtCoreFW.Infrastructure.Persistence;

/// <summary>
/// Base DbContext for all AqtCoreFW modules.
/// Enforces: per-module schema, soft-delete + tenant global filters, outbox table, audit interceptors.
///
/// Two patterns per module (Security requirement — CRITICAL):
///   ModuleDbContext            — global filters ON (TenantId + IsDeleted) — ALL business logic
///   AdminModuleDbContext : ModuleDbContext — IgnoreQueryFilters() — ONLY Admin services; must accept explicit tenantId param
///
/// ArchUnit rules (Phase 10):
///   - Classes NOT in *.Admin.* namespace MUST NOT call IgnoreQueryFilters()
///   - AdminModuleDbContext methods MUST have explicit tenantId param (no ITenantContext auto-inject)
/// </summary>
public abstract class ModuleDbContext : DbContext
{
    private readonly ITenantContext _tenantContext;

    protected ModuleDbContext(DbContextOptions options, ITenantContext tenantContext)
        : base(options)
    {
        _tenantContext = tenantContext;
    }

    /// <summary>Schema name for this module (e.g. "notifications", "identity", "cases").</summary>
    protected abstract string SchemaName { get; }

    public DbSet<OutboxMessage> OutboxMessages { get; set; } = default!;

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);

        // Guard: only configure SQL Server split-query when a relational provider is already set.
        // When tests pass UseInMemoryDatabase() options, IsConfigured=true but the provider is
        // InMemory — calling UseSqlServer here would register a second provider and throw
        // "Only a single database provider can be registered."
        // In production, SQL Server is set via AddDbContext factory before OnConfiguring fires;
        // IsConfigured=true and the provider is already SqlServer — calling UseSqlServer again
        // augments it with SplitQuery behavior without conflict.
        if (optionsBuilder.IsConfigured && IsRelationalProvider(optionsBuilder))
        {
            // Split queries by default — prevents MultipleCollectionIncludeWarning and
            // avoids cartesian-product N+1 when Include() spans ≥2 collection navigations.
            optionsBuilder.UseSqlServer(sql => sql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery));
        }

        optionsBuilder.ConfigureWarnings(w =>
        {
            // PendingModelChangesWarning — design-time model may differ from runtime
            // (FileRecordConfiguration needs IConfiguration for encryption key at runtime
            // but design-time factory provides empty config → different model snapshot).
            w.Ignore(RelationalEventId.PendingModelChangesWarning);

            // SkippedEntityTypeConfigurationWarning — FileRecordConfiguration has a non-parameterless
            // ctor (requires IConfiguration for encryption). ApplyConfigurationsFromAssembly skips it
            // by design; the module registers it manually via modelBuilder.ApplyConfiguration().
            w.Ignore(CoreEventId.SkippedEntityTypeConfigurationWarning);

            // NoEntityTypeConfigurationsWarning — MasterData, Organization, and SystemParams
            // modules configure entities via Fluent API directly in OnModelCreating, not via
            // separate IEntityTypeConfiguration classes. This is intentional.
            w.Ignore(CoreEventId.NoEntityTypeConfigurationsWarning);
        });
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema(SchemaName);

        // Entity configurations from the calling module's assembly (not this base assembly)
        modelBuilder.ApplyConfigurationsFromAssembly(GetType().Assembly);

        // Combined soft-delete + tenant filter (EF Core supports one filter per entity type)
        modelBuilder.ApplyGlobalFilters(_tenantContext);

        modelBuilder.ConfigureOutbox(SchemaName);
    }

    /// <summary>
    /// Default no-tracking query — use in ALL query handlers (~25% memory reduction).
    /// ArchUnit (Phase 10): IQueryHandler MUST NOT call Set&lt;T&gt;() or TrackingQuery&lt;T&gt;() directly.
    /// </summary>
    public IQueryable<T> Query<T>() where T : class => Set<T>().AsNoTracking();

    /// <summary>
    /// Tracking query — use ONLY in command handlers that update entities.
    /// Apply AsSplitQuery() when Include() has ≥ 2 collection navigations.
    /// </summary>
    public IQueryable<T> TrackingQuery<T>() where T : class => Set<T>();

    /// <summary>
    /// Returns true when a relational (SQL Server) provider is already configured.
    /// Returns false for InMemory provider (used in unit tests) — prevents dual-provider conflict.
    /// </summary>
    private static bool IsRelationalProvider(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.Options.Extensions.OfType<RelationalOptionsExtension>().Any();
}
