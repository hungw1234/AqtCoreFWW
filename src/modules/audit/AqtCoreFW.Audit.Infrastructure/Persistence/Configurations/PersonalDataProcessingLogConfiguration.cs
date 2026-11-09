using AqtCoreFW.Audit.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Audit.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF mapping for <see cref="PersonalDataProcessingLog"/>. T3 plain-class:
/// InternalId = bigint identity PK, MessageId = Guid AK for external addressing.
/// </summary>
public sealed class PersonalDataProcessingLogConfiguration : IEntityTypeConfiguration<PersonalDataProcessingLog>
{
    public void Configure(EntityTypeBuilder<PersonalDataProcessingLog> b)
    {
        b.ToTable("PersonalDataProcessingLogs");
        b.HasKey(e => e.InternalId);
        b.Property(e => e.InternalId).UseIdentityColumn();
        b.HasAlternateKey(e => e.MessageId);

        b.Property(e => e.CreatedAt).HasColumnType("datetime2(3)");

        b.Property(e => e.DataCategory).HasMaxLength(64).IsRequired();
        b.Property(e => e.Purpose).HasMaxLength(256).IsRequired();
        b.Property(e => e.LegalBasis).HasMaxLength(64).IsRequired();
        b.Property(e => e.ProcessingAction).HasMaxLength(64).IsRequired();

        b.HasIndex(e => new { e.TenantId, e.ProcessedAt });
        b.HasIndex(e => e.DataSubjectId);
        b.HasIndex(e => e.ProcessorId);
    }
}
