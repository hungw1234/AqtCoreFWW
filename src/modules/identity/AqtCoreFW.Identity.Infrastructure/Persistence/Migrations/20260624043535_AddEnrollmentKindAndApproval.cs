using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AqtCoreFW.Identity.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// ALTER SignerCertEnrollments: adds Kind, ApprovalStatus, ApprovedBy, ApprovedAt,
    /// RejectionReason, PublicId columns (P1 expansion — multi-provider + approval lifecycle).
    ///
    /// State-ordering (M3a): columns added nullable → UPDATE backfill → ALTER NOT NULL for
    /// Kind and ApprovalStatus. This avoids "cannot add NOT NULL column without default to a
    /// non-empty table" errors on live rows.
    ///
    /// Backfill decisions (M3b):
    ///   - ApprovalStatus: Approved (2) WHERE IsActive=1 (existing rows are admin-created active
    ///     enrollments — backfilling Approved keeps current signers unblocked).
    ///     Revoked rows (IsActive=0) → Rejected (3) (revoked = no longer bind-able, must not
    ///     become Approved without a new possession-proof challenge).
    ///   - Kind: RemoteViettelMysign (1) for all existing rows — before this expansion only
    ///     Mysign remote enrollment existed.
    ///   - PublicId: NEWID() per row — random Guid, unique (M1 anti-IDOR AK).
    ///
    /// Pre-migration probe note (M3c): before applying in production, verify row count and
    /// distinct CreatedBy provenance via:
    ///   SELECT COUNT(*), COUNT(DISTINCT CreatedBy) FROM identity.SignerCertEnrollments WHERE IsDeleted=0
    /// Expected: all rows CreatedBy = admin user id (EnrollSignerCertCommand is admin-only path).
    /// If non-admin CreatedBy values exist, review before applying.
    ///
    /// Down() explicitly drops all added columns and indexes — rollback safe (additive only).
    /// </summary>
    public partial class AddEnrollmentKindAndApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── Step 1: Add columns nullable first (M3a — cannot add NOT NULL to non-empty table) ──

            migrationBuilder.AddColumn<int>(
                name: "ApprovalStatus",
                schema: "identity",
                table: "SignerCertEnrollments",
                type: "int",
                nullable: true);  // nullable initially for backfill ordering

            migrationBuilder.AddColumn<int>(
                name: "Kind",
                schema: "identity",
                table: "SignerCertEnrollments",
                type: "int",
                nullable: true);  // nullable initially for backfill ordering

            migrationBuilder.AddColumn<Guid>(
                name: "PublicId",
                schema: "identity",
                table: "SignerCertEnrollments",
                type: "uniqueidentifier",
                nullable: true);  // nullable initially for backfill

            migrationBuilder.AddColumn<DateTime>(
                name: "ApprovedAt",
                schema: "identity",
                table: "SignerCertEnrollments",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ApprovedBy",
                schema: "identity",
                table: "SignerCertEnrollments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectionReason",
                schema: "identity",
                table: "SignerCertEnrollments",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: true);

            // ── Step 2: Backfill all existing rows (M3b) ─────────────────────────────────────────

            // Kind: all existing rows were Remote Mysign (only provider before P1).
            migrationBuilder.Sql(
                "UPDATE [identity].[SignerCertEnrollments] SET [Kind] = 1 WHERE [Kind] IS NULL");

            // ApprovalStatus: Approved (2) for active rows; Rejected (3) for revoked rows.
            // Revoked rows must NOT become Approved — they are already deactivated for a reason.
            migrationBuilder.Sql(
                "UPDATE [identity].[SignerCertEnrollments] " +
                "SET [ApprovalStatus] = CASE WHEN [IsActive] = 1 THEN 2 ELSE 3 END " +
                "WHERE [ApprovalStatus] IS NULL");

            // PublicId: unique random Guid per row (NEWID() is per-row in an UPDATE).
            migrationBuilder.Sql(
                "UPDATE [identity].[SignerCertEnrollments] SET [PublicId] = NEWID() WHERE [PublicId] IS NULL");

            // ── Step 3: Alter to NOT NULL now that all rows have values (M3a) ───────────────────

            migrationBuilder.AlterColumn<int>(
                name: "ApprovalStatus",
                schema: "identity",
                table: "SignerCertEnrollments",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "Kind",
                schema: "identity",
                table: "SignerCertEnrollments",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "PublicId",
                schema: "identity",
                table: "SignerCertEnrollments",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            // ── Step 4: Create indexes ────────────────────────────────────────────────────────────

            // Anti-IDOR unique AK on PublicId (M1).
            migrationBuilder.CreateIndex(
                name: "UX_SignerCertEnrollment_PublicId",
                schema: "identity",
                table: "SignerCertEnrollments",
                column: "PublicId",
                unique: true);

            // Fast scan for ListPendingEnrollmentsQuery (P3 admin listing).
            migrationBuilder.CreateIndex(
                name: "IX_SignerCertEnrollment_TenantId_UserId_ApprovalStatus",
                schema: "identity",
                table: "SignerCertEnrollments",
                columns: new[] { "TenantId", "UserId", "ApprovalStatus" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // M3d: explicit Down — drop indexes first, then columns.

            migrationBuilder.DropIndex(
                name: "UX_SignerCertEnrollment_PublicId",
                schema: "identity",
                table: "SignerCertEnrollments");

            migrationBuilder.DropIndex(
                name: "IX_SignerCertEnrollment_TenantId_UserId_ApprovalStatus",
                schema: "identity",
                table: "SignerCertEnrollments");

            migrationBuilder.DropColumn(
                name: "ApprovalStatus",
                schema: "identity",
                table: "SignerCertEnrollments");

            migrationBuilder.DropColumn(
                name: "ApprovedAt",
                schema: "identity",
                table: "SignerCertEnrollments");

            migrationBuilder.DropColumn(
                name: "ApprovedBy",
                schema: "identity",
                table: "SignerCertEnrollments");

            migrationBuilder.DropColumn(
                name: "Kind",
                schema: "identity",
                table: "SignerCertEnrollments");

            migrationBuilder.DropColumn(
                name: "PublicId",
                schema: "identity",
                table: "SignerCertEnrollments");

            migrationBuilder.DropColumn(
                name: "RejectionReason",
                schema: "identity",
                table: "SignerCertEnrollments");
        }
    }
}
