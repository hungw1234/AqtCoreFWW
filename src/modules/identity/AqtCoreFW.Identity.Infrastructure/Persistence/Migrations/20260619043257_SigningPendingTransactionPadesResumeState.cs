using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AqtCoreFW.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SigningPendingTransactionPadesResumeState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PadesContentsGapStart",
                schema: "identity",
                table: "SigningPendingTransactions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PadesContentsHexLength",
                schema: "identity",
                table: "SigningPendingTransactions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PadesPlaceholderStorageKey",
                schema: "identity",
                table: "SigningPendingTransactions",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "PadesSignedAttributesDer",
                schema: "identity",
                table: "SigningPendingTransactions",
                type: "varbinary(4000)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PadesSignedStorageKey",
                schema: "identity",
                table: "SigningPendingTransactions",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "PadesSignerCertDer",
                schema: "identity",
                table: "SigningPendingTransactions",
                type: "varbinary(8000)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PadesContentsGapStart",
                schema: "identity",
                table: "SigningPendingTransactions");

            migrationBuilder.DropColumn(
                name: "PadesContentsHexLength",
                schema: "identity",
                table: "SigningPendingTransactions");

            migrationBuilder.DropColumn(
                name: "PadesPlaceholderStorageKey",
                schema: "identity",
                table: "SigningPendingTransactions");

            migrationBuilder.DropColumn(
                name: "PadesSignedAttributesDer",
                schema: "identity",
                table: "SigningPendingTransactions");

            migrationBuilder.DropColumn(
                name: "PadesSignedStorageKey",
                schema: "identity",
                table: "SigningPendingTransactions");

            migrationBuilder.DropColumn(
                name: "PadesSignerCertDer",
                schema: "identity",
                table: "SigningPendingTransactions");
        }
    }
}
