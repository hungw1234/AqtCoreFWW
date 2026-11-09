using AqtCoreFW.Audit.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Audit.Infrastructure.Persistence.Configurations;

public sealed class LoginAttemptConfiguration : IEntityTypeConfiguration<LoginAttempt>
{
    public void Configure(EntityTypeBuilder<LoginAttempt> b)
    {
        b.ToTable("LoginAttempts");
        b.HasKey(e => e.InternalId);
        b.Property(e => e.InternalId).UseIdentityColumn();
        b.HasAlternateKey(e => e.MessageId);

        b.Property(e => e.CreatedAt).HasColumnType("datetime2(3)");

        b.Property(e => e.Email).HasMaxLength(256).IsRequired();
        b.Property(e => e.IpAddress).HasMaxLength(64).IsRequired();
        b.Property(e => e.UserAgent).HasMaxLength(512);
        b.Property(e => e.FailureReason).HasMaxLength(256);

        b.HasIndex(e => new { e.UserId, e.AttemptedAt });
        b.HasIndex(e => new { e.IpAddress, e.AttemptedAt });
        b.HasIndex(e => e.AttemptedAt);
    }
}
