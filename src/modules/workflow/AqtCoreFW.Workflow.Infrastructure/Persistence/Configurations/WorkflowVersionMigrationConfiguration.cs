using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Workflow.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF config for WorkflowVersionMigration.
/// Table: workflow.VersionMigrations.
/// RowVersion: optimistic concurrency for status transitions.
/// StateMappingsJson: max 8000 chars. MappingHash: max 64 chars (SHA-256 hex = 64).
/// Base class auto-adds: IX_IsDeleted, IX_TenantId_Id, IX_TenantId_CreatedAt.
/// </summary>
public sealed class WorkflowVersionMigrationConfiguration
    : EntityTypeConfigurationBase<WorkflowVersionMigration>
{
    protected override void ConfigureEntity(EntityTypeBuilder<WorkflowVersionMigration> builder)
    {
        builder.ToTable("VersionMigrations");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.SourceDefinitionId).IsRequired();
        builder.Property(m => m.TargetDefinitionId).IsRequired();
        builder.Property(m => m.StateMappingsJson).HasMaxLength(8000).IsRequired();
        builder.Property(m => m.MappingHash).HasMaxLength(64).IsRequired();
        builder.Property(m => m.Status).IsRequired()
            .HasConversion<int>();
        builder.Property(m => m.TenantId).IsRequired();
        builder.Property(m => m.HangfireJobId).HasMaxLength(200);
        // ValueComparer with structural equality for byte[] RowVersion.
        // EF InMemory uses GetKeyValueComparer() for concurrency token comparison —
        // HasConversion<byte[]>(comparer, providerComparer) sets BOTH comparers correctly,
        // enabling InMemory to use SequenceEqual instead of reference equality.
        var byteArrayComparer = new ValueComparer<byte[]>(
            (a, b) => a != null && b != null && a.SequenceEqual(b),
            v => v.Aggregate(0, (a, b) => HashCode.Combine(a, b)),
            v => v.ToArray());

        builder.Property(m => m.RowVersion)
            .IsRowVersion()
            .IsConcurrencyToken()
            .HasConversion<byte[]>(byteArrayComparer, byteArrayComparer);

        // Lookup: active migrations per tenant per source definition
        builder.HasIndex(m => new { m.TenantId, m.SourceDefinitionId, m.Status })
            .HasDatabaseName("IX_VersionMigrations_TenantId_SourceDefId_Status");
    }
}
