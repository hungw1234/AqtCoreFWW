using AqtCoreFW.Audit.Domain.Entities;
using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Audit.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF mapping for <see cref="RtbfRequest"/>. T3 plain-class: InternalId = bigint identity PK,
/// MessageId = Guid AK for external addressing.
///
/// Ctor variants:
///   • Parameterless — design-time migration scaffolding; no encryption (plain nvarchar).
///   • IKeyResolver — runtime path; wires EncryptedStringConverter for SubjectEmail and
///     CitizenNationalId (QĐ 742 §4.4 Wave-4: Confidential PII encrypted at rest).
///
/// Column widths for encrypted columns:
///   • SubjectEmail: nvarchar(1024) — AES-256-GCM of a 254-char email ≈ 376 chars
///     (v{N}: prefix + Base64[IV:12 + cipher + tag:16]); nvarchar(256) overflows.
///   • CitizenNationalId: nvarchar(256) — AES-256-GCM of a 12-char ID ≈ 76 chars;
///     nvarchar(12) cannot hold ciphertext. 256 gives headroom for algorithm switch.
/// </summary>
public sealed class RtbfRequestConfiguration : IEntityTypeConfiguration<RtbfRequest>
{
    private readonly IKeyResolver? _keyResolver;

    /// <summary>Design-time / migration scaffolding ctor — PII columns stored as plain nvarchar.</summary>
    public RtbfRequestConfiguration() { }

    /// <summary>
    /// Runtime ctor — wires versioned EncryptedStringConverter for SubjectEmail and CitizenNationalId.
    /// Pass <c>null</c> to fall back to unencrypted (dev-only scenario without Vault).
    /// </summary>
    public RtbfRequestConfiguration(IKeyResolver? keyResolver)
    {
        _keyResolver = keyResolver;
    }

    public void Configure(EntityTypeBuilder<RtbfRequest> b)
    {
        b.ToTable("RtbfRequests");
        b.HasKey(e => e.InternalId);
        b.Property(e => e.InternalId).UseIdentityColumn();
        b.HasAlternateKey(e => e.MessageId);

        b.Property(e => e.CreatedAt).HasColumnType("datetime2(3)");
        b.Property(e => e.RejectionReason).HasColumnType("nvarchar(max)");
        b.Property(e => e.FailureLog).HasColumnType("nvarchar(max)");
        b.Property(e => e.Status).HasConversion<string>().HasMaxLength(32).IsRequired();

        b.HasIndex(e => new { e.TenantId, e.Status });
        b.HasIndex(e => e.DataSubjectId);
        b.HasIndex(e => e.RequestedAt);

        // ── QĐ742 §4.4 Wave-4: Confidential PII encryption ──────────────────
        // SubjectEmail and CitizenNationalId are Confidential — encrypted at rest
        // when IKeyResolver is available (runtime). Column widths widened from the
        // original nvarchar(256)/nvarchar(12) to nvarchar(1024)/nvarchar(256) by the
        // EncryptRtbfPiiColumns migration (AlterColumn explicit in Up/Down).

        if (_keyResolver is not null)
        {
            var converter = (Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter)
                new EncryptedStringConverter(_keyResolver.GetKey, _keyResolver.CurrentVersion);

            b.Property(e => e.SubjectEmail)
                .HasMaxLength(1024)
                .HasConversion(converter);

            b.Property(e => e.CitizenNationalId)
                .HasMaxLength(256)
                .HasConversion(converter);
        }
        else
        {
            // Design-time path: no converter, but columns are nvarchar(1024)/nvarchar(256) per migration.
            b.Property(e => e.SubjectEmail)
                .HasMaxLength(1024);

            b.Property(e => e.CitizenNationalId)
                .HasMaxLength(256);
        }
    }
}
