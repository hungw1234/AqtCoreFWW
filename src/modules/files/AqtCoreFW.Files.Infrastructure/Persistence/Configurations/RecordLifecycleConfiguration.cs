using AqtCoreFW.Files.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Files.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF config for RecordLifecycle (T3 plain-class: InternalId bigint PK + MessageId Guid AK).
/// One lifecycle record per FileRecord — unique constraint on FileRecordId enforces this.
/// ScheduledDestroyAt indexed for enforcement job to find records due for destruction.
/// Phase-4b B3b: T3 does NOT extend Entity — use IEntityTypeConfiguration&lt;T&gt; directly.
///   PK = InternalId (bigint IDENTITY). MessageId = Guid AK.
///   FileRecordId = long FK to FileRecords.Id (bigint after T2 re-tier).
///   RetentionPolicyId = long? FK to RetentionPolicies.Id (bigint after T1 re-tier).
/// </summary>
public sealed class RecordLifecycleConfiguration : IEntityTypeConfiguration<RecordLifecycle>
{
    public void Configure(EntityTypeBuilder<RecordLifecycle> builder)
    {
        builder.ToTable("RecordLifecycles");

        // T3 PK: InternalId (bigint IDENTITY — never exposed externally)
        builder.HasKey(l => l.InternalId);
        builder.Property(l => l.InternalId).ValueGeneratedOnAdd().UseIdentityColumn();

        // T3 AK: MessageId (Guid v7 monotonic — outbox dedup / event correlation)
        builder.Property(l => l.MessageId).HasColumnType("uniqueidentifier").IsRequired();
        builder.HasIndex(l => l.MessageId).IsUnique().HasDatabaseName("AK_RecordLifecycles_MessageId");

        builder.Property(l => l.CreatedAt).HasColumnType("datetime2(3)").IsRequired();

        // FK to FileRecord.Id (bigint) — long after Phase-4a T2 re-tier
        builder.Property(l => l.FileRecordId).IsRequired();

        builder.Property(l => l.CurrentStatus).IsRequired();

        // FK to RetentionPolicy.Id (bigint) — long? nullable
        builder.Property(l => l.RetentionPolicyId);

        builder.Property(l => l.ArchivedAt).HasColumnType("datetime2(3)");
        builder.Property(l => l.ScheduledDestroyAt).HasColumnType("datetime2(3)");
        builder.Property(l => l.DestroyedAt).HasColumnType("datetime2(3)");
        builder.Property(l => l.DestroyedBy);

        // One lifecycle record per file
        builder.HasIndex(l => l.FileRecordId)
            .IsUnique()
            .HasDatabaseName("UX_RecordLifecycles_FileRecordId");

        // Enforcement job: find records scheduled for destruction
        builder.HasIndex(l => new { l.CurrentStatus, l.ScheduledDestroyAt })
            .HasDatabaseName("IX_RecordLifecycles_Status_ScheduledDestroyAt");
    }
}
