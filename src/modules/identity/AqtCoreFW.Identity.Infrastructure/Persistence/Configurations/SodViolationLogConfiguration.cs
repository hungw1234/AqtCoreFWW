using AqtCoreFW.Identity.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Identity.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for SodViolationLog (T3 append-only).
/// Table: identity.SodViolationLogs
///
/// Schema-preservation note (Phase-04b):
///   DB columns still use pre-T3 names: Id (PK), DetectedAt (timestamp).
///   T3 C# properties InternalId + CreatedAt are mapped to those DB columns via HasColumnName.
///   A Phase-04b migration will rename columns once the full schema sync is completed.
///
/// FK: SodRuleId → SodConflictRules(Id) ON DELETE RESTRICT.
///   SodConflictRule is T1 (long PK). SodRuleId is long? post AMEND-B2-Identity.
///   DB column is still uniqueidentifier — will be migrated to bigint in Phase-04b.
/// </summary>
public sealed class SodViolationLogConfiguration : IEntityTypeConfiguration<SodViolationLog>
{
    public void Configure(EntityTypeBuilder<SodViolationLog> builder)
    {
        builder.ToTable("SodViolationLogs");

        // T3 PK: long InternalId maps to DB column "Id" (pre-T3 name, preserved for schema compat)
        builder.HasKey(l => l.InternalId);
        builder.Property(l => l.InternalId)
            .HasColumnName("Id")
            .ValueGeneratedOnAdd();

        // T3 AK: MessageId (Guid v7 monotonic)
        builder.Property(l => l.MessageId)
            .IsRequired();
        builder.HasAlternateKey(l => l.MessageId);

        // CreatedAt maps to DB column "DetectedAt" (pre-T3 name, preserved for schema compat)
        builder.Property(l => l.CreatedAt)
            .HasColumnName("DetectedAt")
            .IsRequired();

        builder.Property(l => l.TenantId)
            .IsRequired();

        builder.Property(l => l.UserId);
        builder.Property(l => l.RoleId);

        // SodRuleId: long? FK to SodConflictRules(Id). DB column is uniqueidentifier pre-migration.
        builder.Property(l => l.SodRuleId);

        builder.Property(l => l.Justification)
            .HasMaxLength(2000);

        builder.Property(l => l.ActorId)
            .IsRequired();

        // Index for UI filter (tenant + date DESC) and P5 purge job
        // Index column name references DB "DetectedAt" — use HasColumnName on the index manually
        builder.HasIndex(l => new { l.TenantId, l.CreatedAt })
            .HasDatabaseName("IX_SodViolationLogs_TenantId_DetectedAt")
            .IsDescending(false, true); // TenantId ASC, DetectedAt DESC

        // FK to SodConflictRules — ON DELETE RESTRICT (soft-delete only; blocks raw-SQL hard-delete)
        builder.HasOne<SodConflictRule>()
            .WithMany()
            .HasForeignKey(l => l.SodRuleId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);
    }
}
