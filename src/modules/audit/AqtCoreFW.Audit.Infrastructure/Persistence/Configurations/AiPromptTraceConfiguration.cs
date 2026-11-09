using AqtCoreFW.Audit.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Audit.Infrastructure.Persistence.Configurations;

public sealed class AiPromptTraceConfiguration : IEntityTypeConfiguration<AiPromptTrace>
{
    public void Configure(EntityTypeBuilder<AiPromptTrace> b)
    {
        b.ToTable("AiPromptTraces");
        b.HasKey(e => e.InternalId);
        b.Property(e => e.InternalId).UseIdentityColumn();
        b.HasAlternateKey(e => e.MessageId);

        b.Property(e => e.CreatedAt).HasColumnType("datetime2(3)");

        b.Property(e => e.ModelName).HasMaxLength(256).IsRequired();
        b.Property(e => e.PromptHash).HasMaxLength(128).IsRequired();
        b.Property(e => e.ResponseHash).HasMaxLength(128);
        b.Property(e => e.PromptText).HasColumnType("nvarchar(max)"); // encrypted at app level
        b.Property(e => e.ClassificationLevel).HasMaxLength(64).IsRequired();
        b.Property(e => e.Cost).HasColumnType("decimal(18,6)");

        b.HasIndex(e => new { e.TenantId, e.CreatedAt });
        b.HasIndex(e => e.SessionId);
        b.HasIndex(e => e.ModelName);
    }
}
