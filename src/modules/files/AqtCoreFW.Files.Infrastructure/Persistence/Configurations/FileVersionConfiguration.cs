using AqtCoreFW.Files.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Files.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF config for FileVersion (T3 plain-class: InternalId bigint PK + MessageId Guid AK).
/// Unique constraint on (FileRecordId, VersionNumber) prevents duplicate version numbers per file.
/// ContentHash indexed for deduplication checks across versions.
/// Phase-4b B3b: T3 does NOT extend Entity — use IEntityTypeConfiguration&lt;T&gt; directly.
///   PK = InternalId (bigint IDENTITY). MessageId = Guid AK (uniqueidentifier).
///   FileRecordId = long FK to FileRecords.Id.
/// </summary>
public sealed class FileVersionConfiguration : IEntityTypeConfiguration<FileVersion>
{
    public void Configure(EntityTypeBuilder<FileVersion> builder)
    {
        builder.ToTable("FileVersions");

        // T3 PK: InternalId (bigint IDENTITY — never exposed externally)
        builder.HasKey(v => v.InternalId);
        builder.Property(v => v.InternalId).ValueGeneratedOnAdd().UseIdentityColumn();

        // T3 AK: MessageId (Guid v7 monotonic — outbox dedup / event correlation)
        builder.Property(v => v.MessageId).HasColumnType("uniqueidentifier").IsRequired();
        builder.HasIndex(v => v.MessageId).IsUnique().HasDatabaseName("AK_FileVersions_MessageId");

        builder.Property(v => v.CreatedAt).HasColumnType("datetime2(3)").IsRequired();

        // FK to FileRecord.Id (bigint) — long after Phase-4a T2 re-tier
        builder.Property(v => v.FileRecordId).IsRequired();
        builder.Property(v => v.VersionNumber).IsRequired();
        builder.Property(v => v.StoragePath).HasMaxLength(1024).IsRequired();
        builder.Property(v => v.FileSize).IsRequired();
        builder.Property(v => v.ContentHash).HasMaxLength(64).IsRequired();
        builder.Property(v => v.UploadedBy).IsRequired();
        builder.Property(v => v.UploadedAt).HasColumnType("datetime2(3)").IsRequired();
        builder.Property(v => v.Comment).HasMaxLength(500);
        builder.Property(v => v.TenantId).IsRequired();

        // Version numbers must be unique per file record
        builder.HasIndex(v => new { v.FileRecordId, v.VersionNumber })
            .IsUnique()
            .HasDatabaseName("UX_FileVersions_FileRecordId_VersionNumber");

        // Deduplication: find existing version by content hash within tenant
        builder.HasIndex(v => new { v.TenantId, v.ContentHash })
            .HasDatabaseName("IX_FileVersions_TenantId_ContentHash");
    }
}
