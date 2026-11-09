using AqtCoreFW.Audit.Domain.Entities;
using AqtCoreFW.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Audit.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF config for CompliancePolicyEvaluation — evaluation results for AI governance compliance.
/// T1: AuditableEntity : Entity subclass — EntityTypeConfigurationBase applies universals:
///   bigint PK+Identity, datetime2(3) timestamps, IX_*_IsDeleted, HasQueryFilter(!IsDeleted), PageCompression.
/// </summary>
public sealed class CompliancePolicyEvaluationConfiguration
    : EntityTypeConfigurationBase<CompliancePolicyEvaluation>
{
    protected override void ConfigureEntity(EntityTypeBuilder<CompliancePolicyEvaluation> b)
    {
        b.ToTable("CompliancePolicyEvaluations");

        b.Property(e => e.EntityType).HasMaxLength(256).IsRequired();
        b.Property(e => e.Result).HasConversion<string>().HasMaxLength(32).IsRequired();
        b.Property(e => e.Details).HasColumnType("nvarchar(max)");

        b.HasIndex(e => new { e.PolicyId, e.EvaluatedAt });
        b.HasIndex(e => new { e.EntityType, e.EntityId });
    }
}
