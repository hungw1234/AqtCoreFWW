using AqtCoreFW.Collaboration.Domain.Entities;
using AqtCoreFW.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Collaboration.Infrastructure.Persistence.Configurations;

public sealed class ConversationConfiguration : EntityTypeConfigurationBase<Conversation>
{
    protected override void ConfigureEntity(EntityTypeBuilder<Conversation> builder)
    {
        builder.ToTable("Conversations");

        builder.Property(c => c.Type)
            .HasConversion<string>()
            .HasMaxLength(20).IsRequired();

        builder.Property(c => c.Name).HasMaxLength(200);

        // Ignore navigation-only domain events — outbox handles external dispatch
        builder.Ignore(c => c.DomainEvents);

        // Conversation has many ConversationMembers (ConversationId FK)
        builder.HasMany(c => c.Members)
            .WithOne()
            .HasForeignKey(m => m.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);

        // Base provides (TenantId, Id) + (TenantId, CreatedAt) for ITenantScoped — manual TenantId index dropped.
        builder.HasIndex(c => new { c.TenantId, c.IsArchived });
    }
}
