using AqtCoreFW.Ai.Domain.Entities;
using AqtCoreFW.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Ai.Infrastructure.Persistence.Configurations;

public sealed class AiPromptTemplateConfiguration : EntityTypeConfigurationBase<AiPromptTemplate>
{
    protected override void ConfigureEntity(EntityTypeBuilder<AiPromptTemplate> builder)
    {
        builder.ToTable("AiPromptTemplates");

        builder.Property(t => t.Name).HasMaxLength(200).IsRequired();

        builder.Property(t => t.Category)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(t => t.SystemPrompt)
            .HasColumnType("nvarchar(max)")
            .IsRequired();

        builder.Property(t => t.UserPromptTemplate)
            .HasColumnType("nvarchar(max)")
            .IsRequired();

        builder.HasIndex(t => t.Category);
        builder.HasIndex(t => new { t.IsActive, t.Category });
    }
}
