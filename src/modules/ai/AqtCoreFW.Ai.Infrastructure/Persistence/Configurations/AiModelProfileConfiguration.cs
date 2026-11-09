using AqtCoreFW.Ai.Domain.Entities;
using AqtCoreFW.Ai.Domain.Enums;
using AqtCoreFW.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Ai.Infrastructure.Persistence.Configurations;

public sealed class AiModelProfileConfiguration : EntityTypeConfigurationBase<AiModelProfile>
{
    protected override void ConfigureEntity(EntityTypeBuilder<AiModelProfile> builder)
    {
        builder.ToTable("AiModelProfiles");

        builder.Property(p => p.Name).HasMaxLength(200).IsRequired();
        builder.Property(p => p.ModelId).HasMaxLength(200).IsRequired();
        builder.Property(p => p.Endpoint).HasMaxLength(500);

        builder.Property(p => p.Provider)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(p => p.Temperature)
            .HasColumnType("decimal(5,4)");

        builder.Property(p => p.CostPerToken)
            .HasColumnType("decimal(18,10)");

        builder.HasIndex(p => p.Provider);
        builder.HasIndex(p => new { p.IsDefault, p.IsActive });
    }
}
