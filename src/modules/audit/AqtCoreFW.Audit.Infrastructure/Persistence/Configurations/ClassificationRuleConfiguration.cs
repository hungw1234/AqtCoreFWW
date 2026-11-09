using AqtCoreFW.Audit.Domain.Entities;
using AqtCoreFW.SharedKernel.Compliance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Audit.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF mapping for <see cref="ClassificationRule"/>.
/// Level (int enum) is the PK — one row per <see cref="DataClassificationLevel"/>.
/// Stored in the default "audit" schema (from AuditDbContext.HasDefaultSchema).
/// </summary>
public sealed class ClassificationRuleConfiguration : IEntityTypeConfiguration<ClassificationRule>
{
    public void Configure(EntityTypeBuilder<ClassificationRule> b)
    {
        b.ToTable("ClassificationRules");

        b.HasKey(e => e.Level);
        b.Property(e => e.Level).HasConversion<int>().ValueGeneratedNever();

        b.Property(e => e.MustEncryptAtRest).IsRequired();
        b.Property(e => e.AllowExport).IsRequired();
        b.Property(e => e.MaxRetentionDays);

        b.Property(e => e.UpdatedBy).HasMaxLength(256).IsRequired();
        b.Property(e => e.UpdatedAt).HasColumnType("datetime2(3)").IsRequired();

        b.Property(e => e.RowVersion).IsRowVersion();

        b.HasData(
            ClassificationRule.Seed(DataClassificationLevel.Public,      mustEncryptAtRest: false, allowExport: true,  maxRetentionDays: null),
            ClassificationRule.Seed(DataClassificationLevel.Internal,    mustEncryptAtRest: false, allowExport: true,  maxRetentionDays: 365),
            ClassificationRule.Seed(DataClassificationLevel.Confidential, mustEncryptAtRest: true, allowExport: false, maxRetentionDays: 1825),
            ClassificationRule.Seed(DataClassificationLevel.Restricted,   mustEncryptAtRest: true, allowExport: false, maxRetentionDays: 365)
        );
    }
}
