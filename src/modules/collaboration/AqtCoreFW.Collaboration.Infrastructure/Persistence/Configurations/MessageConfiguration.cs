using AqtCoreFW.Collaboration.Domain.Entities;
using AqtCoreFW.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Collaboration.Infrastructure.Persistence.Configurations;

public sealed class MessageConfiguration : EntityTypeConfigurationBase<Message>
{
    protected override void ConfigureEntity(EntityTypeBuilder<Message> builder)
    {
        builder.ToTable("Messages");

        builder.Property(m => m.Content).HasMaxLength(10_000).IsRequired();
        builder.Property(m => m.Metadata).HasMaxLength(4_000);

        builder.Property(m => m.ContentType)
            .HasConversion<string>()
            .HasMaxLength(20).IsRequired();

        // No cascade — messages persist even if conversation is soft-deleted
        builder.HasOne<Conversation>()
            .WithMany()
            .HasForeignKey(m => m.ConversationId)
            .OnDelete(DeleteBehavior.NoAction);

        // Ignore domain events
        builder.Ignore(m => m.DomainEvents);
        // IsMessageDeleted is a computed property, not mapped to a column
        builder.Ignore(m => m.IsMessageDeleted);

        // Base provides (TenantId, Id) + (TenantId, CreatedAt) for ITenantScoped — manual TenantId index dropped.
        builder.HasIndex(m => new { m.ConversationId, m.CreatedAt });
    }
}
