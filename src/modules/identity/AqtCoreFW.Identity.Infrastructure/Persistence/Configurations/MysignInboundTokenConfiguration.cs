using AqtCoreFW.Identity.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Identity.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for MysignInboundToken ([identity] schema, W6 async-callback).
///
/// PK: string TokenHash (SHA-256 hex, uppercase) — raw token never stored.
/// No audit base class — plain entity, token records are transient (cleanup by ExpiresAt).
///
/// Indexes:
///   IX_MysignInboundToken_ExpiresAt — cleanup query for expired tokens.
/// </summary>
public sealed class MysignInboundTokenConfiguration
    : IEntityTypeConfiguration<MysignInboundToken>
{
    public void Configure(EntityTypeBuilder<MysignInboundToken> builder)
    {
        builder.ToTable("MysignInboundTokens");

        // SHA-256 hex = 64 hex chars; allow buffer
        builder.HasKey(t => t.TokenHash);

        builder.Property(t => t.TokenHash)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(t => t.IssuedAt)
            .IsRequired();

        builder.Property(t => t.ExpiresAt)
            .IsRequired();

        builder.Property(t => t.LastUsedAt)
            .IsRequired(false);

        // Sentinel = true: a token is never inserted already-revoked (revocation happens via Update),
        // so marking true as the "unset" value makes EF always emit the explicit false on INSERT.
        builder.Property(t => t.IsRevoked)
            .IsRequired()
            .HasDefaultValueSql("0") // false
            .HasSentinel(true);

        // Index: cleanup job + expiry check query
        builder.HasIndex(t => t.ExpiresAt)
            .HasDatabaseName("IX_MysignInboundToken_ExpiresAt");
    }
}
