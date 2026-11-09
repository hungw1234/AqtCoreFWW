using AqtCoreFW.Ai.Domain.Entities;
using AqtCoreFW.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Ai.Infrastructure.Persistence.Configurations;

public sealed class AiChatMessageConfiguration : EntityTypeConfigurationBase<AiChatMessage>
{
    protected override void ConfigureEntity(EntityTypeBuilder<AiChatMessage> builder)
    {
        builder.ToTable("AiChatMessages");

        builder.Property(m => m.SessionId).IsRequired();
        builder.Property(m => m.Role)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        // Content is null for Confidential+ messages (only hash stored)
        builder.Property(m => m.Content).HasMaxLength(8000);
        builder.Property(m => m.ContentHash).HasMaxLength(64).IsRequired();
        builder.Property(m => m.IsContentEncrypted).IsRequired();

        builder.HasIndex(m => m.SessionId);
        builder.HasIndex(m => new { m.SessionId, m.IsContentEncrypted });
    }
}
