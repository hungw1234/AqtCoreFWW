using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Identity.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for MfaPolicy — identity schema, unique index on Role.
/// Seeds 3 system rows for SystemAdmin / Admin / GovOfficer (QĐ742 1.6.a).
/// Deterministic IDs allow idempotent HasData re-runs across migrations.
/// </summary>
public sealed class MfaPolicyConfiguration : EntityTypeConfigurationBase<MfaPolicy>
{
    // Deterministic seed IDs — long PK; EF emits IDENTITY_INSERT ON for HasData.
    private const long SeedSystemAdminId = 1L;
    private const long SeedAdminId       = 2L;
    private const long SeedGovOfficerId  = 3L;

    // Deterministic Guid v7 PublicId constants for seed rows (Phase-07 T2 retier).
    // Fixed-pattern for traceability; v7 form so they blend with runtime-issued v7 ids.
    private static readonly Guid SeedSystemAdminPublicId = Guid.Parse("018f5e80-0001-7001-8000-000000000001");
    private static readonly Guid SeedAdminPublicId       = Guid.Parse("018f5e80-0001-7001-8000-000000000002");
    private static readonly Guid SeedGovOfficerPublicId  = Guid.Parse("018f5e80-0001-7001-8000-000000000003");

    // System actor used as CreatedBy for seeded rows.
    private static readonly Guid SystemActorId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    protected override void ConfigureEntity(EntityTypeBuilder<MfaPolicy> builder)
    {
        builder.Property(p => p.Role)
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(p => p.IsMandatory).IsRequired();

        builder.Property(p => p.GraceDays)
            .IsRequired()
            .HasDefaultValueSql("7")
            // Sentinel = -1 (impossible: validator enforces GraceDays >= 0), so EF always emits
            // the explicit value on INSERT — including a legitimate 0. Without this, EF treats 0 as
            // its implicit int sentinel, omits the column, and the DB DEFAULT 7 silently overrides a
            // zero-grace "must enrol immediately" policy — downgrading the intended security posture.
            .HasSentinel(-1);

        // Store MfaFactor flags enum as int column.
        builder.Property(p => p.AllowedFactors)
            .HasConversion<int>()
            .IsRequired();

        // Optimistic concurrency.
        builder.Property(p => p.RowVersion)
            .IsRowVersion()
            .IsConcurrencyToken();

        // One policy per role — Role is the semantic PK for lookups.
        builder.HasIndex(p => p.Role).IsUnique();

        // ── Seed data (3 system rows per QĐ742 1.6.a) ───────────────────────
        // SystemAdmin: mandatory, GraceDays=0 (P1-1: must enrol before policy enable).
        // Admin:       mandatory, GraceDays=7.
        // GovOfficer:  optional,  GraceDays=7, Totp+EmailOtp.
        // CreatedAt/UpdatedAt use fixed UTC so migration hash is deterministic.
        // Note: RowVersion is DB-managed — omitted from HasData (EF will default to 0x00).
        // HasData with enum value (not int cast) — EF applies HasConversion<int> during migration.
        builder.HasData(
            new
            {
                Id              = SeedSystemAdminId,
                PublicId        = SeedSystemAdminPublicId,
                Role            = "SystemAdmin",
                IsMandatory     = true,
                GraceDays       = 0,
                AllowedFactors  = MfaFactor.Totp,
                CreatedBy       = SystemActorId,
                ModifiedBy      = (Guid?)null,
                ClassificationLevel = AqtCoreFW.SharedKernel.Domain.ClassificationLevel.Internal,
                CreatedAt       = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc),
                UpdatedAt       = (DateTime?)null,
                IsDeleted       = false,
            },
            new
            {
                Id              = SeedAdminId,
                PublicId        = SeedAdminPublicId,
                Role            = "Admin",
                IsMandatory     = true,
                GraceDays       = 7,
                AllowedFactors  = MfaFactor.Totp,
                CreatedBy       = SystemActorId,
                ModifiedBy      = (Guid?)null,
                ClassificationLevel = AqtCoreFW.SharedKernel.Domain.ClassificationLevel.Internal,
                CreatedAt       = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc),
                UpdatedAt       = (DateTime?)null,
                IsDeleted       = false,
            },
            new
            {
                Id              = SeedGovOfficerId,
                PublicId        = SeedGovOfficerPublicId,
                Role            = "GovOfficer",
                IsMandatory     = false,
                GraceDays       = 7,
                AllowedFactors  = MfaFactor.Totp | MfaFactor.EmailOtp,
                CreatedBy       = SystemActorId,
                ModifiedBy      = (Guid?)null,
                ClassificationLevel = AqtCoreFW.SharedKernel.Domain.ClassificationLevel.Internal,
                CreatedAt       = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc),
                UpdatedAt       = (DateTime?)null,
                IsDeleted       = false,
            });
    }
}
