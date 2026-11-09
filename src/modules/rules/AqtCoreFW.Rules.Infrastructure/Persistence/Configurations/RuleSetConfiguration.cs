using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.Rules.Domain.Entities;
using AqtCoreFW.Rules.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Rules.Infrastructure.Persistence.Configurations;

public sealed class RuleSetConfiguration : EntityTypeConfigurationBase<RuleSet>
{
    protected override void ConfigureEntity(EntityTypeBuilder<RuleSet> builder)
    {
        builder.ToTable("RuleSets");

        builder.Property(r => r.Name).HasMaxLength(200).IsRequired();
        builder.Property(r => r.Description).HasMaxLength(1000);
        builder.Property(r => r.Category).HasMaxLength(100);
        builder.Property(r => r.TargetEntityType).HasMaxLength(200);

        builder.Property(r => r.Status)
            .HasConversion<string>()
            .HasMaxLength(30).IsRequired();

        builder.HasIndex(r => new { r.TenantId, r.Status });
        builder.HasIndex(r => r.Category);

        builder.HasMany(r => r.Rules)
            .WithOne()
            .HasForeignKey(rule => rule.RuleSetId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(r => r.Versions)
            .WithOne()
            .HasForeignKey(v => v.RuleSetId)
            .OnDelete(DeleteBehavior.Cascade);

        // Domain events are in-memory only — not persisted
        builder.Ignore(r => r.DomainEvents);
    }
}
