using AqtCoreFW.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Infrastructure.Configuration;

/// <summary>
/// Base EF entity configuration for all Entity subclasses (long Id PK).
///
/// Phase-2 Foundation Refactor (2026-05-03): collapsed TId generic; Entity is non-generic with bigint identity Id.
/// Auto-applies schema universals:
///   - Id: bigint IDENTITY, PK, default clustered (overridden by HotTableConfigurationBase for hot tables)
///   - CreatedAt/UpdatedAt: datetime2(3) (CLR DateTime, UTC convention)
///   - PublicId: uniqueidentifier NOT NULL when entity implements IPublicIdentified (set by domain factory via IGuidGenerator — no SQL DEFAULT fallback)
///   - MessageId: uniqueidentifier NOT NULL when entity implements IMessageIdentified
///   - SoftDelete query filter (IsDeleted = false)
///   - PageCompression annotation (consumed by Phase-6 post-migration script: ALTER INDEX ... REBUILD WITH (DATA_COMPRESSION = PAGE))
///   - Tenant-scoped composite indexes for ITenantScoped entities: (TenantId, Id) + (TenantId, CreatedAt DESC)
///   - Soft-delete index for fast filter scan
///
/// Usage:
///   public class NotificationConfiguration : EntityTypeConfigurationBase&lt;Notification&gt;
///   {
///       protected override void ConfigureEntity(EntityTypeBuilder&lt;Notification&gt; builder) { ... }
///   }
/// </summary>
public abstract class EntityTypeConfigurationBase<T> : IEntityTypeConfiguration<T>
    where T : Entity
{
    public virtual void Configure(EntityTypeBuilder<T> builder)
    {
        ConfigureKey(builder);
        ConfigureTimestamps(builder);
        ConfigureSoftDelete(builder);
        ConfigurePublicAndMessageIds(builder);
        ConfigurePageCompressionAnnotation(builder);
        ConfigureTenantCompositeIndexes(builder);

        ConfigureEntity(builder);
    }

    /// <summary>Implement entity-specific column mappings, constraints, and indexes.</summary>
    protected abstract void ConfigureEntity(EntityTypeBuilder<T> builder);

    /// <summary>bigint IDENTITY PK. Default clustered. HotTableConfigurationBase overrides to non-clustered + composite clustered (CreatedAt, Id).</summary>
    protected virtual void ConfigureKey(EntityTypeBuilder<T> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedOnAdd().UseIdentityColumn();
    }

    protected virtual void ConfigureTimestamps(EntityTypeBuilder<T> builder)
    {
        builder.Property(x => x.CreatedAt).HasColumnType("datetime2(3)").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnType("datetime2(3)");
    }

    protected virtual void ConfigureSoftDelete(EntityTypeBuilder<T> builder)
    {
        builder.HasQueryFilter(x => !x.IsDeleted);
        builder.HasIndex(nameof(Entity.IsDeleted))
            .HasDatabaseName($"IX_{typeof(T).Name}_IsDeleted");
    }

    protected virtual void ConfigurePublicAndMessageIds(EntityTypeBuilder<T> builder)
    {
        // T2: IPublicIdentified — Guid AK, set by factory via IGuidGenerator. No SQL DEFAULT (M2 amendment 2026-05-03).
        if (typeof(IPublicIdentified).IsAssignableFrom(typeof(T)))
        {
            builder.Property(nameof(IPublicIdentified.PublicId))
                   .HasColumnType("uniqueidentifier")
                   .IsRequired();

            builder.HasIndex(nameof(IPublicIdentified.PublicId))
                   .IsUnique()
                   .HasDatabaseName($"AK_{typeof(T).Name}_PublicId");
        }

        // T3: IMessageIdentified — Guid AK for outbox dedup / event correlation.
        if (typeof(IMessageIdentified).IsAssignableFrom(typeof(T)))
        {
            builder.Property(nameof(IMessageIdentified.MessageId))
                   .HasColumnType("uniqueidentifier")
                   .IsRequired();

            builder.HasIndex(nameof(IMessageIdentified.MessageId))
                   .IsUnique()
                   .HasDatabaseName($"AK_{typeof(T).Name}_MessageId");
        }
    }

    /// <summary>Annotation consumed by Phase-6 post-migration script. EF migrations DO NOT emit DDL for this — out-of-band ALTER INDEX required.</summary>
    protected virtual void ConfigurePageCompressionAnnotation(EntityTypeBuilder<T> builder)
    {
        builder.HasAnnotation("SqlServer:PageCompression", "PAGE");
    }

    /// <summary>Composite indexes for tenant-scoped entities — primary access pattern is (TenantId, …).</summary>
    protected virtual void ConfigureTenantCompositeIndexes(EntityTypeBuilder<T> builder)
    {
        if (!typeof(ITenantScoped).IsAssignableFrom(typeof(T))) return;

        builder.HasIndex("TenantId", nameof(Entity.Id))
            .HasDatabaseName($"IX_{typeof(T).Name}_TenantId_Id");

        builder.HasIndex("TenantId", nameof(Entity.CreatedAt))
            .HasDatabaseName($"IX_{typeof(T).Name}_TenantId_CreatedAt")
            .IsDescending(false, true); // TenantId ASC, CreatedAt DESC
    }

    /// <summary>Helper: adds (TenantId, {statusProperty}) composite index for workflow-state entities.</summary>
    protected static void AddStatusIndex(EntityTypeBuilder<T> builder, string statusPropertyName = "Status")
    {
        builder.HasIndex("TenantId", statusPropertyName)
            .HasDatabaseName($"IX_{typeof(T).Name}_TenantId_{statusPropertyName}");
    }
}
