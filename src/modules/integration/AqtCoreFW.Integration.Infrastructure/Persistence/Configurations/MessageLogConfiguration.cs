using AqtCoreFW.Integration.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Integration.Infrastructure.Persistence.Configurations;

public sealed class MessageLogConfiguration : IEntityTypeConfiguration<MessageLog>
{
    public void Configure(EntityTypeBuilder<MessageLog> builder)
    {
        builder.ToTable("MessageLogs");
        // T3 plain-class: PK is InternalId (long), AK is MessageId (Guid).
        builder.HasKey(m => m.InternalId);
        builder.HasAlternateKey(m => m.MessageId);

        builder.Property(m => m.CreatedAt).HasColumnType("datetime2(3)");

        builder.Property(m => m.MessageType).HasMaxLength(200).IsRequired();
        builder.Property(m => m.Payload).HasColumnType("nvarchar(max)");
        builder.Property(m => m.CorrelationId).HasMaxLength(200);

        builder.Property(m => m.Direction)
            .HasConversion<string>()
            .HasMaxLength(20).IsRequired();

        builder.Property(m => m.Status)
            .HasConversion<string>()
            .HasMaxLength(30).IsRequired();

        builder.HasIndex(m => new { m.TenantId, m.PartnerId, m.SentAt });
        builder.HasIndex(m => m.CorrelationId);

        // FK to Partner
        builder.HasOne<Partner>()
            .WithMany()
            .HasForeignKey(m => m.PartnerId)
            .OnDelete(DeleteBehavior.Restrict);

        // FK to Contract (optional — SetNull on delete)
        builder.HasOne<Contract>()
            .WithMany()
            .HasForeignKey(m => m.ContractId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
