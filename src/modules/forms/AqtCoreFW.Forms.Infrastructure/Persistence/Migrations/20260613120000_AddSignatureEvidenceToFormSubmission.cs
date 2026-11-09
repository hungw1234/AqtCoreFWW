using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AqtCoreFW.Forms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSignatureEvidenceToFormSubmission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SignatureChainEvidence",
                schema: "forms",
                table: "FormSubmissions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SignatureVerdict",
                schema: "forms",
                table: "FormSubmissions",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SignatureVerifiedAt",
                schema: "forms",
                table: "FormSubmissions",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "SignerCertDer",
                schema: "forms",
                table: "FormSubmissions",
                type: "varbinary(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SignerSerial",
                schema: "forms",
                table: "FormSubmissions",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SignerSubject",
                schema: "forms",
                table: "FormSubmissions",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SignatureChainEvidence",
                schema: "forms",
                table: "FormSubmissions");

            migrationBuilder.DropColumn(
                name: "SignatureVerdict",
                schema: "forms",
                table: "FormSubmissions");

            migrationBuilder.DropColumn(
                name: "SignatureVerifiedAt",
                schema: "forms",
                table: "FormSubmissions");

            migrationBuilder.DropColumn(
                name: "SignerCertDer",
                schema: "forms",
                table: "FormSubmissions");

            migrationBuilder.DropColumn(
                name: "SignerSerial",
                schema: "forms",
                table: "FormSubmissions");

            migrationBuilder.DropColumn(
                name: "SignerSubject",
                schema: "forms",
                table: "FormSubmissions");
        }
    }
}
