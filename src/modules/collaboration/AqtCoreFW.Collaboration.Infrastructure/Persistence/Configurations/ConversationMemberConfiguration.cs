using AqtCoreFW.Collaboration.Domain.Entities;
using AqtCoreFW.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Collaboration.Infrastructure.Persistence.Configurations;

public sealed class ConversationMemberConfiguration : EntityTypeConfigurationBase<ConversationMember>
{
    protected override void ConfigureEntity(EntityTypeBuilder<ConversationMember> builder)
    {
        builder.ToTable("ConversationMembers");

        builder.Property(m => m.Role)
            .HasConversion<string>()
            .HasMaxLength(20).IsRequired();

        // Index for fast lookup of all conversations a user belongs to
        builder.HasIndex(m => m.UserId);
        builder.HasIndex(m => new { m.ConversationId, m.UserId }).IsUnique();
    }
}
