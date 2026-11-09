using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Workflow.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF config for WorkflowVersionMigrationItem (child guard table).
/// Table: workflow.VersionMigrationItems.
/// UNIQUE (MigrationId, InstanceId): idempotency guard for Hangfire retry safety.
/// INDEX (MigrationId, Status): paged FE query.
/// Cascade delete from parent migration.
/// NOT derived from EntityTypeConfigurationBase — it's a plain Entity, not AuditableEntity.
/// </summary>
public sealed class WorkflowVersionMigrationItemConfiguration
    : IEntityTypeConfiguration<WorkflowVersionMigrationItem>
{
    public void Configure(EntityTypeBuilder<WorkflowVersionMigrationItem> builder)
    {
        builder.ToTable("VersionMigrationItems");
        // T3 entity: InternalId is the bigint IDENTITY PK (not "Id" — T3 uses InternalId/MessageId pattern).
        builder.HasKey(i => i.InternalId);
        builder.Property(i => i.InternalId).ValueGeneratedOnAdd().UseIdentityColumn();
        // MessageId: T3 Guid AK (v7 monotonic), unique for outbox dedup.
        builder.Property(i => i.MessageId).HasColumnType("uniqueidentifier").IsRequired();
        builder.HasIndex(i => i.MessageId).IsUnique().HasDatabaseName("AK_WorkflowVersionMigrationItem_MessageId");

        builder.Property(i => i.CreatedAt).HasColumnType("datetime2(3)");

        builder.Property(i => i.MigrationId).IsRequired();
        builder.Property(i => i.InstanceId).IsRequired();
        builder.Property(i => i.OldStateId).IsRequired();
        builder.Property(i => i.NewStateId).IsRequired();
        builder.Property(i => i.Status).IsRequired()
            .HasConversion<int>();
        builder.Property(i => i.ErrorMessage).HasMaxLength(1000);
        builder.Property(i => i.AttemptCount).IsRequired().HasDefaultValue(0);
        // ValueComparer ensures byte[] RowVersion uses structural equality
        // (required for EF InMemory provider to correctly match concurrency tokens)
        var byteArrayComparer = new ValueComparer<byte[]>(
            (a, b) => a != null && b != null && a.SequenceEqual(b),
            v => v.Aggregate(0, (a, b) => HashCode.Combine(a, b)),
            v => v.ToArray());

        builder.Property(i => i.RowVersion)
            .IsRowVersion()
            .IsConcurrencyToken()
            .HasConversion<byte[]>(byteArrayComparer, byteArrayComparer);

        // Idempotency guard: Hangfire retry cannot insert duplicate (MigrationId, InstanceId)
        builder.HasIndex(i => new { i.MigrationId, i.InstanceId })
            .IsUnique()
            .HasDatabaseName("UQ_VersionMigrationItems_MigrationId_InstanceId");

        // Paged FE query: items for a migration filtered by status
        builder.HasIndex(i => new { i.MigrationId, i.Status })
            .HasDatabaseName("IX_VersionMigrationItems_MigrationId_Status");

        // Cascade delete when parent migration is deleted
        builder.HasOne<WorkflowVersionMigration>()
            .WithMany()
            .HasForeignKey(i => i.MigrationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
