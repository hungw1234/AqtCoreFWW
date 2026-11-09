using System;
using AqtCoreFW.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AqtCoreFW.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSignerEnrollmentNonce : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SignerEnrollmentNonces",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Challenge = table.Column<byte[]>(type: "varbinary(64)", maxLength: 64, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ConsumedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SignerEnrollmentNonces", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SignerEnrollmentNonce_ExpiresAt",
                schema: "identity",
                table: "SignerEnrollmentNonces",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_SignerEnrollmentNonce_TenantId_UserId",
                schema: "identity",
                table: "SignerEnrollmentNonces",
                columns: new[] { "TenantId", "UserId" });

            // RLS — applied after the table exists. Each statement is its own batch. The nonce is read and
            // consumed in the issuing user's request scope (ambient tenant set), so RLS isolates it exactly
            // like the other Identity signing tables. The hourly EnrollmentNonceCleanupJob sweeps spent rows
            // per-tenant (SESSION_CONTEXT pinned via TenantScopedSweep) so it stays correct under least-priv.
            foreach (var sql in RlsMigrationHelper.GenerateRlsPolicyStatements(
                "identity", "SignerEnrollmentNonces"))
                migrationBuilder.Sql(sql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var sql in RlsMigrationHelper.DropRlsPolicyStatements(
                "identity", "SignerEnrollmentNonces"))
                migrationBuilder.Sql(sql);

            migrationBuilder.DropTable(
                name: "SignerEnrollmentNonces",
                schema: "identity");
        }
    }
}
