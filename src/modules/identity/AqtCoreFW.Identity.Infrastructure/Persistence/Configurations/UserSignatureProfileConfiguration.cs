using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Identity.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="UserSignatureProfile"/> ([identity] schema).
///
/// User-owned signature profiles — display name + optional image keys + optional cert link.
/// Tenant-scoped + soft-delete + optimistic concurrency (RowVersion).
///
/// Key constraints:
/// - PublicId unique index (T2 anti-IDOR AK) — name AK_UserSignatureProfile_PublicId (via base).
/// - Filtered unique index on IsDefault: at most one default profile per (TenantId, UserId)
///   WHERE IsDefault=1 AND IsDeleted=0 — DB-level backstop backing the domain invariant.
/// - FK to SignerCertEnrollments: NoAction (matches Collab FK pattern; 0 hard-delete paths).
///
/// Derives from EntityTypeConfigurationBase which auto-applies:
///   - Id PK (bigint IDENTITY), CreatedAt/UpdatedAt (datetime2(3)), soft-delete query filter +
///     IX_UserSignatureProfile_IsDeleted index, PublicId AK index (AK_UserSignatureProfile_PublicId),
///     (TenantId, Id) and (TenantId, CreatedAt DESC) composite indexes, PageCompression annotation.
/// </summary>
public sealed class UserSignatureProfileConfiguration : EntityTypeConfigurationBase<UserSignatureProfile>
{
    protected override void ConfigureEntity(EntityTypeBuilder<UserSignatureProfile> builder)
    {
        builder.ToTable("UserSignatureProfiles");

        builder.Property(e => e.TenantId).IsRequired();
        builder.Property(e => e.UserId).IsRequired();

        builder.Property(e => e.DisplayName)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(e => e.IsDefault)
            .IsRequired()
            .HasDefaultValueSql("0")
            // Sentinel=false: IsDefault=false is the "unset" value (EF suppresses it on INSERT letting DB default=0 apply).
            // IsDefault=true is always user-set → EF always emits it explicitly on INSERT/UPDATE.
            .HasSentinel(false);

        // Nullable image storage keys (M2 — columns additive, written at stamp slice P4/P5).
        builder.Property(e => e.FullSignatureKey)
            .HasMaxLength(512)
            .IsRequired(false);

        builder.Property(e => e.InitialSignatureKey)
            .HasMaxLength(512)
            .IsRequired(false);

        // FK to SignerCertEnrollments.Id (long PK) — nullable (profile may exist without a cert link).
        // NoAction delete behaviour: matches Collab FK pattern; cert enrollments are never hard-deleted.
        builder.Property(e => e.SignerCertEnrollmentId).IsRequired(false);
        builder.HasOne<SignerCertEnrollment>()
            .WithMany()
            .HasForeignKey(e => e.SignerCertEnrollmentId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        // Per-profile visible-stamp toggles for the Serial / Nhà CC lines. DB default 1 (true) so existing
        // rows keep the prior always-shown behaviour. Sentinel=true: the entity's C# default is true, so on
        // INSERT EF suppresses the CLR-default (true) letting the DB default=1 apply, while an explicit false
        // is always emitted — this is what lets a user actually turn either line off. Without the sentinel EF
        // treats bool's CLR default (false) as "unset" and would silently drop an intended false on INSERT
        // (the profile would still show the line), which is also the source of the EF model warning.
        builder.Property(e => e.ShowSerial)
            .IsRequired()
            .HasDefaultValueSql("1")
            .HasSentinel(true);

        builder.Property(e => e.ShowIssuer)
            .IsRequired()
            .HasDefaultValueSql("1")
            .HasSentinel(true);

        // Optimistic concurrency token — SQL rowversion, auto-managed by DB.
        builder.Property(e => e.RowVersion)
            .IsRowVersion()
            .IsRequired(false);

        // IsDeleted with DB default 0 + sentinel (base handles soft-delete query filter + index).
        // Sentinel=true: IsDeleted=true is the "unset" value so EF always emits explicit false on INSERT.
        builder.Property(e => e.IsDeleted)
            .IsRequired()
            .HasDefaultValueSql("0")
            .HasSentinel(true);

        // Fast user listing path.
        builder.HasIndex(e => new { e.TenantId, e.UserId })
            .HasDatabaseName("IX_UserSignatureProfile_TenantId_UserId");

        // DB-level backstop for ≤1 default/user invariant: filtered unique on IsDefault=1 live rows.
        // Named explicitly so integration tests can pin the constraint name in inner-exception messages.
        builder.HasIndex(e => new { e.TenantId, e.UserId, e.IsDefault })
            .IsUnique()
            .HasFilter("[IsDefault] = 1 AND [IsDeleted] = 0")
            .HasDatabaseName("UX_UserSignatureProfile_Tenant_User_Default");
    }
}
