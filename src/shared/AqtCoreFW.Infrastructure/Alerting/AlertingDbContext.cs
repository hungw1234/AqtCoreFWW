using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AqtCoreFW.Infrastructure.Alerting;

/// <summary>
/// Minimal DbContext for alerting entities — schema: alerting.
/// Uses plain DbContext (not ModuleDbContext) — alerting is cross-cutting infrastructure,
/// not a tenant-scoped module, so the global TENANT filter is intentionally omitted.
/// AlertRules are system-wide. Soft-delete IS filtered: AlertRuleConfiguration inherits
/// EntityTypeConfigurationBase, which applies HasQueryFilter(!IsDeleted) — DeleteRule
/// soft-deletes and the rule disappears from all queries. Enabled is a separate
/// activation flag, not the soft-delete mechanism.
/// </summary>
public sealed class AlertingDbContext : DbContext
{
    public AlertingDbContext(DbContextOptions<AlertingDbContext> options) : base(options) { }

public DbSet<AlertRule> AlertRules => Set<AlertRule>();

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
        modelBuilder.HasDefaultSchema("alerting");
        modelBuilder.ApplyConfiguration(new AlertRuleConfiguration());
    }
}
