using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AqtCoreFW.Integration.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPartnerPublicId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PublicId",
                schema: "integration",
                table: "Partners",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // Backfill existing rows with NEWID() — dev-only template repo (no prod data)
            migrationBuilder.Sql(
                "UPDATE [integration].[Partners] SET [PublicId] = NEWID() WHERE [PublicId] = '00000000-0000-0000-0000-000000000000'");

            migrationBuilder.CreateIndex(
                name: "AK_Partner_PublicId",
                schema: "integration",
                table: "Partners",
                column: "PublicId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "AK_Partner_PublicId",
                schema: "integration",
                table: "Partners");

            migrationBuilder.DropColumn(
                name: "PublicId",
                schema: "integration",
                table: "Partners");
        }
    }
}
