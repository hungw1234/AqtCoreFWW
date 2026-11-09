using AqtCoreFW.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using DomainThread = AqtCoreFW.Collaboration.Domain.Entities.Thread;

namespace AqtCoreFW.Collaboration.Infrastructure.Persistence.Configurations;

public sealed class ThreadConfiguration : EntityTypeConfigurationBase<DomainThread>
{
    protected override void ConfigureEntity(EntityTypeBuilder<DomainThread> builder)
    {
        builder.ToTable("Threads");

        // ParentMessage FK — no cascade, thread records can outlive soft-deleted messages
        builder.HasOne<AqtCoreFW.Collaboration.Domain.Entities.Message>()
            .WithMany()
            .HasForeignKey(t => t.ParentMessageId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(t => t.ParentMessageId).IsUnique();
        builder.HasIndex(t => t.ConversationId);
    }
}
