using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AqtCoreFW.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FixPublicIdIndexNamesAndAddBaseIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameIndex(
                name: "UX_UserSignatureProfile_PublicId",
                schema: "identity",
                table: "UserSignatureProfiles",
                newName: "AK_UserSignatureProfile_PublicId");

            migrationBuilder.RenameIndex(
                name: "UX_SignerCertEnrollment_PublicId",
                schema: "identity",
                table: "SignerCertEnrollments",
                newName: "AK_SignerCertEnrollment_PublicId");

            migrationBuilder.AlterColumn<DateTime>(
                name: "UpdatedAt",
                schema: "identity",
                table: "UserSignatureProfiles",
                type: "datetime2(3)",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "datetime2",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                schema: "identity",
                table: "UserSignatureProfiles",
                type: "datetime2(3)",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "datetime2");

            migrationBuilder.AlterColumn<DateTime>(
                name: "UpdatedAt",
                schema: "identity",
                table: "SignerCertEnrollments",
                type: "datetime2(3)",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "datetime2",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                schema: "identity",
                table: "SignerCertEnrollments",
                type: "datetime2(3)",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "datetime2");

            migrationBuilder.CreateIndex(
                name: "IX_UserSignatureProfile_IsDeleted",
                schema: "identity",
                table: "UserSignatureProfiles",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_UserSignatureProfile_TenantId_CreatedAt",
                schema: "identity",
                table: "UserSignatureProfiles",
                columns: new[] { "TenantId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_UserSignatureProfile_TenantId_Id",
                schema: "identity",
                table: "UserSignatureProfiles",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_SignerCertEnrollment_IsDeleted",
                schema: "identity",
                table: "SignerCertEnrollments",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_SignerCertEnrollment_TenantId_CreatedAt",
                schema: "identity",
                table: "SignerCertEnrollments",
                columns: new[] { "TenantId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_SignerCertEnrollment_TenantId_Id",
                schema: "identity",
                table: "SignerCertEnrollments",
                columns: new[] { "TenantId", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserSignatureProfile_IsDeleted",
                schema: "identity",
                table: "UserSignatureProfiles");

            migrationBuilder.DropIndex(
                name: "IX_UserSignatureProfile_TenantId_CreatedAt",
                schema: "identity",
                table: "UserSignatureProfiles");

            migrationBuilder.DropIndex(
                name: "IX_UserSignatureProfile_TenantId_Id",
                schema: "identity",
                table: "UserSignatureProfiles");

            migrationBuilder.DropIndex(
                name: "IX_SignerCertEnrollment_IsDeleted",
                schema: "identity",
                table: "SignerCertEnrollments");

            migrationBuilder.DropIndex(
                name: "IX_SignerCertEnrollment_TenantId_CreatedAt",
                schema: "identity",
                table: "SignerCertEnrollments");

            migrationBuilder.DropIndex(
                name: "IX_SignerCertEnrollment_TenantId_Id",
                schema: "identity",
                table: "SignerCertEnrollments");

            migrationBuilder.RenameIndex(
                name: "AK_UserSignatureProfile_PublicId",
                schema: "identity",
                table: "UserSignatureProfiles",
                newName: "UX_UserSignatureProfile_PublicId");

            migrationBuilder.RenameIndex(
                name: "AK_SignerCertEnrollment_PublicId",
                schema: "identity",
                table: "SignerCertEnrollments",
                newName: "UX_SignerCertEnrollment_PublicId");

            migrationBuilder.AlterColumn<DateTime>(
                name: "UpdatedAt",
                schema: "identity",
                table: "UserSignatureProfiles",
                type: "datetime2",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "datetime2(3)",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                schema: "identity",
                table: "UserSignatureProfiles",
                type: "datetime2",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "datetime2(3)");

            migrationBuilder.AlterColumn<DateTime>(
                name: "UpdatedAt",
                schema: "identity",
                table: "SignerCertEnrollments",
                type: "datetime2",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "datetime2(3)",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                schema: "identity",
                table: "SignerCertEnrollments",
                type: "datetime2",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "datetime2(3)");
        }
    }
}
