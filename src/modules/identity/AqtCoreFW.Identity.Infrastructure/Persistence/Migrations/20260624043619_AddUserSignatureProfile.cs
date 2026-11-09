using System;
using AqtCoreFW.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AqtCoreFW.Identity.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Creates the UserSignatureProfiles table (P1 — new aggregate).
    ///
    /// Design notes:
    ///   - T2 entity: long Id (IDENTITY PK) + Guid PublicId (unique AK, anti-IDOR).
    ///   - Tenant-scoped + soft-delete (IsDeleted, global query filter in EF config).
    ///   - RowVersion (SQL rowversion) for optimistic concurrency.
    ///   - FK to SignerCertEnrollments.Id: NoAction — no cascade needed; 0 hard-delete paths in module.
    ///   - Filtered unique index on IsDefault (DB backstop for ≤1 default/user invariant).
    ///   - Image columns (FullSignatureKey, InitialSignatureKey) added nullable — written at stamp
    ///     slice P4/P5; additive now (M2).
    ///
    /// Down() drops table + indexes cleanly (table has no inbound FKs — purely additive rollback).
    /// </summary>
    public partial class AddUserSignatureProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserSignatureProfiles",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0"),
                    SignerCertEnrollmentId = table.Column<long>(type: "bigint", nullable: true),
                    FullSignatureKey = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    InitialSignatureKey = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0"),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ClassificationLevel = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSignatureProfiles", x => x.Id);
                    // NoAction: cert enrollments are never hard-deleted in this module.
                    table.ForeignKey(
                        name: "FK_UserSignatureProfiles_SignerCertEnrollments_SignerCertEnrollmentId",
                        column: x => x.SignerCertEnrollmentId,
                        principalSchema: "identity",
                        principalTable: "SignerCertEnrollments",
                        principalColumn: "Id");
                });

            // T2 AK: PublicId unique index (anti-IDOR — API resolves by PublicId not long Id).
            migrationBuilder.CreateIndex(
                name: "UX_UserSignatureProfile_PublicId",
                schema: "identity",
                table: "UserSignatureProfiles",
                column: "PublicId",
                unique: true);

            // Fast user listing path.
            migrationBuilder.CreateIndex(
                name: "IX_UserSignatureProfile_TenantId_UserId",
                schema: "identity",
                table: "UserSignatureProfiles",
                columns: new[] { "TenantId", "UserId" });

            // FK support index.
            migrationBuilder.CreateIndex(
                name: "IX_UserSignatureProfiles_SignerCertEnrollmentId",
                schema: "identity",
                table: "UserSignatureProfiles",
                column: "SignerCertEnrollmentId");

            // Filtered unique: at most one IsDefault=1 per (TenantId, UserId) among non-deleted rows.
            // Named explicitly so integration tests can pin constraint name in inner-exception messages.
            migrationBuilder.CreateIndex(
                name: "UX_UserSignatureProfile_Tenant_User_Default",
                schema: "identity",
                table: "UserSignatureProfiles",
                columns: new[] { "TenantId", "UserId", "IsDefault" },
                unique: true,
                filter: "[IsDefault] = 1 AND [IsDeleted] = 0");

            // RLS — applied after the table and indexes exist. Each statement is its own batch.
            // Mirrors the pattern in AddSignerCertEnrollment migration (tenant PII table invariant).
            foreach (var sql in RlsMigrationHelper.GenerateRlsPolicyStatements(
                "identity", "UserSignatureProfiles"))
                migrationBuilder.Sql(sql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop RLS before dropping the table (policy must be removed before table can be dropped).
            foreach (var sql in RlsMigrationHelper.DropRlsPolicyStatements(
                "identity", "UserSignatureProfiles"))
                migrationBuilder.Sql(sql);

            // Table has no inbound FKs — drop is safe and fully reverses the Up().
            migrationBuilder.DropTable(
                name: "UserSignatureProfiles",
                schema: "identity");
        }
    }
}
