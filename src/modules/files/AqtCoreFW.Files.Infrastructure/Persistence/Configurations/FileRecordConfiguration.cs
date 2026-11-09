using AqtCoreFW.Files.Domain.Entities;
using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Files.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for FileRecord aggregate (T2: long Id PK + Guid PublicId AK).
/// UploaderDisplayName encrypted at rest (AES-256-GCM) — PII field.
/// StorageKey indexed for ClamAV job lookups.
///
/// IKeyResolver is preferred over raw byte[] so the converter writes v{N}: prefixes
/// and decrypts any stored version (dual-decode, D10.2).
/// No parameterless ctor — forces manual registration in FilesDbContext.OnModelCreating
/// so IKeyResolver can be injected at runtime.
/// </summary>
public sealed class FileRecordConfiguration : EntityTypeConfigurationBase<FileRecord>
{
    private readonly IKeyResolver? _keyResolver;

    /// <summary>
    /// Runtime ctor — wires versioned EncryptedStringConverter for UploaderDisplayName.
    /// Pass <c>null</c> to fall back to unencrypted (dev-only scenario without Vault).
    /// </summary>
    public FileRecordConfiguration(IKeyResolver? keyResolver)
    {
        _keyResolver = keyResolver;
    }

    protected override void ConfigureEntity(EntityTypeBuilder<FileRecord> builder)
    {
        builder.ToTable("FileRecords");

        builder.Property(f => f.OriginalFileName).HasMaxLength(255).IsRequired();
        builder.Property(f => f.StorageKey).HasMaxLength(512).IsRequired();
        builder.Property(f => f.ContentType).HasMaxLength(127).IsRequired();
        builder.Property(f => f.ChecksumSha256).HasMaxLength(64).IsRequired();
        builder.Property(f => f.Status).IsRequired();
        builder.Property(f => f.SizeBytes).IsRequired();
        builder.Property(f => f.TenantId).IsRequired();
        builder.Property(f => f.UploadedBy).IsRequired();

        // PII field — encrypted at rest when IKeyResolver is available (runtime).
        // Falls back to plain nvarchar(512) during design-time migration scaffolding.
        if (_keyResolver is not null)
        {
            builder.Property(f => f.UploaderDisplayName)
                .HasMaxLength(1024) // encrypted value is longer than plaintext
                .HasConversion((Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter)
                    new EncryptedStringConverter(_keyResolver.GetKey, _keyResolver.CurrentVersion));
        }
        else
        {
            builder.Property(f => f.UploaderDisplayName).HasMaxLength(512);
        }

        builder.HasIndex(f => f.StorageKey).IsUnique().HasDatabaseName("UX_FileRecords_StorageKey");
        builder.HasIndex(f => new { f.TenantId, f.Status }).HasDatabaseName("IX_FileRecords_TenantId_Status");
        builder.HasIndex(f => f.UploadedBy).HasDatabaseName("IX_FileRecords_UploadedBy");
    }
}
