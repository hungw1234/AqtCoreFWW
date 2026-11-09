using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Identity.Infrastructure.Persistence.Configurations;

public sealed class UserDelegationConfiguration : EntityTypeConfigurationBase<UserDelegation>
{
    protected override void ConfigureEntity(EntityTypeBuilder<UserDelegation> builder)
    {
        builder.Property(d => d.Reason).HasMaxLength(500);
        builder.HasIndex(d => new { d.DelegateId, d.IsRevoked, d.ValidTo });
        builder.HasIndex(d => d.DelegatorId);
    }
}

public sealed class AccessReviewRecordConfiguration : EntityTypeConfigurationBase<AccessReviewRecord>
{
    protected override void ConfigureEntity(EntityTypeBuilder<AccessReviewRecord> builder)
    {
        builder.HasIndex(r => new { r.UserId, r.Decision });
        builder.HasIndex(r => r.NextReviewDue);
    }
}

public sealed class ConsentRecordConfiguration : EntityTypeConfigurationBase<ConsentRecord>
{
    protected override void ConfigureEntity(EntityTypeBuilder<ConsentRecord> builder)
    {
        builder.Property(c => c.Purpose).HasMaxLength(200).IsRequired();
        builder.Property(c => c.LegalBasis).HasMaxLength(100).IsRequired();
        builder.Property(c => c.DataSubjectType).HasMaxLength(50).IsRequired(false);
        builder.Property(c => c.EvidenceJson).HasColumnType("nvarchar(max)").IsRequired(false);
        builder.HasIndex(c => new { c.DataSubjectId, c.IsWithdrawn });
        // Manual TenantId index kept — ConsentRecord not ITenantScoped.
        builder.HasIndex(c => c.TenantId);
    }
}

public sealed class AttributeRuleConfiguration : EntityTypeConfigurationBase<AttributeRule>
{
    protected override void ConfigureEntity(EntityTypeBuilder<AttributeRule> builder)
    {
        builder.Property(r => r.Resource).HasMaxLength(100).IsRequired();
        builder.Property(r => r.Action).HasMaxLength(100).IsRequired();
        builder.Property(r => r.AttributeKey).HasMaxLength(100).IsRequired();
        builder.Property(r => r.AttributeValue).HasMaxLength(200).IsRequired();
        builder.HasIndex(r => new { r.AttributeKey, r.AttributeValue, r.TenantId });
    }
}
