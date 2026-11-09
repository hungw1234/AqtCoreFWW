using AqtCoreFW.Audit.Domain.Entities;
using AqtCoreFW.Audit.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Audit.Infrastructure.Persistence.Configurations;

public sealed class AiOutputReviewConfiguration : IEntityTypeConfiguration<AiOutputReview>
{
    public void Configure(EntityTypeBuilder<AiOutputReview> b)
    {
        b.ToTable("AiOutputReviews");
        b.HasKey(e => e.InternalId);
        b.Property(e => e.InternalId).UseIdentityColumn();
        b.HasAlternateKey(e => e.MessageId);

        b.Property(e => e.CreatedAt).HasColumnType("datetime2(3)");

        b.Property(e => e.Decision).HasConversion<string>().HasMaxLength(32).IsRequired();
        b.Property(e => e.Reason).HasMaxLength(2000);

        b.HasIndex(e => e.PromptTraceId);
        b.HasIndex(e => e.Decision);
    }
}
