using AqtCoreFW.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Infrastructure.Configuration;

/// <summary>
/// EF entity configuration variant for HOT tables — high write throughput, time-ordered access pattern.
///
/// Phase-2 Foundation Refactor (2026-05-03): composite clustered index on (CreatedAt, Id) replaces default
/// PK clustering. Reduces page splits + improves locality for time-range queries at 1B-row scale.
///
/// Usage: derive instead of EntityTypeConfigurationBase when entity is high-write/time-scanned
/// (e.g. WorkflowInstanceHistory, MessageLog, AuditTrail).
/// </summary>
public abstract class HotTableConfigurationBase<T> : EntityTypeConfigurationBase<T>
    where T : Entity
{
    /// <summary>PK Id non-clustered; composite clustered key on (CreatedAt, Id).</summary>
    protected override void ConfigureKey(EntityTypeBuilder<T> builder)
    {
        builder.HasKey(x => x.Id).IsClustered(false);
        builder.Property(x => x.Id).ValueGeneratedOnAdd().UseIdentityColumn();

        builder.HasIndex(x => new { x.CreatedAt, x.Id })
               .IsClustered()
               .HasDatabaseName($"CIX_{typeof(T).Name}_CreatedAt_Id");
    }
}
