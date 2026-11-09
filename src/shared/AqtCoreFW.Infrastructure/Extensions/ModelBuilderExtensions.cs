using AqtCoreFW.Infrastructure.Persistence.Outbox;
using AqtCoreFW.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace AqtCoreFW.Infrastructure.Extensions;

/// <summary>EF ModelBuilder extension methods shared across all module DbContexts.</summary>
public static class ModelBuilderExtensions
{
    /// <summary>
    /// Applies combined global query filters to all entity types:
    ///   - ISoftDeletable  → WHERE IsDeleted = false
    ///   - ITenantScoped   → WHERE TenantId = tenantContext.TenantId (skipped for SystemAdmin or no tenant)
    ///
    /// EF Core supports ONE query filter per entity. This method combines both conditions
    /// in a single lambda so neither overwrites the other.
    ///
    /// AdminModuleDbContext calls IgnoreQueryFilters() to bypass — ONLY for admin-scoped services.
    /// </summary>
    public static ModelBuilder ApplyGlobalFilters(this ModelBuilder modelBuilder, ITenantContext tenantContext)
    {
        // Capture the scoped ITenantContext instance — EF Core re-evaluates per query,
        // so TenantId is resolved at query time (not model-build time).
        var tenantContextExpr = Expression.Constant(tenantContext);
        var currentTenantId = Expression.Property(tenantContextExpr, nameof(ITenantContext.TenantId));
        var isSystemAdmin = Expression.Property(tenantContextExpr, nameof(ITenantContext.IsSystemAdmin));
        var tenantHasValue = Expression.Property(currentTenantId, "HasValue");
        var tenantValue = Expression.Property(currentTenantId, "Value");

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var clrType = entityType.ClrType;
            var isSoftDeletable = typeof(ISoftDeletable).IsAssignableFrom(clrType);
            var isTenantScoped = typeof(ITenantScoped).IsAssignableFrom(clrType);

            if (!isSoftDeletable && !isTenantScoped) continue;

            var parameter = Expression.Parameter(clrType, "e");

            // Build soft-delete clause: !e.IsDeleted
            Expression? softDeleteClause = null;
            if (isSoftDeletable)
            {
                var isDeletedProp = Expression.Property(parameter, nameof(ISoftDeletable.IsDeleted));
                softDeleteClause = Expression.Not(isDeletedProp);
            }

            // Build tenant clause: isSystemAdmin || !tenantHasValue || e.TenantId == tenantContext.TenantId.Value
            // i.e. when not SystemAdmin AND tenant is set, enforce TenantId match
            Expression? tenantClause = null;
            if (isTenantScoped)
            {
                var tenantIdProp = Expression.Property(parameter, nameof(ITenantScoped.TenantId));
                var tenantMatch = Expression.Equal(tenantIdProp, tenantValue);

                // shouldFilter = !isSystemAdmin && tenantHasValue
                var shouldFilter = Expression.AndAlso(Expression.Not(isSystemAdmin), tenantHasValue);

                // tenantClause = !shouldFilter || tenantMatch  ≡  isSystemAdmin || !hasValue || match
                tenantClause = Expression.OrElse(Expression.Not(shouldFilter), tenantMatch);
            }

            // Combine: both conditions must be true (AND)
            Expression combined = (softDeleteClause, tenantClause) switch
            {
                ({ } sd, { } tc) => Expression.AndAlso(sd, tc),
                ({ } sd, null) => sd,
                (null, { } tc) => tc,
                _ => throw new InvalidOperationException("Unreachable — filtered above.")
            };

            var lambda = Expression.Lambda(combined, parameter);
            entityType.SetQueryFilter(lambda);
        }

        return modelBuilder;
    }

    /// <summary>
    /// Configures the OutboxMessages table within the specified module schema.
    /// Each module owns its outbox table — no cross-schema outbox dependencies.
    /// </summary>
    public static ModelBuilder ConfigureOutbox(this ModelBuilder modelBuilder, string schemaName)
    {
        modelBuilder.Entity<OutboxMessage>(b =>
        {
            b.ToTable("OutboxMessages", schemaName);
            b.HasKey(e => e.Id);
            b.Property(e => e.EventType).HasMaxLength(500).IsRequired();
            b.Property(e => e.Payload).HasColumnType("nvarchar(max)").IsRequired();
            b.Property(e => e.SchemaName).HasMaxLength(50);
            b.Property(e => e.Error).HasMaxLength(2000);

            // OUTBOX-1-DISP (v2.133p12): claim lock for atomic dispatch.
            b.Property(e => e.ClaimedAtUtc);

            // Partial index for efficient polling — only unprocessed messages
            b.HasIndex(e => e.ProcessedAtUtc)
                .HasDatabaseName($"IX_OutboxMessages_{schemaName}_ProcessedAt");
        });

        return modelBuilder;
    }
}
