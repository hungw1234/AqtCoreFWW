using AqtCoreFW.Ai.Domain.Entities;
using AqtCoreFW.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Ai.Infrastructure.Persistence.Configurations;

public sealed class AiSessionConfiguration : EntityTypeConfigurationBase<AiSession>
{
    protected override void ConfigureEntity(EntityTypeBuilder<AiSession> builder)
    {
        builder.ToTable("AiSessions");

        builder.Property(s => s.TenantId).IsRequired();
        builder.Property(s => s.UserId).IsRequired();
        builder.Property(s => s.StartedAt).IsRequired();
        builder.Property(s => s.LastActivityAt).IsRequired();
        builder.Property(s => s.MessageCount).IsRequired();

        builder.HasIndex(s => new { s.TenantId, s.UserId });
        builder.HasIndex(s => s.LastActivityAt);

        builder.HasMany(s => s.Messages)
            .WithOne()
            .HasForeignKey(m => m.SessionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
