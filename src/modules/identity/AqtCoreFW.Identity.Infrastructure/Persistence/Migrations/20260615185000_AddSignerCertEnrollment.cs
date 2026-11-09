using System;
using AqtCoreFW.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AqtCoreFW.Identity.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Adds the per-signer certificate enrollment store (forge-resistant binding key for the
    /// cert==assigned-signer invariant) plus its tenant Row-Level Security policy.
    ///
    /// Model change (new table) → this migration has a companion .Designer.cs. The RLS DDL is applied
    /// as raw SQL in Up() after the table exists (RLS is not part of the EF model snapshot).
    /// </summary>
    public partial class AddSignerCertEnrollment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SignerCertEnrollments",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SerialNumber = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Issuer = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    Thumbprint = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "1"),
                    EnrolledAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0"),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ClassificationLevel = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SignerCertEnrollments", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SignerCertEnrollment_TenantId_UserId_IsActive",
                schema: "identity",
                table: "SignerCertEnrollments",
                columns: new[] { "TenantId", "UserId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "UX_SignerCertEnrollment_Tenant_User_Serial_Issuer_Active",
                schema: "identity",
                table: "SignerCertEnrollments",
                columns: new[] { "TenantId", "UserId", "SerialNumber", "Issuer" },
                unique: true,
                filter: "[IsDeleted] = 0 AND [IsActive] = 1");

            // RLS — applied after the table exists. Each statement is its own batch.
            foreach (var sql in RlsMigrationHelper.GenerateRlsPolicyStatements(
                "identity", "SignerCertEnrollments"))
                migrationBuilder.Sql(sql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var sql in RlsMigrationHelper.DropRlsPolicyStatements(
                "identity", "SignerCertEnrollments"))
                migrationBuilder.Sql(sql);

            migrationBuilder.DropTable(
                name: "SignerCertEnrollments",
                schema: "identity");
        }
    }
}
