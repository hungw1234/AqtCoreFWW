using System.Text.Json;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Identity.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for ApplicationUser.
///
/// Ctor variants:
///   • Parameterless — design-time migration scaffolding; no encryption (plain nvarchar).
///   • IKeyResolver — runtime path; wires EncryptedStringConverter with versioned key.
///
/// IKeyResolver is preferred over raw byte[] so the converter can write v{N}: prefixes
/// and decrypt any stored version (dual-decode, D10.2).
/// </summary>
public sealed class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    private readonly IKeyResolver? _keyResolver;

    /// <summary>Design-time / migration scaffolding ctor — TotpSecret stored as plain nvarchar.</summary>
    public ApplicationUserConfiguration() { }

    /// <summary>
    /// Runtime ctor — wires versioned EncryptedStringConverter for TotpSecret.
    /// Pass <c>null</c> to fall back to unencrypted (dev-only scenario without Vault).
    /// </summary>
    public ApplicationUserConfiguration(IKeyResolver? keyResolver)
    {
        _keyResolver = keyResolver;
    }

    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.Property(u => u.FullName).HasMaxLength(200).IsRequired();
        builder.Property(u => u.DepartmentCode).HasMaxLength(50);
        builder.Property(u => u.ClearanceLevel).IsRequired();

        // QĐ 742 1.3.a / 1.4.d: auth lifecycle fields — explicit mapping.
        // MustChangePassword: no DB default — CLR default `false` is the desired baseline.
        // EF emits actual value on every INSERT (avoids BoolWithDefaultWarning where DB default == CLR default).
        builder.Property(u => u.MustChangePassword)
            .IsRequired();
        builder.Property(u => u.ConsecutiveLockoutCount)
            .IsRequired()
            .HasDefaultValueSql("0");
        builder.Property(u => u.LastLockoutAt)
            .IsRequired(false);
        builder.Property(u => u.AutoDisabledAt)
            .IsRequired(false);

        // ExtraProperties: JSON column — teams extend without forking
        // ValueComparer ensures EF Core correctly detects dictionary mutations during change tracking
        var extraPropsComparer = new ValueComparer<Dictionary<string, JsonElement>>(
            (a, b) => JsonSerializer.Serialize(a, (JsonSerializerOptions?)null) ==
                      JsonSerializer.Serialize(b, (JsonSerializerOptions?)null),
            v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null).GetHashCode(),
            v => JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                (JsonSerializerOptions?)null) ?? new Dictionary<string, JsonElement>());

        builder.Property(u => u.ExtraProperties)
            .HasColumnType("nvarchar(max)")
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(v,
                    (JsonSerializerOptions?)null) ?? new Dictionary<string, JsonElement>())
            .Metadata.SetValueComparer(extraPropsComparer);

        // ── QĐ742 §4.4 Email encryption (Wave-3) ────────────────────────────────
        // Email + NormalizedEmail are Confidential — encrypted at rest when IKeyResolver available.
        // The unique lookup key shifts from NormalizedEmail → NormalizedEmailHash (see index below).
        //
        // Column width: always nvarchar(1024) — AES-256-GCM ciphertext for a 254-char email
        // encodes to ~376 chars (v{N}:prefix + Base64[IV+cipher+tag]); nvarchar(256) overflows.
        // HasMaxLength(1024) is declared in BOTH branches so the design-time snapshot reflects
        // the widened column (AddEmailHashColumns migration AlterColumn email nvarchar(256)→1024).
        if (_keyResolver is not null)
        {
            var emailConverter = (Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter)
                new EncryptedStringConverter(_keyResolver.GetKey, _keyResolver.CurrentVersion);

            builder.Property(u => u.Email)
                .HasMaxLength(1024)
                .HasConversion(emailConverter)
                .IsRequired(false);

            builder.Property(u => u.NormalizedEmail)
                .HasMaxLength(1024)
                .HasConversion(emailConverter)
                .IsRequired(false);
        }
        else
        {
            // Design-time path: no converter, but column is still nvarchar(1024) per migration.
            builder.Property(u => u.Email)
                .HasMaxLength(1024)
                .IsRequired(false);

            builder.Property(u => u.NormalizedEmail)
                .HasMaxLength(1024)
                .IsRequired(false);
        }

        // ── QĐ742 §4.4 Email hash columns (Fixup P0-1: two-column split) ────────────────────────
        // Global columns (Guid.Empty pepper) — used by FindByEmailAsync (cross-tenant login path).
        // Tenant columns (per-tenant pepper) — populated when TenantId is non-null; future admin queries.
        // 44 chars = Base64(32 bytes HMAC-SHA-256); 64 max gives headroom for algorithm switch.
        builder.Property(u => u.EmailHashGlobal)
            .HasColumnName("EmailHashGlobal")
            .HasMaxLength(64)
            .IsRequired(false);

        builder.Property(u => u.NormalizedEmailHashGlobal)
            .HasColumnName("NormalizedEmailHashGlobal")
            .HasMaxLength(64)
            .IsRequired(false);

        builder.Property(u => u.EmailHashTenant)
            .HasColumnName("EmailHashTenant")
            .HasMaxLength(64)
            .IsRequired(false);

        builder.Property(u => u.NormalizedEmailHashTenant)
            .HasColumnName("NormalizedEmailHashTenant")
            .HasMaxLength(64)
            .IsRequired(false);

        // Unique login-lookup index on NormalizedEmailHashGlobal (replaces NormalizedEmail unique index).
        // Non-deterministic AES-GCM ciphertext cannot carry a unique constraint.
        builder.HasIndex(u => u.NormalizedEmailHashGlobal)
            .IsUnique()
            .HasFilter("[NormalizedEmailHashGlobal] IS NOT NULL")
            .HasDatabaseName("IX_AspNetUsers_NormalizedEmailHashGlobal");

        // Non-unique tenant-scoped index — different tenants may share email, no uniqueness guarantee.
        builder.HasIndex(u => u.NormalizedEmailHashTenant)
            .IsUnique(false)
            .HasFilter("[NormalizedEmailHashTenant] IS NOT NULL")
            .HasDatabaseName("IX_AspNetUsers_NormalizedEmailHashTenant");

        // ── QĐ742 §4.4 PhoneNumber encryption (Fixup P1-1) ──────────────────────────────────────
        // PhoneNumber is Confidential (QĐ 742 §66) — encrypted at rest when IKeyResolver available.
        // Column widened to nvarchar(1024) to accommodate AES-256-GCM ciphertext (migration required).
        if (_keyResolver is not null)
        {
            builder.Property(u => u.PhoneNumber)
                .HasMaxLength(1024)
                .HasConversion((Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter)
                    new EncryptedStringConverter(_keyResolver.GetKey, _keyResolver.CurrentVersion))
                .IsRequired(false);
        }
        else
        {
            builder.Property(u => u.PhoneNumber)
                .HasMaxLength(1024)
                .IsRequired(false);
        }

        // ── Signing national-id (CCCD) encryption ───────────────────────────────────────────────
        // SigningNationalId is the CA-side user_id (Confidential PII, QĐ742) — encrypted at rest when
        // IKeyResolver available. Same nvarchar(1024) shape as PhoneNumber/TotpSecret so the ciphertext
        // fits and both ctor branches declare the column identically (design-time snapshot parity).
        if (_keyResolver is not null)
        {
            builder.Property(u => u.SigningNationalId)
                .HasMaxLength(1024)
                .HasConversion((Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter)
                    new EncryptedStringConverter(_keyResolver.GetKey, _keyResolver.CurrentVersion))
                .IsRequired(false);
        }
        else
        {
            builder.Property(u => u.SigningNationalId)
                .HasMaxLength(1024)
                .IsRequired(false);
        }

        builder.HasIndex(u => u.TenantId);
        builder.HasIndex(u => u.DepartmentCode);
        builder.HasIndex(u => new { u.TenantId, u.IsActive });

        // Delegations navigation
        builder.HasMany(u => u.DelegationsAsDelegate)
            .WithOne(d => d.Delegate)
            .HasForeignKey(d => d.DelegateId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(u => u.DelegationsAsDelegator)
            .WithOne(d => d.Delegator)
            .HasForeignKey(d => d.DelegatorId)
            .OnDelete(DeleteBehavior.Restrict);

        // ── QĐ742 1.6 MFA fields ────────────────────────────────────────────

        // TotpSecret: encrypted at rest when IKeyResolver is available (runtime).
        // Falls back to plain nvarchar(1024) during design-time migration scaffolding.
        if (_keyResolver is not null)
        {
            builder.Property(u => u.TotpSecret)
                .HasMaxLength(1024) // encrypted value longer than 44-char base32 plaintext
                // Cast required: EncryptedStringConverter is ValueConverter<string,string> (non-nullable)
                // but TotpSecret is string? — EF handles nulls before invoking the converter.
                .HasConversion((Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter)
                    new EncryptedStringConverter(_keyResolver.GetKey, _keyResolver.CurrentVersion))
                .IsRequired(false);
        }
        else
        {
            builder.Property(u => u.TotpSecret)
                .HasMaxLength(1024)
                .IsRequired(false);
        }

        builder.Property(u => u.TotpEnrolledAt)
            .HasColumnType("datetime2(3)")
            .IsRequired(false);

        // RecoveryCodesHash: JSON array of 16 BCrypt hashes — nvarchar(max) to avoid truncation.
        builder.Property(u => u.RecoveryCodesHash)
            .HasColumnType("nvarchar(max)")
            .IsRequired(false);

        builder.Property(u => u.MfaGraceUntil)
            .HasColumnType("datetime2(3)")
            .IsRequired(false);
    }
}
