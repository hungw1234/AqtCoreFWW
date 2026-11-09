using AqtCoreFW.Files.Domain.Entities;
using AqtCoreFW.Files.Infrastructure.Persistence.Configurations;
using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Files.Infrastructure.Persistence;

/// <summary>
/// Files module DB context — owns the "files" schema.
/// FileRecord.UploaderDisplayName is PII-encrypted via EncryptedStringConverter.
/// Manually applies FileRecordConfiguration with IKeyResolver so versioned encryption
/// key is injected at runtime (v{N}: prefix, dual-decode per D10.2).
/// </summary>
public sealed class FilesDbContext : ModuleDbContext
{
    private readonly IKeyResolver? _keyResolver;

    public FilesDbContext(
        DbContextOptions<FilesDbContext> options,
        ITenantContext tenantContext,
        IKeyResolver? keyResolver)
        : base(options, tenantContext)
    {
        _keyResolver = keyResolver;
    }

    protected override string SchemaName => "files";

    public DbSet<FileRecord> FileRecords { get; set; } = default!;

    // M08: Document lifecycle entities
    public DbSet<FileVersion> FileVersions { get; set; } = default!;
    public DbSet<DocumentTemplate> DocumentTemplates { get; set; } = default!;
    public DbSet<RetentionPolicy> RetentionPolicies { get; set; } = default!;
    public DbSet<RecordLifecycle> RecordLifecycles { get; set; } = default!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Base applies schema, soft-delete filter, outbox, and assembly-scanned configs.
        // FileRecordConfiguration has no parameterless ctor so it's skipped by assembly scan.
        base.OnModelCreating(modelBuilder);

        // Manually apply with IKeyResolver so versioned encryption key is available.
        modelBuilder.ApplyConfiguration(new FileRecordConfiguration(_keyResolver));
    }
}
