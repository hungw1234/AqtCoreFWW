using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AqtCoreFW.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSignatureProfileStampVisibilityToggles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ShowIssuer",
                schema: "identity",
                table: "UserSignatureProfiles",
                type: "bit",
                nullable: false,
                defaultValueSql: "1");

            migrationBuilder.AddColumn<bool>(
                name: "ShowSerial",
                schema: "identity",
                table: "UserSignatureProfiles",
                type: "bit",
                nullable: false,
                defaultValueSql: "1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ShowIssuer",
                schema: "identity",
                table: "UserSignatureProfiles");

            migrationBuilder.DropColumn(
                name: "ShowSerial",
                schema: "identity",
                table: "UserSignatureProfiles");
        }
    }
}
