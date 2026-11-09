using AqtCoreFW.Collaboration.Domain.Entities;
using AqtCoreFW.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Collaboration.Infrastructure.Persistence.Configurations;

public sealed class MessageReadStateConfiguration : EntityTypeConfigurationBase<MessageReadState>
{
    protected override void ConfigureEntity(EntityTypeBuilder<MessageReadState> builder)
    {
        builder.ToTable("MessageReadStates");

        // Composite unique — one read-state record per user per message
        builder.HasIndex(r => new { r.MessageId, r.UserId }).IsUnique();

        builder.HasIndex(r => r.UserId);

        // FK to Message — no cascade (module convention: receipts outlive soft-deleted messages, and
        // messages are never hard-deleted). Referential-integrity guard: a receipt cannot reference a
        // non-existent message. Mirrors Thread→Message / Message→Conversation (both NoAction).
        builder.HasOne<Message>()
            .WithMany()
            .HasForeignKey(r => r.MessageId)
            .OnDelete(DeleteBehavior.NoAction);

        // Intentionally tenant-less: no TenantId column, no RLS policy. Tenant-safety rides on the
        // tenant-scoped message lookup (MarkAsRead validates the message in-tenant before writing; the
        // read query gates on m.TenantId + RLS on Messages). Do not "restore parity" by adding RLS here.
    }
}
