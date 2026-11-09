using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Identity.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="SignerCertEnrollment"/> ([identity] schema).
///
/// Per-signer enrolled signing certificate — the forge-resistant binding key for the
/// cert==assigned-signer invariant. Tenant-scoped + soft-delete (mirrors other Identity entities);
/// RLS is added by the AddSignerCertEnrollment migration.
///
/// P1 expansion: adds Kind, ApprovalStatus, ApprovedBy, ApprovedAt, RejectionReason, PublicId.
///
/// Derives from EntityTypeConfigurationBase which auto-applies:
///   - Id PK (bigint IDENTITY), CreatedAt/UpdatedAt (datetime2(3)), soft-delete query filter +
///     IX_SignerCertEnrollment_IsDeleted index, PublicId AK index (AK_SignerCertEnrollment_PublicId),
///     (TenantId, Id) and (TenantId, CreatedAt DESC) composite indexes, PageCompression annotation.
/// </summary>
public sealed class SignerCertEnrollmentConfiguration : EntityTypeConfigurationBase<SignerCertEnrollment>
{
    protected override void ConfigureEntity(EntityTypeBuilder<SignerCertEnrollment> builder)
    {
        builder.ToTable("SignerCertEnrollments");

        builder.Property(e => e.TenantId).IsRequired();
        builder.Property(e => e.UserId).IsRequired();

        builder.Property(e => e.SerialNumber)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(e => e.Issuer)
            .HasMaxLength(512)
            .IsRequired();

        builder.Property(e => e.Thumbprint)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(e => e.Subject)
            .HasMaxLength(512)
            .IsRequired(false);

        builder.Property(e => e.IsActive)
            .IsRequired()
            .HasDefaultValueSql("1")
            // Sentinel = false: a row is never inserted already-inactive (revoke happens via Update),
            // so marking false as the "unset" value makes EF always emit the explicit true on INSERT.
            .HasSentinel(false);

        builder.Property(e => e.EnrolledAt).IsRequired();

        // ── P1 expansion: provider kind + approval lifecycle ──────────────────────────────────

        // Stored as int; enum values: RemoteViettelMysign=1, UsbTokenVgca=2.
        builder.Property(e => e.Kind)
            .IsRequired()
            .HasConversion<int>();

        // Stored as int; enum values: Pending=1, Approved=2, Rejected=3.
        builder.Property(e => e.ApprovalStatus)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(e => e.ApprovedBy)
            .IsRequired(false);

        builder.Property(e => e.ApprovedAt)
            .IsRequired(false);

        builder.Property(e => e.RejectionReason)
            .HasMaxLength(512)
            .IsRequired(false);

        // ── Phase 01 decouple: subject-type classification + display name ────────────────────────

        // Stored as int; enum values: Personal=0 (default for historical rows), Organization=1.
        builder.Property(e => e.SubjectType)
            .IsRequired()
            .HasConversion<int>()
            .HasDefaultValue(CertSubjectType.Personal);

        builder.Property(e => e.DisplayName)
            .HasMaxLength(256)
            .IsRequired(false);

        // IsDeleted with DB default 0 + sentinel (base handles soft-delete query filter + index).
        builder.Property(e => e.IsDeleted).IsRequired().HasDefaultValueSql("0").HasSentinel(true);

        // Primary lookup path: active enrollments for a signer within a tenant.
        builder.HasIndex(e => new { e.TenantId, e.UserId, e.IsActive })
            .HasDatabaseName("IX_SignerCertEnrollment_TenantId_UserId_IsActive");

        // Admin listing: pending enrollments across users for a tenant — ListPendingEnrollmentsQuery (P3).
        builder.HasIndex(e => new { e.TenantId, e.UserId, e.ApprovalStatus })
            .HasDatabaseName("IX_SignerCertEnrollment_TenantId_UserId_ApprovalStatus");

        // One active enrollment per (tenant, user, serial, issuer) — filtered unique on live + active rows.
        // WHERE IsActive=1: H3 — Reject sets IsActive=false so the row drops out of this index scope,
        // permitting a new Pending enrollment with the same serial+issuer (re-enroll after rejection).
        builder.HasIndex(e => new { e.TenantId, e.UserId, e.SerialNumber, e.Issuer })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0 AND [IsActive] = 1")
            .HasDatabaseName("UX_SignerCertEnrollment_Tenant_User_Serial_Issuer_Active");
    }
}
